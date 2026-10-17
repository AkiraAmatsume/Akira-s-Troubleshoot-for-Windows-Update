// “设置”应用修复：重新注册 windows.immersivecontrolpanel 包。
using System;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 重新注册“设置”应用。
    ///
    /// 脚本内部不能无差别 catch 后静默继续，否则 powershell.exe 恒返回 0，
    /// 本方法就会恒返回 true、界面恒显示“已修复”。因此改为：
    ///   - 找到并成功注册的包数累加到 $ok
    ///   - 结束时输出 OK / FAIL / NOPKG，并据此设置退出码
    /// NOPKG：系统本就不含该组件（LTSB/LTSC、Server 等），无对象可修，
    ///        视为“无需修复”，不判失败。
    /// </summary>
    public class SettingsAppFix : IFixItem
    {
        public string Id { get { return IssueIds.SettingsApp; } }

        private readonly Action<string> _notify;

        public SettingsAppFix(Action<string> notify)
        {
            _notify = notify;
        }

        public string FollowUpHint
        {
            get
            {
                return "“设置”应用未能重新注册。可在 PowerShell 中手动执行：\n" +
                       "   Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers | " +
                       "ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register " +
                       "\"$($_.InstallLocation)\\AppXManifest.xml\" }";
            }
        }

        public bool Fix()
        {
            string script =
                "$ok=0;$fail=0;" +
                "$pk=@(Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers -ErrorAction SilentlyContinue);" +
                "if($pk.Count -eq 0){Write-Output 'NOPKG';exit 0}" +
                "else{foreach($p in $pk){try{Add-AppxPackage -DisableDevelopmentMode -Register " +
                "\"$($p.InstallLocation)\\AppXManifest.xml\" -ErrorAction Stop;$ok++}catch{$fail++}}}" +
                "if($ok -gt 0 -and $fail -eq 0){Write-Output 'OK';exit 0}else{Write-Output 'FAIL';exit 1}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 120000);

            string output = r.StdOut ?? "";
            if (output.IndexOf("NOPKG", StringComparison.OrdinalIgnoreCase) >= 0)
                return true; // 本系统不含该组件，无需修复

            bool ok = r.Code == 0 &&
                      output.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!ok)
            {
                if (_notify != null)
                    _notify("重新注册“设置”应用未成功完成（退出码 " + r.Code + "），可稍后在 PowerShell 中手动重试。");
            }
            return ok;
        }
    }
}
