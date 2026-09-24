using SamsaraWest.Core;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 骨架自检画面。它自己把自己挂到运行期（不依赖场景里预先摆好的对象），
    /// 因为「启动流程没跑完」正是它最需要出现的时刻 —— 如果还得靠场景接线把它带起来，
    /// 那么接线漏挂时它恰好不会出现，等于没有。
    ///
    /// 用 IMGUI 而不是 uGUI + TMP 的理由：ADR-014 规定「正式界面一律 uGUI + TMP」，
    /// 而本画面不是正式界面，是诊断层（与 <see cref="ErrorOverlay"/> 同类）。uGUI + TMP 需要
    /// TMP 基础资源与一套带中文字形的字体资产，那是内容生产期的事；本画面若强依赖它们，
    /// 就会在「字体资产还没做」的现在直接画不出任何字 —— 正好失去它存在的意义。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkeletonBootScreen : MonoBehaviour
    {
        public const int FontSize = 20;

        /// <summary>自检重算间隔。刻意不做成每帧：每帧拼字符串是这个骨架自己明令禁止的事。</summary>
        public const float RefreshIntervalSeconds = 1f;

        private const int Margin = 16;
        private const int PanelWidth = 620;
        private const int AccentHeight = 3;
        private const int ColorStripHeight = 6;
        private const int ColorStripGap = 6;

        /// <summary>五行取色，同时充当「渲染通路确实在画东西」的凭据：万一字体全挂了，这块色带仍在。</summary>
        private static readonly Color32[] ElementColors =
        {
            new Color32(0xE8, 0xE6, 0xDF, 0xFF), // metal
            new Color32(0x3F, 0xA3, 0x4D, 0xFF), // wood
            new Color32(0x2B, 0x6C, 0xB0, 0xFF), // water
            new Color32(0xC0, 0x39, 0x2B, 0xFF), // fire
            new Color32(0xC9, 0xA2, 0x27, 0xFF), // earth
        };

        /// <summary>
        /// 中文字形候选，按可用性依次尝试。<b>只写 ASCII 字体名</b>：本文件在 UI 层，
        /// 任何中文字面量都会被硬编码中文扫描拦下。这些是操作系统字体名，不是玩家可见文案。
        /// </summary>
        private static readonly string[] CjkFontCandidates =
        {
            "Microsoft YaHei",
            "SimHei",
            "Noto Sans CJK SC",
            "Source Han Sans SC",
            "PingFang SC",
            "Hiragino Sans GB",
            "Arial Unicode MS",
        };

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.88f);
        private static readonly Color HealthyAccent = new Color(0.20f, 0.68f, 0.55f, 1f);
        private static readonly Color UnhealthyAccent = new Color(0.85f, 0.31f, 0.26f, 1f);

        private static SkeletonBootScreen _instance;

        private SkeletonSelfCheckReport _report;
        private Font _font;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _hintStyle;
        private float _nextRefreshAt;
        private bool _logged;

        public static SkeletonBootScreen Instance => _instance;

        /// <summary>关闭后连创建都不再创建。给「骨架期结束、正式界面接管」留的开关。</summary>
        public static bool Enabled { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        public SkeletonSelfCheckReport Report => _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            EnsureCreated();
        }

        public static SkeletonBootScreen EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (!Enabled)
            {
                return null;
            }

            var host = new GameObject("SkeletonBootScreen");
            DontDestroyOnLoad(host);
            return host.AddComponent<SkeletonBootScreen>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _font = ResolveCjkFont();
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            Rebuild();
        }

        /// <summary>重算自检并刷新下一次重算时间。测试直接调它，不依赖等待。</summary>
        public void Rebuild()
        {
            _report = SkeletonSelfCheck.Build();
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;

            if (_logged)
            {
                return;
            }

            _logged = true;
            GameLog.Info(
                LogChannel.UI,
                $"Skeleton boot screen active: healthy={_report.IsHealthy}, lines={_report.Lines.Count}, font={(_font == null ? "builtin" : _font.name)}.");
        }

        /// <summary>
        /// 找一个有中文字形的系统字体。找不到就退回内置字体：那时中文会画不出来，
        /// 但纯 ASCII 的键名与色带仍在，画面不会重新变成「什么都看不见」。
        /// </summary>
        private static Font ResolveCjkFont()
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(CjkFontCandidates, FontSize);
                if (font != null)
                {
                    return font;
                }
            }
            catch (System.Exception exception)
            {
                GameLog.Warn(LogChannel.UI, $"OS font lookup failed, falling back to builtin font: {exception.Message}");
                return null;
            }

            GameLog.Warn(LogChannel.UI, "No CJK OS font found, falling back to builtin font.");
            return null;
        }

        private void OnGUI()
        {
            if (!Enabled || !IsVisible)
            {
                return;
            }

            // 用 IMGUI 自己的事件识别按键，而不是 UnityEngine.Input：
            // 后者在「仅 Input System」的输入后端下会直接抛异常，而 IMGUI 事件与输入后端无关。
            var current = Event.current;
            if (current != null && current.type == EventType.KeyDown && current.keyCode == KeyCode.F1)
            {
                IsVisible = false;
                current.Use();
                return;
            }

            EnsureStyles();

            var lines = _report == null ? System.Array.Empty<string>() : _report.Lines;
            var lineHeight = FontSize + 8;
            var headerHeight = FontSize + 14;
            var height = Margin + headerHeight + AccentHeight + lines.Count * lineHeight + ColorStripHeight + Margin;
            var panel = new Rect(Margin, Margin, PanelWidth, height);

            DrawRect(panel, PanelColor);
            DrawRect(new Rect(panel.x, panel.y, panel.width, AccentHeight), _report != null && _report.IsHealthy ? HealthyAccent : UnhealthyAccent);

            var y = panel.y + AccentHeight + 8f;
            for (var i = 0; i < lines.Count; i++)
            {
                var style = i == 0 ? _titleStyle : i == lines.Count - 1 ? _hintStyle : _lineStyle;
                GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, lineHeight), lines[i], style);
                y += lineHeight;
            }

            var stripWidth = (panel.width - 24f - ColorStripGap * (ElementColors.Length - 1)) / ElementColors.Length;
            for (var i = 0; i < ElementColors.Length; i++)
            {
                DrawRect(
                    new Rect(panel.x + 12f + i * (stripWidth + ColorStripGap), y + 6f, stripWidth, ColorStripHeight),
                    ElementColors[i]);
            }
        }

        private void EnsureStyles()
        {
            if (_lineStyle != null)
            {
                return;
            }

            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
            };
            _lineStyle.normal.textColor = new Color(0.92f, 0.93f, 0.95f);

            if (_font != null)
            {
                _lineStyle.font = _font;
            }

            _titleStyle = new GUIStyle(_lineStyle)
            {
                fontSize = FontSize + 8,
                fontStyle = FontStyle.Bold,
            };

            _hintStyle = new GUIStyle(_lineStyle);
            _hintStyle.normal.textColor = new Color(0.62f, 0.66f, 0.72f);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
