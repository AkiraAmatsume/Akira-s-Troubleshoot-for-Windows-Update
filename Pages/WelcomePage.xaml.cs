using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer.Pages
{
    public partial class WelcomePage : UserControl
    {
        public WelcomePage()
        {
            InitializeComponent();
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            // 首次点击若未提权，先自提权重启（会在检测之前完成，结果完整）
            if (!Elevation.IsAdministrator())
            {
                if (Elevation.RelaunchElevated())
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show(
                        "未获得管理员权限，无法继续。\n\n请点击“是”允许 Windows 的权限提示后重试。",
                        "Akira's Troubleshoot",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            StartButton.IsEnabled = false;
            StartButton.Content = "正在备份注册表…";

            string backup = await Task.Run(() => RegistryBackup.RunBackup());
            AppState.BackupPath = backup;

            if (backup == null)
            {
                var r = MessageBox.Show(
                    "未能生成可用的注册表策略备份（可能是桌面路径不可写，或相关策略键均不存在）。\n\n" +
                    "继续修复将无法通过“恢复备份”撤销对策略的更改。是否仍要继续？",
                    "Akira's Troubleshoot",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes)
                {
                    StartButton.IsEnabled = true;
                    StartButton.Content = "开始检测";
                    return;
                }
            }

            var main = (MainWindow)Window.GetWindow(this);
            if (main != null) main.Navigate(new DetectingPage());
        }
    }
}

