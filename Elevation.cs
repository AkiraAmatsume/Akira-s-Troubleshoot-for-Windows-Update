using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace WindowsUpdateFixer
{
    /// <summary>
    /// 按需提权辅助。程序以 asInvoker 方式启动；
    /// 当用户点击"开始检测/修复"时，若当前非管理员，则以 runas 重启自身。
    /// </summary>
    public static class Elevation
    {
        /// <summary>当前进程是否以管理员身份运行。</summary>
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 以管理员身份重新启动自身，并退出当前实例。
        /// 返回 true 表示已成功启动提权后的新进程（调用方应随即退出）。
        /// 说明：用户拒绝 UAC 时 Process.Start 会抛异常，被 catch 后返回 false；
        /// 若能拿到 Process 句柄，再校验其未立即退出，避免“提权进程启动后立即退出”仍误判成功。
        /// </summary>
        public static bool RelaunchElevated()
        {
            try
            {
                string exePath = Assembly.GetEntryAssembly().Location;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"   // 触发 UAC
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    // 存活校验：提权进程若立即退出（如启动即崩），视为失败
                    try
                    {
                        p.WaitForInputIdle(3000);
                    }
                    catch
                    {
                        // 某些情况下无法等待空闲，忽略
                    }
                    return !p.HasExited;
                }
            }
            catch (Exception)
            {
                // 用户拒绝 UAC 或启动失败
                return false;
            }
        }
    }
}
