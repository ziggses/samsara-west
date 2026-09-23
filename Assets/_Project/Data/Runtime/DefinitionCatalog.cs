using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 全部数据定义的索引资产。由编辑器工具扫描 Assets 后写入，
    /// 运行期只做只读查询，并保证「重复 ID 会被发现」。
    /// </summary>
    [CreateAssetMenu(fileName = "DefinitionCatalog", menuName = "SamsaraWest/数据目录 DefinitionCatalog")]
    public sealed class DefinitionCatalog : ScriptableObject
    {
        [SerializeField]
        [Tooltip("由 Tools > SamsaraWest > 重建数据目录 生成，不要手工维护。")]
        private List<DefinitionBase> _definitions = new List<DefinitionBase>();

        [NonSerialized]
        private Dictionary<string, DefinitionBase> _byId;

        [NonSerialized]
        private List<ValidationIssue> _duplicates;

        public IReadOnlyList<DefinitionBase> Definitions => _definitions;

        public int Count => _definitions.Count;

        /// <summary>上次 <see cref="Rebuild"/> 发现的重复 ID 记录。</summary>
        public IReadOnlyList<ValidationIssue> Duplicates =>
            _duplicates ?? (IReadOnlyList<ValidationIssue>)Array.Empty<ValidationIssue>();

        public bool IsIndexBuilt => _byId != null;

        /// <summary>编辑器工具使用：替换整个列表。</summary>
        public void SetDefinitions(IEnumerable<DefinitionBase> definitions)
        {
            _definitions.Clear();
            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    _definitions.Add(definition);
                }
            }

            _byId = null;
        }

        /// <summary>
        /// 重建索引；<paramref name="report"/> 非空时顺带执行全量校验。重复 ID 与不合规 ID 会被写入报告。
        /// 报告为空表示只建索引，字段级校验会被跳过——子类 <c>Validate</c> 不做空保护，不能收到 null。
        /// </summary>
        public void Rebuild(ValidationReport report = null)
        {
            _byId = new Dictionary<string, DefinitionBase>(StringComparer.Ordinal);
            _duplicates = new List<ValidationIssue>();

            for (var i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null)
                {
                    report?.Warn("CAT_NULL_ENTRY", $"数据目录第 {i} 项为空引用，已跳过。", assetPath: name);
                    continue;
                }

                var id = definition.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    report?.Error(
                        "CAT_EMPTY_ID",
                        $"存在没有 ID 的数据定义：{definition.GetType().Name}（资产名 {definition.name}）。",
                        assetPath: definition.name);
                    continue;
                }

                if (_byId.TryGetValue(id, out var existing))
                {
                    var issue = new ValidationIssue(
                        ValidationSeverity.Error,
                        "DEF_ID_DUPLICATE",
                        $"ID '{id}' 重复：{existing.GetType().Name}({existing.name}) 与 {definition.GetType().Name}({definition.name})。",
                        id,
                        definition.name);
                    _duplicates.Add(issue);
                    report?.Add(issue);
                    continue;
                }

                _byId[id] = definition;

                // 只有确实要收集结论时才逐条校验：Validate(null) 会让子类的 report.Error 直接空引用。
                if (report != null)
                {
                    definition.Validate(report);
                }
            }

            if (report != null)
            {
                GameLogRef(report);
            }
        }

        public int IndexedCount => _byId?.Count ?? 0;

        public bool TryGet(string id, out DefinitionBase definition)
        {
            if (string.IsNullOrEmpty(id))
            {
                definition = null;
                return false;
            }

            EnsureIndex();
            return _byId.TryGetValue(id, out definition);
        }

        public bool TryGet<T>(string id, out T definition) where T : DefinitionBase
        {
            definition = null;
            if (!TryGet(id, out var found))
            {
                return false;
            }

            definition = found as T;
            return definition != null;
        }

        public T Get<T>(string id) where T : DefinitionBase
        {
            if (TryGet<T>(id, out var definition))
            {
                return definition;
            }

            throw new KeyNotFoundException($"数据目录中找不到 ID '{id}'（期望类型 {typeof(T).Name}）。");
        }

        public IEnumerable<T> OfKind<T>() where T : DefinitionBase
        {
            for (var i = 0; i < _definitions.Count; i++)
            {
                if (_definitions[i] is T typed)
                {
                    yield return typed;
                }
            }
        }

        private void EnsureIndex()
        {
            if (_byId == null)
            {
                Rebuild();
            }
        }

        private void GameLogRef(ValidationReport report)
        {
            if (report.HasErrors)
            {
                Core.GameLog.Error(Core.LogChannel.Data, $"数据目录校验发现 {report.ErrorCount} 个错误。", name);
            }
            else
            {
                Core.GameLog.Info(Core.LogChannel.Data, $"数据目录校验通过，共索引 {IndexedCount} 条定义。", name);
            }
        }
    }
}
