// 应用入口：注册全局异常处理，把未捕获异常统一转为“致命错误”覆盖窗。
//
// 启动窗口由 App.xaml 的 StartupUri 指定为 MainWindow.xaml，本类不手动创建主窗口。
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WindowsUpdateFixer.Services;
using WindowsUpdateFixer.Views;

namespace WindowsUpdateFixer
{
    public partial class App : Application
    {
        // 致命错误覆盖窗是否已显示，避免同一异常反复弹出
        private bool _fatalShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 三类未捕获异常统一接管：
            //   - UI 线程异常：DispatcherUnhandledException
            //   - 后台线程异常：AppDomain.UnhandledException（无法阻止进程退出，仅用于提示）
            //   - 未观察的 Task 异常：TaskScheduler.UnobservedTaskException
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // UI 线程异常：显示覆盖窗并标记已处理，避免进程直接退出
            ShowFatal(e.Exception);
            e.Handled = true;
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // 后台线程异常：进程可能即将终止，仍尝试提示（能显示则显示）
            ShowFatal(e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // 未观察的 Task 异常：标记为已观察，避免升级为进程终止；仅在首次时提示
            ShowFatal(e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// 显示致命错误覆盖窗。
        /// 同一时刻只显示一个：若已显示或已处理过则不重复弹出。
        /// </summary>
        private void ShowFatal(Exception ex)
        {
            if (_fatalShown) return;
            _fatalShown = true;

            var report = ErrorReporter.Build(ex);
            var win = new FatalErrorWindow(report);

            // 以主窗口为 Owner，FatalErrorWindow 关闭后据此复位并回到欢迎页
            if (MainWindow != null && !ReferenceEquals(MainWindow, win))
                win.Owner = MainWindow;

            win.ShowDialog();

            // 覆盖窗关闭后允许后续异常再次提示
            _fatalShown = false;
        }
    }
}
