// 更新页面策略修复：删除导致更新页面被隐藏的策略值。
using System;
using Microsoft.Win32;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 删除导致更新页面被隐藏的具体策略值。
    ///
    /// 只删除真正会造成“隐藏/禁用更新页面”的值，不无差别删除整个
    /// ...\Policies\Microsoft\Windows\WindowsUpdate 策略键（该键可能承载正常的
    /// 延迟更新、驱动策略等设置，删除会误伤）。
    ///
    /// 关键：reg delete 删除“不存在的值”会返回非 0，若直接累计会误判失败。
    /// 检测阶段只要有任意一处命中即判需修复，但四处值通常只存在其中一两处，
    /// 其余本就不存在——按退出码判定必然把“已修复”报成“失败”。
    /// 因此这里改为：先判断值是否存在，仅删除存在的；删除后再复查确已消失。
    /// </summary>
    public class PolicyFix : IFixItem
    {
        public string Id { get { return IssueIds.Policy; } }

        // 步骤级失败通知回调，由 RepairService 注入（同一次运行内共享同一回调）。
        private readonly Action<string> _notify;

        public PolicyFix(Action<string> notify)
        {
            _notify = notify;
        }

        public string FollowUpHint
        {
            get
            {
                return "策略未能完全移除。可能是域策略或安全软件重新写入了这些值，" +
                       "请确认当前计算机未受组策略统一管理。";
            }
        }

        // 与检测侧读取的路径一致
        private const string ExplorerPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        public bool Fix()
        {
            bool ok = true;

            ok &= DeletePolicyValue(RegistryHive.LocalMachine, ExplorerPath, "SettingsPageVisibility");
            ok &= DeletePolicyValue(RegistryHive.CurrentUser, ExplorerPath, "SettingsPageVisibility");
            ok &= DeletePolicyValue(RegistryHive.LocalMachine, ExplorerPath, "NoWindowsUpdate");
            ok &= DeletePolicyValue(RegistryHive.CurrentUser, ExplorerPath, "NoWindowsUpdate");

            return ok;
        }

        /// <summary>
        /// 删除指定注册表值，并以“删除后该值确实不存在”判定成功。
        /// 值本就不存在 → 视为成功（无需删除），不会因 reg delete 返回非 0 而误判。
        /// </summary>
        private bool DeletePolicyValue(RegistryHive hive, string path, string name)
        {
            // 不存在即无需处理
            if (!RegistryValueExists(hive, path, name)) return true;

            string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
            ShellRunner.Run("reg.exe",
                "delete \"" + hiveName + "\\" + path + "\" /v " + name + " /f", 15000);

            // 以最终状态为准：删除后复查，确认该值已消失
            bool stillExists = RegistryValueExists(hive, path, name);
            if (stillExists)
            {
                if (_notify != null) _notify("未能删除策略值 " + hiveName + "\\" + name + "。");
            }
            return !stillExists;
        }

        /// <summary>
        /// 判断注册表值是否存在（不关心其内容）。
        /// 显式使用原生视图：x86 进程的 RegistryView.Default 会重定向到 WOW6432Node，
        /// 那里通常没有这些策略值，会导致判断错误。
        /// </summary>
        private static bool RegistryValueExists(RegistryHive hive, string path, string name)
        {
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Registry32;

                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return false;
                    return k.GetValue(name) != null;
                }
            }
            catch
            {
                // 读取失败时保守返回 true：交给删除动作去尝试，
                // 避免“读不到就当作不存在”从而跳过真正需要删除的值。
                return true;
            }
        }
    }
}
