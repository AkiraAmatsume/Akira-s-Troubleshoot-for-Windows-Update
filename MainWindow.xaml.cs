using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Pages;
using WindowsUpdateFixer.Services;

namespace WindowsUpdateFixer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 外部命令关键失败 → 底部状态栏通知（非致命）
            // 经 UiThread 调度：窗口关闭后回调被静默丢弃，不再抛异常。
            ShellRunner.CommandFailed = msg => UiThread.Invoke(Dispatcher, () => ShowStatus(msg));

            // 启动进入欢迎页
            Navigate(new WelcomePage());
        }

        /// <summary>
        /// 切换页面容器内容，并播放淡入动画。
        /// </summary>
        public void Navigate(UserControl page)
        {
            page.Opacity = 0;
            PageHost.Content = page;

            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            page.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// 在底部状态栏显示一条非致命通知（仅作通知，不打断操作）。
        /// 显示最近一条，不自动消失。
        /// </summary>
        public void ShowStatus(string message, bool isWarning = true)
        {
            StatusText.Text = message;
            StatusGlyph.Text = isWarning ? "\uE7BA" : "\uE73E"; // 警告 / 勾
            StatusGlyph.Foreground = (Brush)FindResource(
                isWarning ? "WarningBrush" : "SuccessBrush");
        }

        /// <summary>状态栏恢复为“就绪”。</summary>
        public void ResetStatus()
        {
            StatusText.Text = "就绪";
            StatusGlyph.Text = "\uE73E";
            StatusGlyph.Foreground = (Brush)FindResource("SuccessBrush");
        }

        /// <summary>
        /// 致命错误后复位内存中的运行状态并回到欢迎页。
        /// restoreResult 非空时，在状态栏追加恢复结果提示。
        /// </summary>
        public void ResetToWelcome(RestoreResult? restoreResult)
        {
            // 状态复位：清空检测/修复进度与备份引用，避免残留上一轮状态
            AppState.BeginNewRun();
            AppState.BackupPath = null;

            Navigate(new WelcomePage());

            if (restoreResult.HasValue)
            {
                switch (restoreResult.Value)
                {
                    case RestoreResult.Success:
                        ShowStatus("注册表策略备份已恢复。", false);
                        break;
                    case RestoreResult.Partial:
                        ShowStatus("注册表策略备份已部分恢复，部分设置未能还原。");
                        break;
                    case RestoreResult.NoBackup:
                        // 防御性：FatalErrorWindow 无备份时已隐藏恢复按钮，正常流程不会触发
                        ShowStatus("未找到可用的注册表策略备份。");
                        break;
                    default:
                        ShowStatus("注册表策略备份恢复失败。");
                        break;
                }
            }
            else
            {
                ResetStatus();
            }
        }
    }
}
