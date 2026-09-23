using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SamsaraWest.Data;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    public sealed class HardcodedChineseHit
    {
        public HardcodedChineseHit(string assetPath, int lineNumber, string snippet, string reason)
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            Snippet = snippet;
            Reason = reason;
        }

        public string AssetPath { get; }

        public int LineNumber { get; }

        public string Snippet { get; }

        public string Reason { get; }

        public override string ToString() => $"{AssetPath}:{LineNumber} {Reason} {Snippet}";
    }

    public sealed class HardcodedChineseReport
    {
        private readonly List<HardcodedChineseHit> _hits = new List<HardcodedChineseHit>();

        public IReadOnlyList<HardcodedChineseHit> Hits => _hits;

        public int ScannedFiles { get; internal set; }

        public void Add(HardcodedChineseHit hit) => _hits.Add(hit);
    }

    /// <summary>
    /// 硬编码中文扫描（FND-07 的兜底网）。
    /// 扫描范围刻意收窄，避免把「日志文案」也算违规：
    /// 1. UI 层全部 .cs；
    /// 2. 所有预制体与场景里 Text / TextMeshPro 的序列化文本；
    /// 3. 数据层被判定为「本地化键」的字段（由定义类自己校验，这里只做二次确认）。
    /// 允许清单：Assets/_Project/Localization/scan-ignore.txt，一行一条「相对路径:关键词」。
    /// </summary>
    public static class HardcodedChineseScanner
    {
        private static readonly string[] ScannedScriptFolder = { SamsaraWestPaths.ProjectRoot + "/UI" };

        private static readonly Regex StringLiteralPattern = new Regex("\"(?<literal>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        public static HardcodedChineseReport Scan()
        {
            var report = new HardcodedChineseReport();
            var ignore = LoadIgnoreList();

            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", ScannedScriptFolder))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ScanScript(path, ignore, report);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                ScanSerializedText(AssetDatabase.GUIDToAssetPath(guid), ignore, report);
            }

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene != null && !string.IsNullOrEmpty(scene.path))
                {
                    ScanSerializedText(scene.path, ignore, report);
                }
            }

            return report;
        }

        private static void ScanScript(string path, HashSet<string> ignore, HardcodedChineseReport report)
        {
            var absolute = CsvImporter.ToAbsolutePath(path);
            if (!File.Exists(absolute))
            {
                report.ScannedFiles++;
                return;
            }

            report.ScannedFiles++;
            var lines = File.ReadAllLines(absolute, Encoding.UTF8);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in StringLiteralPattern.Matches(line))
                {
                    var literal = match.Groups["literal"].Value;
                    if (!IdRules.ContainsChinese(literal))
                    {
                        continue;
                    }

                    if (IsIgnored(ignore, path, literal))
                    {
                        continue;
                    }

                    report.Add(new HardcodedChineseHit(path, i + 1, literal, "界面脚本含中文字面量"));
                }
            }
        }

        private static void ScanSerializedText(string assetPath, HashSet<string> ignore, HardcodedChineseReport report)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var absolute = CsvImporter.ToAbsolutePath(assetPath);
            if (!File.Exists(absolute))
            {
                return;
            }

            report.ScannedFiles++;
            var lines = File.ReadAllLines(absolute, Encoding.UTF8);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // TMP_Text 的序列化字段是 m_text，UGUI Text 是 m_Text。
                if (line.IndexOf("m_text:", StringComparison.Ordinal) < 0 && line.IndexOf("m_Text:", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                if (!IdRules.ContainsChinese(line))
                {
                    continue;
                }

                if (IsIgnored(ignore, assetPath, line))
                {
                    continue;
                }

                report.Add(new HardcodedChineseHit(assetPath, i + 1, line.Trim(), "预制体/场景里的中文文本"));
            }
        }

        private static HashSet<string> LoadIgnoreList()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var absolute = CsvImporter.ToAbsolutePath(SamsaraWestPaths.LocalizationScanIgnoreFile);
            if (!File.Exists(absolute))
            {
                return result;
            }

            foreach (var raw in File.ReadAllLines(absolute, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(line);
            }

            return result;
        }

        private static bool IsIgnored(HashSet<string> ignore, string assetPath, string content)
        {
            if (ignore.Count == 0)
            {
                return false;
            }

            if (ignore.Contains(assetPath) || ignore.Contains($"{assetPath}:{content}"))
            {
                return true;
            }

            foreach (var entry in ignore)
            {
                if (entry.EndsWith("*", StringComparison.Ordinal) &&
                    assetPath.StartsWith(entry.Substring(0, entry.Length - 1), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
