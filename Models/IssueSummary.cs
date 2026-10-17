// 检测/修复结果汇总模型：统一统计口径，供检测页与结果页共用。
using System.Collections.Generic;

namespace WindowsUpdateFixer.Models
{
    /// <summary>
    /// 对一组 IssueItem 的统计汇总，供检测页与结果页共用，避免各页各自遍历计数。
    /// </summary>
    public class IssueSummary
    {
        /// <summary>确有问题且可自动修复的项（检测页“可修复问题”计数）。</summary>
        public List<IssueItem> AutoFixable { get; private set; }

        /// <summary>已成功修复的项。</summary>
        public List<IssueItem> Fixed { get; private set; }

        /// <summary>修复失败的项（需手动处理，计入失败数）。</summary>
        public List<IssueItem> Failed { get; private set; }

        /// <summary>需手动处理的全部项（失败项 + 有问题但不可自动修复项）。</summary>
        public List<IssueItem> Manual { get; private set; }

        public int AutoFixableCount { get { return AutoFixable.Count; } }
        public int FixedCount { get { return Fixed.Count; } }
        public int FailedCount { get { return Failed.Count; } }
        public int ManualCount { get { return Manual.Count; } }

        // 说明：原 NoIssue（FixedCount==0 && ManualCount==0）已删除。
        // 它只被 DonePage 的“无需修复”分支使用，而该分支不可达：
        // 检测页仅在 AutoFixableCount > 0 时才提供“开始修复”入口，
        // 能进入结果页的用户必然至少修复过一项，因此该判断恒为 false。

        /// <summary>按检测/修复状态统计一组 IssueItem。</summary>
        public static IssueSummary From(IEnumerable<IssueItem> issues)
        {
            var summary = new IssueSummary
            {
                AutoFixable = new List<IssueItem>(),
                Fixed = new List<IssueItem>(),
                Failed = new List<IssueItem>(),
                Manual = new List<IssueItem>()
            };

            foreach (var i in issues)
            {
                if (i.NeedsFix && i.CanAutoFix)
                    summary.AutoFixable.Add(i);

                if (i.State == FixState.Done)
                {
                    summary.Fixed.Add(i);
                    continue;
                }

                if (i.State == FixState.Failed)
                {
                    summary.Failed.Add(i);
                    summary.Manual.Add(i);
                    continue;
                }

                // 确有问题但本就不能自动修复（如未激活）：需手动处理
                if (i.NeedsFix && !i.CanAutoFix)
                    summary.Manual.Add(i);
            }

            return summary;
        }
    }
}
