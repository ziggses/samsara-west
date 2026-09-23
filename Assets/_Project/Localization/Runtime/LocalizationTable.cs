using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamsaraWest.Localization
{
    /// <summary>
    /// 文本表。骨架期只做中文单语，但键与文本分离，
    /// 之后加语言只需再建一张同键表，UI 代码一行都不用改。
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "SamsaraWest/本地化文本表")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string _key;
            [SerializeField] [TextArea(1, 6)] private string _text;

            public string Key => _key;

            public string Text => _text;

            public Entry()
            {
            }

            public Entry(string key, string text)
            {
                _key = key;
                _text = text;
            }
        }

        [SerializeField] private string _language = "zh-Hans";
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public string Language => _language;

        public IReadOnlyList<Entry> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>编辑器导入工具使用：整体替换文本内容。</summary>
        public void SetEntries(string language, IEnumerable<Entry> entries)
        {
            _language = string.IsNullOrWhiteSpace(language) ? _language : language;
            _entries.Clear();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Key))
                    {
                        _entries.Add(entry);
                    }
                }
            }
        }
    }
}
