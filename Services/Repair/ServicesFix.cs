// 更新服务启动类型修复：以 sc config 为权威来源设置启动类型。
using System;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 以 sc config 为权威来源设置更新相关服务的启动类型。
    ///
    /// 关于“服务不存在”的处理（与 SettingsAppFix 的 NOPKG 特判对齐）：
    /// 不同 Windows 版本 / 精简系统上，部分服务可能本就不存在
    /// （例如较新版本才引入的 WaaSMedicSvc、或某些镜像裁掉了 DoSvc）。
    /// 对不存在的服务执行 sc config 会返回非 0，若直接 ok &amp;= 会把
    /// “无需修复”误判为“修复失败”。因此这里先判断服务是否存在：
    ///   不存在 → 跳过，不计入成败；存在 → 照常设置并以结果累计。
    /// </summary>
    public class ServicesFix : IFixItem
    {
        public string Id { get { return IssueIds.Services; } }

        private readonly Action<string> _notify;

        public ServicesFix(Action<string> notify)
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
            bool ok = true;
            const string sdset = "D:(A;;CCLCSWRPLORC;;;AU)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPWPDTLOCRRC;;;SY)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

            // wuauserv：启动类型 + 安全描述符，二者须同时成功才算整体成功
            if (ServiceHelper.ServiceExists("wuauserv"))
            {
                ok &= ServiceHelper.SetStartType("wuauserv", "demand");
                ok &= ServiceHelper.SetSecurity("wuauserv", sdset);
            }

            SetStartTypeIfExists(ref ok, "bits", "demand");

            SetStartTypeIfExists(ref ok, "UsoSvc", "auto");

            SetStartTypeIfExists(ref ok, "DoSvc", "delayed-auto");

            SetStartTypeIfExists(ref ok, "CryptSvc", "auto");

            SetStartTypeIfExists(ref ok, "msiserver", "demand");

            SetStartTypeIfExists(ref ok, "TrustedInstaller", "demand");

            // WaaSMedicSvc 的 FailureActions 二进制必须沿用 reg add，Start 值一并走注册表。
            // 该服务在较旧版本 Windows 上可能不存在，故同样先判存在性。
            if (ServiceHelper.ServiceExists("WaaSMedicSvc"))
            {
                ok &= ServiceHelper.SetStartRegistry("WaaSMedicSvc", 3);
                ServiceHelper.SetFailureActionsBinary("WaaSMedicSvc",
                    "840300000000000000000000030000001400000001000000c0d4010001000000e09304000000000000000000");
            }

            return ok;
        }

        /// <summary>
        /// 服务存在时才设置启动类型；不存在则跳过（不计失败）。
        /// 供 Fix 内多处复用，避免重复写存在性判断。
        /// </summary>
        private static void SetStartTypeIfExists(ref bool ok, string service, string startType)
        {
            if (!ServiceHelper.ServiceExists(service)) return;
            ok &= ServiceHelper.SetStartType(service, startType);
        }
    }
}
