// WinSxS 组件恢复：从组件存储还原 Windows Update 关键文件。
using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsUpdateFixer.Services
{
    /// <summary>
    /// 单个文件的恢复结果。
    /// 区分“找不到源”与其他失败，是因为二者的后续建议不同：
    /// 找不到源（组件存储里也没有）时应建议用户运行
    /// DISM /Online /Cleanup-Image /RestoreHealth 与 sfc /scannow。
    /// </summary>
    public enum RestoreOutcome
    {
        /// <summary>文件已存在或恢复成功。</summary>
        Ok,

        /// <summary>WinSxS 中找不到该文件的源，无法恢复。</summary>
        SourceNotFound,

        /// <summary>找到源但复制/落地失败（权限、目标目录不可写等）。</summary>
        Failed
    }

    public static class WinSxSRestore
    {
        private static string Sys32
        {
            // 统一由 SystemCompat 提供真实 System32（本进程为 x86，
            // 直接拼 "System32" 会被 WOW64 重定向到 SysWOW64）。
            get { return SystemCompat.RawSystem32(); }
        }

        private static string WinSxS
        {
            get { return Path.Combine(SystemCompat.SystemRoot, "WinSxS"); }
        }

        /// <summary>
        /// 从 WinSxS 恢复单个文件到其“首选候选目录”。
        /// 例如 MoUsoCoreWorker.exe 在 Win10/11 上应位于 UUS\amd64\，
        /// 而不是 System32 —— 还原到错误目录会污染系统目录且检测仍判缺失。
        /// 1) 在候选目录中任一位置已存在则无需恢复；
        /// 2) 否则按 RequiredFile.RelativeDirs[0] 作为还原目标；
        /// 3) 查索引定位 WinSxS 源，robocopy /B 复制，icacls 设所有者。
        ///
        /// 返回值区分“找不到源”与“复制失败”，以便上层给出不同建议。
        /// </summary>
        public static RestoreOutcome RestoreFile(RequiredFile file)
        {
            // 任一候选目录已存在 → 认为完好，无需恢复
            if (UpdateFiles.ResolveExistingPath(file) != null) return RestoreOutcome.Ok;

            string root = UpdateFiles.SystemRoot;
            string targetDir = Path.Combine(root, file.RelativeDirs[0]);
            string target = Path.Combine(targetDir, file.FileName);

            string srcDir = LocateSourceDir(file.FileName);
            // 组件存储中也没有该文件：属于“源缺失”，需用户借助 DISM/SFC 修复
            if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir))
                return RestoreOutcome.SourceNotFound;

            try
            {
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            }
            catch
            {
                return RestoreOutcome.Failed;
            }

            ShellRunner.RunSilent("robocopy.exe",
                "\"" + srcDir + "\" \"" + targetDir + "\" " + file.FileName + " /B /R:1 /W:1", 120000);

            if (!File.Exists(target)) return RestoreOutcome.Failed;

            ShellRunner.RunSilent("icacls.exe",
                "\"" + target + "\" /setowner \"NT SERVICE\\TrustedInstaller\"", 15000);

            return RestoreOutcome.Ok;
        }

        /// <summary>
        /// WinSxS 文件索引：一次扫描，供本次修复流程内所有文件复用。
        ///
        /// 原实现对每个缺失文件各执行一次 dir /s /b 全盘扫描。
        /// 实测本机 WinSxS 约 3 万个目录，单次扫描 20–41 秒，
        /// 多个文件缺失时耗时成倍增长，且 41 秒已逼近 60 秒超时上限，
        /// 一旦超时即被当作“找不到源文件”而误判恢复失败。
        ///
        /// 现改为：首次使用时单次枚举 WinSxS，按文件名建立“文件名 → 候选目录列表”索引，
        /// 后续文件直接查表。索引仅在本次进程内有效，不额外占用长期内存。
        /// 调用 InvalidateIndex() 可在需要时强制重建。
        /// </summary>
        private static Dictionary<string, List<string>> _index;

        /// <summary>清空索引，下次定位时重新扫描。</summary>
        public static void InvalidateIndex()
        {
            _index = null;
        }

        /// <summary>
        /// 建立文件名索引。只枚举“目录 + 一层文件名”，不递归读取文件属性，
        /// 因此比 dir /s /b 快得多，且不受超时限制（纯 .NET 遍历）。
        /// </summary>
        private static Dictionary<string, List<string>> BuildIndex()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!Directory.Exists(WinSxS)) return map;

                foreach (var componentDir in Directory.EnumerateDirectories(WinSxS))
                {
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(componentDir); }
                    catch { continue; }   // 个别目录无权限时跳过，不影响整体

                    foreach (var file in files)
                    {
                        string name;
                        try { name = Path.GetFileName(file); }
                        catch { continue; }

                        List<string> list;
                        if (!map.TryGetValue(name, out list))
                        {
                            list = new List<string>();
                            map[name] = list;
                        }
                        list.Add(componentDir);
                    }
                }
            }
            catch
            {
                // 扫描失败时返回已收集到的部分，调用方按“找不到”处理
            }

            return map;
        }

        /// <summary>
        /// 在 WinSxS 中定位包含指定文件的源目录。
        /// 优先选择路径含 "amd64" 的目录；多个候选时取完整路径字典序最大者
        /// （WinSxS 目录名末尾带版本号，字典序大即较新版本）。
        /// 找不到返回 null。
        /// </summary>
        private static string LocateSourceDir(string fileName)
        {
            if (fileName == null) return null;

            // 首次调用时建立索引，后续文件直接复用
            var index = _index;
            if (index == null)
            {
                index = BuildIndex();
                _index = index;
            }

            List<string> dirs;
            if (!index.TryGetValue(fileName, out dirs) || dirs == null || dirs.Count == 0)
                return null;

            string best = null;
            bool bestIsAmd64 = false;

            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                bool isAmd64 = dir.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) >= 0;

                // amd64 优先；同为 amd64（或同非 amd64）时取路径字典序最大者
                bool better =
                    (isAmd64 && !bestIsAmd64) ||
                    (isAmd64 == bestIsAmd64 &&
                     (best == null || string.CompareOrdinal(dir, best) > 0));

                if (better)
                {
                    best = dir;
                    bestIsAmd64 = isAmd64;
                }
            }

            return best;
        }

        /// <summary>
        /// 恢复被改名为 &lt;baseName&gt;_BAK.dll 的文件。
        /// takeown → icacls grant → ren → setowner → remove。
        /// </summary>
        public static bool RestoreRenamedDll(string baseName)
        {
            string bak = Path.Combine(Sys32, baseName + "_BAK.dll");
            string target = Path.Combine(Sys32, baseName + ".dll");

            if (!File.Exists(bak)) return true; // 没有改名文件，视为正常

            ShellRunner.RunSilent("takeown.exe", "/f \"" + bak + "\"", 15000);
            ShellRunner.RunSilent("icacls.exe", "\"" + bak + "\" /grant *S-1-1-0:F", 15000);

            try
            {
                // 目标若已存在先移除，避免 ren 失败
                if (File.Exists(target)) File.Delete(target);
                File.Move(bak, target);
            }
            catch
            {
                return false;
            }

            ShellRunner.RunSilent("icacls.exe",
                "\"" + target + "\" /setowner \"NT SERVICE\\TrustedInstaller\"", 15000);
            ShellRunner.RunSilent("icacls.exe",
                "\"" + target + "\" /remove *S-1-1-0", 15000);

            return true;
        }

        /// <summary>检测改名文件是否存在。</summary>
        public static bool HasRenamedDll(string baseName)
        {
            return File.Exists(Path.Combine(Sys32, baseName + "_BAK.dll"));
        }
    }
}
