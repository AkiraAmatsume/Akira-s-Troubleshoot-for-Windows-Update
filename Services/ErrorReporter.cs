// 错误报告：格式化 错误代码 / 位置(方法名+调用栈) / 原因，并按类别给出 XP 风格解决方案文案。
using System;
using System.Diagnostics;
using System.Text;

namespace WindowsUpdateFixer.Services
{
    /// <summary>错误类别，用于分流出对应的解决方案文案。</summary>
    public enum ErrorCategory
    {
        Permission,     // 权限不足
        SystemResource, // 系统 / 文件资源
        OutOfMemory,    // 内存 / 资源耗尽
        MissingComponent, // DLL / 组件缺失
        Unknown         // 其他未知
    }

    /// <summary>一次错误报告的整理结果，供界面展示。</summary>
    public class ErrorReport
    {
        public string Code;          // 错误代码，如 0x80070005 (HResult=-2147024891)
        public string Location;      // 位置：方法名
        public string Stack;         // 调用栈
        public string Reason;        // 原因（异常消息）
        public string Solution;      // XP 风格解决方案文案
        public ErrorCategory Category;
    }

    public static class ErrorReporter
    {
        /// <summary>把异常整理为可展示的错误报告。</summary>
        public static ErrorReport Build(Exception ex)
        {
            var report = new ErrorReport();
            report.Code = FormatCode(ex);
            report.Location = FirstUserMethod(ex);
            report.Stack = BuildStack(ex);
            report.Reason = ex != null ? ex.Message : "(无异常信息)";
            report.Category = Classify(ex);
            report.Solution = SolutionText(report.Category);
            return report;
        }

        private static string FormatCode(Exception ex)
        {
            if (ex == null) return "0x00000000";
            return string.Format("0x{0:X8} (HResult={1})", unchecked((uint)ex.HResult), ex.HResult);
        }

        /// <summary>取调用栈中第一个属于本项目的方法名；若没有则取最外层帧。</summary>
        private static string FirstUserMethod(Exception ex)
        {
            if (ex == null) return "(未知位置)";

            foreach (var frame in new StackTrace(ex, false).GetFrames())
            {
                var method = frame.GetMethod();
                if (method == null || method.DeclaringType == null) continue;
                string ns = method.DeclaringType.Namespace;
                if (ns != null && ns.StartsWith("WindowsUpdateFixer", StringComparison.Ordinal))
                    return method.DeclaringType.FullName + "." + method.Name;
            }

            var frames = new StackTrace(ex, false).GetFrames();
            if (frames.Length > 0)
            {
                var m = frames[0].GetMethod();
                if (m != null && m.DeclaringType != null)
                    return m.DeclaringType.FullName + "." + m.Name;
            }
            return "(未知位置)";
        }

        private static string BuildStack(Exception ex)
        {
            if (ex == null) return "(无调用栈)";
            var sb = new StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                sb.AppendLine(e.StackTrace ?? "(无调用栈)");
                if (e.InnerException != null)
                    sb.AppendLine("---- 内部异常 ----");
            }
            return sb.ToString().TrimEnd();
        }

        private static ErrorCategory Classify(Exception ex)
        {
            if (ex == null) return ErrorCategory.Unknown;

            if (ex is UnauthorizedAccessException)
                return ErrorCategory.Permission;
            if (ex is OutOfMemoryException)
                return ErrorCategory.OutOfMemory;
            if (ex is DllNotFoundException || ex is BadImageFormatException
                || ex is System.IO.FileNotFoundException)
                return ErrorCategory.MissingComponent;
            if (ex is System.IO.IOException || ex is System.IO.DirectoryNotFoundException)
                return ErrorCategory.SystemResource;

            // 按 HRESULT 兜底
            switch (unchecked((uint)ex.HResult))
            {
                case 0x80070005: // E_ACCESSDENIED
                    return ErrorCategory.Permission;
                case 0x8007000E: // E_OUTOFMEMORY
                    return ErrorCategory.OutOfMemory;
                case 0x8007007E: // HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND)
                case 0x800700C1: // ERROR_BAD_EXE_FORMAT
                    return ErrorCategory.MissingComponent;
                case 0x80070002: // ERROR_FILE_NOT_FOUND
                case 0x80070003: // ERROR_PATH_NOT_FOUND
                    return ErrorCategory.SystemResource;
            }

            return ErrorCategory.Unknown;
        }

        private static string SolutionText(ErrorCategory category)
        {
            switch (category)
            {
                case ErrorCategory.Permission:
                    return "您没有足够的使用权限完成本操作。\n\n" +
                           "请以管理员身份重新登录，或与您的计算机管理员联系，以获得所需的访问权限。";

                case ErrorCategory.SystemResource:
                    return "由于必要的系统文件或资源暂时无法访问，程序被终止。\n\n" +
                           "请确认磁盘未被占用、相关文件未被其它程序锁定，然后重新启动本程序。";

                case ErrorCategory.OutOfMemory:
                    return "由于系统内存或可用资源不足，程序无法继续运行，已被终止。\n\n" +
                           "请关闭其它正在运行的程序，释放内存后重试。若问题持续出现，请与您的计算机管理员联系。";

                case ErrorCategory.MissingComponent:
                    return "由于缺少必需的组件或组件已损坏，程序被终止。\n\n" +
                           "请重新安装本程序；若问题持续出现，请与您的软件发行商联系。";

                default:
                    return "由于发生意外错误，程序被终止。\n\n" +
                           "请保存您的工作并重新启动本程序。若问题持续出现，请与您的软件发行商联系。";
            }
        }
    }
}
