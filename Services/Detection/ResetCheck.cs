// 更新缓存状态检测。
using System.IO;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Detection
{
    /// <summary>
    /// 检测 SoftwareDistribution / catroot2 缓存目录状态。
    ///
    /// 缓存体积偏大本身不是故障：更新下载完补丁后 SoftwareDistribution 超过 500MB 很常见，
    /// 而重置缓存需重新下载全部更新元数据，代价较大。故体积偏大仅作提示（NeedsFix=false）；
    /// 真正需要重置的是目录缺失/损坏。
    /// </summary>
    public class ResetCheck : ICheckItem
    {
        public string ProgressText { get { return "正在检查更新缓存…"; } }

        private static string SystemRoot
        {
            get { return UpdateFiles.SystemRoot; }
        }

        public IssueItem Check()
        {
            string softwareDistribution = Path.Combine(SystemRoot, "SoftwareDistribution");
            string catroot2 = Path.Combine(SystemRoot, @"System32\catroot2");

            bool sdMissing = !Directory.Exists(softwareDistribution);
            bool catMissing = !Directory.Exists(catroot2);

            long sizeMB = 0;
            if (!sdMissing) sizeMB = DirSizeMB(softwareDistribution);

            if (sdMissing || catMissing)
            {
                // 区分严重程度：
                //   SoftwareDistribution 缺失/损坏 → 确实需要重置（Problem，可自动修）
                //   仅 catroot2 缺失              → 部分精简镜像或首次启动尚未初始化时
                //                                   会短暂不存在，不足以判定故障，
                //                                   仅提示（Warning），不自动移走缓存目录。
                if (sdMissing)
                {
                    return new IssueItem
                    {
                        Id = IssueIds.Reset,
                        Title = "更新缓存需重置",
                        Description = "更新缓存目录缺失或损坏，将重置并重新注册更新 DLL。",
                        Detail = "SoftwareDistribution 缺失" +
                                 (catMissing ? "\ncatroot2 缺失" : ""),
                        Severity = IssueSeverity.Problem,
                        NeedsFix = true,
                        CanAutoFix = true
                    };
                }

                return new IssueItem
                {
                    Id = IssueIds.Reset,
                    Title = "catroot2 目录缺失（暂不建议自动重置）",
                    Description = "未找到 catroot2 目录。部分精简系统或刚安装的系统在首次使用前" +
                                  "可能尚未创建该目录，通常不影响更新。如更新报错再考虑重置缓存。",
                    Detail = "catroot2 缺失",
                    Severity = IssueSeverity.Warning,
                    NeedsFix = false,
                    CanAutoFix = false,
                    ManualHint = "如更新反复失败，可在停止 wuauserv 与 cryptsvc 后手动重置 catroot2。"
                };
            }

            // 说明：缓存体积偏大“不是故障”。更新进行中或刚下载完补丁时，
            // SoftwareDistribution 超过 500MB 相当常见，而重置缓存需要重新下载全部
            // 更新元数据，代价不小。因此这里只作为提示信息展示（NeedsFix=false），
            // 不进入修复流程；真正需要重置的场景是目录缺失/损坏，已在上面处理。
            bool big = sizeMB > 500;
            return new IssueItem
            {
                Id = IssueIds.Reset,
                Title = "更新缓存状态正常",
                Description = big
                    ? "SoftwareDistribution 体积偏大（约 " + sizeMB + " MB）。这通常不是故障，" +
                      "仅当更新反复失败时建议手动重置缓存。"
                    : "SoftwareDistribution 缓存体积正常。",
                Severity = IssueSeverity.Ok,
                NeedsFix = false,
                CanAutoFix = false,
                ManualHint = big
                    ? "如更新反复失败，可先停止 wuauserv 后手动重命名 SoftwareDistribution 目录。"
                    : null
            };
        }

        private static long DirSizeMB(string dir)
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total / (1024 * 1024);
            }
            catch { return 0; }
        }
    }
}
