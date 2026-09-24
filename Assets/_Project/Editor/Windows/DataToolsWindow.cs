using System.Text;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>策划与程序共用的数据工具窗口：导入、校验、看问题清单。</summary>
    public sealed class DataToolsWindow : EditorWindow
    {
        private ValidationReport _report;
        private string _log = string.Empty;
        private Vector2 _logScroll;
        private int _catalogCount;

        [MenuItem("SamsaraWest/数据/数据工具窗口 %#d", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<DataToolsWindow>("数据工具");
            window.minSize = new Vector2(680f, 420f);
            window.Show();
        }

        /// <summary>
        /// 战斗节奏校算：按最低配打法估算每场遭遇打几回合，用来核对「常规 4–6 回合、Boss 10–15 回合」
        /// 这条设计目标。与 EncounterPacingTests 共用同一套模型，所以这里看到的就是测试断言的口径。
        /// </summary>
        [MenuItem("SamsaraWest/数据/战斗节奏校算", priority = 22)]
        public static void EstimatePacing()
        {
            const string context = "战斗节奏校算";
            var summary = CsvImporter.ImportAll(false);
            if (summary.Report.HasErrors)
            {
                GameLog.Warn(LogChannel.Data, "表格有错误，节奏校算基于可能过期的数据。", context);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            if (catalog == null)
            {
                GameLog.Error(LogChannel.Data, "定义目录资产缺失，先执行一次全量重导。", context);
                return;
            }

            var config = BattleConfig.CreateDefault();
            try
            {
                var total = 0;
                for (var chapter = 1; chapter <= 8; chapter++)
                {
                    var report = EncounterPacingEstimator.EstimateChapter(config, catalog, chapter);
                    if (report.Encounters.Count == 0)
                    {
                        continue;
                    }

                    GameLog.Info(LogChannel.Data, $"第 {chapter} 章：{report.Encounters.Count} 场遭遇", context);
                    foreach (var pacing in report.Encounters)
                    {
                        GameLog.Info(LogChannel.Data, "    " + pacing, context);
                        total++;
                    }

                    foreach (var problem in report.Problems)
                    {
                        GameLog.Warn(LogChannel.Data, problem, context);
                    }
                }

                GameLog.Info(LogChannel.Data, $"校算完成，共 {total} 场遭遇；口径见 Docs/战斗数值-v1.md。", context);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("数据管线（CSV → ScriptableObject → 目录资产）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"表格目录：{SamsaraWestPaths.DataTables}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"资产目录：{SamsaraWestPaths.DefinitionsRoot}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"当前目录条目：{_catalogCount}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("增量导入（只处理改动过的表）", GUILayout.Height(26)))
                {
                    RunImport(false);
                }

                if (GUILayout.Button("全量重导", GUILayout.Height(26)))
                {
                    RunImport(true);
                }

                if (GUILayout.Button("只校验", GUILayout.Height(26)))
                {
                    RunValidate();
                }

                if (GUILayout.Button("打开表格目录", GUILayout.Height(26)))
                {
                    EditorUtility.RevealInFinder(CsvImporter.ToAbsolutePath(SamsaraWestPaths.DataTables));
                }
            }

            EditorGUILayout.Space(6f);
            ValidationReportView.Draw(_report, "没有发现问题。");

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("执行记录", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MaxHeight(140f));
            EditorGUILayout.TextArea(_log, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private void RunImport(bool force)
        {
            var summary = CsvImporter.ImportAll(force);
            _report = summary.Report;
            _catalogCount = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset)?.Count ?? 0;

            var builder = new StringBuilder();
            builder.AppendLine($"导入完成：处理 {summary.ImportedTables} 张表、跳过 {summary.SkippedTables} 张，行数 {summary.RowCount}，新建 {summary.CreatedAssets}，更新 {summary.UpdatedAssets}，疑似废弃资产 {summary.OrphanAssets}。");
            foreach (var line in summary.Log)
            {
                builder.AppendLine(line);
            }

            _log = builder.ToString();
            Debug.Log($"[SamsaraWest] 数据导入完成。{summary.Report.Summary()}");
        }

        private void RunValidate()
        {
            CsvImporter.RebuildCatalog(new CsvImportSummary());
            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            _report = new ValidationReport();
            catalog?.Rebuild(_report);
            _catalogCount = catalog?.Count ?? 0;
            _log = $"校验完成。{_report.Summary()}";
            Debug.Log($"[SamsaraWest] 数据校验完成。{_report.Summary()}");
        }
    }
}
