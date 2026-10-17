using System.Collections.ObjectModel;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services
{
    /// <summary>
    /// 应用级运行状态（当前为全局静态可变单例）。


    /// 说明：服务层与页面共享同一集合，属已知的隐藏耦合。当前实现基于此，
    /// 若后续需要支持单元测试，可改为按会话注入的实例。
    /// </summary>
    public static class AppState
    {
        public static ObservableCollection<IssueItem> Issues { get; private set; }

        public static string BackupPath { get; set; }

        static AppState()
        {
            Issues = new ObservableCollection<IssueItem>();
        }

        /// <summary>
        /// 开始新一轮检测前复位运行状态：清空旧结果。
        /// 由检测流程统一调用，避免状态复位散落在各页面构造函数里
        /// （那样一旦执行路径绕开某个页面，就会残留上一轮的 State）。
        /// 清空后由 DiagnosticService 重新填充全新项，故无需再遍历复位各 State。
        /// </summary>
        public static void BeginNewRun()
        {
            Issues.Clear();
        }

        /// 准备修复阶段：把“确有问题且可自动修复”的项复位为 Pending。
        /// 检测阶段即可修项可能被预置过状态，这里统一重置，保证修复页从干净状态开始。
        public static void PrepareForRepair()
        {
            foreach (var i in Issues)
                if (i.NeedsFix && i.CanAutoFix) i.State = FixState.Pending;
        }
    }
}
