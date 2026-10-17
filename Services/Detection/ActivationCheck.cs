// 系统激活状态检测。
using System;
using Microsoft.Win32;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测系统激活状态。
    ///
    /// 三态处理，避免把“宽限期”或“读不到”直接说成“未激活”：
    ///   1      → 已激活（Ok）
    ///   2/3/6  → 宽限期：系统仍属已激活，只是许可证需刷新/联网（Ok，不提示去激活）
    ///   0      → 未授权：确需激活（Warning + NeedsFix）
    ///   4/5    → 非正版/通知模式：需处理（Warning + NeedsFix）
    ///   -1     → 读取失败：无法确认，不能断言未激活（Warning，不可自动修复）
    /// </summary>
    public class ActivationCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查系统激活状态…"; } }

        public IssueItem Check()
        {
            // 优先用 SoftwareLicensingProduct（与 slmgr /xpr、设置里的激活状态一致）。
            // LicenseStatus: 1=已激活；0=未授权；2=宽限期；3=额外宽限；4=非正版宽限；5=通知；6=延长宽限。
            int status = QueryActivationStatus();

            // 注册表 LicenseStatus 常不存在，仅作兜底（取不到则保持 -1）。
            if (status < 0)
            {
                try
                {
                    RegistryView view = Environment.Is64BitOperatingSystem
                        ? RegistryView.Registry64
                        : RegistryView.Registry32;

                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = root.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform"))
                    {
                        if (k != null)
                        {
                            object v = k.GetValue("LicenseStatus");
                            if (v != null) status = Convert.ToInt32(v);
                        }
                    }
                }
                catch { }
            }

            bool activated = status == 1;
            bool grace = status == 2 || status == 3 || status == 6;
            bool unknown = status < 0;
            bool unlicensed = status == 0 || status == 4 || status == 5;

            string title;
            if (activated) title = "Windows 已激活";
            else if (grace) title = "Windows 已激活（许可证处于宽限期）";
            else if (unknown) title = "无法确认激活状态";
            else title = "Windows 未激活";

            return new IssueItem
            {
                Id = IssueIds.Activation,
                Title = title,
                Description = activated
                    ? "系统激活状态正常。"
                    : grace
                        ? "系统已激活，但许可证处于宽限期，通常联网后会自动恢复。不影响更新组件的修复。"
                        : unknown
                            ? "未能读取激活状态（WMI/注册表查询均失败），这不代表系统未激活。可运行 slmgr /xpr 手动确认。"
                            : "未激活可能影响部分更新，请先激活系统。",
                Detail = (activated || grace) ? null : ActivationStatusText(status),
                Severity = activated || grace
                    ? IssueSeverity.Ok
                    : IssueSeverity.Warning,
                // 只有“确实未授权/非正版”才需要处理；宽限期与读取失败都不进修复流程
                NeedsFix = unlicensed,
                CanAutoFix = false,
                ManualHint = unlicensed ? "请先激活系统，否则部分更新无法接收。" : null
            };
        }

        /// <summary>
        /// 通过 WMI/CIM 查询已激活许可证状态。返回 LicenseStatus（1=已激活），
        /// 查询失败或无匹配产品时返回 -1。
        /// </summary>
        private int QueryActivationStatus()
        {
            string script =
                "$p=Get-CimInstance SoftwareLicensingProduct " +
                "-Filter \"PartialProductKey IS NOT NULL AND ApplicationId='55c92734-d682-4d71-983e-d6ec3f16059f'\" " +
                "-ErrorAction SilentlyContinue | Select-Object -First 1;" +
                "if($p){Write-Output $p.LicenseStatus}else{Write-Output -1}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 30000);
            if (r.Code != 0) return -1;

            int status;
            if (int.TryParse((r.StdOut ?? "").Trim(), out status)) return status;
            return -1;
        }

        /// <summary>把 LicenseStatus 数字转成用户可读的说明（用于未激活时的 Detail）。</summary>
        private static string ActivationStatusText(int status)
        {
            switch (status)
            {
                case 0: return "许可证状态：未授权（LicenseStatus=0）";
                case 2: return "许可证状态：处于宽限期（LicenseStatus=2）";
                case 3: return "许可证状态：处于额外宽限（LicenseStatus=3）";
                case 4: return "许可证状态：非正版宽限期（LicenseStatus=4）";
                case 5: return "许可证状态：通知模式（LicenseStatus=5）";
                case 6: return "许可证状态：延长宽限期（LicenseStatus=6）";
                case -1: return "无法读取激活状态";
                default: return "许可证状态未知（LicenseStatus=" + status + "）";
            }
        }
    }
}
