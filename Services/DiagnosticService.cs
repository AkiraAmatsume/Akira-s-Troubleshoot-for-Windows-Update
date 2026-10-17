// Windows Update 组件检测调度。
// 各检测项实现为 Services/Detection 下独立类，本类只负责按顺序调度与进度上报。
using System;
using System.Threading.Tasks;
using WindowsUpdateFixer.Models;
using WindowsUpdateFixer.Services.Detection;

namespace WindowsUpdateFixer.Services
{
    public class DiagnosticService
    {
        /// <summary>
        /// 检测项顺序。兼容性置于首位：其结论决定后续各项是否可信。
        /// </summary>
        private static readonly ICheckItem[] Items =
        {
            new CompatibilityCheck(),
            new PolicyCheck(),
            new ServicesCheck(),
            new RecoveryCheck(),
            new FilesCheck(),
            new ResetCheck(),
            new TasksCheck(),
            new SettingsAppCheck(),
            new ActivationCheck()
        };

        /// <summary>顺序执行全部检测项，通过回调报告进度与结果。</summary>
        public Task RunAsync(Action<string> onProgress, Action<IssueItem> onItemFound)
        {
            return Task.Run(() =>
            {
                foreach (var item in Items)
                {
                    Check(onProgress, onItemFound, item.ProgressText, item.Check);
                }
            });
        }

        private void Check(Action<string> onProgress, Action<IssueItem> onItemFound,
                           string progress, Func<IssueItem> fn)
        {
            if (onProgress != null) onProgress(progress);
            IssueItem item;
            try
            {
                item = fn();
            }
            catch (Exception ex)
            {
                item = new IssueItem
                {
                    Id = IssueIds.Unknown,
                    Title = "检测出错",
                    Description = ex.Message,
                    Severity = IssueSeverity.Problem,
                    CanAutoFix = false
                };
            }
            if (onItemFound != null) onItemFound(item);
        }
    }
}
