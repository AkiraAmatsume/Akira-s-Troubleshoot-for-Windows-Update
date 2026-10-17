// 核心更新组件文件修复：从组件存储恢复缺失或被改名的文件。
using System;
using System.Collections.Generic;
using WindowsUpdateFixer.Models;

namespace WindowsUpdateFixer.Services.Repair
{
    /// <summary>
    /// 从 WinSxS 组件存储恢复缺失/被改名的核心更新文件。
    /// </summary>
    public class FilesFix : IFixItem
    {
        public string Id { get { return IssueIds.Files; } }

        private readonly Action<string> _notify;

        public FilesFix(Action<string> notify)
        {
            _notify = notify;
        }

        /// <summary>
        /// 最近一次 Fix() 中“组件存储也无法提供源”的文件名。
        /// 供 FollowUpHint 展示具体缺哪些文件，而不是只看到一个数字。
        /// </summary>
        private List<string> _missingSourceFiles = new List<string>();

        public string FollowUpHint
        {
            get
            {
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
            }
        }

        public bool Fix()
        {
            bool ok = true;

            // 记录“组件存储中也没有源”的文件：这类文件无法靠本工具恢复，
            // 需要用户手动运行 DISM/SFC 先行修复组件存储。
            var missingSource = new List<string>();

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

            // 传出明细，供 FollowUpHint 拼进后续建议
            _missingSourceFiles = missingSource;

            if (missingSource.Count > 0)
            {
                if (_notify != null)
                    _notify("组件存储中缺少以下文件的源，无法自动恢复：" +
                            string.Join("、", missingSource.ToArray()));
            }

            return ok;
        }
    }
}
