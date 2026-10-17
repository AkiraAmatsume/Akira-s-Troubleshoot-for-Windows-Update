// 外部命令执行工具。
using System;
using System.Diagnostics;
using System.Text;

namespace WindowsUpdateFixer.Services
{
    public static class ShellRunner
    {
        public struct CommandResult
        {
            public int Code;
            public string StdOut;
            public string StdErr;

            /// <summary>是否因超时被强制结束。为 true 时 Code 恒为 -1。</summary>
            public bool TimedOut;
        }

        /// <summary>
        /// 关键点通知回调：外部命令返回非 0 且被判定为“非预期但不足以致命”时，回调给界面（状态栏）。
        /// 由 MainWindow 注入，避免服务层直接依赖界面。
        /// </summary>
        public static Action<string> CommandFailed;

        /// <summary>
        /// 执行外部命令，返回 (exitCode, stdout, stderr)。超时则 Kill 并返回 code = -1。
        /// </summary>
        public static CommandResult Run(string exe, string args, int timeoutMs = 15000)
        {
            var res = new CommandResult { Code = -1, StdOut = "", StdErr = "" };
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var p = new Process { StartInfo = psi })
                {
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        res.Code = -1;
                        res.TimedOut = true;
                        res.StdOut = sbOut.ToString();
                        res.StdErr = sbErr.ToString();
                        return res;
                    }

                    res.Code = p.ExitCode;
                    res.StdOut = sbOut.ToString();
                    res.StdErr = sbErr.ToString();
                    return res;
                }
            }
            catch (Exception ex)
            {
                res.Code = -1;
                res.StdErr = ex.Message;
                return res;
            }
        }

        /// <summary>只关心退出码。</summary>
        public static bool RunSilent(string exe, string args, int timeoutMs = 15000)
        {
            return Run(exe, args, timeoutMs).Code == 0;
        }

        /// <summary>
        /// 执行“期望成功”的关键命令：非 0 时通过 CommandFailed 回调状态栏提示，
        /// 并返回是否成功。适用于应成功却失败时需要告知用户的场景。
        /// </summary>
        public static bool RunExpectSuccess(string exe, string args, string friendlyName, int timeoutMs = 15000)
        {
            var r = Run(exe, args, timeoutMs);
            if (r.Code != 0)
            {
                string msg = friendlyName + "未成功完成（退出码 " + r.Code + "）";
                var cb = CommandFailed;
                if (cb != null) cb(msg);
                return false;
            }
            return true;
        }

        /// <summary>通过 cmd 执行带管道/重定向的命令行（如 echo Y| net stop）。</summary>
        public static CommandResult RunCmd(string commandLine, int timeoutMs = 30000)
        {
            return Run("cmd.exe", "/C " + commandLine, timeoutMs);
        }
    }
}

