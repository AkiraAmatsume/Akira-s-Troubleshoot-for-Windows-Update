// 检测项统一接口：每个具体检测项实现为独立类，DiagnosticService 只负责按顺序调度。
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 单个检测项。实现类只负责“一项”的判定，不关心调度顺序与进度上报。
    /// </summary>
    public interface ICheckItem
    {
        /// <summary>进度条上显示的提示文案。</summary>
        string ProgressText { get; }

        /// <summary>执行检测并返回结果项。</summary>
        IssueItem Check();
    }
}
