using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer.Pages
{
    public partial class DonePage : UserControl
    {
        /// <summary>修复收尾的 gpupdate 是否成功（null=未执行）。用于在结果页补充提示。</summary>
        private readonly bool? _groupPolicyRefreshed;

        public DonePage() : this(null) { }

        public DonePage(bool? groupPolicyRefreshed)
        {
            InitializeComponent();
            _groupPolicyRefreshed = groupPolicyRefreshed;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // 统计口径统一由 IssueSummary 计算
            var summary = IssueSummary.From(AppState.Issues);

            FixedList.ItemsSource = summary.Fixed;

            // 顶部图标与标题
            if (summary.FailedCount > 0)
            {
                ResultIconBox.Background = (Brush)Application.Current.FindResource("WarningContainerBrush");
                ResultGlyph.Foreground = (Brush)Application.Current.FindResource("WarningBrush");
                ResultGlyph.Text = "\uE7BA";
                TitleText.Text = "修复部分完成";
                SubtitleText.Text = "共完成 " + summary.FixedCount + " 项，" + summary.FailedCount + " 项失败。请查看下方详情。";
            }
            else
            {
                // 说明：不存在“无需修复”分支。检测页仅在 AutoFixableCount > 0 时
                // 才提供“开始修复”入口，因此能走到这里的用户必然至少修复过一项，
                // 原 summary.NoIssue 判断实际不可达，已移除。
                TitleText.Text = "修复完成";
                SubtitleText.Text = "共完成 " + summary.FixedCount + " 项修复，建议重启电脑使更改完全生效。";
            }

            if (summary.FixedCount == 0) FixedGroup.Visibility = Visibility.Collapsed;

            if (summary.ManualCount > 0)
            {
                ManualList.ItemsSource = summary.Manual;
                ManualGroup.Visibility = Visibility.Visible;
            }

            ApplyGroupPolicyHint();
        }

        /// <summary>
        /// gpupdate 失败时在结果页补充一行提示。
        /// 该项是修复收尾的“尽力而为”步骤，单独执行（gpupdate /force 也可以补做），
        /// 但必须在结果页明确展示，而不是仅在底部状态栏短暂显示。
        /// </summary>
        private void ApplyGroupPolicyHint()
        {
            if (_groupPolicyRefreshed != false) return;

            GroupPolicyHint.Text = "收尾的组策略刷新（gpupdate）未成功完成。这通常不影响已完成的修复，" +
                                   "如需立即生效，可重新启动电脑，或手动运行 gpupdate /force。";
            GroupPolicyHintBox.Visibility = Visibility.Visible;
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Reboot_Click(object sender, RoutedEventArgs e)
        {
            ShellRunner.RunSilent("shutdown.exe", "/r /t 5 /c \"Akira's Troubleshoot\"", 15000);
            Application.Current.Shutdown();
        }
    }
}
