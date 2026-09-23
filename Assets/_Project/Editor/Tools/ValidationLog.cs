using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>
    /// 把校验结论落进 Unity 日志。窗口与命令行共用同一套输出格式——
    /// 无头运行时没有界面可看，日志是唯一能说明「到底哪一行数据错了」的地方。
    /// 输出顺序刻意是「先错误、后警告」，这样被截断时丢掉的只会是最不重要的提示。
    /// </summary>
    public static class ValidationLog
    {
        /// <summary>单次最多列出多少条错误与警告，避免日志被刷爆；完整清单在数据工具窗口。</summary>
        public const int DefaultMax = 40;

        public static void Write(string title, ValidationReport report, int max = DefaultMax)
        {
            if (report == null || report.Issues.Count == 0)
            {
                return;
            }

            var notable = 0;
            var info = 0;
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity >= ValidationSeverity.Warning)
                {
                    notable++;
                }
                else
                {
                    info++;
                }
            }

            var shown = WriteWithSeverity(title, report, ValidationSeverity.Error, 0, max);
            shown += WriteWithSeverity(title, report, ValidationSeverity.Warning, shown, max);

            if (notable > shown)
            {
                Debug.LogWarning($"[SamsaraWest] {title}：另有 {notable - shown} 条错误/警告未在日志中列出（共 {notable} 条），完整清单见 SamsaraWest/数据/数据工具窗口。");
            }

            if (info > 0)
            {
                // 提示级（例如「美术未接入前缺图标」）单独汇总成一行，不再逐条铺开。
                Debug.Log($"[SamsaraWest] {title}：另有 {info} 条提示级结论（不阻塞导入），见数据工具窗口。");
            }
        }

        private static int WriteWithSeverity(
            string title,
            ValidationReport report,
            ValidationSeverity severity,
            int alreadyShown,
            int max)
        {
            var count = 0;
            for (var i = 0; i < report.Issues.Count && alreadyShown + count < max; i++)
            {
                var issue = report.Issues[i];
                if (issue.Severity != severity)
                {
                    continue;
                }

                count++;
                var line = $"[SamsaraWest] {title}：{issue}";
                if (severity == ValidationSeverity.Error)
                {
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }

            return count;
        }
    }
}
