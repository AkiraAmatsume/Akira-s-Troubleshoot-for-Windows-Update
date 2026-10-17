using System.Windows;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer.Views
{
    /// <summary>
    /// 致命错误的顶层覆盖窗。盖住主窗口，展示 错误代码 / 位置 / 原因 / 调用栈 / 建议，
    /// 并提供“回到欢迎页”与“读取注册表策略备份并恢复，然后回到欢迎页”两种处理。
    /// </summary>
    public partial class FatalErrorWindow : Window
    {
        private readonly ErrorReport _report;

        public FatalErrorWindow(ErrorReport report)
        {
            InitializeComponent();
            _report = report;

            // 回填详情
            CodeText.Text = report.Code;
            LocationText.Text = report.Location;
            ReasonText.Text = report.Reason;
            StackText.Text = report.Stack;
            SolutionText.Text = report.Solution;

            // 无可用备份时隐藏“读取备份并恢复”按钮
            if (!BackupRestore.HasBackup())
                RestoreButton.Visibility = Visibility.Collapsed;
        }

        private void Welcome_Click(object sender, RoutedEventArgs e)
        {
            // 放弃恢复，直接复位并回欢迎页
            ResetAndReturn(null);
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            // 先询问用户是否确认恢复“注册表策略备份”的更改
            var confirm = MessageBox.Show(
                "是否读取本次操作前生成的“注册表策略备份”，并将其还原到系统中？\n\n" +
                "注意：此操作仅还原注册表策略设置，并非完整的系统还原。",
                "Akira's Troubleshoot",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            // 恢复过程可能失败，全部吞掉并在完成后统一提示
            RestoreResult result;
            try
            {
                result = BackupRestore.Restore();
            }
            catch
            {
                result = RestoreResult.Failed;
            }

            ResetAndReturn(result);
        }

        /// <summary>复位状态、关闭覆盖窗并导航回欢迎页；restoreResult 用于追加恢复结果提示。</summary>
        private void ResetAndReturn(RestoreResult? restoreResult)
        {
            Close();

            var main = Owner as MainWindow;
            if (main != null)
                main.ResetToWelcome(restoreResult);
        }
    }
}
