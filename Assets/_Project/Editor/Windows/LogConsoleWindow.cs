using System;
using System.Collections.Generic;
using System.Text;
using SamsaraWest.Core;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor
{
    /// <summary>
    /// 日志控制台（FND-08）：按级别与频道过滤 GameLog 的环形缓冲。
    /// 运行期错误面板只覆盖 Development Build，这里覆盖编辑器内的排查场景。
    /// </summary>
    public sealed class LogConsoleWindow : EditorWindow
    {
        private static readonly string[] ChannelNames = Enum.GetNames(typeof(LogChannel));

        private LogLevel _minLevel = LogLevel.Info;
        private string _filter = string.Empty;
        private int _channelIndex;
        private Vector2 _scroll;
        private bool _onlyFailures;

        [MenuItem("SamsaraWest/诊断/日志控制台", priority = 40)]
        public static void Open()
        {
            var window = GetWindow<LogConsoleWindow>("日志控制台");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _minLevel = (LogLevel)EditorGUILayout.EnumPopup(_minLevel, GUILayout.Width(110f));
                _channelIndex = EditorGUILayout.Popup(_channelIndex, Prepend("全部频道", ChannelNames), GUILayout.Width(120f));
                _onlyFailures = GUILayout.Toggle(_onlyFailures, "只看失败", EditorStyles.toolbarButton, GUILayout.Width(80f));
                _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    Repaint();
                }

                if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    GameLog.History.Clear();
                    Repaint();
                }

                if (GUILayout.Button("复制", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildText();
                }
            }

            EditorGUILayout.LabelField($"缓冲容量 {GameLog.History.Capacity}，当前 {GameLog.History.Count} 条，累计写入 {GameLog.TotalCount} 条。", EditorStyles.miniLabel);

            var records = GameLog.Snapshot(_minLevel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            var shown = 0;
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (!Matches(record))
                {
                    continue;
                }

                shown++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var previous = GUI.contentColor;
                    GUI.contentColor = ColorFor(record.Level);
                    EditorGUILayout.LabelField(record.Level.ToString(), GUILayout.Width(52f));
                    GUI.contentColor = previous;

                    EditorGUILayout.LabelField($"{record.ElapsedSeconds:0.000}s", GUILayout.Width(72f));
                    EditorGUILayout.LabelField(record.Channel.ToString(), GUILayout.Width(80f));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(record.Context) ? "-" : record.Context, GUILayout.Width(140f));
                    EditorGUILayout.LabelField(record.Message, EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"显示 {shown} / {records.Length} 条。", EditorStyles.miniLabel);
        }

        private bool Matches(in LogRecord record)
        {
            if (_onlyFailures && !record.IsFailure)
            {
                return false;
            }

            if (_channelIndex > 0 && !string.Equals(record.Channel.ToString(), ChannelNames[_channelIndex - 1], StringComparison.Ordinal))
            {
                return false;
            }

            if (_filter.Length == 0)
            {
                return true;
            }

            return record.Message.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (record.Context ?? string.Empty).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string BuildText()
        {
            var records = GameLog.Snapshot(_minLevel);
            var builder = new StringBuilder(records.Length * 128);
            for (var i = 0; i < records.Length; i++)
            {
                if (Matches(records[i]))
                {
                    builder.AppendLine(records[i].ToString());
                }
            }

            return builder.ToString();
        }

        private static string[] Prepend(string first, string[] rest)
        {
            var result = new List<string>(rest.Length + 1) { first };
            result.AddRange(rest);
            return result.ToArray();
        }

        private static Color ColorFor(LogLevel level) => level switch
        {
            LogLevel.Error => new Color(0.95f, 0.45f, 0.40f),
            LogLevel.Warning => new Color(0.95f, 0.80f, 0.35f),
            LogLevel.Info => new Color(0.80f, 0.85f, 0.90f),
            _ => new Color(0.60f, 0.65f, 0.70f),
        };
    }
}
