// 核心更新组件文件检测。
using System.IO;
using System.Text;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测核心更新文件是否缺失或被改名。
    /// 关键文件按“多候选目录”查找：不同 Windows 版本存放位置不同
    /// （新版在 UUS\&lt;arch&gt;\，旧版在 System32），单一目录判定必然误报。
    /// </summary>
    public class FilesCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查核心组件文件…"; } }

        private static string SystemRoot
        {
            get { return UpdateFiles.SystemRoot; }
        }

        public IssueItem Check()
        {
            var detail = new StringBuilder();
            int count = 0;

            // 被改名的 DLL 只可能出现在真实 System32 中（不受 WOW64 重定向影响）
            string sys32 = UpdateFiles.RawSystem32();
            foreach (var n in UpdateFiles.RenamedDllBaseNames)
            {
                if (WinSxSRestore.HasRenamedDll(n))
                {
                    detail.AppendLine(n + ".dll 被改名为 " + n + "_BAK.dll");
                    count++;
                }
            }

            // 关键文件按“多候选目录”查找：不同 Windows 版本存放位置不同
            // （新版在 UUS\<arch>\，旧版在 System32），单一目录判定必然误报。
            // 候选顺序已由 UpdateFiles 依据系统版本决定，两个候选都会查，
            // 因此版本判断有偏差时也不会造成误报缺失。
            foreach (var f in UpdateFiles.RequiredFiles)
            {
                if (UpdateFiles.ResolveExistingPath(f) == null)
                {
                    string where = string.Join("、", f.RelativeDirs);
                    // 仅当 UUS 目录根本不存在时，说明这是旧版布局，无需列出 UUS 候选
                    if (!Directory.Exists(Path.Combine(SystemRoot, Path.Combine("UUS", UpdateFiles.NativeArchFolder))))
                        where = UpdateFiles.RawSystem32();

                    detail.AppendLine(f.FileName + " 缺失（已查找：" + where + "）");
                    count++;
                }
            }

            bool hasProblem = count > 0;
            return new IssueItem
            {
                Id = IssueIds.Files,
                Title = hasProblem ? "核心更新文件缺失或被改名" : "核心组件文件完整",
                Description = hasProblem
                    ? "检测到 " + count + " 个文件缺失或被改名，将尝试从组件存储恢复。"
                    : "更新所需的关键文件均存在。",
                Detail = hasProblem ? detail.ToString().Trim() : null,
                Severity = hasProblem ? IssueSeverity.Problem : IssueSeverity.Ok,
                NeedsFix = hasProblem,
                CanAutoFix = true,
                // 检测阶段先给出通用建议；若修复时确认组件存储中也没有源，
                // RepairService 会写入更具体的 FollowUpHint（含 DISM/SFC 命令）。
                ManualHint = hasProblem
                    ? "若本工具无法恢复，可先以管理员身份依次运行 DISM /Online /Cleanup-Image /RestoreHealth 与 sfc /scannow，再重新检测。"
                    : null
            };
        }
    }
}
