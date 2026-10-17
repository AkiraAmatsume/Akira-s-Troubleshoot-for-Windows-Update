// 更新计划任务状态检测。
using System;
using System.Text;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测 Windows Update 相关计划任务是否被禁用。
    /// 三态：查不到 → Warning 且不可自动修复（无依据）；查到禁用 → Warning 可修；否则 Ok。
    /// </summary>
    public class TasksCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查计划任务…"; } }

        public IssueItem Check()
        {
            // 输出每个被禁用任务的“路径\名称”，便于用户知道具体是哪些任务。
            string script =
                "$paths=@('\\Microsoft\\Windows\\InstallService*','\\Microsoft\\Windows\\UpdateOrchestrator*'," +
                "'\\Microsoft\\Windows\\UpdateAssistant*','\\Microsoft\\Windows\\WaaSMedic*'," +
                "'\\Microsoft\\Windows\\WindowsUpdate*','\\Microsoft\\WindowsUpdate*');" +
                "foreach($p in $paths){Get-ScheduledTask -TaskPath $p -ErrorAction SilentlyContinue|" +
                "Where-Object{$_.State -eq 'Disabled'}|ForEach-Object{Write-Output ($_.TaskPath + $_.TaskName)}}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 60000);

            // 脚本本身失败（执行策略、超时、任务计划服务异常）时，输出为空，
            // 不能据此断言“任务均正常”，否则会像服务检测那样漏掉真实问题。
            bool unreadable = r.Code != 0 || r.TimedOut;

            var disabledNames = new System.Collections.Generic.List<string>();
            if (!unreadable && !string.IsNullOrEmpty(r.StdOut))
            {
                foreach (var line in r.StdOut.Split(new[] { '\r', '\n' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length > 0) disabledNames.Add(t);
                }
            }

            int disabled = disabledNames.Count;
            bool bad = disabled > 0;

            string detail = null;
            if (bad)
            {
                var sb = new StringBuilder();
                sb.AppendLine("被禁用任务（" + disabled + " 个）：");
                foreach (var n in disabledNames) sb.AppendLine("• " + n);
                detail = sb.ToString().Trim();
            }
            else if (unreadable)
            {
                detail = r.TimedOut
                    ? "查询计划任务超时，未能确认其启用状态。"
                    : "查询计划任务失败（退出码 " + r.Code + "），未能确认其启用状态。";
            }

            return new IssueItem
            {
                Id = IssueIds.Tasks,
                Title = bad ? "部分更新计划任务被禁用"
                      : unreadable ? "更新计划任务状态无法确认"
                      : "更新计划任务状态正常",
                Description = bad
                    ? "检测到 " + disabled + " 个任务被禁用，将重新启用。"
                    : unreadable
                        ? "无法读取计划任务状态，请手动检查任务计划程序中与 Windows Update 相关的任务。"
                        : "UpdateOrchestrator 相关任务均已启用。",
                Detail = detail,
                Severity = bad ? IssueSeverity.Warning
                         : unreadable ? IssueSeverity.Warning
                         : IssueSeverity.Ok,
                NeedsFix = bad,
                CanAutoFix = bad
            };
        }
    }
}
