// 系统版本与兼容性判断。
//
// 独立成模块的原因：
//   1) 部分检测项依赖 Windows 版本（例如更新组件位置：
//      Win10/11 主流位于 C:\Windows\UUS\<arch>\，旧版位于 System32）。
//   2) 注册表 ProductName 不可靠：Windows 11 上仍写作 "Windows 10"，
//      因此版本判断必须以构建号为准。
//   3) 本程序为 x86 进程，部分 API/路径受 WOW64 影响，相关判断集中在此。
using System;
using Microsoft.Win32;

namespace WindowsUpdateFixer.Services
{
    /// <summary>系统版本族。仅区分本工具关心的几类。</summary>
    public enum WindowsFamily
    {
        Unknown = 0,
        Windows7,
        Windows8,      // 含 8.1
        Windows10,
        Windows11,
        WindowsServer  // Server 2016 及以上
    }

    /// <summary>
    /// 采集到的系统信息，供兼容性判断与检测项使用。
    /// </summary>
    public sealed class SystemInfo
    {
        public WindowsFamily Family { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Build { get; set; }
        public int Ubr { get; set; }

        /// <summary>注册表 ProductName 原文（可能写着 Windows 10，即使实际是 11）。</summary>
        public string ProductName { get; set; }

        /// <summary>EditionID，如 Professional / Enterprise / EnterpriseS / ServerStandard。</summary>
        public string EditionId { get; set; }

        /// <summary>InstallationType：Client 或 Server。</summary>
        public string InstallationType { get; set; }

        /// <summary>DisplayVersion，如 22H2 / 26H2（旧版本可能为空）。</summary>
        public string DisplayVersion { get; set; }

        public bool Is64BitOs { get; set; }

        /// <summary>是否为 LTSB/LTSC（长期服务版）。</summary>
        public bool IsLtsc { get; set; }

        /// <summary>组装的完整版本串，如 "10.0.26300.9711"。</summary>
        public string FullVersion
        {
            get
            {
                string v = Major + "." + Minor + "." + Build;
                if (Ubr > 0) v += "." + Ubr;
                return v;
            }
        }
    }

    public static class SystemCompat
    {
        /// <summary>
        /// Windows 11 的最低构建号（含 21H2）。
        /// 22000 为 Win11 首发，Win10 的最高构建为 19045。
        /// </summary>
        private const int Windows11MinBuild = 22000;

        /// <summary>Windows 10 的最低构建号（1507 为 10240）。</summary>
        private const int Windows10MinBuild = 10240;

        /// <summary>
        /// 系统信息缓存。
        /// 版本与路径探测只在此处实现，DiagnosticService / UpdateFiles 均通过这里取值，
        /// 避免重复读取注册表与判断口径不一致。
        /// 系统版本在一次程序运行内不会变化，故缓存安全。
        /// </summary>
        private static SystemInfo _cached;

        /// <summary>获取系统信息（带缓存）。失败时返回尽量完整的对象，不抛异常。</summary>
        public static SystemInfo Current
        {
            get
            {
                if (_cached == null) _cached = GetSystemInfo();
                return _cached;
            }
        }

        /// <summary>采集系统信息。失败时返回尽量完整的对象，不抛异常。</summary>
        public static SystemInfo GetSystemInfo()
        {
            var info = new SystemInfo
            {
                // 仅作为“注册表读不到时”的兜底。


                // 注意：本程序为 x86 进程，Environment.OSVersion 实际返回
                // 6.2.9200（Windows 8 的兼容值），与真实版本无关，
                // 因此不能作为主依据。真实版本以注册表 CurrentBuild 为准。
                Major = Environment.OSVersion.Version.Major,
                Minor = Environment.OSVersion.Version.Minor,
                Build = Environment.OSVersion.Version.Build,
                Is64BitOs = Environment.Is64BitOperatingSystem
            };

            ReadRegistry(info);

            info.Family = DetectFamily(info);
            info.IsLtsc = IsLtscEdition(info.EditionId);

            return info;
        }

        /// <summary>
        /// 本机真实构建号。
        /// 不可用 Environment.OSVersion（x86 进程返回 6.2.9200 兼容值）。
        /// 不要用 RegistryView.Default（x86 进程会被重定向到 WOW6432Node）。
        /// </summary>
        public static int RealBuildNumber()
        {
            return Current.Build;
        }

        /// <summary>是否为 Windows 11 及以上。</summary>
        public static bool IsWindows11OrLater
        {
            get { return Current.Build >= Windows11MinBuild; }
        }

        /// <summary>
        /// 系统根目录（%SystemRoot%）。
        /// 正常情况下该环境变量一定存在；但若为 null，后续所有 Path.Combine
        /// 都会抛 ArgumentNullException 且难以定位原因，故回退到 Windows 文件夹。
        /// </summary>
        public static string SystemRoot
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("SystemRoot");
                if (!string.IsNullOrEmpty(root)) return root;

                root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(root)) return root;

                // 最后兜底：按盘符惯例拼接（仅在极端环境下才会走到）
                return "C:\\Windows";
            }
        }

        /// <summary>
        /// 真实 System32 目录（不受 WOW64 重定向影响）。
        /// 本项目为 x86 进程，直接拼 "System32" 会被重定向到 SysWOW64；
        /// 64 位系统上取 SysWOW64 的父目录才是真正的 System32，
        /// 32 位系统上没有重定向，直接用 System32。
        /// </summary>
        public static string RawSystem32()
        {
            string root = SystemRoot;
            if (!Environment.Is64BitOperatingSystem)
                return System.IO.Path.Combine(root, "System32");

            string sysWow64 = System.IO.Path.Combine(root, "SysWOW64");
            string parent = System.IO.Path.GetDirectoryName(sysWow64);
            return System.IO.Path.Combine(string.IsNullOrEmpty(parent) ? root : parent, "System32");
        }

        /// <summary>本机原生架构子目录名：64 位用 amd64，32 位用 x86。</summary>
        public static string NativeArchFolder
        {
            get { return Environment.Is64BitOperatingSystem ? "amd64" : "x86"; }
        }

        private static void ReadRegistry(SystemInfo info)
        {
            try
            {
                // 必须显式使用 Registry64：
                // 本程序为 x86 进程，RegistryView.Default 会被重定向到
                // SOFTWARE\WOW6432Node，实测其中 ProductName/EditionID 与真实值不同
                // （例如真实为 Windows 10 Pro，32 位视图读到 Windows 10 Enterprise）。
                // 构建号在两个视图中一致，但产品名称会误导用户，故统一读 64 位视图。
                RegistryView view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Registry32;

                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = baseKey.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return;

                    info.ProductName = key.GetValue("ProductName") as string;
                    info.EditionId = key.GetValue("EditionID") as string;
                    info.InstallationType = key.GetValue("InstallationType") as string;
                    info.DisplayVersion = key.GetValue("DisplayVersion") as string;

                    // 以注册表 CurrentBuild 为准（Environment.OSVersion 在 x86 进程中
                    // 可能返回 6.2.9200 这类兼容值，不可信）
                    object cb = key.GetValue("CurrentBuild");
                    string cbStr = cb == null ? null : cb.ToString();
                    int build;
                    if (cbStr != null && int.TryParse(cbStr, out build) && build > 0)
                    {
                        info.Build = build;
                        if (build >= 10000) info.Major = 10;   // 构建号已进入 10.x 时代
                    }

                    object ubr = key.GetValue("UBR");
                    int u;
                    if (ubr != null && int.TryParse(ubr.ToString(), out u) && u > 0)
                        info.Ubr = u;
                }
            }
            catch
            {
                // 读取失败时保留 Environment 提供的信息
            }
        }

        /// <summary>
        /// 判定系统族。
        /// 关键：不能用 ProductName 判断 —— Windows 11 上它写作 "Windows 10"。
        /// 只能依据构建号与 InstallationType。
        /// </summary>
        private static WindowsFamily DetectFamily(SystemInfo info)
        {
            bool isServer = string.Equals(info.InstallationType, "Server",
                                          StringComparison.OrdinalIgnoreCase);

            if (isServer)
                return WindowsFamily.WindowsServer;

            if (info.Build >= Windows11MinBuild) return WindowsFamily.Windows11;
            if (info.Build >= Windows10MinBuild)
            {
                // 构建号 >= 10240 但 < 22000：Windows 10（含 2015–2022 各版本）
                return WindowsFamily.Windows10;
            }

            if (info.Build >= 9200) return WindowsFamily.Windows8;   // 8.1 为 9600
            if (info.Build >= 7600) return WindowsFamily.Windows7;

            return WindowsFamily.Unknown;
        }

        private static bool IsLtscEdition(string editionId)
        {
            if (string.IsNullOrEmpty(editionId)) return false;

            // EnterpriseS = LTSB/LTSC；IoTEnterpriseS = IoT LTSC
            return editionId.IndexOf("EnterpriseS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>系统族的可读名称。</summary>
        public static string FamilyName(WindowsFamily family)
        {
            switch (family)
            {
                case WindowsFamily.Windows7: return "Windows 7";
                case WindowsFamily.Windows8: return "Windows 8/8.1";
                case WindowsFamily.Windows10: return "Windows 10";
                case WindowsFamily.Windows11: return "Windows 11";
                case WindowsFamily.WindowsServer: return "Windows Server";
                default: return "未知系统";
            }
        }

        /// <summary>
        /// 本工具是否支持该系统。
        /// 支持范围：Windows 10 / 11。Server 与更早版本不支持——
        /// 因为其更新组件布局与服务集合与本工具假设不同，强行修复有风险。
        /// </summary>
        public static bool IsSupported(SystemInfo info)
        {
            return info.Family == WindowsFamily.Windows10 ||
                   info.Family == WindowsFamily.Windows11;
        }

        /// <summary>
        /// 系统信息的一行摘要，用于界面展示。
        /// 形如：Windows 11（10.0.26300.9711）· 64 位 · Pro
        /// </summary>
        public static string Describe(SystemInfo info)
        {
            string name = FamilyName(info.Family);
            string arch = info.Is64BitOs ? "64 位" : "32 位";

            string edition = info.EditionId;
            if (info.IsLtsc) edition = string.IsNullOrEmpty(edition) ? "LTSC" : edition + "（LTSC）";

            string s = name + "（" + info.FullVersion + "）· " + arch;
            if (!string.IsNullOrEmpty(edition)) s += " · " + edition;
            if (!string.IsNullOrEmpty(info.DisplayVersion)) s += " · " + info.DisplayVersion;
            return s;
        }
    }
}
