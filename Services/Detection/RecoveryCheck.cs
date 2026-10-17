// 服务自动恢复配置检测。
using System.Text;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测关键服务是否配置了失败自动恢复（sc failure）。
    /// 未配置时判为 Warning，可自动修复。
    /// </summary>
    public class RecoveryCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查服务自动恢复配置…"; } }

        public IssueItem Check()
        {
            string[] svcs = { "wuauserv", "bits", "UsoSvc", "DoSvc", "CryptSvc" };
            var detail = new StringBuilder();
            bool hasProblem = false;
            foreach (var s in svcs)
            {
                if (!ServiceHelper.HasFailureActions(s))
                {
                    detail.AppendLine(s + " 未配置失败恢复");
                    hasProblem = true;
                }
            }
            return new IssueItem
            {
                Id = IssueIds.Recovery,
                Title = hasProblem ? "服务自动恢复未配置" : "服务自动恢复已配置",
                Description = hasProblem
                    ? "部分服务失败后不会自动恢复，将配置为自动重启。"
                    : "关键服务失败后会自动重启。",
                Detail = hasProblem ? detail.ToString().Trim() : null,
                Severity = hasProblem ? IssueSeverity.Warning : IssueSeverity.Ok,
                NeedsFix = hasProblem,
                // 此处只表示“可修复”；是否真的执行由 NeedsFix 决定
                CanAutoFix = true
            };
        }
    }
}
