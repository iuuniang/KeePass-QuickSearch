using System;
using System.Collections.Generic;
using Microsoft.International.Converters.PinYinConverter;

namespace QuickSearch
{
    public static class PinyinHelper
    {

        // 缓存：字符 -> 拼音列表（去声调）
        // 例如：'行' -> ["HANG", "XING"]
        private static readonly Dictionary<char, string[]> _pinyinCache = new Dictionary<char, string[]>();
        private static readonly object _lockObj = new object();

        /// <summary>
        /// 判断 source 文本是否包含 pattern (支持拼音、首字母、混合)
        /// </summary>
        public static bool ContainsPinyin(string source, string pattern)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(pattern))
                return false;

            // 预处理：如果 Pattern 不包含 ASCII，直接走原始 IndexOf 即可（全中文搜索）
            // 如果 Source 也不包含中文，直接走原始 IndexOf
            // 这里为了逻辑简单，我们只在 source 包含 pattern 找不到时调用此函数

            // 简单清理
            pattern = pattern.Trim();

            // 遍历 Source，寻找匹配的起点
            for (int i = 0; i < source.Length; i++)
            {
                // 尝试从 source[i] 开始匹配 pattern
                if (IsMatchRecursive(source, i, pattern, 0))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 递归匹配逻辑
        /// </summary>
        private static bool IsMatchRecursive(string source, int sIdx, string pattern, int pIdx)
        {
            // Pattern 匹配完成，成功
            if (pIdx >= pattern.Length)
                return true;

            // Source 耗尽但 Pattern 未完，失败
            if (sIdx >= source.Length)
                return false;

            char sChar = source[sIdx];
            char pChar = pattern[pIdx];

            // 1. 精确字符匹配 (忽略大小写)
            if (CharEquals(sChar, pChar))
            {
                // 贪婪匹配：继续下一个字符
                if (IsMatchRecursive(source, sIdx + 1, pattern, pIdx + 1))
                    return true;
            }

            // 2. 拼音匹配 (仅当 Source 为中文字符时)
            if (IsChinese(sChar))
            {
                string[] pinyins = GetPinyins(sChar);
                if (pinyins != null)
                {
                    foreach (string py in pinyins)
                    {
                        // 检查当前拼音是否匹配 Pattern 的当前片段
                        // 两种情况：
                        // A. 首字母匹配：Pattern="zg", Source="中国" -> 'z' matches "ZHONG"
                        // B. 全拼/部分拼音匹配：Pattern="zhongg", Source="中国" -> "zhong" matches "ZHONG"

                        int matchLen = GetMatchingLength(py, pattern, pIdx);
                        if (matchLen > 0)
                        {
                            // 匹配成功 matchLen 个字符，Source 移动 1 位（汉字），Pattern 移动 matchLen 位
                            if (IsMatchRecursive(source, sIdx + 1, pattern, pIdx + matchLen))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 计算拼音和 Pattern 片段的匹配长度
        /// </summary>
        private static int GetMatchingLength(string pinyin, string pattern, int pIdx)
        {
            int len = 0;
            int maxLen = Math.Min(pinyin.Length, pattern.Length - pIdx);

            for (int i = 0; i < maxLen; i++)
            {
                if (CharEquals(pinyin[i], pattern[pIdx + i]))
                {
                    len++;
                }
                else
                {
                    break;
                }
            }

            // 逻辑修正：
            // 如果匹配的是首字母，返回1
            // 如果用户输入 "zh"，拼音是 "ZHONG"，我们应该消费 2 个字符
            // 如果用户输入 "zhong"，我们消费 5 个字符
            // 只要有前缀匹配即可
            return len;
        }

        private static bool CharEquals(char a, char b)
        {
            return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }

        private static bool IsChinese(char c)
        {
            return c >= 0x4e00 && c <= 0x9fa5;
        }

        private static string[] GetPinyins(char c)
        {
            lock (_lockObj)
            {
                if (_pinyinCache.ContainsKey(c))
                {
                    return _pinyinCache[c];
                }
            }

            try
            {
                if (ChineseChar.IsValidChar(c))
                {
                    ChineseChar cc = new ChineseChar(c);
                    // Pinyins 返回的数据包含声调数字（如 HONG2），我们需要去掉并去重
                    HashSet<string> validPinyins = new HashSet<string>();
                    foreach (string py in cc.Pinyins)
                    {
                        if (!string.IsNullOrEmpty(py))
                        {
                            // 去除末尾声调数字
                            string cleanPy = py.Substring(0, py.Length - 1);
                            validPinyins.Add(cleanPy);
                        }
                    }

                    string[] result = new string[validPinyins.Count];
                    validPinyins.CopyTo(result);

                    lock (_lockObj)
                    {
                        if (!_pinyinCache.ContainsKey(c))
                            _pinyinCache[c] = result;
                    }
                    return result;
                }
            }
            catch
            {
                // 忽略异常，作为非中文字符处理
            }

            // 非中文或异常，存入 null 避免重复尝试
            lock (_lockObj)
            {
                if (!_pinyinCache.ContainsKey(c))
                    _pinyinCache[c] = null;
            }
            return null;
        }

        /// <summary>
        /// 异步预热拼音库，避免首次搜索卡顿
        /// </summary>
        public static void Preload()
        {
            // 使用 Task.Run (C# 5.0/.NET 4.5+) 在后台线程加载
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // 随便初始化一个汉字，强制触发 ChineseChar 内部的静态构造函数加载字典
                    var dummy = new ChineseChar('一');
                    // 甚至可以预加载一些常用字符到我们自己的缓存中（可选）
                }
                catch
                {
                    // 忽略预热错误
                }
            });
        }
    }
}