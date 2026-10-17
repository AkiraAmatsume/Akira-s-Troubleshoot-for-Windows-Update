// 读取本次操作前生成的“注册表策略备份”(.reg) 并恢复。
using System;
using System.IO;

namespace WindowsUpdateFixer.Services
{
    public enum RestoreResult
    {
        NoBackup,   // 没有可用的备份目录
        Success,    // 全部恢复成功
        Partial,    // 部分恢复成功
        Failed      // 恢复失败
    }

    /// <summary>
    /// 策略备份恢复。注意：恢复时无差别 import 备份目录下所有 *.reg
    /// （备份由 RegistryBackup 生成，通常仅含 4 个策略键）。
    /// </summary>
    public static class BackupRestore
    {
        /// <summary>
        /// 备份目录是否存在且可恢复。
        /// 判据与 Restore() 保持一致：不仅目录存在，还至少要有一个 .reg 文件。
        /// 否则会出现“显示恢复按钮、点下去必然失败”的情况。
        /// 若当前会话未记录备份路径，会先尝试从桌面定位历史备份。
        /// </summary>
        public static bool HasBackup()
        {
            string dir = ResolveBackupDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            try
            {
                return Directory.GetFiles(dir, "*.reg").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 确定可用的备份目录。
        ///
        /// AppState.BackupPath 只在内存中有效，程序退出即丢失；
        /// 而备份的典型用途恰恰是“事后发现被改坏，重新打开程序再恢复”。
        /// 因此当内存路径不可用时，回到桌面查找 WU_Restore_Backup_* 目录，
        /// 取时间戳最新且确实含有 .reg 文件的一个。
        /// </summary>
        public static string ResolveBackupDir()
        {
            // 优先使用当前会话刚生成的路径
            string dir = AppState.BackupPath;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && HasRegFiles(dir))
                return dir;

            return FindLatestBackupOnDisk();
        }

        /// <summary>
        /// 备份目录名前缀。
        /// 由 RegistryBackup.RunBackup 生成目录时引用，本类查找时也引用，
        /// 确保命名规则只有一处定义——否则改一处漏一处会让查找静默失效。
        /// </summary>
        public const string BackupDirPrefix = "WU_Restore_Backup_";

        private static bool HasRegFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*.reg").Length > 0; }
            catch { return false; }
        }

        /// <summary>
        /// 在桌面查找最新的、可用的备份目录；找不到返回 null。
        /// 目录名形如 WU_Restore_Backup_yyyyMMdd_HHmmss，按名称排序即按时间排序。
        /// </summary>
        public static string FindLatestBackupOnDisk()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return null;

                string best = null;
                foreach (var d in Directory.GetDirectories(desktop, BackupDirPrefix + "*"))
                {
                    if (!HasRegFiles(d)) continue;

                    // 名称末尾为时间戳，字典序比较即时间先后（同格式定长）
                    if (best == null || string.CompareOrdinal(
                            Path.GetFileName(d), Path.GetFileName(best)) > 0)
                        best = d;
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将备份目录内的 .reg 文件逐个 reg import 回注册表。
        /// 返回恢复结果，供界面提示。
        /// </summary>
        public static RestoreResult Restore()
        {
            string dir = ResolveBackupDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return RestoreResult.NoBackup;

            int total = 0, ok = 0;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.reg"))
                {
                    total++;
                    try
                    {
                        var r = ShellRunner.Run("reg.exe",
                            "import \"" + file + "\"", 15000);
                        if (r.Code == 0) ok++;
                    }
                    catch
                    {
                        // 单个文件失败不影响其它文件，继续尝试
                    }
                }
            }
            catch
            {
                return RestoreResult.Failed;
            }

            if (total == 0) return RestoreResult.Failed;
            if (ok == total) return RestoreResult.Success;
            return ok == 0 ? RestoreResult.Failed : RestoreResult.Partial;
        }
    }
}
