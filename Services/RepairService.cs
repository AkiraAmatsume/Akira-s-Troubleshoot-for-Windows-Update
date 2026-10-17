// 修复执行调度。
// 各修复项实现为 Services/Repair 下独立类，本类只负责按 ID 调度、状态回填与收尾。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Services.Repair;

namespace WindowsUpdateFixer.Services
{
    public class RepairService
    {
        /// <summary>
        /// 非致命失败通知回调：某个修复项执行失败时回调给界面（状态栏），由页面注入。
        /// </summary>
        public Action<string> RepairStepFailed;

        /// <summary>
        /// 收尾的 gpupdate 是否成功。null 表示尚未执行完毕。
        /// 该步骤不计入各项修复的成功/失败，但失败时会影响“建议重启生效”的可信度，
        /// 因此需要让结果页能看到，而不是仅在底部状态栏短暂显示。
        /// </summary>
        public bool? GroupPolicyRefreshed { get; private set; }

        /// <summary>按 IssueIds 索引的修复项。构造时创建，整个运行周期复用。</summary>
        private readonly Dictionary<string, IFixItem> _fixers;

        public RepairService()
        {
            // RepairStepFailed 为实例字段、由界面在运行前注入，故回调在执行时才读取它，
            // 不能用构造时的快照，否则页面晚于构造赋值会导致通知丢失。
            Action<string> notify = msg =>
            {
                var cb = RepairStepFailed;
                if (cb != null) cb(msg);
            };

            _fixers = new Dictionary<string, IFixItem>
            {
                { IssueIds.Policy,      new PolicyFix(notify) },
                { IssueIds.Services,    new ServicesFix(notify) },
                { IssueIds.Recovery,    new RecoveryFix(notify) },
                { IssueIds.Files,       new FilesFix(notify) },
                { IssueIds.Reset,       new ResetFix(notify) },
                { IssueIds.Tasks,       new TasksFix(notify) },
                { IssueIds.SettingsApp, new SettingsAppFix(notify) }
            };
        }

        /// <summary>顺序遍历 AppState.Issues，对 CanAutoFix=true 的项执行修复。</summary>
        public Task RunAsync(Action<IssueItem> onItemUpdated)
        {
            return Task.Run(() =>
            {
                foreach (var item in AppState.Issues)
                {
                    // 不可自动修复的问题项：标记为 Skipped（界面显示“需手动处理”），
                    // 由结果页列入“需要手动处理”分组。
                    if (item.NeedsFix && !item.CanAutoFix)
                    {
                        SetState(onItemUpdated, item, FixState.Skipped);
                        continue;
                    }

                    // 健康项或不可修项直接忽略，不进入修复流程
                    if (!(item.NeedsFix && item.CanAutoFix)) continue;

                    SetState(onItemUpdated, item, FixState.Running);

                    IFixItem fixer;
                    bool ok;
                    try
                    {
                        ok = _fixers.TryGetValue(item.Id, out fixer) && fixer.Fix();
                    }
                    catch
                    {
                        fixer = null;
                        ok = false;
                        // 执行中抛出异常：各 Fix 内部的步骤级通知可能未执行到，这里补一条通用提示
                        var cb = RepairStepFailed;
                        if (cb != null) cb("修复项执行异常：" + item.Title);
                    }

                    // 修复失败时补上“后续建议”，让结果页能展示，
                    // 而不是仅在底部状态栏短暂显示。
                    if (!ok && fixer != null) item.FollowUpHint = fixer.FollowUpHint;

                    SetState(onItemUpdated, item, ok ? FixState.Done : FixState.Failed);
                }
                // 收尾：刷新组策略
                // 结果记录到 GroupPolicyRefreshed，供结果页展示；不计入各修复项的成败。
                GroupPolicyRefreshed = ShellRunner.RunExpectSuccess(
                    "gpupdate.exe", "/force", "刷新组策略（gpupdate）", 60000);
            });
        }

        private void SetState(Action<IssueItem> cb, IssueItem item, FixState state)
        {
            item.State = state;
            if (cb != null) cb(item);
        }
    }
}
