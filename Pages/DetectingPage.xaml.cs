using System;
using System.Windows;
using System.Windows.Controls;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer.Pages
{
    public partial class DetectingPage : UserControl
    {
        private readonly DiagnosticService _service = new DiagnosticService();

        public DetectingPage()
        {
            InitializeComponent();

            // 复位运行状态（清空旧结果 + 各页面不再各自维护 State 重置）
            AppState.BeginNewRun();
            CheckList.ItemsSource = AppState.Issues;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // 所有 UI 更新均经 UiThread 调度：窗口若在此期间关闭，回调被静默丢弃，
            // 不会因 Dispatcher 已关闭而抛异常、进而弹出“操作未能完成”。
            await _service.RunAsync(
                progress => UiThread.Invoke(Dispatcher, () => CurrentText.Text = progress),
                item => UiThread.Invoke(Dispatcher, () =>
                {
                    // 结果写入全局集合与界面无关，必须保留；
                    // 即使用户已离开本页，检测结论也要完整记录，供后续页面使用。
                    AppState.Issues.Add(item);
                }));

            // 检测完成：Dispatcher 已关闭时直接返回，后续界面操作无意义
            if (!UiThread.IsUsable(Dispatcher)) return;

            UiThread.Invoke(Dispatcher, ApplyCompletedState);
        }

        /// <summary>检测完成后更新界面：进度条收起、标题与按钮按统计结果切换。</summary>
        private void ApplyCompletedState()
        {
            Progress.Visibility = Visibility.Collapsed;
            CurrentText.Text = "检测完成";

            // 统计口径统一由 IssueSummary 计算（仅“确有问题且可自动修复”计为可修复问题）
            var summary = IssueSummary.From(AppState.Issues);

            if (summary.AutoFixableCount > 0)
            {
                HeaderText.Text = "检测到 " + summary.AutoFixableCount + " 个可修复问题";
                RepairButton.Visibility = Visibility.Visible;
                CloseButton.Visibility = Visibility.Visible;
            }
            else
            {
                HeaderText.Text = "系统更新组件正常";
                CloseButton.Visibility = Visibility.Visible;
            }
        }

        private void Repair_Click(object sender, RoutedEventArgs e)
        {
            var main = (MainWindow)Window.GetWindow(this);
            if (main != null) main.Navigate(new RepairingPage());
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
