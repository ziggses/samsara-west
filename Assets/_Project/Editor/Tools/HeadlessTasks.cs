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
    /// 退出码：0 通过，2 校验有错误，3 命中硬编码中文。
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
