using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace ALTRun.Services
{
    public static class PinyinHelper
    {
        private static readonly Encoding? GbEncoding;

        // GB2312 一级常用字（共 3755 字，严格按拼音首字母正序排列）的 23 个声母分区边界点
        private static readonly int[] SecPosValueList = new int[]
        {
            0xB0A1, 0xB0C5, 0xB2C1, 0xB4EE, 0xB6EA, 0xB7A2, 0xB8C1, 0xB9FE,
            0xBBF7, 0xBFA6, 0xC0AC, 0xC2E8, 0xC4C3, 0xC5B6, 0xC5BE, 0xC6DA,
            0xC8BB, 0xC8F6, 0xCBFA, 0xCDDA, 0xCEF4, 0xD1B9, 0xD4D1, 0xD7FA
        };

        private static readonly char[] FirstLetterList = new char[]
        {
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h',
            'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q',
            'r', 's', 't', 'w', 'x', 'y', 'z'
        };

        // 常见特殊汉字、高频二级字、专有名词直通常用字典
        private static readonly Dictionary<char, char> SpecialCharMap = new()
        {
            { '哔', 'b' }, { '哩', 'l' }, { '钉', 'd' }, { '酷', 'k' }, { '易', 'y' },
            { '腾', 't' }, { '讯', 'x' }, { '微', 'w' }, { '信', 'x' }, { '截', 'j' },
            { '图', 't' }, { '计', 'j' }, { '算', 's' }, { '器', 'q' }, { '端', 'd' },
            { '设', 's' }, { '置', 'z' }, { '管', 'g' }, { '理', 'l' }, { '工', 'g' },
            { '具', 'j' }, { '控', 'k' }, { '制', 'z' }, { '任', 'r' }, { '务', 'w' },
            { '浏', 'l' }, { '览', 'l' }, { '编', 'b' }, { '辑', 'j' }, { '画', 'h' },
            { '播', 'b' }, { '放', 'f' }, { '相', 'x' }, { '机', 'j' }, { '天', 't' },
            { '气', 'q' }, { '日', 'r' }, { '历', 'l' }, { '笔', 'b' }, { '记', 'j' },
            { '邮', 'y' }, { '件', 'j' }, { '游', 'y' }, { '戏', 'x' }, { '商', 's' },
            { '店', 'd' }, { '系', 'x' }, { '统', 't' }, { '视', 's' }, { '频', 'p' },
            { '音', 'y' }, { '乐', 'y' }, { '百', 'b' }, { '度', 'd' }, { '歌', 'g' },
            { '谷', 'g' }, { '网', 'w' }, { '络', 'l' }, { '蓝', 'l' }, { '牙', 'y' },
            { '声', 's' }, { '狼', 'l' }, { '毫', 'h' }, { '小', 'x' }, { '抖', 'd' },
            { '拼', 'p' }, { '奇', 'q' }, { '艺', 'y' }, { '淘', 't' }, { '宝', 'b' },
            { '阿', 'a' }, { '里', 'l' }, { '暴', 'b' }, { '风', 'f' }, { '狗', 'g' },
            { '脑', 'n' }
        };

        static PinyinHelper()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                GbEncoding = Encoding.GetEncoding("GB18030");
            }
            catch
            {
                try
                {
                    GbEncoding = Encoding.GetEncoding(936);
                }
                catch
                {
                    GbEncoding = null;
                }
            }
        }

        /// <summary>
        /// 提取中文文本拼音首字母序列 (例如 "计算器" -> "jsq", "截图工具" -> "jtgj")
        /// </summary>
        public static string GetInitials(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);

            // 原实现对每个汉字都 GetBytes(new char[]{ c })，一次刷新会分配成百上千个临时数组。
            // 改成从 ArrayPool 租两块缓冲，连续汉字按段一次性编码，消除逐字分配。
            char[]? charBuffer = null;
            byte[]? byteBuffer = null;
            if (GbEncoding != null)
            {
                charBuffer = ArrayPool<char>.Shared.Rent(text.Length);
                // GB18030 的少数生僻字会编码成 4 字节，按 4 倍租用以防 GetBytes 缓冲不足
                byteBuffer = ArrayPool<byte>.Shared.Rent(text.Length * 4);
            }

            try
            {
                int runStart = -1;   // 连续汉字段起点（-1 表示无待处理段）
                int runEnd = -1;     // 终点（不含）

                // 把累积的连续汉字段按 GB2312 边界定位首字母
                void FlushChineseRun()
                {
                    if (runStart < 0 || charBuffer == null || byteBuffer == null || GbEncoding == null)
                    {
                        runStart = -1;
                        return;
                    }

                    int length = runEnd - runStart;
                    for (int k = 0; k < length; k++) charBuffer[k] = text[runStart + k];
                    int byteCount = GbEncoding.GetBytes(charBuffer, 0, length, byteBuffer, 0);

                    for (int b = 0; b + 1 < byteCount; b += 2)
                    {
                        int code = (byteBuffer[b] << 8) | byteBuffer[b + 1];
                        for (int s = 0; s < 23; s++)
                        {
                            if (code >= SecPosValueList[s] && code < SecPosValueList[s + 1])
                            {
                                sb.Append(FirstLetterList[s]);
                                break;
                            }
                        }
                    }
                    runStart = -1;
                }

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    // 1. 英文字母与数字直接保留小写
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    {
                        FlushChineseRun();
                        sb.Append(c);
                    }
                    else if (c >= 'A' && c <= 'Z')
                    {
                        FlushChineseRun();
                        sb.Append(char.ToLowerInvariant(c));
                    }
                    // 2. 特配与二级字直接查表（优先于通用 GB2312 区间）
                    else if (SpecialCharMap.TryGetValue(c, out char mapped))
                    {
                        FlushChineseRun();
                        sb.Append(mapped);
                    }
                    // 3. GB2312 一级字库区间快速精确定位 (3755 个标准汉字)
                    else if (c >= 0x4E00 && c <= 0x9FA5 && GbEncoding != null)
                    {
                        if (runStart < 0) runStart = i;
                        runEnd = i + 1;
                    }
                    else
                    {
                        FlushChineseRun();
                    }
                }
                FlushChineseRun();
            }
            catch
            {
                // 异常保底：已拼出的首字母仍可用
            }
            finally
            {
                if (charBuffer != null) ArrayPool<char>.Shared.Return(charBuffer);
                if (byteBuffer != null) ArrayPool<byte>.Shared.Return(byteBuffer);
            }

            return sb.ToString();
        }
    }
}
