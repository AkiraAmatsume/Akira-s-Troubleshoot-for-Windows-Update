using System.ComponentModel;

namespace WindowsUpdateFixer.Models
{
    public enum IssueSeverity { Ok, Warning, Problem }

    public enum FixState { Pending, Running, Done, Failed, Skipped }

    /// <summary>检测/修复项的 Id 常量，避免各处用字符串魔法值。</summary>
    public static class IssueIds
    {
        public const string Policy = "policy";
        public const string Services = "services";
        public const string Recovery = "recovery";
        public const string Files = "files";
        public const string Reset = "reset";
        public const string Tasks = "tasks";
        public const string SettingsApp = "settings-app";
        public const string Activation = "activation";
        public const string Compatibility = "compatibility";
        public const string Unknown = "unknown";
    }

    public class IssueItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Detail { get; set; }

        /// <summary>检测判定为“有问题”（需要处理）。与能否自动修复无关。</summary>
        public bool NeedsFix { get; set; }

        /// <summary>该问题能否由程序自动修复。不代表此项一定有问题。</summary>
        public bool CanAutoFix { get; set; }

        public string ManualHint { get; set; }

        /// <summary>
        /// 修复阶段才会确定的“后续建议”（例如文件无法从组件存储恢复时，
        /// 建议用户手动运行 DISM /RestoreHealth 与 sfc /scannow）。
        /// 由 RepairService 在修复失败时写入，结果页据此展示，避免只留在状态栏。
        /// </summary>
        private string _followUpHint;
        public string FollowUpHint
        {
            get { return _followUpHint; }
            set { _followUpHint = value; Raise("FollowUpHint"); Raise("HasFollowUpHint"); }
        }

        /// <summary>是否需在结果页展示后续建议。</summary>
        public bool HasFollowUpHint
        {
            get { return !string.IsNullOrEmpty(_followUpHint); }
        }

        private IssueSeverity _severity = IssueSeverity.Ok;
        public IssueSeverity Severity
        {
            get { return _severity; }
            set { _severity = value; Raise("Severity"); Raise("SeverityGlyph"); Raise("SeverityBrush"); }
        }

        private FixState _state = FixState.Pending;
        public FixState State
        {
            get { return _state; }
            set { _state = value; Raise("State"); Raise("StateText"); Raise("StateGlyph"); Raise("StateBrush"); }
        }

        // ---- 派生：检测阶段图标 / 颜色 ----
        public string SeverityGlyph
        {
            get
            {
                switch (Severity)
                {
                    case IssueSeverity.Warning: return "\uE7BA"; // 警告
                    case IssueSeverity.Problem: return "\uEA39"; // 叉
                    default: return "\uE73E";                    // 勾
                }
            }
        }

        public string SeverityBrush
        {
            get
            {
                switch (Severity)
                {
                    case IssueSeverity.Warning: return "WarningBrush";
                    case IssueSeverity.Problem: return "ErrorBrush";
                    default: return "SuccessBrush";
                }
            }
        }

        // ---- 派生：修复阶段状态文字 / 图标 ----
        public string StateText
        {
            get
            {
                switch (State)
                {
                    case FixState.Running: return "处理中…";
                    case FixState.Done: return "已修复";
                    case FixState.Failed: return "失败";
                    // 与结果页“需要手动处理”的归类保持同一说法：同一件事不出现两种叫法
                    case FixState.Skipped: return "需手动处理";
                    default: return "等待";
                }
            }
        }

        public string StateGlyph
        {
            get
            {
                switch (State)
                {
                    case FixState.Done: return "\uE73E";   // 勾
                    case FixState.Failed: return "\uEA39"; // 叉
                    default: return "\uE9F5";              // 空心圆
                }
            }
        }

        public string StateBrush
        {
            get
            {
                switch (State)
                {
                    case FixState.Done: return "SuccessBrush";
                    case FixState.Failed: return "ErrorBrush";
                    case FixState.Running: return "PrimaryBrush";
                    default: return "OutlineBrush";
                }
            }
        }
    }
}
