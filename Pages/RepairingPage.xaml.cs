using System.Windows;
using System.Windows.Controls;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer.Pages
{
    public partial class RepairingPage : UserControl
    {
        private readonly RepairService _service = new RepairService();

        public RepairingPage()
        {
            InitializeComponent();

            // 修复项失败 → 底部状态栏通知（非致命）
            _service.RepairStepFailed = msg => UiThread.Invoke(Dispatcher, () =>
            {
                var main = (MainWindow)Window.GetWindow(this);
                if (main != null) main.ShowStatus(msg);
            });

            // 进入修复页复位可修项状态（统一在 AppState 处理，防止检测阶段残留）
            AppState.PrepareForRepair();

            RepairList.ItemsSource = AppState.Issues;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            await _service.RunAsync(item => UiThread.Invoke(Dispatcher, () =>
            {
                if (item.State == FixState.Running)
                    CurrentText.Text = "正在处理：" + item.Title;
            }));

            // 修复完成：窗口已关闭时不再导航
            if (!UiThread.IsUsable(Dispatcher)) return;

            UiThread.Invoke(Dispatcher, () =>
            {
                Progress.Visibility = Visibility.Collapsed;
                CurrentText.Text = "修复完成";

                var main = (MainWindow)Window.GetWindow(this);
                // 把 gpupdate 结果带给结果页，使“收尾步骤失败”这件事可见
                if (main != null) main.Navigate(new DonePage(_service.GroupPolicyRefreshed));
            });
        }
    }
}
