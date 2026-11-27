using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ALTRun.Services
{
    /// <summary>
    /// "发送到 ALTRun"跨进程文件信箱。
    /// <para>
    /// v2 是单实例程序：右键"发送到 ALTRun"启动的第二个进程会被互斥体拦下并直接退出，
    /// 仅靠 <see cref="App.WakeupEventName"/> 只能唤醒主窗口、无法把文件路径交给它。
    /// 这里用一个约定位置的文本文件做信箱：第二个进程把参数里的文件路径写入，
    /// 再 Set 唤醒事件；已运行实例被唤醒后读取并清空，交给主窗口添加快捷项。
    /// </para>
    /// <para>
    /// 所有 IO 异常一律吞掉并记日志——信箱失败最多导致这次"发送到"不生效，不能影响主程序。
    /// </para>
    /// </summary>
    public static class FileSendMailbox
    {
        private const string MailboxFileName = "pending_send_files.txt";

        private static string MailboxPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "ALTRun", MailboxFileName);
            }
        }

        /// <summary>第二个实例调用：把文件路径追加写入信箱（UTF-8，一行一个）。</summary>
        public static void Append(IEnumerable<string> files)
        {
            var paths = files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (paths.Count == 0) return;

            try
            {
                string dir = Path.GetDirectoryName(MailboxPath)!;
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                foreach (var p in paths)
                {
                    // 用制表符转义路径中的换行（Windows 文件名本身不允许换行，保险起见）
                    sb.Append(p.Replace("\r", string.Empty).Replace("\n", string.Empty)).Append('\n');
                }
                // 追加而非覆盖：短时间内连续发送多个文件时不会互相冲掉
                File.AppendAllText(MailboxPath, sb.ToString(), new UTF8Encoding(false));
                AppLog.Write($"[FileSendMailbox] 写入 {paths.Count} 个路径 -> {MailboxPath}");
            }
            catch (Exception ex)
            {
                AppLog.Write("[FileSendMailbox] 写入失败", ex);
            }
        }

        /// <summary>主实例调用：读取并立即清空信箱，返回去重后的文件列表。</summary>
        public static IReadOnlyList<string> TakeAll()
        {
            try
            {
                if (!File.Exists(MailboxPath)) return Array.Empty<string>();

                string content = File.ReadAllText(MailboxPath, Encoding.UTF8);
                var result = content.Split('\n')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0 && File.Exists(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 读完即删（而非清空内容）：删除后若第二个进程再 Append 会重建新文件，
                // 不会被本次清空抹掉。失败也不影响本次处理（下次会重复，但添加逻辑按路径去重）。
                File.Delete(MailboxPath);
                AppLog.Write($"[FileSendMailbox] 读取 {result.Count} 个路径并清空");
                return result;
            }
            catch (Exception ex)
            {
                AppLog.Write("[FileSendMailbox] 读取失败", ex);
                return Array.Empty<string>();
            }
        }
    }
}
