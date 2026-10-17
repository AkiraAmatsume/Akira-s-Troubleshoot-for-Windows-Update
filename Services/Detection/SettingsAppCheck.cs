// “设置”应用注册状态检测。
using System;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测“设置”应用（windows.immersivecontrolpanel）的注册状态。
    ///
    /// 用 -AllUsers 查询系统级安装状态，与修复侧口径保持一致：若只查当前用户，
    /// 当前用户从未初始化“设置”应用时系统级已安装的包也查不到，会被误判为“需重新注册”。
    ///
    /// 不含该组件的 SKU（LTSB/LTSC、Server 等）判为 Ok，不产生始终无法消除的告警。
    /// </summary>
    public class SettingsAppCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查“设置”应用…"; } }

        public IssueItem Check()
        {
            // 输出约定：FOUND=系统中存在该包；NOTFOUND=系统不含该组件。
            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command " +
                "\"$p=@(Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers -ErrorAction SilentlyContinue);" +
                "if($p.Count -gt 0){Write-Output 'FOUND'}else{Write-Output 'NOTFOUND'}\"", 30000);

            // PowerShell 本身失败 → 无法确认，按“读不到”处理，不判为需修复
            if (r.Code != 0 || r.TimedOut)
            {
                return new IssueItem
                {
                    Id = IssueIds.SettingsApp,
                    Title = "“设置”应用状态无法确认",
                    Description = "查询 Appx 包失败，未能确认“设置”应用是否正常。",
                    Severity = IssueSeverity.Warning,
                    NeedsFix = false,
                    CanAutoFix = false
                };
            }

            string output = r.StdOut ?? "";
            bool found = output.IndexOf("FOUND", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!found)
            {
                // 该 SKU（LTSB/LTSC、Server 等）本就不包含“设置”应用，
                // 不应判为需要修复，否则会产生始终无法消除的告警。
                return new IssueItem
                {
                    Id = IssueIds.SettingsApp,
                    Title = "本系统不包含“设置”应用（无需处理）",
                    Description = "系统中未找到 windows.immersivecontrolpanel 包，" +
                                  "该版本 Windows（如 LTSB/LTSC 或 Server）不包含“设置”应用，属正常情况。",
                    Severity = IssueSeverity.Ok,
                    NeedsFix = false,
                    CanAutoFix = false
                };
            }

            return new IssueItem
            {
                Id = IssueIds.SettingsApp,
                Title = "“设置”应用状态正常",
                Description = "系统设置应用已正确注册。",
                Severity = IssueSeverity.Ok,
                NeedsFix = false,
                CanAutoFix = true
            };
        }
    }
}
