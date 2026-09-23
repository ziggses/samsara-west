using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SamsaraWest.Data;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>一次导入的汇总结果，供窗口显示与测试断言。</summary>
    public sealed class CsvImportSummary
    {
        private readonly List<string> _log = new List<string>();

        public ValidationReport Report { get; } = new ValidationReport();

        public IReadOnlyList<string> Log => _log;

        public int ImportedTables { get; internal set; }

        public int SkippedTables { get; internal set; }

        public int CreatedAssets { get; internal set; }

        public int UpdatedAssets { get; internal set; }

        public int RowCount { get; internal set; }

        public int OrphanAssets { get; internal set; }

        public void Say(string message)
        {
            _log.Add(message);
        }
    }

    /// <summary>
    /// CSV → ScriptableObject 导入管线（FND-06）。
    /// 设计要点：
    /// 1. 以文件哈希做增量，未变的表整张跳过；
    /// 2. 按 ID 定位既有资产并<b>就地更新</b>，保住 GUID，场景引用不会断；
    /// 3. 表里删掉的行只报警、不自动删资产——删除是破坏性操作，交给人来点。
    /// </summary>
    public static class CsvImporter
    {
        public static CsvImportSummary ImportAll(bool force)
        {
            var summary = new CsvImportSummary();
            var manifest = LoadOrCreateManifest();

            foreach (var binding in DefinitionImportMap.Bindings)
            {
                ImportTable(binding, manifest, force, summary);
            }

            var tablesFolder = ToAbsolutePath(SamsaraWestPaths.DataTables);
            foreach (var unmapped in DefinitionImportMap.FindUnmappedTables(tablesFolder))
            {
                summary.Report.Warn(
                    "IMPORT_TABLE_UNMAPPED",
                    $"表 {unmapped}.csv 没有对应的定义类型，已跳过。请先在 DefinitionImportMap 登记。",
                    assetPath: $"{SamsaraWestPaths.DataTables}/{unmapped}.csv");
            }

            // 一张表都没导入时不要碰清单：SetDirty 会把内容不变的资产整份重写一遍，mtime 一变，
            // 工作区就平白多出一个「看起来改过」的生成物。（单表导入时 ImportTable 已经标脏过清单。）
            if (summary.ImportedTables > 0)
            {
                EditorUtility.SetDirty(manifest);
            }

            AssetDatabase.SaveAssets();

            RebuildCatalog(summary);
            return summary;
        }

        public static bool ImportTable(
            DefinitionImportBinding binding,
            DataImportManifest manifest,
            bool force,
            CsvImportSummary summary)
        {
            var tableAssetPath = binding.TableAssetPath;
            var absolutePath = ToAbsolutePath(tableAssetPath);
            if (!File.Exists(absolutePath))
            {
                summary.Report.Warn("IMPORT_TABLE_MISSING", $"表文件不存在，未导入：{tableAssetPath}", assetPath: tableAssetPath);
                return false;
            }

            CsvTable table;
            try
            {
                // ReadAllText 会自动识别并剥离 BOM，策划用 Excel 另存也不会把首列名带脏。
                var text = File.ReadAllText(absolutePath, Encoding.UTF8);
                table = CsvParser.Parse(text, Path.GetFileName(tableAssetPath));
            }
            catch (CsvFormatException ex)
            {
                summary.Report.Error("IMPORT_CSV_FORMAT", $"{ex.Message}（第 {ex.LineNumber} 行）", assetPath: tableAssetPath);
                return false;
            }
            catch (IOException ex)
            {
                summary.Report.Error("IMPORT_IO", $"读取失败：{ex.Message}", assetPath: tableAssetPath);
                return false;
            }

            var contentHash = table.ComputeHash().ToString(CultureInfo.InvariantCulture);
            if (!force && manifest.TryGet(binding.TableName, out var previous) &&
                previous.ContentHash == contentHash && AssetsStillExist(previous))
            {
                summary.SkippedTables++;
                summary.Say($"{binding.DisplayName}：内容未变，跳过（{table.RowCount} 行）。");
                return false;
            }

            EnsureFolder(binding.OutputFolder);
            var known = IndexExisting(binding);

            var producedPaths = new List<string>();
            var producedIds = new HashSet<string>(StringComparer.Ordinal);
            var report = new ValidationReport();
            var createdHere = 0;
            var updatedHere = 0;
            var missingIconsHere = 0;

            foreach (var row in table.Rows)
            {
                if (row.IsEmpty)
                {
                    continue;
                }

                string rowId = null;
                if (row.TryGet("id", out var rawId))
                {
                    rowId = rawId?.Trim();
                }

                DefinitionBase target = null;
                string assetPath = null;
                if (!string.IsNullOrEmpty(rowId))
                {
                    assetPath = $"{binding.OutputFolder}/{rowId}.asset";
                    target = AssetDatabase.LoadAssetAtPath<DefinitionBase>(assetPath);
                }

                if (target == null && !string.IsNullOrEmpty(rowId) && known.TryGetValue(rowId, out var knownAsset) && knownAsset != null)
                {
                    // ID 没变但文件名换了：用 MoveAsset 保住 GUID，引用继续有效。
                    var oldPath = AssetDatabase.GetAssetPath(knownAsset);
                    if (!string.IsNullOrEmpty(oldPath) && AssetDatabase.MoveAsset(oldPath, assetPath) == string.Empty)
                    {
                        target = knownAsset;
                    }
                }

                if (target == null)
                {
                    target = CsvDefinitionMapper.Map(binding.DefinitionType, row, table, report);
                    if (target == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(assetPath))
                    {
                        report.Error(
                            "IMPORT_ROW_ID_MISSING",
                            $"第 {row.LineNumber} 行缺少 id，无法落库，已跳过。",
                            assetPath: tableAssetPath,
                            fieldName: "id");
                        continue;
                    }

                    AssetDatabase.CreateAsset(target, assetPath);
                    summary.CreatedAssets++;
                    createdHere++;
                }
                else
                {
                    CsvDefinitionMapper.Apply(target, row, table, report);
                    EditorUtility.SetDirty(target);
                    summary.UpdatedAssets++;
                    updatedHere++;
                }

                producedPaths.Add(assetPath);
                producedIds.Add(target.Id);
                if (target.Icon == null)
                {
                    missingIconsHere++;
                }

                summary.RowCount++;
            }

            foreach (var pair in known)
            {
                if (!producedIds.Contains(pair.Key))
                {
                    summary.OrphanAssets++;
                    report.Warn(
                        "IMPORT_ORPHAN_ASSET",
                        $"资产 {pair.Key} 已不在表中，但未自动删除（避免误删引用）。确认后再手动删除。",
                        definitionId: pair.Key,
                        assetPath: AssetDatabase.GetAssetPath(pair.Value));
                }
            }

            // 缺图标按表汇总成一条：美术未接入时每条定义都缺，逐条报会淹掉真正的问题。
            if (missingIconsHere > 0)
            {
                report.Warn(
                    "DEF_ICONS_PENDING",
                    $"{binding.DisplayName}：{missingIconsHere} 条定义尚无图标（美术接入前属预期，接入后由资源挂接工具回填）。",
                    assetPath: tableAssetPath);
            }

            summary.Report.Absorb(report);

            manifest.GetOrCreate(binding.TableName).Update(contentHash, producedPaths, DateTime.UtcNow);
            EditorUtility.SetDirty(manifest);
            summary.ImportedTables++;
            summary.Say($"{binding.DisplayName}：导入 {producedPaths.Count} 条（新建 {createdHere}，更新 {updatedHere}）。");
            return true;
        }

        /// <summary>把 Definitions 下所有资产重新收进目录资产并跑一次全量校验。</summary>
        public static DefinitionCatalog RebuildCatalog(CsvImportSummary summary)
        {
            EnsureFolder(SamsaraWestPaths.GeneratedRoot);

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DefinitionCatalog>();
                AssetDatabase.CreateAsset(catalog, SamsaraWestPaths.DefinitionCatalogAsset);
            }

            var definitions = new List<DefinitionBase>();
            foreach (var binding in DefinitionImportMap.Bindings)
            {
                var guids = AssetDatabase.FindAssets($"t:{binding.DefinitionType.Name}", new[] { binding.OutputFolder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var definition = AssetDatabase.LoadAssetAtPath<DefinitionBase>(path);
                    if (definition != null)
                    {
                        definitions.Add(definition);
                    }
                }
            }

            // 排序后写入：目录资产的序列化顺序稳定，git diff 才有意义。
            definitions.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));

            // 每次跑初始化都会重建目录，但重建结果常常和磁盘上一模一样。内容没变就别落盘，
            // 否则 mtime 白白变掉，工作区又多一个假改动。
            var contentChanged = CatalogContentDiffers(catalog, definitions);
            catalog.SetDefinitions(definitions);

            var validation = new ValidationReport();
            catalog.Rebuild(validation);
            summary?.Report.Absorb(validation);
            summary?.Say($"目录重建：{catalog.Count} 条定义，重复 ID {catalog.Duplicates.Count} 条，校验问题 {validation.Issues.Count} 条。");

            if (contentChanged)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            return catalog;
        }

        /// <summary>
        /// 目录资产序列化下去的只有 _definitions 这一份列表（ID 索引与重复项都是 NonSerialized），
        /// 所以按引用逐项比对就能确定要不要落盘——同一资产在同一次会话里只会有一个实例。
        /// </summary>
        private static bool CatalogContentDiffers(DefinitionCatalog catalog, List<DefinitionBase> definitions)
        {
            var current = catalog.Definitions;
            if (current.Count != definitions.Count)
            {
                return true;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                if (!ReferenceEquals(current[i], definitions[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static DataImportManifest LoadOrCreateManifest()
        {
            EnsureFolder(SamsaraWestPaths.GeneratedRoot);
            var manifest = AssetDatabase.LoadAssetAtPath<DataImportManifest>(SamsaraWestPaths.ImportManifestAsset);
            if (manifest != null)
            {
                return manifest;
            }

            manifest = ScriptableObject.CreateInstance<DataImportManifest>();
            AssetDatabase.CreateAsset(manifest, SamsaraWestPaths.ImportManifestAsset);
            return manifest;
        }

        public static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            var parts = assetFolder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static bool AssetsStillExist(DataImportManifest.Entry entry)
        {
            if (entry.AssetPaths.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < entry.AssetPaths.Count; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<DefinitionBase>(entry.AssetPaths[i]) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, DefinitionBase> IndexExisting(DefinitionImportBinding binding)
        {
            var result = new Dictionary<string, DefinitionBase>(StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(binding.OutputFolder))
            {
                return result;
            }

            var guids = AssetDatabase.FindAssets($"t:{binding.DefinitionType.Name}", new[] { binding.OutputFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<DefinitionBase>(path);
                if (definition != null && !string.IsNullOrEmpty(definition.Id))
                {
                    result[definition.Id] = definition;
                }
            }

            return result;
        }
    }
}
