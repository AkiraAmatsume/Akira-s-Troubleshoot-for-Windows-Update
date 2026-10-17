// 更新服务状态检测。
using System.Text;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测关键更新服务的启动类型。
    ///
    /// 仅当 Start=4（禁用）判为异常；读取失败（StartValueUnknown）不再静默当作正常，
    /// 而是单独列出并提示无法判定，避免非英文系统上“服务被禁用却报告状态正常”的结论错误。
    /// </summary>
    public class ServicesCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查更新服务状态…"; } }

        public IssueItem Check()
        {
            string[] svcs = { "wuauserv", "BITS", "UsoSvc", "DoSvc", "CryptSvc", "TrustedInstaller", "WaaSMedicSvc", "msiserver" };
            var detail = new StringBuilder();
            bool hasProblem = false;
            var unreadable = new System.Collections.Generic.List<string>();

            foreach (var s in svcs)
            {
                int v = ServiceHelper.GetStartValue(s);
                if (v == ServiceHelper.StartValueUnknown)
                {
                    unreadable.Add(s);
                    continue;
                }
                if (v == 4)
                {
                    detail.AppendLine(s + " 被禁用");
                    hasProblem = true;
                }
            }

            // 有服务无法读取：不能断言“状态正常”，但也不可自动修复（无依据），
            // 因此判为 Warning + 需人工确认、不可自动修复。
            if (unreadable.Count > 0)
            {
                detail.AppendLine("无法读取启动类型（可能不存在或无权限）：" + string.Join("、", unreadable.ToArray()));
            }

            bool unknown = unreadable.Count > 0;

            return new IssueItem
            {
                Id = IssueIds.Services,
                Title = hasProblem ? "部分更新服务被禁用"
                      : unknown ? "部分更新服务状态无法确认"
                      : "更新服务状态正常",
                Description = hasProblem
                    ? "部分关键服务已被禁用，将恢复为系统默认值。"
                    : unknown
                        ? "无法读取以下服务的启动类型，请确认这些服务在本系统中是否存在。"
                        : "Windows Update、BITS、UsoSvc 等服务启动类型正常。",
                // 仅在“确有禁用”时展示明细，避免无问题时也显示一串读不到的项
                Detail = (hasProblem || unknown) ? detail.ToString().Trim() : null,
                Severity = hasProblem ? IssueSeverity.Problem
                         : unknown ? IssueSeverity.Warning
                         : IssueSeverity.Ok,
                NeedsFix = hasProblem,
                CanAutoFix = hasProblem
            };
        }
    }
}
