using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Editor;
using SamsaraWest.Localization;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 数据管线的验收测试：17 张表能导、能增量跳过、就地更新不断引用、
    /// 校验零错误零重复 ID，本地化键被数据资产完整覆盖，界面无硬编码中文。
    /// 这些断言直接跑真实工程数据，坏数据会让它们立刻变红。
    /// </summary>
    public sealed class DataPipelineTests
    {
        private const string ItemsTable = "items";

        [Test]
        public void ImportMap_CoversSeventeenTablesAndEveryTableFileExists()
        {
            var bindings = DefinitionImportMap.Bindings;
            Assert.AreEqual(17, bindings.Count, "定义类型数量变了就要同步改这里，避免漏登记的表在导入时被静默跳过。");

            var tableNames = new HashSet<string>();
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                Assert.IsTrue(tableNames.Add(binding.TableName), $"表名重复：{binding.TableName}");
                Assert.IsTrue(
                    typeof(DefinitionBase).IsAssignableFrom(binding.DefinitionType),
                    $"{binding.DisplayName} 的定义类型必须继承 DefinitionBase。");
                Assert.IsTrue(
                    File.Exists(CsvImporter.ToAbsolutePath(binding.TableAssetPath)),
                    $"缺少策划表：{binding.TableAssetPath}");
            }
        }

        [Test]
        public void ImportAll_ReportsNoErrorsAndNoDuplicateIds()
        {
            // 强制全量导入：清单已是最新时增量会整表跳过，那测不到「表能导、数据没错」这件事。
            // 增量语义另由 ImportAll_SecondRun_SkipsEveryUnchangedTable 覆盖。
            var summary = CsvImporter.ImportAll(force: true);

            Assert.AreEqual(0, summary.Report.ErrorCount, Describe(summary));
            Assert.AreEqual(DefinitionImportMap.Bindings.Count, summary.ImportedTables + summary.SkippedTables);
            Assert.Greater(summary.RowCount, 0, "一行都没导入说明表结构或映射已经不对了。");

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            Assert.IsNotNull(catalog, "导入后必须存在定义目录资产。");
            Assert.AreEqual(0, catalog.Duplicates.Count, "重复 ID 必须为 0，这是本次交付的验收项。");
            Assert.Greater(catalog.Count, 0);
        }

        [Test]
        public void ImportAll_SecondRun_SkipsEveryUnchangedTable()
        {
            // 增量导入的核心承诺：改一张表只处理一张表。表没变时应当整张跳过。
            CsvImporter.ImportAll(force: false);

            var second = CsvImporter.ImportAll(force: false);

            Assert.AreEqual(0, second.Report.ErrorCount, Describe(second));
            Assert.AreEqual(DefinitionImportMap.Bindings.Count, second.SkippedTables, "内容未变的表应当全部跳过。");
            Assert.AreEqual(0, second.ImportedTables);
            Assert.AreEqual(0, second.CreatedAssets);
            Assert.AreEqual(0, second.UpdatedAssets);
        }

        [Test]
        public void GeneratedArtifacts_AreNotRewrittenWhenNothingChanged()
        {
            // 「跳过」如果只做到逻辑上跳过，Unity 仍会把内容相同的资产整份重写：mtime 一变，
            // 跑完初始化工作区就多出几个假改动。所以这里不看逻辑，直接看文件写入时间。
            CsvImporter.ImportAll(force: true);
            LocalizationImporter.ImportAll();
            AssetDatabase.SaveAssets();

            var artifacts = new[]
            {
                SamsaraWestPaths.ImportManifestAsset,
                SamsaraWestPaths.DefinitionCatalogAsset,
                SamsaraWestPaths.LocalizationTableAsset,
            };

            var before = new System.DateTime[artifacts.Length];
            for (var i = 0; i < artifacts.Length; i++)
            {
                before[i] = File.GetLastWriteTimeUtc(CsvImporter.ToAbsolutePath(artifacts[i]));
            }

            CsvImporter.ImportAll(force: false);
            LocalizationImporter.ImportAll();

            for (var i = 0; i < artifacts.Length; i++)
            {
                Assert.AreEqual(
                    before[i],
                    File.GetLastWriteTimeUtc(CsvImporter.ToAbsolutePath(artifacts[i])),
                    $"{artifacts[i]} 内容未变却被重写，跑完初始化工作区会平白多出改动。");
            }
        }

        [Test]
        public void ImportTable_ForcedUpdate_KeepsAssetPathAndGuid()
        {
            Assert.IsTrue(DefinitionImportMap.TryResolve(ItemsTable, out var binding));

            var guids = AssetDatabase.FindAssets($"t:{binding.DefinitionType.Name}", new[] { binding.OutputFolder });
            Assert.Greater(guids.Length, 0, "先跑一次导入再谈就地更新。");

            var guid = guids[0];
            var pathBefore = AssetDatabase.GUIDToAssetPath(guid);
            var idBefore = AssetDatabase.LoadAssetAtPath<DefinitionBase>(pathBefore).Id;

            var summary = new CsvImportSummary();
            Assert.IsTrue(CsvImporter.ImportTable(binding, CsvImporter.LoadOrCreateManifest(), force: true, summary: summary));

            Assert.AreEqual(0, summary.Report.ErrorCount, Describe(summary));
            Assert.AreEqual(0, summary.CreatedAssets, "已存在资产必须就地更新而不是重建。");
            Assert.Greater(summary.UpdatedAssets, 0);
            Assert.AreEqual(pathBefore, AssetDatabase.GUIDToAssetPath(guid), "GUID 与路径不能变，否则场景引用会断。");
            Assert.AreEqual(idBefore, AssetDatabase.LoadAssetAtPath<DefinitionBase>(pathBefore).Id);
        }

        [Test]
        public void ImportManifest_RecordsHashAndAssetsForEveryTable()
        {
            CsvImporter.ImportAll(force: false);
            var manifest = CsvImporter.LoadOrCreateManifest();

            for (var i = 0; i < DefinitionImportMap.Bindings.Count; i++)
            {
                var binding = DefinitionImportMap.Bindings[i];
                Assert.IsTrue(manifest.TryGet(binding.TableName, out var entry), $"清单缺少表 {binding.TableName} 的记录。");
                Assert.IsNotEmpty(entry.ContentHash, $"{binding.TableName} 应有内容哈希，否则增量导入无法判断是否变更。");
                Assert.Greater(entry.AssetPaths.Count, 0, $"{binding.TableName} 应记录产出的资产路径。");
                Assert.IsNotEmpty(entry.ImportedAtUtc);
            }
        }

        [Test]
        public void LocalizationImport_ReportsNoErrorsAndCoversEveryKeyUsedByData()
        {
            // 先确保目录资产是最新的，交叉校验才有意义。
            CsvImporter.RebuildCatalog(null);

            var summary = LocalizationImporter.ImportAll();

            Assert.AreEqual(0, summary.Report.ErrorCount, Describe(summary));
            Assert.Greater(summary.KeyCount, 0);
            Assert.AreEqual(0, summary.MissingKeysUsedByData, "数据资产的显示名/描述键必须全在文本表里登记。");

            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(SamsaraWestPaths.LocalizationTableAsset);
            Assert.IsNotNull(table);
            Assert.AreEqual(summary.KeyCount, table.Count);

            var service = new LocalizationService(table);
            Assert.AreEqual(summary.KeyCount, service.KeyCount);
        }

        [Test]
        public void LocalizationImport_GeneratesCompilableConstantClass()
        {
            LocalizationImporter.ImportAll();

            var absolute = CsvImporter.ToAbsolutePath(SamsaraWestPaths.LocalizationKeysSource);
            Assert.IsTrue(File.Exists(absolute), "文本键常量类应当被生成，写错键名要编不过而不是运行期空白。");

            var source = File.ReadAllText(absolute, Encoding.UTF8);
            StringAssert.Contains($"public static class {LocalizationImporter.ConstantClassName}", source);
            StringAssert.Contains("public const string ", source);
            Assert.IsFalse(source.Contains("\r\n"), "常量类统一用 LF，避免跨平台产生无意义的 diff。");
        }

        [Test]
        public void LocalizationImport_SecondRun_DoesNotRewriteConstantClass()
        {
            LocalizationImporter.ImportAll();

            var second = LocalizationImporter.ImportAll();

            Assert.IsFalse(second.ConstantsRewritten, "内容未变时不应重写常量类，否则每次导入都会产生 git diff。");
        }

        [Test]
        public void HardcodedChineseScanner_FindsNothing()
        {
            var report = HardcodedChineseScanner.Scan();

            Assert.Greater(report.ScannedFiles, 0, "扫描范围至少要覆盖到 UI 脚本，否则这个测试是空的。");
            Assert.AreEqual(0, report.Hits.Count, DescribeHits(report));
        }

        private static string Describe(CsvImportSummary summary) => Describe(summary.Report, summary.Log);

        private static string Describe(LocalizationImportSummary summary) => Describe(summary.Report, summary.Log);

        private static string Describe(ValidationReport report, IReadOnlyList<string> log)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < report.Issues.Count; i++)
            {
                builder.AppendLine(report.Issues[i].ToString());
            }

            for (var i = 0; i < log.Count; i++)
            {
                builder.AppendLine(log[i]);
            }

            return builder.ToString();
        }

        private static string DescribeHits(HardcodedChineseReport report)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < report.Hits.Count; i++)
            {
                builder.AppendLine(report.Hits[i].ToString());
            }

            return builder.ToString();
        }
    }
}
