
// Windows Update 关键文件清单（检测与修复共用，避免两处不一致）。
//
// 职责边界：本文件只负责“有哪些文件、可以在哪些目录查找”，
// 不自行探测系统版本与特殊路径——那些统一由 SystemCompat 提供，
// 确保版本判断只有一处实现。
using System;
using System.IO;

namespace WindowsUpdateFixer.Services
{
    /// <summary>
    /// 一个“关键文件”的定义：文件名 + 允许出现的相对目录（相对 %SystemRoot%）。
    /// Windows 更新组件的存放位置随版本变化：
    ///   - Windows 10/11 较新版本：C:\Windows\UUS\amd64\（或 UUS\x86\）
    ///   - 旧版本：C:\Windows\System32\
    /// 因此不能用单一目录判定“缺失”，必须逐目录查找。
    /// </summary>
    public sealed class RequiredFile
    {
        public RequiredFile(string fileName, params string[] relativeDirs)
        {
            FileName = fileName;
            RelativeDirs = relativeDirs;
        }

        /// <summary>文件名（含扩展名）。</summary>
        public string FileName { get; private set; }

        /// <summary>允许的候选目录（相对 %SystemRoot%），按优先级排列。</summary>
        public string[] RelativeDirs { get; private set; }
    }

    public static class UpdateFiles
    {
        // 说明：SystemRoot / RawSystem32 / NativeArchFolder / RealBuildNumber
        // 均由 SystemCompat 提供，此处仅作转发以便调用点可读，
        // 不重复实现任何探测逻辑。

        /// <summary>系统根目录（%SystemRoot%）。</summary>
        public static string SystemRoot
        {
            get { return SystemCompat.SystemRoot; }
        }

        /// <summary>真实 System32 目录（不受 WOW64 重定向影响）。</summary>
        public static string RawSystem32()
        {
            return SystemCompat.RawSystem32();
        }

        /// <summary>本机更新组件架构子目录名：64 位用 amd64，32 位用 x86。</summary>
        public static string NativeArchFolder
        {
            get { return SystemCompat.NativeArchFolder; }
        }

        /// <summary>本机真实构建号（委托 SystemCompat，避免重复读注册表）。</summary>
        public static int RealBuildNumber()
        {
            return SystemCompat.RealBuildNumber();
        }

        /// <summary>是否为 Windows 11 及以上（构建号 >= 22000）。</summary>
        private static bool IsWindows11OrLater
        {
            get { return SystemCompat.IsWindows11OrLater; }
        }

        /// <summary>
        /// 应存在于系统中的更新组件文件。
        /// 每个文件给出按优先级排列的候选目录；任一目录存在即视为“存在”。
        ///
        /// 设计说明：
        ///   - 更新组件在不同 Windows 版本中位置不同：Win10/11 较新版本位于
        ///     C:\Windows\UUS\amd64\，旧版本位于 C:\Windows\System32\。
        ///     因此不能用单一目录判定缺失，改为多候选目录。
        ///   - MusNotification.exe / MusNotificationUx.exe 已在新版 Windows 中移除，
        ///     不再校验，以免造成永久误报。
        ///
        /// 候选顺序按系统版本决定：新版系统优先 UUS，旧版优先 System32。
        /// 两个候选始终都查，因此即便版本判断有偏差，也不会误报缺失。
        /// </summary>
        public static RequiredFile[] RequiredFiles
        {
            get
            {
                string uus = Path.Combine("UUS", NativeArchFolder);
                string sys32 = RawSystem32();

                // 优先顺序与系统主流布局一致：Win10/11 用 UUS，更早的系统用 System32
                bool preferUus = IsWindows11OrLater;
                string first = preferUus ? uus : sys32;
                string second = preferUus ? sys32 : uus;

                return new[]
                {
                    new RequiredFile("MoUsoCoreWorker.exe",  first, second),
                    new RequiredFile("usosvcimpl.dll",       first, second),
                    new RequiredFile("UIEOrchestrator.exe",  first, second),
                    new RequiredFile("WaaSMedicSvcImpl.dll", first, second),
                    new RequiredFile("WaaSMedicAgent.exe",   first, second),
                    new RequiredFile("WaaSMedicCapsule.dll", first, second),
                    new RequiredFile("storewuauthcore.dll",  first, second)
                };
            }
        }

        /// <summary>
        /// 描述本机更新组件的预期目录，用于兼容性检测项的明细展示。
        /// 形如："C:\Windows\UUS\amd64（首选）、C:\Windows\System32（兼容）"
        /// </summary>
        public static string ComponentDirHint()
        {
            string uusFull = Path.Combine(SystemRoot, Path.Combine("UUS", NativeArchFolder));
            string sys32 = RawSystem32();

            bool uusExists = Directory.Exists(uusFull);
            bool preferUus = IsWindows11OrLater;

            string primary = preferUus ? uusFull : sys32;
            string secondary = preferUus ? sys32 : uusFull;

            string s = primary + "（首选）";
            s += "、" + secondary + "（兼容）";
            if (!uusExists) s += "；未发现 UUS 目录，按旧版布局（System32）判定";
            return s;
        }

        /// <summary>被改名为 &lt;name&gt;_BAK.dll 时需要还原的基名（不含扩展名）。</summary>
        public static readonly string[] RenamedDllBaseNames = { "WaaSMedicSvc", "wuaueng" };

        /// <summary>
        /// 在候选目录中查找文件，返回第一个存在的完整路径；都不存在返回 null。
        /// </summary>
        public static string ResolveExistingPath(RequiredFile file)
        {
            string root = SystemRoot;
            foreach (var rel in file.RelativeDirs)
            {
                string full = Path.Combine(root, rel, file.FileName);
                if (File.Exists(full)) return full;
            }
            return null;
        }
    }
}
