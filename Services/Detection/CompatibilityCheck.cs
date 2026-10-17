// 系统版本兼容性检测。
using System.Text;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 判定系统版本，并据此确认本工具是否适用。
    /// 关键：Windows 11 的注册表 ProductName 仍写作 "Windows 10"（微软已知问题），
    /// 因此版本判定基于构建号：>= 22000 为 Windows 11。
    /// 该项同时为 FilesCheck 提供“更新组件应位于哪个目录”的判断依据。
    /// </summary>
    public class CompatibilityCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查系统版本兼容性…"; } }

        public IssueItem Check()
        {
            var info = SystemCompat.Current;

            bool supported = SystemCompat.IsSupported(info);

            var detail = new StringBuilder();
            detail.AppendLine("系统：" + SystemCompat.Describe(info));
            detail.AppendLine("产品名称（注册表）：" + (info.ProductName ?? "（未读取到）"));
            detail.AppendLine("构建号：" + info.Build + (info.Ubr > 0 ? "." + info.Ubr : ""));
            detail.AppendLine("组件目录：" + UpdateFiles.ComponentDirHint());

            if (info.Family == WindowsFamily.WindowsServer)
            {
                detail.AppendLine();
                detail.AppendLine("Windows Server 的更新组件布局与服务集合与本工具假设不同，");
                detail.AppendLine("自动修复可能不适用。建议仅参考检测结果。");
            }
            else if (!supported)
            {
                detail.AppendLine();
                detail.AppendLine("本工具面向 Windows 10 / 11，当前系统不在支持范围内。");
            }

            string title;
            string description;
            IssueSeverity severity;

            if (supported)
            {
                title = SystemCompat.FamilyName(info.Family) + " · 系统兼容";
                description = "本工具支持当前系统版本。系统信息：" + SystemCompat.Describe(info);
                severity = IssueSeverity.Ok;
            }
            else if (info.Family == WindowsFamily.WindowsServer)
            {
                title = "Windows Server · 未验证兼容性";
                description = "当前为 Windows Server，本工具主要面向 Windows 10/11，" +
                              "修复动作可能不适用，请谨慎使用。";
                severity = IssueSeverity.Warning;
            }
            else
            {
                title = SystemCompat.FamilyName(info.Family) + " · 系统不受支持";
                description = "本工具面向 Windows 10 / 11，当前系统版本不在支持范围内，" +
                              "检测与修复结果可能不准确。";
                severity = IssueSeverity.Warning;
            }

            return new IssueItem
            {
                Id = IssueIds.Compatibility,
                Title = title,
                Description = description,
                Detail = detail.ToString().Trim(),
                Severity = severity,
                // 兼容性不是“可修复的问题”，只做信息展示，不进入修复流程
                NeedsFix = false,
                CanAutoFix = false,
                ManualHint = supported
                    ? null
                    : "请使用与当前系统版本对应的修复方法。"
            };
        }
    }
}
