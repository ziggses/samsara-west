using SamsaraWest.Battle;
using SamsaraWest.Data;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>
    /// 无头（CI / 命令行）任务入口。每个方法都能单独当 -executeMethod 的目标，
    /// 并用退出码表达结果，便于流水线判定成败：
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;工程&gt; ^
    ///   -executeMethod SamsaraWest.Editor.HeadlessTasks.ImportAndValidate ^
    ///   -logFile &lt;日志&gt;
    /// </code>
    /// 退出码：0 通过，2 校验有错误或节奏校算不完整，3 命中硬编码中文。
    /// </summary>
    public static class HeadlessTasks
    {
        public const int ExitOk = 0;
        public const int ExitValidationFailed = 2;
        public const int ExitHardcodedChinese = 3;

        /// <summary>全量重导入（忽略哈希）+ 本地化导入 + 全量校验。CI 与「改过导入逻辑」后跑这个。</summary>
        public static void ImportAndValidate()
        {
            var import = CsvImporter.ImportAll(true);
            var localization = LocalizationImporter.ImportAll();

            foreach (var line in import.Log)
            {
                Debug.Log($"[SamsaraWest] {line}");
            }

            Debug.Log($"[SamsaraWest] 导入完成：表 {import.ImportedTables} 张、行 {import.RowCount} 条、新建 {import.CreatedAssets}、更新 {import.UpdatedAssets}、跳过 {import.SkippedTables}。");
            Debug.Log($"[SamsaraWest] {import.Report.Summary()}");
            Debug.Log($"[SamsaraWest] {localization.Report.Summary()}");
            ValidationLog.Write("数据导入", import.Report);
            ValidationLog.Write("本地化导入", localization.Report);

            var failed = import.Report.HasErrors || localization.Report.HasErrors;
            Debug.Log($"[CI] data_errors={CountErrors(import.Report)} loc_errors={CountErrors(localization.Report)} loc_keys={localization.KeyCount} constants_rewritten={localization.ConstantsRewritten}");
            Finish(failed ? ExitValidationFailed : ExitOk);
        }

        /// <summary>不重导入，直接校验现有资产。用于「只想确认当前工程是干净的」。</summary>
        public static void ValidateOnly()
        {
            var summary = new CsvImportSummary();
            CsvImporter.RebuildCatalog(summary);

            var localization = LocalizationImporter.ImportAll();
            Debug.Log($"[SamsaraWest] {summary.Report.Summary()}");
            ValidationLog.Write("数据校验", summary.Report);
            ValidationLog.Write("本地化校验", localization.Report);

            var failed = summary.Report.HasErrors || localization.Report.HasErrors;
            Debug.Log($"[CI] data_errors={CountErrors(summary.Report)} loc_errors={CountErrors(localization.Report)} loc_keys={localization.KeyCount}");
            Finish(failed ? ExitValidationFailed : ExitOk);
        }

        /// <summary>
        /// 战斗节奏校算：全量重导入后按最低配打法打印每场遭遇打几回合，
        /// 让「常规 4–6 回合、Boss 10–15 回合」这条设计目标在不打开编辑器的前提下也能一眼核对。
        /// 越界只报警告：判死的红线由 <c>EncounterPacingTests</c> 持有，这里不做第二套断言。
        /// 退出码：0 校算完整，2 有遭遇算不出来（缺敌人、血量写坏导致回合数不收敛）。
        /// </summary>
        public static void EstimatePacing()
        {
            var import = CsvImporter.ImportAll(true);
            if (import.Report.HasErrors)
            {
                Debug.LogWarning("[SamsaraWest] 表格有错误，节奏校算基于可能不完整的数据。");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            if (catalog == null)
            {
                Debug.LogError("[SamsaraWest] 定义目录资产缺失，无法校算。");
                Finish(ExitValidationFailed);
                return;
            }

            catalog.Rebuild();
            var config = BattleConfig.CreateDefault();
            try
            {
                var total = 0;
                var problems = 0;
                for (var chapter = 1; chapter <= 8; chapter++)
                {
                    var report = EncounterPacingEstimator.EstimateChapter(config, catalog, chapter);
                    if (report.Encounters.Count == 0 && report.Problems.Count == 0)
                    {
                        continue;
                    }

                    Debug.Log($"[SamsaraWest] 第 {chapter} 章：{report.Encounters.Count} 场遭遇");
                    foreach (var pacing in report.Encounters)
                    {
                        Debug.Log($"[SamsaraWest]     {pacing}");
                        total++;
                        if (TryDescribeOutOfRange(pacing, out var note))
                        {
                            Debug.LogWarning($"[SamsaraWest]     {note}");
                        }
                    }

                    foreach (var problem in report.Problems)
                    {
                        Debug.LogWarning($"[SamsaraWest]     {problem}");
                        problems++;
                    }
                }

                Debug.Log($"[CI] pacing_encounters={total} pacing_problems={problems}");
                Finish(problems > 0 ? ExitValidationFailed : ExitOk);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>把「超出设计目标」写成一句人话。只用于命令行提示，不参与判死。</summary>
        private static bool TryDescribeOutOfRange(EncounterPacing pacing, out string note)
        {
            if (pacing.IsBoss)
            {
                if (pacing.Rounds < 10 || pacing.Rounds > 15)
                {
                    note = $"{pacing.EncounterId} 是 Boss 战，设计目标是 10–15 回合，当前 {pacing.Rounds} 回合。";
                    return true;
                }

                if (pacing.PeakRoundHealthShare > 0.25f)
                {
                    note = $"{pacing.EncounterId} 单回合峰值 {pacing.PeakRoundHealthShare:P0}，超过阶段间距 25%。";
                    return true;
                }
            }
            else if (pacing.Rounds < 4 || pacing.Rounds > 6)
            {
                note = $"{pacing.EncounterId} 是常规遭遇，设计目标是 4–6 回合，当前 {pacing.Rounds} 回合。";
                return true;
            }

            note = null;
            return false;
        }

        /// <summary>硬编码中文扫描（FND-07 兜底网）。命中即失败，避免中文文案绕过文本键。</summary>
        public static void ScanHardcodedChinese()
        {
            var report = HardcodedChineseScanner.Scan();
            Debug.Log($"[SamsaraWest] 硬编码中文扫描：查了 {report.ScannedFiles} 个文件，命中 {report.Hits.Count} 处。");
            for (var i = 0; i < report.Hits.Count; i++)
            {
                Debug.LogWarning($"[SamsaraWest] {report.Hits[i]}");
            }

            Debug.Log($"[CI] hardcoded_hits={report.Hits.Count} scanned_files={report.ScannedFiles}");
            Finish(report.Hits.Count > 0 ? ExitHardcodedChinese : ExitOk);
        }

        private static int CountErrors(ValidationReport report)
        {
            var count = 0;
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity == ValidationSeverity.Error)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Finish(int exitCode)
        {
            Debug.Log($"[CI] exit_code={exitCode}");
            if (!Application.isBatchMode)
            {
                return;
            }

            // 批量模式下必须显式退出：否则 -quit 会在退出码上给出 0，流水线看不出失败。
            EditorApplication.Exit(exitCode);
        }
    }
}
