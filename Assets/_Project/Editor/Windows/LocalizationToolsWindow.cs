using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>本地化工具：导入中文表、生成 key 常量、扫描硬编码中文。</summary>
    public sealed class LocalizationToolsWindow : EditorWindow
    {
        private SamsaraWest.Data.ValidationReport _report;
        private HardcodedChineseReport _scan;
        private string _log = string.Empty;
        private Vector2 _scroll;

        [MenuItem("SamsaraWest/本地化/本地化工具窗口", priority = 21)]
        public static void Open()
        {
            var window = GetWindow<LocalizationToolsWindow>("本地化工具");
            window.minSize = new Vector2(680f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("本地化（中文单语，界面全量走 key）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"源表：{LocalizationImporter.SourceTableAssetPath}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"生成的常量：{SamsaraWestPaths.LocalizationKeysSource}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导入并生成常量", GUILayout.Height(26)))
                {
                    RunImport();
                }

                if (GUILayout.Button("扫描硬编码中文", GUILayout.Height(26)))
                {
                    _scan = HardcodedChineseScanner.Scan();
                    _log = $"扫描完成：检查 {_scan.ScannedFiles} 个文件，命中 {_scan.Hits.Count} 处。";
                }

                if (GUILayout.Button("打开源表目录", GUILayout.Height(26)))
                {
                    EditorUtility.RevealInFinder(CsvImporter.ToAbsolutePath(SamsaraWestPaths.LocalizationTables));
                }
            }

            EditorGUILayout.Space(6f);
            ValidationReportView.Draw(_report, "本地化表没有发现问题。");

            if (_scan != null)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField($"硬编码中文命中 {_scan.Hits.Count} 处（允许清单：{SamsaraWestPaths.LocalizationScanIgnoreFile}）", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(200f));
                foreach (var hit in _scan.Hits)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"{hit.AssetPath}:{hit.LineNumber}", GUILayout.Width(360f));
                        EditorGUILayout.LabelField($"{hit.Reason} → {hit.Snippet}", EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("执行记录", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_log, EditorStyles.wordWrappedLabel);
        }

        private void RunImport()
        {
            var summary = LocalizationImporter.ImportAll();
            _report = summary.Report;

            var builder = new StringBuilder();
            builder.AppendLine($"导入 {summary.KeyCount} 个键；常量{(summary.ConstantsRewritten ? "已重写" : "无变化")}；数据引用了但未登记的键 {summary.MissingKeysUsedByData} 个。");
            foreach (var line in summary.Log)
            {
                builder.AppendLine(line);
            }

            _log = builder.ToString();
            Debug.Log($"[SamsaraWest] 本地化导入完成。{summary.Report.Summary()}");
        }
    }
}
