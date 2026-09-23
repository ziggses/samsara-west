using System;
using System.Collections.Generic;
using System.Text;
using SamsaraWest.Core;

namespace SamsaraWest.Localization
{
    /// <summary>
    /// 本地化服务。所有界面文案必须经此取词（任务书 FND-07）。
    /// 未命中键时返回可辨识的占位符并记警告，绝不静默返回中文原文——
    /// 否则「硬编码中文」会以「看起来正常」的方式溜进发布版。
    /// </summary>
    public interface ILocalizationService : IService
    {
        string Language { get; }

        int KeyCount { get; }

        bool HasKey(string key);

        string Get(string key);

        /// <summary>取词并做占位符替换，占位符写成 {0} {1}。</summary>
        string Format(string key, params object[] args);

        /// <summary>运行期出现过的缺失键，供错误面板与编辑器扫描使用。</summary>
        IReadOnlyCollection<string> MissingKeys { get; }
    }

    public sealed class LocalizationService : ILocalizationService
    {
        private readonly LocalizationTable _table;
        private readonly Dictionary<string, string> _lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

        public LocalizationService(LocalizationTable table)
        {
            _table = table;
            Language = table == null || string.IsNullOrWhiteSpace(table.Language) ? "zh-Hans" : table.Language;

            if (table == null)
            {
                return;
            }

            for (var i = 0; i < table.Entries.Count; i++)
            {
                var entry = table.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (_lookup.ContainsKey(entry.Key))
                {
                    GameLog.Warn(LogChannel.Data, $"本地化文本表存在重复键，保留首条并忽略后一条：{entry.Key}", table.name);
                    continue;
                }

                _lookup.Add(entry.Key, entry.Text ?? string.Empty);
            }
        }

        public string Language { get; }

        public int KeyCount => _lookup.Count;

        public IReadOnlyCollection<string> MissingKeys => _missing;

        public void OnRegistered(IServiceRegistry registry)
        {
            GameLog.Info(LogChannel.Data, $"本地化就绪：语言 {Language}，共 {_lookup.Count} 条文本。", _table == null ? null : _table.name);
        }

        public void OnUnregistered()
        {
        }

        public bool HasKey(string key) => !string.IsNullOrEmpty(key) && _lookup.ContainsKey(key);

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_lookup.TryGetValue(key, out var text))
            {
                return text;
            }

            if (_missing.Add(key))
            {
                GameLog.Warn(LogChannel.Data, $"本地化缺失键：{key}。界面将显示占位符。", _table == null ? null : _table.name);
            }

            return $"[[{key}]]";
        }

        public string Format(string key, params object[] args)
        {
            var template = Get(key);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException exception)
            {
                GameLog.Error(
                    LogChannel.Data,
                    $"文本键 {key} 的占位符与参数数量不匹配：{exception.Message}",
                    _table == null ? null : _table.name);
                return template;
            }
        }

        /// <summary>编辑器扫描工具使用：列出表里定义了但运行时从未被取用的键（潜在冗余）。</summary>
        public IEnumerable<string> EnumerateKeys() => _lookup.Keys;

        /// <summary>供扫描工具把「表里缺哪些键」输出成人可读文本。</summary>
        public string DescribeMissingKeys()
        {
            if (_missing.Count == 0)
            {
                return "无缺失键。";
            }

            var builder = new StringBuilder();
            foreach (var key in _missing)
            {
                builder.AppendLine(key);
            }

            return builder.ToString();
        }
    }
}
