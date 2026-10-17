
// 修复执行：遍历需修复项并逐项执行。
using System;
using System.IO;
using System.Threading.Tasks;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services
{
    public class RepairService
    {
        /// <summary>
        /// 非致命失败通知回调：某个修复项执行失败时回调给界面（状态栏），由页面注入。
        /// </summary>
        public Action<string> RepairStepFailed;

        /// <summary>
        /// 收尾的 gpupdate 是否成功。null 表示尚未执行完毕。
        /// 该步骤不计入各项修复的成功/失败，但失败时会影响“建议重启生效”的可信度，
        /// 因此需要让结果页能看到，而不是仅在底部状态栏短暂显示。
        /// </summary>
        public bool? GroupPolicyRefreshed { get; private set; }

        private static string SystemRoot
        {
            // 统一由 SystemCompat 提供，避免与其它模块各自读取环境变量
            get { return SystemCompat.SystemRoot; }
        }

        /// <summary>顺序遍历 AppState.Issues，对 CanAutoFix=true 的项执行修复。</summary>
        public Task RunAsync(Action<IssueItem> onItemUpdated)
        {
            return Task.Run(() =>
            {
                foreach (var item in AppState.Issues)
                {
                    // 不可自动修复的问题项：标记为 Skipped（界面显示“需手动处理”），
                    // 由结果页列入“需要手动处理”分组。
                    if (item.NeedsFix && !item.CanAutoFix)
                    {
                        SetState(onItemUpdated, item, FixState.Skipped);
                        continue;
                    }

                    // 健康项或不可修项直接忽略，不进入修复流程
                    if (!(item.NeedsFix && item.CanAutoFix)) continue;

                    SetState(onItemUpdated, item, FixState.Running);
                    bool ok;
                    try
                    {
                        ok = DispatchFix(item.Id);
                    }
                    catch
                    {
                        ok = false;
                        // 执行中抛出异常：各 Fix 内部的步骤级通知可能未执行到，这里补一条通用提示
                        var cb = RepairStepFailed;
                        if (cb != null) cb("修复项执行异常：" + item.Title);
                    }

                    // 修复失败时补上“后续建议”，让结果页能展示，
                    // 而不是仅在底部状态栏短暂显示。
                    if (!ok) item.FollowUpHint = FollowUpHintFor(item.Id);

                    SetState(onItemUpdated, item, ok ? FixState.Done : FixState.Failed);
                }
                // 收尾：刷新组策略
                // 结果记录到 GroupPolicyRefreshed，供结果页展示；不计入各修复项的成败。
                GroupPolicyRefreshed = ShellRunner.RunExpectSuccess(
                    "gpupdate.exe", "/force", "刷新组策略（gpupdate）", 60000);
            });
        }

        private void SetState(Action<IssueItem> cb, IssueItem item, FixState state)
        {
            item.State = state;
            if (cb != null) cb(item);
        }

        /// <summary>
        /// 某个修复项失败后，给用户的后续建议（显示在结果页的“需要手动处理”分组中）。
        ///
        /// 说明：本工具全程不联网，也不会代替用户执行 DISM——修复组件存储需要联网
        /// 从 Windows Update 获取源，耗时且可能失败。因此这里只给出可直接照做的
        /// 命令，由用户自行决定是否执行。
        /// </summary>
        private string FollowUpHintFor(string id)
        {
            switch (id)
            {
                case IssueIds.Files:
                    string list = "";
                    if (_missingSourceFiles != null && _missingSourceFiles.Count > 0)
                    {
                        list = "组件存储中缺少以下文件的源：\n   " +
                               string.Join("\n   ", _missingSourceFiles.ToArray()) + "\n\n";
                    }
                    return list +
                           "请以管理员身份依次运行以下命令，完成后重新用本工具检测：\n" +
                           "   DISM /Online /Cleanup-Image /RestoreHealth\n" +
                           "   sfc /scannow\n" +
                           "若仍无法修复，请使用与当前系统版本一致的 Windows ISO 执行就地升级。";

                case IssueIds.Reset:
                    return "更新缓存重置未完全成功。请重启电脑后重新运行本工具；" +
                           "若仍失败，可先手动确认 wuauserv、bits、CryptSvc 能否正常启动。\n\n" +
                           "提示：重置缓存会把缓存目录移动到 SoftwareDistribution.old / catroot2.old，" +
                           "这部分改动不在“注册表策略备份”的恢复范围内。" +
                           "若需撤销，可停止 wuauserv、bits、CryptSvc 后，把上述 .old 目录改回原名" +
                           "（先移走现有的同名目录）。";

                case IssueIds.Tasks:
                    return "计划任务未能全部启用。可打开“任务计划程序”，" +
                           "检查 Microsoft\\Windows\\UpdateOrchestrator 等路径下的任务是否被禁用。";

                case IssueIds.SettingsApp:
                    return "“设置”应用未能重新注册。可在 PowerShell 中手动执行：\n" +
                           "   Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers | " +
                           "ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register " +
                           "\"$($_.InstallLocation)\\AppXManifest.xml\" }";

                case IssueIds.Services:
                case IssueIds.Recovery:
                    return "服务配置未能完全恢复。请确认相关服务未被安全软件拦截，" +
                           "重启后可重新运行本工具再试。";

                case IssueIds.Policy:
                    return "策略未能完全移除。可能是域策略或安全软件重新写入了这些值，" +
                           "请确认当前计算机未受组策略统一管理。";

                default:
                    return null;
            }
        }

        private bool DispatchFix(string id)
        {
            switch (id)
            {
                case IssueIds.Policy: return FixPolicy();
                case IssueIds.Services: return FixServices();
                case IssueIds.Recovery: return FixRecovery();
                case IssueIds.Files: return FixFiles();
                case IssueIds.Reset: return FixReset();
                case IssueIds.Tasks: return FixTasks();
                case IssueIds.SettingsApp: return FixSettingsApp();
                default: return true; // activation 等不可修项不会走到这里
            }
        }

        // ---- 6.1 policy：删除导致更新页面被隐藏的具体策略值 ----
        // 只删除真正会造成“隐藏/禁用更新页面”的值，不再无差别删除整个
        // ...\Policies\Microsoft\Windows\WindowsUpdate 策略键（该键可能承载正常的
        // 延迟更新、驱动策略等设置，删除会误伤）。
        //
        // 关键：reg delete 删除“不存在的值”会返回非 0，若直接 ok &= 会误判失败。
        // 检测阶段只要有任意一处命中即判需修复，但四处值通常只存在其中一两处，
        // 其余三处本就不存在——按退出码判定必然把“已修复”报成“失败”。
        // 因此这里改为：先判断值是否存在，仅删除存在的；删除后再复查确已消失。
        private bool FixPolicy()
        {
            bool ok = true;

            // 与 DiagnosticService.HasHiddenUpdatePage / HasNoWindowsUpdate 读取的路径一致
            const string ExplorerPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

            ok &= DeletePolicyValue(Microsoft.Win32.RegistryHive.LocalMachine, ExplorerPath, "SettingsPageVisibility");
            ok &= DeletePolicyValue(Microsoft.Win32.RegistryHive.CurrentUser, ExplorerPath, "SettingsPageVisibility");
            ok &= DeletePolicyValue(Microsoft.Win32.RegistryHive.LocalMachine, ExplorerPath, "NoWindowsUpdate");
            ok &= DeletePolicyValue(Microsoft.Win32.RegistryHive.CurrentUser, ExplorerPath, "NoWindowsUpdate");

            return ok;
        }

        /// <summary>
        /// 删除指定注册表值，并以“删除后该值确实不存在”判定成功。
        /// 值本就不存在 → 视为成功（无需删除），不会因 reg delete 返回非 0 而误判。
        /// </summary>
        private bool DeletePolicyValue(Microsoft.Win32.RegistryHive hive, string path, string name)
        {
            // 不存在即无需处理
            if (!RegistryValueExists(hive, path, name)) return true;

            string hiveName = hive == Microsoft.Win32.RegistryHive.LocalMachine ? "HKLM" : "HKCU";
            ShellRunner.Run("reg.exe",
                "delete \"" + hiveName + "\\" + path + "\" /v " + name + " /f", 15000);

            // 以最终状态为准：删除后复查，确认该值已消失
            bool stillExists = RegistryValueExists(hive, path, name);
            if (stillExists)
            {
                var cb = RepairStepFailed;
                if (cb != null) cb("未能删除策略值 " + hiveName + "\\" + name + "。");
            }
            return !stillExists;
        }

        /// <summary>
        /// 判断注册表值是否存在（不关心其内容）。
        /// 显式使用原生视图：x86 进程的 RegistryView.Default 会重定向到 WOW6432Node，
        /// 那里通常没有这些策略值，会导致判断错误。
        /// </summary>
        private static bool RegistryValueExists(Microsoft.Win32.RegistryHive hive, string path, string name)
        {
            try
            {
                Microsoft.Win32.RegistryView view = Environment.Is64BitOperatingSystem
                    ? Microsoft.Win32.RegistryView.Registry64
                    : Microsoft.Win32.RegistryView.Registry32;

                using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view))
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return false;
                    return k.GetValue(name) != null;
                }
            }
            catch
            {
                // 读取失败时保守返回 true：交给删除动作去尝试，
                // 避免“读不到就当作不存在”从而跳过真正需要删除的值。
                return true;
            }
        }

        // ---- 6.2 services：以 sc config 为权威来源设置启动类型 ----
        //
        // 关于“服务不存在”的处理（与 FixSettingsApp 的 NOPKG 特判对齐）：
        // 不同 Windows 版本 / 精简系统上，部分服务可能本就不存在
        // （例如较新版本才引入的 WaaSMedicSvc、或某些镜像裁掉了 DoSvc）。
        // 对不存在的服务执行 sc config 会返回非 0，若直接 ok &= 会把
        // “无需修复”误判为“修复失败”。因此这里先判断服务是否存在：
        //   不存在 → 跳过，不计入成败；存在 → 照常设置并以结果累计。
        private bool FixServices()
        {
            bool ok = true;
            const string sdset = "D:(A;;CCLCSWRPLORC;;;AU)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPWPDTLOCRRC;;;SY)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

            // wuauserv：启动类型 + 安全描述符，二者须同时成功才算整体成功
            if (ServiceHelper.ServiceExists("wuauserv"))
            {
                ok &= ServiceHelper.SetStartType("wuauserv", "demand");
                ok &= ServiceHelper.SetSecurity("wuauserv", sdset);
            }

            SetStartTypeIfExists(ref ok, "bits", "demand");

            SetStartTypeIfExists(ref ok, "UsoSvc", "auto");

            SetStartTypeIfExists(ref ok, "DoSvc", "delayed-auto");

            SetStartTypeIfExists(ref ok, "CryptSvc", "auto");

            SetStartTypeIfExists(ref ok, "msiserver", "demand");

            SetStartTypeIfExists(ref ok, "TrustedInstaller", "demand");

            // WaaSMedicSvc 的 FailureActions 二进制必须沿用 reg add，Start 值一并走注册表。
            // 该服务在较旧版本 Windows 上可能不存在，故同样先判存在性。
            if (ServiceHelper.ServiceExists("WaaSMedicSvc"))
            {
                ok &= ServiceHelper.SetStartRegistry("WaaSMedicSvc", 3);
                ServiceHelper.SetFailureActionsBinary("WaaSMedicSvc",
                    "840300000000000000000000030000001400000001000000c0d4010001000000e09304000000000000000000");
            }

            return ok;
        }

        /// <summary>
        /// 服务存在时才设置启动类型；不存在则跳过（不计失败）。
        /// 供 FixServices 内多处复用，避免重复写存在性判断。
        /// </summary>
        private static void SetStartTypeIfExists(ref bool ok, string service, string startType)
        {
            if (!ServiceHelper.ServiceExists(service)) return;
            ok &= ServiceHelper.SetStartType(service, startType);
        }

        // ---- 6.3 recovery：5 服务 sc failure ----
        // 同样跳过本机不存在的服务：对不存在的服务执行 sc failure 必然返回非 0，
        // 不处理会把“无需配置”误判成“修复失败”。
        private bool FixRecovery()
        {
            string[] svcs = { "wuauserv", "bits", "UsoSvc", "DoSvc", "CryptSvc" };
            bool ok = true;
            foreach (var s in svcs)
            {
                if (!ServiceHelper.ServiceExists(s)) continue;
                ok &= ServiceHelper.SetFailureActions(s);
            }
            return ok;
        }

        /// <summary>
        /// 最近一次 FixFiles 中“组件存储也无法提供源”的文件名。
        /// 供 RunAsync 写入 FollowUpHint，让用户知道具体缺哪些文件，
        /// 而不是只看到一个数字。
        /// </summary>
        private System.Collections.Generic.List<string> _missingSourceFiles =
            new System.Collections.Generic.List<string>();

        // ---- 6.4 files ----
        private bool FixFiles()
        {
            bool ok = true;

            // 记录“组件存储中也没有源”的文件：这类文件无法靠本工具恢复，
            // 需要用户手动运行 DISM/SFC 先行修复组件存储。
            var missingSource = new System.Collections.Generic.List<string>();

            foreach (var n in UpdateFiles.RenamedDllBaseNames)
                ok &= WinSxSRestore.RestoreRenamedDll(n);

            // 每个文件还原到它的“首选候选目录”（UUS\<arch>\ 优先，其次 System32），
            // 即检测时认为它应该在的位置，避免把文件塞进并不承载它的目录。
            foreach (var f in UpdateFiles.RequiredFiles)
            {
                var outcome = WinSxSRestore.RestoreFile(f);
                if (outcome == RestoreOutcome.SourceNotFound)
                {
                    missingSource.Add(f.FileName);
                    ok = false;
                }
                else if (outcome == RestoreOutcome.Failed)
                {
                    ok = false;
                }
            }

            // 传出明细，供 RunAsync 拼进 FollowUpHint
            _missingSourceFiles = missingSource;

            if (missingSource.Count > 0)
            {
                var cb = RepairStepFailed;
                if (cb != null)
                    cb("组件存储中缺少以下文件的源，无法自动恢复：" +
                       string.Join("、", missingSource.ToArray()));
            }

            return ok;
        }

        // ---- 6.5 reset ----
        private bool FixReset()
        {
            // 关键步骤：缓存目录必须成功移走，否则重置无效，判整体失败。
            // regsvr32 属尽力而为（部分 DLL 系统内本就不存在），失败不判整体失败。
            bool ok = true;

            // 停止服务。记录未能停下的服务：若它们仍占用缓存目录，
            // Directory.Move 会失败，届时能告诉用户具体是哪个服务导致的，
            // 而不是只给出“目录未能移走”这一句泛泛的提示。
            string[] stop = { "wuauserv", "bits", "usosvc", "msiserver", "cryptsvc" };
            var notStopped = new System.Collections.Generic.List<string>();
            foreach (var s in stop)
            {
                if (!ServiceHelper.Stop(s)) notStopped.Add(s);
            }

            string softwareDistribution = Path.Combine(SystemRoot, "SoftwareDistribution");
            string softwareDistributionOld = Path.Combine(SystemRoot, "SoftwareDistribution.old");
            string catroot2 = Path.Combine(SystemRoot, @"System32\catroot2");
            string catroot2Old = Path.Combine(SystemRoot, @"System32\catroot2.old");

            TryDeleteDir(softwareDistributionOld);
            TryDeleteDir(catroot2Old);
            ok &= TryMove(softwareDistribution, softwareDistributionOld);
            ok &= TryMove(catroot2, catroot2Old);

            // 重新注册 21 个更新/加密相关 DLL（尽力而为，不参与结果判定）。
            //
            // 列表已按 Win10/11 现实精简：仅保留系统中实际存在、且注册有意义的组件。
            // 已移除三类在 Win10/11 上无意义的项：
            //   1) 系统中不存在的老组件（XP/Vista/7 遗留）：
            //      msxml.dll、wuaueng1.dll、wucltui.dll、wuweb.dll、wuwebv.dll、
            //      muweb.dll、qmgrprxy.dll、wucltux.dll；
            //   2) 已废弃的国家/地区加密 CSP：gpkcsp.dll、sccbase.dll、slbcsp.dll；
            //   3) 微软明确不再支持自行注册的 initpki.dll。
            // 另外 shell32.dll / ole32.dll / oleaut32.dll 亦不再注册：
            // 它们是系统核心组件，从不（也不应）通过 regsvr32 注册。
            // 旧版清单中这些项对不存在的文件恒失败、对核心组件则属误操作，
            // 保留只会造成“执行了大量操作”的错觉，并无实际效果。
            string[] dlls = { "atl.dll","urlmon.dll","mshtml.dll","shdocvw.dll","browseui.dll",
                "jscript.dll","vbscript.dll","scrrun.dll","msxml3.dll","msxml6.dll",
                "actxprxy.dll","softpub.dll","wintrust.dll","dssenh.dll","rsaenh.dll",
                "cryptdlg.dll","wuapi.dll","wuaueng.dll","wups.dll","wups2.dll",
                "qmgr.dll" };
            foreach (var d in dlls)
                ShellRunner.RunSilent("regsvr32.exe", "/s " + d, 30000);

            // 启动服务（cryptsvc/bits/wuauserv/usosvc/dosvc）。
            // ServiceHelper.Start 现在以“最终是否处于运行中”判定，
            // 不会因服务本就在运行（net start 返回 2）而误判失败。
            string[] start = { "cryptsvc", "bits", "wuauserv", "usosvc", "dosvc" };
            foreach (var s in start)
                ok &= ServiceHelper.Start(s);

            if (!ok)
            {
                string extra = "";
                if (notStopped.Count > 0)
                {
                    extra = "（未能停止的服务：" + string.Join("、", notStopped.ToArray()) +
                            "，可能仍占用缓存目录）";
                }
                var cb = RepairStepFailed;
                if (cb != null)
                    cb("更新缓存重置未完全成功，部分服务未能启动或缓存目录未能移走" + extra + "。");
            }
            return ok;
        }

        /// <summary>删除目录（不存在或删除失败都返回 true，用于清理 .old）。</summary>
        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        /// <summary>
        /// 移动目录。源目录不存在视为无需移动（成功）；源存在但移动失败返回 false。
        /// </summary>
        private static bool TryMove(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return true;
                Directory.Move(from, to);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---- 6.6 tasks ----
        private bool FixTasks()
        {
            string script =
                "$paths=@('\\Microsoft\\Windows\\InstallService*','\\Microsoft\\Windows\\UpdateOrchestrator*'," +
                "'\\Microsoft\\Windows\\UpdateAssistant*','\\Microsoft\\Windows\\WaaSMedic*'," +
                "'\\Microsoft\\Windows\\WindowsUpdate*','\\Microsoft\\WindowsUpdate*');" +
                "foreach($p in $paths){Get-ScheduledTask -TaskPath $p -ErrorAction SilentlyContinue|" +
                "Enable-ScheduledTask -ErrorAction SilentlyContinue|Out-Null}";
            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 120000);
            if (r.Code != 0)
            {
                var cb = RepairStepFailed;
                if (cb != null) cb("启用计划任务未成功完成（退出码 " + r.Code + "）");
            }
            return r.Code == 0;
        }

        // ---- 6.7 settings-app ----
        private bool FixSettingsApp()
        {
            // 注意：脚本内部不能无差别 catch 后静默继续，否则 powershell.exe 恒返回 0，
            // 本方法就会恒返回 true、界面恒显示“已修复”。这里改为：
            //   - 找到并成功注册的包数累加到 $ok
            //   - 结束时输出 OK / FAIL / NOPKG，并据此设置退出码
            // NOPKG：系统本就不含该组件（LTSB/LTSC、Server 等），无对象可修，
            //        视为“无需修复”，不判失败。
            string script =
                "$ok=0;$fail=0;" +
                "$pk=@(Get-AppxPackage *windows.immersivecontrolpanel* -AllUsers -ErrorAction SilentlyContinue);" +
                "if($pk.Count -eq 0){Write-Output 'NOPKG';exit 0}" +
                "else{foreach($p in $pk){try{Add-AppxPackage -DisableDevelopmentMode -Register " +
                "\"$($p.InstallLocation)\\AppXManifest.xml\" -ErrorAction Stop;$ok++}catch{$fail++}}}" +
                "if($ok -gt 0 -and $fail -eq 0){Write-Output 'OK';exit 0}else{Write-Output 'FAIL';exit 1}";

            var r = ShellRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", 120000);

            string output = r.StdOut ?? "";
            if (output.IndexOf("NOPKG", StringComparison.OrdinalIgnoreCase) >= 0)
                return true; // 本系统不含该组件，无需修复

            bool ok = r.Code == 0 &&
                      output.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!ok)
            {
                var cb = RepairStepFailed;
                if (cb != null)
                    cb("重新注册“设置”应用未成功完成（退出码 " + r.Code + "），可稍后在 PowerShell 中手动重试。");
            }
            return ok;
        }

        // 说明：网络代理不再作为检测/修复项，原 FixNetwork（netsh winhttp reset proxy
        // + ipconfig /flushdns）已移除。本工具只修复 Windows Update 组件本身，
        // 不擅自改动用户的网络代理设置。
    }
}
