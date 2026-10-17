// 服务自动恢复配置修复：为关键服务配置 sc failure。
using System;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 为关键服务配置失败自动恢复（sc failure）。
    /// 跳过本机不存在的服务：对不存在的服务执行 sc failure 必然返回非 0，
    /// 不处理会把“无需配置”误判成“修复失败”。
    /// </summary>
    public class RecoveryFix : IFixItem
    {
        public string Id { get { return IssueIds.Recovery; } }

        private readonly Action<string> _notify;

        public RecoveryFix(Action<string> notify)
        {
            _notify = notify;
        }

        public string FollowUpHint
        {
            get
            {
                return "服务配置未能完全恢复。请确认相关服务未被安全软件拦截，" +
                       "重启后可重新运行本工具再试。";
            }
        }

        public bool Fix()
        {
            string[] svcs = { "wuauserv", "bits", "UsoSvc", "DoSvc", "CryptSvc" };
            bool ok = true;
            foreach (var s in svcs)
            {
                if (!ServiceHelper.ServiceExists(s)) continue;
                ok &= ServiceHelper.SetFailureActions(s);
            }
            return ok;
        }
    }
}
