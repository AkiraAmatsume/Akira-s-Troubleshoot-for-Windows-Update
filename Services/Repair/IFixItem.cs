// 修复项统一接口：每个具体修复项实现为独立类，RepairService 只负责按顺序调度。
namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 单个修复项。实现类只负责“一项”的修复与失败后的后续建议。
    /// </summary>
    public interface IFixItem
    {
        /// <summary>对应 IssueIds 的标识，用于调度与结果回填。</summary>
        string Id { get; }

        /// <summary>执行修复并返回是否成功。</summary>
        bool Fix();

        /// <summary>
        /// 修复失败时展示给用户的后续建议（结果页“需要手动处理”分组）。
        /// 无建议时返回 null。允许依赖 Fix() 执行过程中记录的状态。
        /// </summary>
        string FollowUpHint { get; }
    }
}
