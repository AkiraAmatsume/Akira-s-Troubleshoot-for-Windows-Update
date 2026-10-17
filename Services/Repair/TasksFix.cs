// 计划任务修复：重新启用被禁用的更新相关任务。
using System;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 重新启用被禁用的、与 Windows Update 相关的计划任务。
    /// </summary>
    public class TasksFix : IFixItem
    {
        public string Id { get { return IssueIds.Tasks; } }

        private readonly Action<string> _notify;

        public TasksFix(Action<string> notify)
        {
            _notify = notify;
        }

        public string FollowUpHint
        {
            get
            {
                return "计划任务未能全部启用。可打开“任务计划程序”，" +
                       "检查 Microsoft\\Windows\\UpdateOrchestrator 等路径下的任务是否被禁用。";
            }
        }

        public bool Fix()
        {
            string script =
                "$paths=@('\\Microsoft\\Windows\\InstallService*','\\Microsoft\\Windows\\UpdateOrchestrator*'," +
                "'\\Microsoft\\Windows\\UpdateAssistant*','\\Microsoft\\Windows\\WaaSMedic*'," +
                "'\\Microsoft\\Windows\\WindowsUpdate*','\\Microsoft\\WindowsUpdate*');" +
                "foreach($p in $paths){Get-ScheduledTask -TaskPath $p -ErrorAction SilentlyContinue|" +
                "Enable-ScheduledTask -ErrorAction SilentlyContinue|Out-Null}";
            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 120000);
            if (r.Code != 0)
            {
                if (_notify != null) _notify("启用计划任务未成功完成（退出码 " + r.Code + "）");
            }
            return r.Code == 0;
        }
    }
}
