// 把后台回调安全地调度到 UI 线程。
using System;
using System.Windows.Threading;

namespace WindowsUpdateFixer
{
    /// <summary>
    /// 后台线程回调 UI 的统一入口。
    ///
    /// 背景：检测/修复在 Task.Run 中执行，进度与结果通过回调推送给界面。
    /// 若用户在此期间关闭主窗口，Dispatcher 会进入关闭流程，此后直接调用
    /// Dispatcher.Invoke 会抛出 TaskCanceledException / InvalidOperationException，
    /// 并可能被全局异常处理捕获而弹出“操作未能完成”。
    ///
    /// 因此统一经由本类调度：Dispatcher 已关闭或已开始关闭时静默丢弃回调。
    /// </summary>
    public static class UiThread
    {
        /// <summary>
        /// 在 UI 线程上执行 action；Dispatcher 不可用（窗口已关闭/正在关闭）时静默忽略。
        /// 传入的 action 在 UI 线程抛出异常同样被捕获并忽略，避免单个界面刷新失败
        /// 导致整个程序进入致命错误处理。
        /// </summary>
        public static void Invoke(Dispatcher dispatcher, Action action)
        {
            if (action == null) return;
            if (!IsUsable(dispatcher)) return;

            try
            {
                // 已在 UI 线程时直接执行，避免 Invoke 的额外开销与潜在的重入问题
                if (dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    dispatcher.Invoke(action);
                }
            }
            catch (OperationCanceledException) { }
            // ObjectDisposedException 继承自 InvalidOperationException，一并覆盖
            catch (InvalidOperationException) { }
        }

        /// <summary>Dispatcher 是否仍可接受调度。</summary>
        public static bool IsUsable(Dispatcher dispatcher)
        {
            if (dispatcher == null) return false;
            if (dispatcher.HasShutdownStarted) return false;
            if (dispatcher.HasShutdownFinished) return false;
            try
            {
                return dispatcher.Thread != null && dispatcher.Thread.IsAlive;
            }
            catch
            {
                return false;
            }
        }
    }
}
