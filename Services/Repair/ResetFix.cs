// 更新缓存重置修复：移走 SoftwareDistribution / catroot2 并重新注册组件。
using System;
using System.Collections.Generic;
using System.IO;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 重置更新缓存：停止服务、移走缓存目录、重新注册更新 DLL、再启动服务。
    /// </summary>
    public class ResetFix : IFixItem
    {
        public string Id { get { return IssueIds.Reset; } }

        private readonly Action<string> _notify;

        public ResetFix(Action<string> notify)
        {
            _notify = notify;
        }

        public string FollowUpHint
        {
            get
            {
                return "更新缓存重置未完全成功。请重启电脑后重新运行本工具；" +
                       "若仍失败，可先手动确认 wuauserv、bits、CryptSvc 能否正常启动。\n\n" +
                       "提示：重置缓存会把缓存目录移动到 SoftwareDistribution.old / catroot2.old，" +
                       "这部分改动不在“注册表策略备份”的恢复范围内。" +
                       "若需撤销，可停止 wuauserv、bits、CryptSvc 后，把上述 .old 目录改回原名" +
                       "（先移走现有的同名目录）。";
            }
        }

        private static string SystemRoot
        {
            get { return SystemCompat.SystemRoot; }
        }

        public bool Fix()
        {
            // 关键步骤：缓存目录必须成功移走，否则重置无效，判整体失败。
            // regsvr32 属尽力而为（部分 DLL 系统内本就不存在），失败不判整体失败。
            bool ok = true;

            // 停止服务。记录未能停下的服务：若它们仍占用缓存目录，
            // Directory.Move 会失败，届时能告诉用户具体是哪个服务导致的，
            // 而不是只给出“目录未能移走”这一句泛泛的提示。
            string[] stop = { "wuauserv", "bits", "usosvc", "msiserver", "cryptsvc" };
            var notStopped = new List<string>();
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
                if (_notify != null)
                    _notify("更新缓存重置未完全成功，部分服务未能启动或缓存目录未能移走" + extra + "。");
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
    }
}
