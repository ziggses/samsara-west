using System;
using System.Text;
using SamsaraWest.Data;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>把 ValidationReport 画成可点可筛的问题列表，供各工具窗口复用。</summary>
    public static class ValidationReportView
    {
        private static Vector2 _scroll;
        private static ValidationSeverity _minSeverity = ValidationSeverity.Warning;

        public static void Draw(ValidationReport report, string emptyHint)
        {
            if (report == null)
            {
                EditorGUILayout.HelpBox("尚未执行校验。", MessageType.Info);
                return;
            }

            var errors = report.ErrorCount;
            var warnings = report.WarningCount;
            var summary = $"错误 {errors} 条，警告 {warnings} 条，共 {report.Issues.Count} 条。";

            EditorGUILayout.HelpBox(summary, errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _minSeverity = (ValidationSeverity)EditorGUILayout.EnumPopup("最低级别", _minSeverity);
                if (GUILayout.Button("复制全部", GUILayout.Width(90)))
                {
                    CopyToClipboard(report);
                }
            }

            var visible = report.WithSeverity(_minSeverity);
            if (visible.Count == 0)
            {
                EditorGUILayout.LabelField(emptyHint, EditorStyles.miniLabel);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var issue in visible)
            {
                DrawIssue(issue);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawIssue(in ValidationIssue issue)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var color = issue.Severity == ValidationSeverity.Error
                        ? new Color(0.95f, 0.45f, 0.40f)
                        : issue.Severity == ValidationSeverity.Warning
                            ? new Color(0.95f, 0.80f, 0.35f)
                            : new Color(0.65f, 0.75f, 0.90f);

                    var previous = GUI.contentColor;
                    GUI.contentColor = color;
                    EditorGUILayout.LabelField(issue.Severity.ToString(), GUILayout.Width(60));
                    GUI.contentColor = previous;

                    EditorGUILayout.LabelField(issue.Code, GUILayout.Width(220));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(issue.DefinitionId) ? "-" : issue.DefinitionId, GUILayout.Width(180));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(issue.AssetPath) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(issue.AssetPath) == null))
                    {
                        if (GUILayout.Button("定位", GUILayout.Width(60)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(issue.AssetPath);
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);
            }
        }

        private static void CopyToClipboard(ValidationReport report)
        {
            var builder = new StringBuilder(report.Issues.Count * 96);
            builder.AppendLine(report.Summary());
            foreach (var issue in report.Issues)
            {
                builder.AppendLine(issue.ToString());
            }

            EditorGUIUtility.systemCopyBuffer = builder.ToString();
            Debug.Log($"[SamsaraWest] 已复制 {report.Issues.Count} 条校验问题到剪贴板。");
        }
    }
}
