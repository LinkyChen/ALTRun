using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ALTRun.Models;

namespace ALTRun.Services
{
    /// <summary>
    /// 独立使用频次存储：把 Freq 从 ALTRun_Config.json 中拆出，写入同目录的 ALTRun_Freq.json。
    /// 版本更新时只需替换 exe（即使连默认配置一起替换），历史使用次数也不会丢失。
    ///
    /// 匹配策略：优先按 Id（Guid，跨版本稳定）；Id 对不上时回退按 ShortCut（快捷词），
    /// 这样即使配置被重建导致 Id 变化，频次依然能找回来。
    /// 文件缺失或损坏时静默降级——沿用配置内的 Freq，绝不因此中断启动。
    /// </summary>
    public class FrequencyStore
    {
        private const string FileName = "ALTRun_Freq.json";
        private readonly string _filePath;
        private readonly Dictionary<string, int> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _byShortCut = new(StringComparer.OrdinalIgnoreCase);

        public string FilePath => _filePath;

        public FrequencyStore(string appDir)
        {
            _filePath = Path.Combine(appDir, FileName);
        }

        /// <summary>从 ALTRun_Freq.json 读取频次到内存。必须在配置加载之前调用。</summary>
        public void Load()
        {
            _byId.Clear();
            _byShortCut.Clear();

            if (!File.Exists(_filePath)) return;

            try
            {
                // 先读进内存再处理，避免持有句柄时又写同目录文件
                string json;
                using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                var data = JsonSerializer.Deserialize<FrequencyData>(json);
                if (data?.Items == null) return;

                foreach (var entry in data.Items)
                {
                    if (entry == null) continue;
                    if (!string.IsNullOrWhiteSpace(entry.Id)) _byId[entry.Id] = entry.Freq;
                    if (!string.IsNullOrWhiteSpace(entry.ShortCut)) _byShortCut[entry.ShortCut] = entry.Freq;
                }

                AppLog.Write($"[ALTRun] 独立频次已加载: {data.Items.Count} 项 ({FilePath})");
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] 独立频次读取失败（忽略，沿用配置内频次）: {ex.Message}");
            }
        }

        /// <summary>把内存中的频次覆盖到快捷项列表上。未匹配到的项保持配置里的原值。</summary>
        public void ApplyTo(IEnumerable<ShortCutItem> items)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item == null) continue;

                if (!string.IsNullOrWhiteSpace(item.Id) && _byId.TryGetValue(item.Id, out int freqById))
                {
                    item.Freq = freqById;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.ShortCut) && _byShortCut.TryGetValue(item.ShortCut, out int freqByShortCut))
                {
                    item.Freq = freqByShortCut;
                }
            }
        }

        /// <summary>把当前快捷项列表的频次写回 ALTRun_Freq.json（全量覆盖，原子写）。</summary>
        public void SaveFrom(IEnumerable<ShortCutItem> items)
        {
            try
            {
                var data = new FrequencyData
                {
                    Version = 1,
                    SavedAt = DateTime.Now,
                    Items = (items ?? Enumerable.Empty<ShortCutItem>())
                        .Where(x => x != null)
                        .Select(x => new FrequencyEntry
                        {
                            Id = x.Id,
                            ShortCut = x.ShortCut,
                            Freq = x.Freq
                        })
                        .ToList()
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                WriteAllTextAtomic(_filePath, json);
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] 独立频次保存失败: {ex.Message}");
            }
        }

        private static void WriteAllTextAtomic(string path, string content)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content, Encoding.UTF8);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                    return;
                }
                catch (Exception ex)
                {
                    // FAT32 / 网络盘不支持 Replace，退回 Move 覆盖
                    AppLog.Write($"[ALTRun] 独立频次 File.Replace 失败，改用 Move: {ex.Message}");
                }
            }

            try
            {
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    AppLog.Write($"[ALTRun] 独立频次清理临时文件失败: {cleanupEx.Message}");
                }
                throw;
            }
        }
    }

    /// <summary>ALTRun_Freq.json 的序列化模型。</summary>
    public class FrequencyData
    {
        public int Version { get; set; } = 1;
        public DateTime SavedAt { get; set; }
        public List<FrequencyEntry> Items { get; set; } = new();
    }

    public class FrequencyEntry
    {
        public string Id { get; set; } = string.Empty;
        public string ShortCut { get; set; } = string.Empty;
        public int Freq { get; set; }
    }
}
