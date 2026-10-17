// 注册表策略备份：在修复前导出与 Windows Update / Explorer 相关的策略键。
using System;
using System.IO;

namespace WindowsUpdateFixer.Services
{
    /// <summary>
    /// 策略项备份。注意：名称虽为 RegistryBackup，实际仅备份 4 个与 Windows Update /
    /// Explorer 相关的“策略”键（并非整机注册表）。如需扩展范围请同步更新下面的 entries。
    /// </summary>
    public static class RegistryBackup
    {
        private struct Entry
        {
            public string Key;
            public string File;
            public Entry(string key, string file) { Key = key; File = file; }
        }

        /// <summary>
        /// 备份 4 个注册表键到 桌面\WU_Restore_Backup_时间戳\。
        /// 返回备份目录路径；目录创建失败、或一个 .reg 都没导出成功时返回 null。
        /// 部分成功时仍返回目录（调用方据此可提示“部分备份”），
        /// 但至少要求成功导出一个文件，避免“空目录也算备份成功”。
        /// </summary>
        public static string RunBackup()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            // 前缀取自 BackupRestore，避免两处字面量不一致导致“备份生成了却找不到”
            string dir = Path.Combine(desktop,
                BackupRestore.BackupDirPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                return null;
            }

            var entries = new[]
            {
                new Entry(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "HKLM_WindowsUpdate_Policy.reg"),
                new Entry(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "HKCU_WindowsUpdate_Policy.reg"),
                new Entry(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "HKLM_Explorer_Policy.reg"),
                new Entry(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "HKCU_Explorer_Policy.reg"),
            };

            int success = 0;
            foreach (var e in entries)
            {
                string dest = Path.Combine(dir, e.File);
                var r = ShellRunner.Run("reg.exe", "export \"" + e.Key + "\" \"" + dest + "\" /y", 15000);

                // reg export 对“键不存在”会返回非 0，此时不会生成文件——
                // 这属于正常情况（该策略键本就不存在），不应算作备份失败，
                // 但也不能计入成功数。
                if (r.Code == 0 && File.Exists(dest) && new FileInfo(dest).Length > 0)
                    success++;
            }

            if (success == 0)
            {
                // 一个文件都没导出成功：目录里没有任何可恢复内容，
                // 不能让调用方误以为“已备份”。清理这个空目录后返回 null。
                try { Directory.Delete(dir, true); } catch { }
                return null;
            }

            return dir;
        }
    }
}
