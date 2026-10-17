// Windows Update 组件检测。
// 顺序执行 9 个检测项（compatibility / policy / services / recovery / files /
// reset / tasks / settings-app / activation）。
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services
{
    public class DiagnosticService
    {
        private static string SystemRoot
        {
            get { return UpdateFiles.SystemRoot; }
        }

        /// <summary>
        /// 本次检测使用的系统信息。
        /// 统一取自 SystemCompat.Current（全局缓存），不在本类内单独缓存：
        /// 系统版本在一次运行内不变，且版本探测只在此处实现。
        /// </summary>
        private SystemInfo SystemInformation
        {
            get { return SystemCompat.Current; }
        }

        /// <summary>顺序执行 9 个检测项，通过回调报告进度。</summary>
        public Task RunAsync(Action<string> onProgress, Action<IssueItem> onItemFound)
        {
            return Task.Run(() =>
            {
                // 兼容性放在第一项：其结论决定后续各项是否可信
                Check(onProgress, onItemFound, "正在检查系统版本兼容性…", CheckCompatibility);
                Check(onProgress, onItemFound, "正在检查更新页面策略…", CheckPolicy);
                Check(onProgress, onItemFound, "正在检查更新服务状态…", CheckServices);
                Check(onProgress, onItemFound, "正在检查服务自动恢复配置…", CheckRecovery);
                Check(onProgress, onItemFound, "正在检查核心组件文件…", CheckFiles);
                Check(onProgress, onItemFound, "正在检查更新缓存…", CheckReset);
                Check(onProgress, onItemFound, "正在检查计划任务…", CheckTasks);
                Check(onProgress, onItemFound, "正在检查“设置”应用…", CheckSettingsApp);
                Check(onProgress, onItemFound, "正在检查系统激活状态…", CheckActivation);
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

        // ---- 6.0 compatibility ----
        // 判定系统版本，并据此确认本工具是否适用。
        // 关键：Windows 11 的注册表 ProductName 仍写作 "Windows 10"（微软已知问题），
        // 因此版本判定基于构建号：>= 22000 为 Windows 11。
        // 该项同时为 CheckFiles 提供“更新组件应位于哪个目录”的判断依据。
        private IssueItem CheckCompatibility()
        {
            var info = SystemInformation;

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

        // ---- 6.1 policy ----
        // 真正会“隐藏/禁用设置里的 Windows 更新页面”的，只有两类：
        //   1) SettingsPageVisibility 包含 windowsupdate（且为 hide 或 showonly 未列出该页）
        //   2) NoWindowsUpdate == 1
        // 注意：HKLM/HKCU 下的 ...\Policies\Microsoft\Windows\WindowsUpdate 策略键“存在”
        // 本身并不代表更新页面被隐藏（它常被延迟/驱动策略等正常使用），因此不能据此判坏。
        private IssueItem CheckPolicy()
        {
            var found = new StringBuilder();
            bool bad = false;

            bad |= HasHiddenUpdatePage(RegistryHive.CurrentUser, found);
            bad |= HasHiddenUpdatePage(RegistryHive.LocalMachine, found);
            bad |= HasNoWindowsUpdate(RegistryHive.CurrentUser, found);
            bad |= HasNoWindowsUpdate(RegistryHive.LocalMachine, found);

            return new IssueItem
            {
                Id = IssueIds.Policy,
                Title = bad ? "更新页面被策略隐藏" : "更新页面策略正常",
                Description = bad
                    ? "检测到系统设置中“Windows 更新”页面被隐藏或禁用。"
                    : "未检测到隐藏或禁用更新页面的策略。",
                Detail = bad ? found.ToString().Trim() : null,
                Severity = bad ? IssueSeverity.Problem : IssueSeverity.Ok,
                NeedsFix = bad,
                CanAutoFix = bad,
                ManualHint = null
            };
        }

        private const string ExplorerPoliciesPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        /// <summary>
        /// 判断 SettingsPageVisibility 是否真的把“Windows 更新”页隐藏了。
        /// 值形如 "hide:windowsupdate" 或 "showonly:about;...".
        /// </summary>
        private bool HasHiddenUpdatePage(RegistryHive hive, StringBuilder found)
        {
            string value = ReadStringValue(hive, ExplorerPoliciesPath, "SettingsPageVisibility");
            if (string.IsNullOrEmpty(value)) return false;

            string v = value.Trim();

            // hide:... → 列出被隐藏的页；含 windowsupdate 即被隐藏
            if (v.StartsWith("hide", StringComparison.OrdinalIgnoreCase))
            {
                if (v.IndexOf("windowsupdate", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.AppendLine(HiveLabel(hive) + " SettingsPageVisibility=" + value + "（隐藏了更新页面）");
                    return true;
                }
                return false;
            }

            // showonly:... → 只显示列表中的页；未列出 windowsupdate 即为隐藏
            if (v.StartsWith("showonly", StringComparison.OrdinalIgnoreCase))
            {
                if (v.IndexOf("windowsupdate", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    found.AppendLine(HiveLabel(hive) + " SettingsPageVisibility=" + value + "（仅显示部分页，未包含更新页面）");
                    return true;
                }
                return false;
            }

            // 其他格式：保守起见，只有明确出现 windowsupdate 才认为是相关设置，但不判隐藏
            return false;
        }

        /// <summary>NoWindowsUpdate 值 == 1 时才算禁用更新页面。</summary>
        private bool HasNoWindowsUpdate(RegistryHive hive, StringBuilder found)
        {
            object raw = ReadValue(hive, ExplorerPoliciesPath, "NoWindowsUpdate");
            if (raw == null) return false;

            int val;
            try { val = Convert.ToInt32(raw); }
            catch { return false; }

            if (val == 1)
            {
                found.AppendLine(HiveLabel(hive) + " NoWindowsUpdate=1（禁用更新页面）");
                return true;
            }
            return false;
        }

        private static string HiveLabel(RegistryHive hive)
        {
            return hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\";
        }

        private static object ReadValue(RegistryHive hive, string path, string name)
        {
            try
            {
                // 显式使用原生视图：x86 进程的 RegistryView.Default 会被重定向到
                // SOFTWARE\WOW6432Node，那里通常没有这些策略值，会导致漏检。
                RegistryView view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Registry32;

                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var k = root.OpenSubKey(path))
                {
                    if (k != null) return k.GetValue(name);
                }
            }
            catch { }
            return null;
        }

        private static string ReadStringValue(RegistryHive hive, string path, string name)
        {
            object v = ReadValue(hive, path, name);
            return v == null ? null : v.ToString();
        }

        // ---- 6.2 services ----
        private IssueItem CheckServices()
        {
            // 仅当 Start=4（禁用）判为异常；
            // 读取失败（StartValueUnknown）不再静默当作正常，而是单独列出并提示无法判定，
            // 避免非英文系统上“服务被禁用却报告状态正常”的结论错误。
            string[] svcs = { "wuauserv", "BITS", "UsoSvc", "DoSvc", "CryptSvc", "TrustedInstaller", "WaaSMedicSvc", "msiserver" };
            var detail = new StringBuilder();
            bool hasProblem = false;
            var unreadable = new System.Collections.Generic.List<string>();

            foreach (var s in svcs)
            {
                int v = ServiceHelper.GetStartValue(s);
                if (v == ServiceHelper.StartValueUnknown)
                {
                    unreadable.Add(s);
                    continue;
                }
                if (v == 4)
                {
                    detail.AppendLine(s + " 被禁用");
                    hasProblem = true;
                }
            }

            // 有服务无法读取：不能断言“状态正常”，但也不可自动修复（无依据），
            // 因此判为 Warning + 需人工确认、不可自动修复。
            if (unreadable.Count > 0)
            {
                detail.AppendLine("无法读取启动类型（可能不存在或无权限）：" + string.Join("、", unreadable.ToArray()));
            }

            bool unknown = unreadable.Count > 0;

            return new IssueItem
            {
                Id = IssueIds.Services,
                Title = hasProblem ? "部分更新服务被禁用"
                      : unknown ? "部分更新服务状态无法确认"
                      : "更新服务状态正常",
                Description = hasProblem
                    ? "部分关键服务已被禁用，将恢复为系统默认值。"
                    : unknown
                        ? "无法读取以下服务的启动类型，请确认这些服务在本系统中是否存在。"
                        : "Windows Update、BITS、UsoSvc 等服务启动类型正常。",
                // 仅在“确有禁用”时展示明细，避免无问题时也显示一串读不到的项
                Detail = (hasProblem || unknown) ? detail.ToString().Trim() : null,
                Severity = hasProblem ? IssueSeverity.Problem
                         : unknown ? IssueSeverity.Warning
                         : IssueSeverity.Ok,
                NeedsFix = hasProblem,
                CanAutoFix = hasProblem
            };
        }

        // ---- 6.3 recovery ----
        private IssueItem CheckRecovery()
        {
            string[] svcs = { "wuauserv", "bits", "UsoSvc", "DoSvc", "CryptSvc" };
            var detail = new StringBuilder();
            bool hasProblem = false;
            foreach (var s in svcs)
            {
                if (!ServiceHelper.HasFailureActions(s))
                {
                    detail.AppendLine(s + " 未配置失败恢复");
                    hasProblem = true;
                }
            }
            return new IssueItem
            {
                Id = IssueIds.Recovery,
                Title = hasProblem ? "服务自动恢复未配置" : "服务自动恢复已配置",
                Description = hasProblem
                    ? "部分服务失败后不会自动恢复，将配置为自动重启。"
                    : "关键服务失败后会自动重启。",
                Detail = hasProblem ? detail.ToString().Trim() : null,
                Severity = hasProblem ? IssueSeverity.Warning : IssueSeverity.Ok,
                NeedsFix = hasProblem,
                // 此处只表示“可修复”；是否真的执行由 NeedsFix 决定
                CanAutoFix = true
            };
        }

        // ---- 6.4 files ----
        private IssueItem CheckFiles()
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

        // ---- 6.5 reset ----
        private IssueItem CheckReset()
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

        // ---- 6.6 tasks ----
        private IssueItem CheckTasks()
        {
            // 输出每个被禁用任务的“路径\名称”，便于用户知道具体是哪些任务。
            string script =
                "$paths=@('\\Microsoft\\Windows\\InstallService*','\\Microsoft\\Windows\\UpdateOrchestrator*'," +
                "'\\Microsoft\\Windows\\UpdateAssistant*','\\Microsoft\\Windows\\WaaSMedic*'," +
                "'\\Microsoft\\Windows\\WindowsUpdate*','\\Microsoft\\WindowsUpdate*');" +
                "foreach($p in $paths){Get-ScheduledTask -TaskPath $p -ErrorAction SilentlyContinue|" +
                "Where-Object{$_.State -eq 'Disabled'}|ForEach-Object{Write-Output ($_.TaskPath + $_.TaskName)}}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 60000);

            // 脚本本身失败（执行策略、超时、任务计划服务异常）时，输出为空，
            // 不能据此断言“任务均正常”，否则会像服务检测那样漏掉真实问题。
            bool unreadable = r.Code != 0 || r.TimedOut;

            var disabledNames = new System.Collections.Generic.List<string>();
            if (!unreadable && !string.IsNullOrEmpty(r.StdOut))
            {
                foreach (var line in r.StdOut.Split(new[] { '\r', '\n' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length > 0) disabledNames.Add(t);
                }
            }

            int disabled = disabledNames.Count;
            bool bad = disabled > 0;

            string detail = null;
            if (bad)
            {
                var sb = new StringBuilder();
                sb.AppendLine("被禁用任务（" + disabled + " 个）：");
                foreach (var n in disabledNames) sb.AppendLine("• " + n);
                detail = sb.ToString().Trim();
            }
            else if (unreadable)
            {
                detail = r.TimedOut
                    ? "查询计划任务超时，未能确认其启用状态。"
                    : "查询计划任务失败（退出码 " + r.Code + "），未能确认其启用状态。";
            }

            // 三态：查不到 → Warning 且不可自动修复（无依据）；查到禁用 → Warning 可修；否则 Ok
            return new IssueItem
            {
                Id = IssueIds.Tasks,
                Title = bad ? "部分更新计划任务被禁用"
                      : unreadable ? "更新计划任务状态无法确认"
                      : "更新计划任务状态正常",
                Description = bad
                    ? "检测到 " + disabled + " 个任务被禁用，将重新启用。"
                    : unreadable
                        ? "无法读取计划任务状态，请手动检查任务计划程序中与 Windows Update 相关的任务。"
                        : "UpdateOrchestrator 相关任务均已启用。",
                Detail = detail,
                Severity = bad ? IssueSeverity.Warning
                         : unreadable ? IssueSeverity.Warning
                         : IssueSeverity.Ok,
                NeedsFix = bad,
                CanAutoFix = bad
            };
        }

        // ---- 6.7 settings-app ----
        private IssueItem CheckSettingsApp()
        {
            // 用 -AllUsers 查询系统级安装状态，与 FixSettingsApp 的口径保持一致。
            // 原实现不带 -AllUsers，只查当前用户：若当前用户从未初始化“设置”应用，
            // 系统级已安装的包也查不到，会被误判为“需重新注册”。
            // 输出约定：FOUND=系统中存在该包；NOTFOUND=系统不含该组件。
            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command " +
                "\"$p=@(Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers -ErrorAction SilentlyContinue);" +
                "if($p.Count -gt 0){Write-Output 'FOUND'}else{Write-Output 'NOTFOUND'}\"", 30000);

            // PowerShell 本身失败 → 无法确认，按“读不到”处理，不判为需修复
            if (r.Code != 0 || r.TimedOut)
            {
                return new IssueItem
                {
                    Id = IssueIds.SettingsApp,
                    Title = "“设置”应用状态无法确认",
                    Description = "查询 Appx 包失败，未能确认“设置”应用是否正常。",
                    Severity = IssueSeverity.Warning,
                    NeedsFix = false,
                    CanAutoFix = false
                };
            }

            string output = r.StdOut ?? "";
            bool found = output.IndexOf("FOUND", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!found)
            {
                // 该 SKU（LTSB/LTSC、Server 等）本就不包含“设置”应用，
                // 不应判为需要修复，否则会产生始终无法消除的告警。
                return new IssueItem
                {
                    Id = IssueIds.SettingsApp,
                    Title = "本系统不包含“设置”应用（无需处理）",
                    Description = "系统中未找到 windows.immersivecontrolpanel 包，" +
                                  "该版本 Windows（如 LTSB/LTSC 或 Server）不包含“设置”应用，属正常情况。",
                    Severity = IssueSeverity.Ok,
                    NeedsFix = false,
                    CanAutoFix = false
                };
            }

            return new IssueItem
            {
                Id = IssueIds.SettingsApp,
                Title = "“设置”应用状态正常",
                Description = "系统设置应用已正确注册。",
                Severity = IssueSeverity.Ok,
                NeedsFix = false,
                CanAutoFix = true
            };
        }

        // 说明：本工具只负责修复 Windows Update 组件本身，不再检测网络代理。
        // 代理属于用户或企业网络的正常配置，把“存在代理”当作问题会误伤正常系统，
        // 且原先的修复动作（netsh winhttp reset proxy）会改写用户的网络设置，
        // 风险与收益不成比例，故整项移除。

        // ---- 6.8 activation ----
        private IssueItem CheckActivation()
        {
            // 优先用 SoftwareLicensingProduct（与 slmgr /xpr、设置里的激活状态一致）。
            // LicenseStatus: 1=已激活；0=未授权；2=宽限期；3=额外宽限；4=非正版宽限；5=通知；6=延长宽限。
            int status = QueryActivationStatus();

            // 注册表 LicenseStatus 常不存在，仅作兜底（取不到则保持 -1）。
            if (status < 0)
            {
                try
                {
                    RegistryView view = Environment.Is64BitOperatingSystem
                        ? RegistryView.Registry64
                        : RegistryView.Registry32;

                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = root.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform"))
                    {
                        if (k != null)
                        {
                            object v = k.GetValue("LicenseStatus");
                            if (v != null) status = Convert.ToInt32(v);
                        }
                    }
                }
                catch { }
            }

            // 三态处理，避免把“宽限期”或“读不到”直接说成“未激活”：
            //   1      → 已激活（Ok）
            //   2/3/6  → 宽限期：系统仍属已激活，只是许可证需刷新/联网（Ok，不提示去激活）
            //   0      → 未授权：确需激活（Warning + NeedsFix）
            //   4/5    → 非正版/通知模式：需处理（Warning + NeedsFix）
            //   -1     → 读取失败：无法确认，不能断言未激活（Warning，不可自动修复）
            bool activated = status == 1;
            bool grace = status == 2 || status == 3 || status == 6;
            bool unknown = status < 0;
            bool unlicensed = status == 0 || status == 4 || status == 5;

            string title;
            if (activated) title = "Windows 已激活";
            else if (grace) title = "Windows 已激活（许可证处于宽限期）";
            else if (unknown) title = "无法确认激活状态";
            else title = "Windows 未激活";

            return new IssueItem
            {
                Id = IssueIds.Activation,
                Title = title,
                Description = activated
                    ? "系统激活状态正常。"
                    : grace
                        ? "系统已激活，但许可证处于宽限期，通常联网后会自动恢复。不影响更新组件的修复。"
                        : unknown
                            ? "未能读取激活状态（WMI/注册表查询均失败），这不代表系统未激活。可运行 slmgr /xpr 手动确认。"
                            : "未激活可能影响部分更新，请先激活系统。",
                Detail = (activated || grace) ? null : ActivationStatusText(status),
                Severity = activated || grace
                    ? IssueSeverity.Ok
                    : IssueSeverity.Warning,
                // 只有“确实未授权/非正版”才需要处理；宽限期与读取失败都不进修复流程
                NeedsFix = unlicensed,
                CanAutoFix = false,
                ManualHint = unlicensed ? "请先激活系统，否则部分更新无法接收。" : null
            };
        }

        /// <summary>
        /// 通过 WMI/CIM 查询已激活许可证状态。返回 LicenseStatus（1=已激活），
        /// 查询失败或无匹配产品时返回 -1。
        /// </summary>
        private int QueryActivationStatus()
        {
            string script =
                "$p=Get-CimInstance SoftwareLicensingProduct " +
                "-Filter \"PartialProductKey IS NOT NULL AND ApplicationId='55c92734-d682-4d71-983e-d6ec3f16059f'\" " +
                "-ErrorAction SilentlyContinue | Select-Object -First 1;" +
                "if($p){Write-Output $p.LicenseStatus}else{Write-Output -1}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 30000);
            if (r.Code != 0) return -1;

            int status;
            if (int.TryParse((r.StdOut ?? "").Trim(), out status)) return status;
            return -1;
        }

        /// <summary>把 LicenseStatus 数字转成用户可读的说明（用于未激活时的 Detail）。</summary>
        private static string ActivationStatusText(int status)
        {
            switch (status)
            {
                case 0: return "许可证状态：未授权（LicenseStatus=0）";
                case 2: return "许可证状态：处于宽限期（LicenseStatus=2）";
                case 3: return "许可证状态：处于额外宽限（LicenseStatus=3）";
                case 4: return "许可证状态：非正版宽限期（LicenseStatus=4）";
                case 5: return "许可证状态：通知模式（LicenseStatus=5）";
                case 6: return "许可证状态：延长宽限期（LicenseStatus=6）";
                case -1: return "无法读取激活状态";
                default: return "许可证状态未知（LicenseStatus=" + status + "）";
            }
        }
    }
}
