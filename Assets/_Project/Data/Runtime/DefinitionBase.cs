using System;
using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>把 CSV 列名绑定到字段。没有该特性的字段不参与导入。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class CsvColumnAttribute : Attribute
    {
        public CsvColumnAttribute(string column, bool required = false)
        {
            Column = column;
            Required = required;
        }

        public string Column { get; }

        /// <summary>表格缺少该列时是否判为错误（否则记警告并跳过）。</summary>
        public bool Required { get; }
    }

    /// <summary>
    /// 全部 17 个数据定义对象的统一契约：
    /// 唯一 ID、显示名键、描述键、图标、标签、版本与校验规则。
    /// 显示名与描述一律存<b>本地化键</b>，不存中文原文。
    /// </summary>
    public abstract class DefinitionBase : ScriptableObject
    {
        [CsvColumn("id", required: true)]
        [SerializeField]
        private string _id;

        [CsvColumn("displayNameKey", required: true)]
        [SerializeField]
        private string _displayNameKey;

        [CsvColumn("descriptionKey")]
        [SerializeField]
        private string _descriptionKey;

        [CsvColumn("tags")]
        [SerializeField]
        private string[] _tags = Array.Empty<string>();

        [CsvColumn("version")]
        [SerializeField]
        private int _version = 1;

        [Tooltip("图标由美术资源提供，不参与 CSV 导入，需在编辑器中手工指定或由资源导入工具挂接。")]
        [SerializeField]
        private Sprite _icon;

        public string Id => _id;

        public string DisplayNameKey => _displayNameKey;

        public string DescriptionKey => _descriptionKey;

        public string[] Tags => _tags ?? Array.Empty<string>();

        public int Version => _version;

        public Sprite Icon => _icon;

        /// <summary>所属数据种类，决定 ID 命名规则。</summary>
        public abstract DefinitionKind Kind { get; }

        /// <summary>
        /// 通用校验。子类可重写并先调用 <c>base.Validate(report)</c>，
        /// 再追加自身字段的检查。
        /// </summary>
        public virtual void Validate(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_id))
            {
                report.Error("DEF_ID_EMPTY", "ID 不能为空。", assetPath: name, fieldName: "id");
            }
            else if (!IdRules.IsValidId(Kind, _id))
            {
                report.Error(
                    "DEF_ID_PATTERN",
                    $"ID '{_id}' 不符合 {Kind} 的命名规则：{IdRules.GetPattern(Kind)}",
                    _id,
                    fieldName: "id");
            }

            ValidateLocKey(report, _displayNameKey, "displayNameKey", required: true);
            ValidateLocKey(report, _descriptionKey, "descriptionKey", required: false);

            if (_version < 1)
            {
                report.Error("DEF_VERSION_INVALID", $"version 必须 >= 1，当前为 {_version}。", _id, fieldName: "version");
            }

            if (_icon == null)
            {
                // 图标不参与 CSV 导入，只能由美术资源挂接。美术接入前这一条对每个定义都会命中，
                // 因此记为 Info，改由导入器按表汇总成一条 Warning，避免有效警告被淹没。
                report.Info("DEF_ICON_MISSING", "图标未指定（美术资源未接入前属预期）。", _id, fieldName: "icon");
            }

            ValidateTags(report);
        }

        /// <summary>编辑器工具在写回数据时调用，集中管理反射写入。</summary>
        internal void SetIdentity(string id, string displayNameKey, string descriptionKey, string[] tags, int version)
        {
            _id = id;
            _displayNameKey = displayNameKey;
            _descriptionKey = descriptionKey;
            _tags = tags ?? Array.Empty<string>();
            _version = version;
        }

        internal void SetIcon(Sprite icon) => _icon = icon;

        private void ValidateLocKey(ValidationReport report, string key, string fieldName, bool required)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                if (required)
                {
                    report.Error("DEF_LOC_KEY_EMPTY", $"{fieldName} 不能为空，且必须使用本地化键。", _id, fieldName: fieldName);
                }

                return;
            }

            if (IdRules.ContainsChinese(key))
            {
                report.Error(
                    "DEF_LOC_KEY_HAS_CHINESE",
                    $"{fieldName} 含中文原文 '{key}'，必须改为本地化键（任务书 FND-07：UI 文本无硬编码中文）。",
                    _id,
                    fieldName: fieldName);
                return;
            }

            if (!IdRules.IsValidLocalizationKey(key))
            {
                report.Error(
                    "DEF_LOC_KEY_FORMAT",
                    $"{fieldName} '{key}' 不是合法键名，只允许字母、数字、下划线与点号分段。",
                    _id,
                    fieldName: fieldName);
            }
        }

        private void ValidateTags(ValidationReport report)
        {
            if (_tags == null || _tags.Length == 0)
            {
                return;
            }

            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _tags.Length; i++)
            {
                var tag = _tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    report.Warn("DEF_TAG_EMPTY", $"tags 第 {i + 1} 项为空，将被忽略。", _id, fieldName: "tags");
                    continue;
                }

                if (!seen.Add(tag.Trim()))
                {
                    report.Warn("DEF_TAG_DUPLICATE", $"tags 中存在重复项 '{tag}'。", _id, fieldName: "tags");
                }
            }
        }
    }
}
