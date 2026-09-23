using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>
    /// 增量导入的记账本：每张表记录上次导入时的内容哈希与产出的资产路径。
    /// 表没变就整张跳过，策划改一张表不必重导 17 张（FND-06）。
    /// </summary>
    [CreateAssetMenu(fileName = "ImportManifest", menuName = "SamsaraWest/导入清单（工具内部使用）")]
    public sealed class DataImportManifest : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string _tableName;
            [SerializeField] private string _contentHash;
            [SerializeField] private string _importedAtUtc;
            [SerializeField] private List<string> _assetPaths = new List<string>();

            public Entry()
            {
            }

            public Entry(string tableName)
            {
                _tableName = tableName;
            }

            public string TableName => _tableName;

            public string ContentHash => _contentHash;

            public string ImportedAtUtc => _importedAtUtc;

            public IReadOnlyList<string> AssetPaths => _assetPaths;

            public void Update(string contentHash, IEnumerable<string> assetPaths, DateTime utcNow)
            {
                _contentHash = contentHash;
                _importedAtUtc = utcNow.ToString("O");
                _assetPaths.Clear();
                if (assetPaths != null)
                {
                    _assetPaths.AddRange(assetPaths);
                }
            }
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGet(string tableName, out Entry entry)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].TableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public Entry GetOrCreate(string tableName)
        {
            if (TryGet(tableName, out var entry))
            {
                return entry;
            }

            entry = new Entry(tableName);
            _entries.Add(entry);
            return entry;
        }
    }
}
