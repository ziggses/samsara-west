using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SamsaraWest.Data;
using SamsaraWest.Localization;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    public sealed class LocalizationImportSummary
    {
        private readonly List<string> _log = new List<string>();

        public ValidationReport Report { get; } = new ValidationReport();

        public IReadOnlyList<string> Log => _log;

        public int KeyCount { get; internal set; }

        /// <summary>常量类内容是否有变化并被重写。内容一致时不落盘，也就不会让 .g.cs 无谓地产生 git diff。</summary>
        public bool ConstantsRewritten { get; internal set; }

        /// <summary>数据资产里引用了、但本地化表里查不到的键。</summary>
        public int MissingKeysUsedByData { get; internal set; }

        public void Say(string message)
        {
            _log.Add(message);
        }
    }

    /// <summary>
    /// 本地化管线（FND-07）：中文 CSV → LocalizationTable 资产 + key 常量类，
    /// 并反向检查「数据资产引用的键是否都登记过」。
    /// 骨架期只做中文单语，但全量走 key，将来加语言只需再加一张表。
    /// </summary>
    public static class LocalizationImporter
    {
        public const string SourceTableAssetPath = SamsaraWestPaths.LocalizationTables + "/localization-zh-Hans.csv";
        public const string LanguageCode = "zh-Hans";
        public const string ConstantClassName = "LocalizationKeys";

        public static LocalizationImportSummary ImportAll()
        {
            var summary = new LocalizationImportSummary();

            var absolutePath = CsvImporter.ToAbsolutePath(SourceTableAssetPath);
            if (!File.Exists(absolutePath))
            {
                summary.Report.Error("LOC_TABLE_MISSING", $"本地化表不存在：{SourceTableAssetPath}", assetPath: SourceTableAssetPath);
                return summary;
            }

            CsvTable table;
            try
            {
                table = CsvParser.Parse(File.ReadAllText(absolutePath, Encoding.UTF8), Path.GetFileName(SourceTableAssetPath));
            }
            catch (CsvFormatException ex)
            {
                summary.Report.Error("LOC_CSV_FORMAT", $"{ex.Message}（第 {ex.LineNumber} 行）", assetPath: SourceTableAssetPath);
                return summary;
            }

            if (!table.HasColumn("key") || !table.HasColumn("text"))
            {
                summary.Report.Error("LOC_COLUMNS_MISSING", "本地化表必须同时有 key 与 text 两列。", assetPath: SourceTableAssetPath);
                return summary;
            }

            var entries = new List<LocalizationTable.Entry>(table.RowCount);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                if (row.IsEmpty)
                {
                    continue;
                }

                var key = (row.Get("key") ?? string.Empty).Trim();
                var text = (row.Get("text") ?? string.Empty).Trim();

                if (key.Length == 0)
                {
                    summary.Report.Error("LOC_KEY_MISSING", $"第 {row.LineNumber} 行 key 为空。", assetPath: SourceTableAssetPath);
                    continue;
                }

                if (!IdRules.IsValidLocalizationKey(key))
                {
                    summary.Report.Error("LOC_KEY_INVALID", $"文本键命名不合法：{key}", assetPath: SourceTableAssetPath, fieldName: "key");
                    continue;
                }

                if (!seen.Add(key))
                {
                    summary.Report.Error("LOC_KEY_DUPLICATE", $"文本键重复：{key}", assetPath: SourceTableAssetPath, fieldName: "key");
                    continue;
                }

                if (text.Length == 0)
                {
                    summary.Report.Error("LOC_TEXT_EMPTY", $"文本键 {key} 没有内容。", assetPath: SourceTableAssetPath);
                    continue;
                }

                entries.Add(new LocalizationTable.Entry(key, text));
            }

            entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            summary.KeyCount = entries.Count;

            WriteTableAsset(entries, summary);
            summary.ConstantsRewritten = WriteConstants(entries);
            ValidateKeysUsedByDefinitions(entries, summary);

            AssetDatabase.SaveAssets();
            summary.Say($"本地化：{summary.KeyCount} 个键已写入 {SamsaraWestPaths.LocalizationTableAsset}。");
            return summary;
        }

        private static void WriteTableAsset(List<LocalizationTable.Entry> entries, LocalizationImportSummary summary)
        {
            CsvImporter.EnsureFolder(SamsaraWestPaths.LocalizationGeneratedRoot);
            var asset = AssetDatabase.LoadAssetAtPath<LocalizationTable>(SamsaraWestPaths.LocalizationTableAsset);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<LocalizationTable>();
                AssetDatabase.CreateAsset(asset, SamsaraWestPaths.LocalizationTableAsset);
            }
            else if (TableMatches(asset, entries))
            {
                // 文本没变就别标脏：重写同样内容只会让工作区多一个假改动。
                return;
            }

            asset.SetEntries(LanguageCode, entries);
            EditorUtility.SetDirty(asset);
            summary.Say($"本地化表资产已更新（语言 {LanguageCode}）。");
        }

        private static bool TableMatches(LocalizationTable asset, List<LocalizationTable.Entry> entries)
        {
            if (!string.Equals(asset.Language, LanguageCode, StringComparison.Ordinal) || asset.Count != entries.Count)
            {
                return false;
            }

            var current = asset.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(current[i].Key, entries[i].Key, StringComparison.Ordinal) ||
                    !string.Equals(current[i].Text, entries[i].Text, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>生成 key 常量类，让代码里写 LocalizationKeys.UiBattleCommandAttack 而不是裸字符串。</summary>
        private static bool WriteConstants(List<LocalizationTable.Entry> entries)
        {
            var builder = new StringBuilder(entries.Count * 64);
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine("// 本文件由 SamsaraWest/本地化/导入并生成常量 生成，请勿手改。");
            builder.AppendLine("// 改文案请改 Assets/_Project/Localization/Tables/localization-zh-Hans.csv。");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine();
            builder.AppendLine("namespace SamsaraWest.Localization");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>全部界面文本键的编译期常量。写错键名会直接编不过，而不是等到运行期才发现空白。</summary>");
            builder.AppendLine($"    public static class {ConstantClassName}");
            builder.AppendLine("    {");

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var constant = IdRules.ToConstantName(entry.Key);
                if (string.IsNullOrEmpty(constant) || !used.Add(constant))
                {
                    continue;
                }

                builder.AppendLine($"        public const string {constant} = \"{entry.Key}\";");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();

            var content = builder.ToString().Replace("\r\n", "\n");
            var absolutePath = CsvImporter.ToAbsolutePath(SamsaraWestPaths.LocalizationKeysSource);

            if (File.Exists(absolutePath) && string.Equals(File.ReadAllText(absolutePath, Encoding.UTF8).Replace("\r\n", "\n"), content, StringComparison.Ordinal))
            {
                return false;
            }

            CsvImporter.EnsureFolder(SamsaraWestPaths.LocalizationGeneratedRoot);
            File.WriteAllText(absolutePath, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(SamsaraWestPaths.LocalizationKeysSource);
            return true;
        }

        private static void ValidateKeysUsedByDefinitions(List<LocalizationTable.Entry> entries, LocalizationImportSummary summary)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            if (catalog == null || catalog.Count == 0)
            {
                summary.Say("未找到已导入的数据目录，跳过「数据引用键是否登记」的交叉校验。");
                return;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                known.Add(entry.Key);
            }

            foreach (var definition in catalog.Definitions)
            {
                var assetPath = AssetDatabase.GetAssetPath(definition);
                if (!string.IsNullOrEmpty(definition.DisplayNameKey) && !known.Contains(definition.DisplayNameKey))
                {
                    summary.MissingKeysUsedByData++;
                    summary.Report.Error(
                        "LOC_KEY_USED_BY_DATA_MISSING",
                        $"{definition.Id} 的显示名键未登记：{definition.DisplayNameKey}",
                        definitionId: definition.Id,
                        assetPath: assetPath,
                        fieldName: "displayNameKey");
                }

                if (!string.IsNullOrEmpty(definition.DescriptionKey) && !known.Contains(definition.DescriptionKey))
                {
                    summary.MissingKeysUsedByData++;
                    summary.Report.Warn(
                        "LOC_DESC_KEY_USED_BY_DATA_MISSING",
                        $"{definition.Id} 的描述键未登记：{definition.DescriptionKey}",
                        definitionId: definition.Id,
                        assetPath: assetPath,
                        fieldName: "descriptionKey");
                }
            }
        }
    }
}
