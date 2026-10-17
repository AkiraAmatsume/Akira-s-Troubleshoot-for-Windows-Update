// 服务控制封装：读取/设置服务启动类型、状态与失败恢复配置。
using System;

namespace WindowsUpdateFixer.Services
{
    public static class ServiceHelper
    {
        private const string SvcKeyRoot = @"HKLM\SYSTEM\CurrentControlSet\Services\";

        /// <summary>sc config &lt;name&gt; start= &lt;demand|auto|delayed-auto|disabled&gt;</summary>
        public static bool SetStartType(string service, string startType)
        {
            return ShellRunner.RunSilent("sc.exe", "config " + service + " start= " + startType, 15000);
        }

        /// <summary>写注册表 Start 值。</summary>
        public static bool SetStartRegistry(string service, int startValue)
        {
            return ShellRunner.RunSilent("reg.exe",
                "add \"" + SvcKeyRoot + service + "\" /v Start /t REG_DWORD /d " + startValue + " /f", 15000);
        }

        /// <summary>sc sdset &lt;service&gt; &lt;sddl&gt;</summary>
        public static bool SetSecurity(string service, string sddl)
        {
            return ShellRunner.RunSilent("sc.exe", "sdset " + service + " \"" + sddl + "\"", 15000);
        }

        /// <summary>sc failure &lt;service&gt; reset= 86400 actions= restart/60000/restart/60000/restart/60000</summary>
        public static bool SetFailureActions(string service)
        {
            return ShellRunner.RunSilent("sc.exe",
                "failure " + service + " reset= 86400 actions= restart/60000/restart/60000/restart/60000", 15000);
        }

        /// <summary>写 WaaSMedicSvc 的 FailureActions 二进制值（必须沿用 reg add REG_BINARY）。</summary>
        public static bool SetFailureActionsBinary(string service, string hex)
        {
            return ShellRunner.RunSilent("reg.exe",
                "add \"" + SvcKeyRoot + service + "\" /v FailureActions /t REG_BINARY /d " + hex + " /f", 15000);
        }

        /// <summary>
        /// 确保服务处于“运行中”状态。
        ///
        /// 不能用 net start 的退出码判定成败：对已在运行的服务，
        /// net start 返回 2（"服务已启动"，NET HELPMSG 2182），并非 0。
        /// 原有实现直接 ok &amp;= Start(...) 会把正常状态误判为失败
        /// （FixReset 末尾启动 5 个服务，其中 cryptsvc/dosvc/usosvc 常驻运行）。
        ///
        /// 因此改为：先查状态，已运行直接成功；否则启动后复查状态。
        /// </summary>
        public static bool Start(string service)
        {
            int state = GetState(service);

            // 已在运行：无需操作即视为成功
            if (state == ServiceStateRunning) return true;

            ShellRunner.RunSilent("net.exe", "start " + service, 60000);

            // 以“最终是否处于运行中”为判据，而非 net.exe 的退出码
            return GetState(service) == ServiceStateRunning;
        }

        /// <summary>
        /// 确保服务处于“已停止”状态。
        /// 与 Start 对称：net stop 对已停止的服务同样返回非 0，
        /// 因此成功后复查状态，而不是依赖退出码。
        /// </summary>
        public static bool Stop(string service)
        {
            int state = GetState(service);

            if (state == ServiceStateStopped) return true;

            // 自动应答 Y（net stop 对存在依赖的服务会询问是否停止依赖）
            ShellRunner.RunCmd("echo Y| net stop " + service, 60000);

            return GetState(service) == ServiceStateStopped;
        }

        // ---- 服务状态 ----

        public const int ServiceStateStopped = 1;
        public const int ServiceStateStartPending = 2;
        public const int ServiceStateStopPending = 3;
        public const int ServiceStateRunning = 4;

        /// <summary>
        /// 读取服务当前状态（SERVICE_STATUS.dwCurrentState）。
        /// 先看注册表中的控制标志不可靠，统一用 sc query 的数值输出，
        /// 避免解析本地化文本。读取失败返回 -1。
        /// </summary>
        public static int GetState(string service)
        {
            try
            {
                // sc.exe 的输出文本随系统语言变化，但 STATE 行的状态值固定为数字，
                // 形如：STATE  : 4  RUNNING
                var r = ShellRunner.Run("sc.exe", "query \"" + service + "\"", 15000);
                if (r.Code != 0 || string.IsNullOrEmpty(r.StdOut)) return -1;

                foreach (var line in r.StdOut.Split(new[] { '\r', '\n' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (!t.StartsWith("STATE", StringComparison.OrdinalIgnoreCase)) continue;

                    // 取 STATE 之后的第一个整数
                    var m = System.Text.RegularExpressions.Regex.Match(t, @"\d+");
                    int v;
                    if (m.Success && int.TryParse(m.Value, out v)) return v;
                }
            }
            catch { }

            return -1;
        }

        /// <summary>
        /// 判断本机是否安装了指定服务。
        ///
        /// 判据与 <see cref="GetStartValue"/> 保持一致：服务注册表键存在且含 Start 值。
        /// 用途：修复时对“本机本就不存在的服务”跳过，而不是执行 sc config 后
        /// 因返回非 0 把“无需修复”误判成“修复失败”——与 FixSettingsApp 中
        /// NOPKG（系统不含该组件）的特判逻辑对齐。
        /// </summary>
        public static bool ServiceExists(string service)
        {
            return GetStartValue(service) != StartValueUnknown;
        }

        /// <summary>服务“读取失败/无法判定”的哨兵值，与 0–4 的真实启动类型区分开。</summary>
        public const int StartValueUnknown = -1;

        /// <summary>
        /// 读取服务注册表 Start 值（0=boot 1=system 2=auto 3=demand 4=disabled）。
        /// 直接读注册表而非解析 reg.exe 输出，避免非英文系统下文本格式不同导致解析失败。
        /// 显式使用 64 位视图：本进程为 x86，RegistryView.Default 会被重定向到
        /// WOW6432Node，那里没有系统服务的配置项。
        /// 读取失败（服务不存在 / 无权限 / 值缺失）返回 <see cref="StartValueUnknown"/>。
        /// </summary>
        public static int GetStartValue(string service)
        {
            try
            {
                using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                           Microsoft.Win32.RegistryHive.LocalMachine, NativeView))
                using (var key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service))
                {
                    if (key == null) return StartValueUnknown;

                    object raw = key.GetValue("Start");
                    if (raw == null) return StartValueUnknown;

                    return Convert.ToInt32(raw);
                }
            }
            catch
            {
                return StartValueUnknown;
            }
        }

        /// <summary>
        /// 本机服务的真实注册表视图。
        /// x86 进程使用 RegistryView.Default 会读到 SOFTWARE\WOW6432Node，
        /// 而服务配置位于 64 位视图的 SYSTEM 分支下，因此必须显式指定。
        /// </summary>
        private static Microsoft.Win32.RegistryView NativeView
        {
            get
            {
                return Environment.Is64BitOperatingSystem
                    ? Microsoft.Win32.RegistryView.Registry64
                    : Microsoft.Win32.RegistryView.Registry32;
            }
        }

        /// <summary>
        /// 检查服务的 FailureActions 是否存在且“确实配置了动作”。用于 recovery 检测。
        ///
        /// 直接读注册表 REG_BINARY（不解析 reg query 文本，避免本地化/格式差异），
        /// 并按字节判断，而不是对十六进制字符串做 Replace("0","") ——
        /// 后者会把表示“无动作”的大量 00 字节误判为空，导致误报“未配置恢复”。
        ///
        /// SERVICE_FAILURE_ACTIONS 结构（前 16 字节）：
        ///   DWORD ResetPeriod; LPWSTR RebootMsg(指针); DWORD Command(指针);
        ///   DWORD ActionsCount; SC_ACTION Actions[];
        /// 关键判据：ActionsCount（偏移 12）> 0，说明确实配置了恢复动作。
        /// </summary>
        public static bool HasFailureActions(string service)
        {
            try
            {
                using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                           Microsoft.Win32.RegistryHive.LocalMachine, NativeView))
                using (var key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service))
                {
                    if (key == null) return false;

                    var raw = key.GetValue("FailureActions") as byte[];
                    if (raw == null || raw.Length < 16) return false;

                    // 偏移 12 处为 ActionsCount（小端 DWORD）
                    uint actionsCount = BitConverter.ToUInt32(raw, 12);
                    if (actionsCount == 0) return false;

                    // Actions 数组起始于偏移 16，每项 8 字节（SC_ACTION: type + delay）。
                    // 至少要能容纳声明数量的动作项，才算结构完整。
                    return raw.Length >= 16 + actionsCount * 8;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
