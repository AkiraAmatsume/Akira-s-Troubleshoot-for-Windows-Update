// 更新页面策略检测。
using System;
using System.Text;
using Microsoft.Win32;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测真正会“隐藏/禁用设置里的 Windows 更新页面”的策略值。
    ///
    /// 真正会隐藏/禁用更新页面的只有两类：
    ///   1) SettingsPageVisibility 包含 windowsupdate（且为 hide 或 showonly 未列出该页）
    ///   2) NoWindowsUpdate == 1
    ///
    /// 注意：HKLM/HKCU 下的 ...\Policies\Microsoft\Windows\WindowsUpdate 策略键“存在”
    /// 本身并不代表更新页面被隐藏（它常被延迟/驱动策略等正常使用），因此不能据此判坏。
    /// </summary>
    public class PolicyCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查更新页面策略…"; } }

        private const string ExplorerPoliciesPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        public IssueItem Check()
        {
            var found = new StringBuilder();
            bool bad = false;

            bad |= HasHiddenUpdatePage(RegistryHive.CurrentUser, found);
            bad |= HasHiddenUpdatePage(RegistryHive.LocalMachine, found);
            bad |= HasNoWindowsUpdate(RegistryHive.CurrentUser, found);
            bad |= HasNoWindowsUpdate(RegistryHive.LocalMachine, found);

            return new IssueItem
            {
                Id = IssueIds.Policy,
                Title = bad ? "更新页面被策略隐藏" : "更新页面策略正常",
                Description = bad
                    ? "检测到系统设置中“Windows 更新”页面被隐藏或禁用。"
                    : "未检测到隐藏或禁用更新页面的策略。",
                Detail = bad ? found.ToString().Trim() : null,
                Severity = bad ? IssueSeverity.Problem : IssueSeverity.Ok,
                NeedsFix = bad,
                CanAutoFix = bad,
                ManualHint = null
            };
        }

        /// <summary>
        /// 判断 SettingsPageVisibility 是否真的把“Windows 更新”页隐藏了。
        /// 值形如 "hide:windowsupdate" 或 "showonly:about;...".
        /// </summary>
        private bool HasHiddenUpdatePage(RegistryHive hive, StringBuilder found)
        {
            string value = ReadStringValue(hive, ExplorerPoliciesPath, "SettingsPageVisibility");
            if (string.IsNullOrEmpty(value)) return false;

            string v = value.Trim();

            // hide:... → 列出被隐藏的页；含 windowsupdate 即被隐藏
            if (v.StartsWith("hide", StringComparison.OrdinalIgnoreCase))
            {
                if (v.IndexOf("windowsupdate", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.AppendLine(HiveLabel(hive) + " SettingsPageVisibility=" + value + "（隐藏了更新页面）");
                    return true;
                }
                return false;
            }

            // showonly:... → 只显示列表中的页；未列出 windowsupdate 即为隐藏
            if (v.StartsWith("showonly", StringComparison.OrdinalIgnoreCase))
            {
                if (v.IndexOf("windowsupdate", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    found.AppendLine(HiveLabel(hive) + " SettingsPageVisibility=" + value + "（仅显示部分页，未包含更新页面）");
                    return true;
                }
                return false;
            }

            // 其他格式：保守起见，只有明确出现 windowsupdate 才认为是相关设置，但不判隐藏
            return false;
        }

        /// <summary>NoWindowsUpdate 值 == 1 时才算禁用更新页面。</summary>
        private bool HasNoWindowsUpdate(RegistryHive hive, StringBuilder found)
        {
            object raw = ReadValue(hive, ExplorerPoliciesPath, "NoWindowsUpdate");
            if (raw == null) return false;

            int val;
            try { val = Convert.ToInt32(raw); }
            catch { return false; }

            if (val == 1)
            {
                found.AppendLine(HiveLabel(hive) + " NoWindowsUpdate=1（禁用更新页面）");
                return true;
            }
            return false;
        }

        private static string HiveLabel(RegistryHive hive)
        {
            return hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\";
        }

        private static object ReadValue(RegistryHive hive, string path, string name)
        {
            try
            {
                // 显式使用原生视图：x86 进程的 RegistryView.Default 会被重定向到
                // SOFTWARE\WOW6432Node，那里通常没有这些策略值，会导致漏检。
                RegistryView view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Registry32;

                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var k = root.OpenSubKey(path))
                {
                    if (k != null) return k.GetValue(name);
                }
            }
            catch { }
            return null;
        }

        private static string ReadStringValue(RegistryHive hive, string path, string name)
        {
            object v = ReadValue(hive, path, name);
            return v == null ? null : v.ToString();
        }
    }
}
