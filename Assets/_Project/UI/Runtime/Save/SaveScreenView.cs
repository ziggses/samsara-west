using System;
using System.Globalization;
using SamsaraWest.Core;
using SamsaraWest.Localization;
using SamsaraWest.Save;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 存档界面的诊断层视图：一个 F5 存、一个 F9 读，外加一行「上一次干了什么」。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有这个入口</b>：存档接线要是没有按键，就只有用例会碰它——
    /// 那正是「写完了但没人用」的样子。骨架期还没有存档菜单（正式界面见 ADR-014），
    /// 于是先给一层手按的入口，让人能真的存一次、真的读一次。</para>
    ///
    /// <para><b>它只认 <see cref="ISaveCoordinator"/></b>：不认探索、不认账本、更不认组合根。
    /// 「一局的状态怎么收成一份存档」不是界面该知道的事，它只知道「按一下、然后看结果」。
    /// 这与 <c>ProjectSkeletonTests</c> 锁住的「UI 不依赖 Flow」是同一条边界——
    /// 界面能存能读，但不知道是谁在装这些服务。</para>
    ///
    /// <para><b>输入走 IMGUI 事件</b>：与另三层同一取舍，<c>UnityEngine.Input</c> 在
    /// 「仅 Input System」的后端下会直接抛异常。F3 与会另两层共用（都是「收起面板」）。</para>
    ///
    /// <para><b>面板画在左下角</b>：左上角已经被探索面板占着，两个诊断层叠在一起会互相遮住，
    /// 而它们要同时看得见才核对得动（存之前先看一眼人在哪、账记了几条）。</para>
    ///
    /// <para><b>不硬编码中文</b>：所有文案来自文本表，与另三层同一条纪律——
    /// 本层一个中文字面量都不许有，否则硬编码扫描会拦下。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SaveScreenView : MonoBehaviour
    {
        /// <summary>与自检／战斗／探索面板同一档字号。</summary>
        public const int FontSize = 18;

        /// <summary>诊断入口用的槽位。正式存档菜单会有多个槽位，那时这个常量就该退休。</summary>
        public const int DiagnosticSlot = 1;

        private const int Margin = 16;
        private const int PanelWidth = 620;
        private const int AccentHeight = 3;
        private const int Padding = 12;
        private const int Gap = 6;
        private const int LineHeight = FontSize + 8;

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.90f);
        private static readonly Color AccentColor = new Color(0.72f, 0.58f, 0.32f, 1f);
        private static readonly Color LineColor = new Color(0.90f, 0.91f, 0.94f);
        private static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color WarnColor = new Color(0.95f, 0.66f, 0.30f);

        /// <summary>槽位现在是什么样，以及上一次动作把它变成了什么样。</summary>
        public enum SlotStatus
        {
            /// <summary>还没动过、也没问过文件系统。</summary>
            Unknown,

            /// <summary>槽位上没有存档。</summary>
            Empty,

            /// <summary>槽位上有存档，但这一轮还没读过它。</summary>
            Present,

            /// <summary>刚写进去一份。</summary>
            Saved,

            /// <summary>刚读回来一份。</summary>
            Loaded,

            /// <summary>动作失败（校验不过、图不存在、磁盘出错…），细节在日志里。</summary>
            Failed,

            /// <summary>存档服务还没就位。</summary>
            Unavailable,
        }

        private static SaveScreenView _instance;

        private ISaveCoordinator _coordinator;
        private ISaveService _saves;
        private ILocalizationService _localization;

        /// <summary>已解析过的那张注册表。换了一张就说明服务重装过，缓存必须整个丢掉。</summary>
        private IServiceRegistry _registryRef;

        private string _titleLine;
        private string _statusLine;
        private string _hintLine;

        private Font _font;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _hintStyle;

        public static SaveScreenView Instance => _instance;

        /// <summary>关闭后连创建都不再创建。给「正式存档界面接管」留的开关。</summary>
        public static bool Enabled { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 诊断用的「按 F5 存、按 F9 读」是否允许。与另三层同一条口径：
        /// 只在编辑器与开发版里给——正式包里不该有一个按键能把一局的进度灌回运行期。
        /// </summary>
        public static bool IsDiagnosticSaveSupported =>
            Application.isEditor || Debug.isDebugBuild;

        /// <summary>槽位当前的状态。存／读之后变成对应结果。</summary>
        public SlotStatus Status { get; private set; } = SlotStatus.Unknown;

        /// <summary>最近一次画出去的那三行，供用例核对「确实画了文本表里的字」。</summary>
        public string TitleLine => _titleLine;

        public string StatusLine => _statusLine;

        public string HintLine => _hintLine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            EnsureCreated();
        }

        public static SaveScreenView EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (!Enabled)
            {
                return null;
            }

            var host = new GameObject("SaveScreenView");
            DontDestroyOnLoad(host);
            return host.AddComponent<SaveScreenView>();
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
            Tick();
        }

        /// <summary>一步：解析服务 → 首次就绪时建一次快照。每帧调它是安全的。</summary>
        public void Tick()
        {
            EnsureServices();
        }

        /// <summary>
        /// 存到诊断槽位。
        /// </summary>
        /// <remarks>
        /// 公开是为了让用例直接调它：接口能被用例驱动，按键只是接口的一种触发方式。
        /// </remarks>
        public bool SaveToDiagnosticSlot()
        {
            EnsureServices();

            if (_coordinator == null)
            {
                SetStatus(SlotStatus.Unavailable);
                return false;
            }

            var saved = _coordinator.SaveToSlot(DiagnosticSlot);
            SetStatus(saved ? SlotStatus.Saved : SlotStatus.Failed);
            return saved;
        }

        /// <summary>
        /// 从诊断槽位读回来。
        /// </summary>
        /// <remarks>
        /// 先问 <see cref="ISaveService.Exists"/> 再读：没有存档与读坏了要玩家做的事不一样，
        /// 把它们混成一句「失败」会让人去翻一份根本不存在的错误日志。
        /// </remarks>
        public bool LoadFromDiagnosticSlot()
        {
            EnsureServices();

            if (_coordinator == null || _saves == null)
            {
                SetStatus(SlotStatus.Unavailable);
                return false;
            }

            if (!_saves.Exists(DiagnosticSlot))
            {
                SetStatus(SlotStatus.Empty);
                return false;
            }

            var loaded = _coordinator.LoadFromSlot(DiagnosticSlot);
            SetStatus(loaded ? SlotStatus.Loaded : SlotStatus.Failed);
            return loaded;
        }

        private void SetStatus(SlotStatus status)
        {
            Status = status;
            Rebuild();
        }

        /// <summary>立即重算画面文案。用例直接调它，不依赖等待。</summary>
        public void Rebuild()
        {
            _titleLine = Get(SaveTextKeys.Title);
            _hintLine = Get(SaveTextKeys.Hint);

            var status = Status;
            if (status == SlotStatus.Unknown)
            {
                // 还没动过手：如实报「槽位上有没有东西」，而不是报一个假的「已保存」。
                status = _saves != null && _saves.Exists(DiagnosticSlot)
                    ? SlotStatus.Present
                    : SlotStatus.Empty;
            }

            if (status == SlotStatus.Unavailable)
            {
                _statusLine = Get(SaveTextKeys.Unavailable);
                return;
            }

            var key = SaveTextKeys.StatusKey(status);
            var text = Get(key);
            _statusLine = text == null
                ? null
                : string.Format(CultureInfo.InvariantCulture, text, DiagnosticSlot.ToString(CultureInfo.InvariantCulture));
        }

        private void OnGUI()
        {
            if (!Enabled || !IsVisible)
            {
                return;
            }

            HandleKeys();

            // 文本服务没就绪时一个字都不画：界面层不允许自带中文兜底文案。
            if (_localization == null)
            {
                return;
            }

            EnsureStyles();

            var height = PanelHeight();
            var panel = new Rect(Margin, Screen.height - height - Margin, PanelWidth, height);
            DrawRect(panel, PanelColor);
            DrawRect(new Rect(panel.x, panel.y, panel.width, AccentHeight), AccentColor);

            var x = panel.x + Padding;
            var width = panel.width - Padding * 2f;
            var y = panel.y + AccentHeight + Padding;

            GUI.Label(new Rect(x, y, width, FontSize + 10f), _titleLine, _titleStyle);
            y += FontSize + 12f;

            _lineStyle.normal.textColor = Status == SlotStatus.Failed ? WarnColor : LineColor;
            GUI.Label(new Rect(x, y, width, LineHeight), _statusLine, _lineStyle);
            _lineStyle.normal.textColor = LineColor;

            y += LineHeight + Gap;
            GUI.Label(new Rect(x, y, width, LineHeight), _hintLine, _hintStyle);
        }

        /// <summary>
        /// 按键处理。
        /// </summary>
        /// <remarks>
        /// 与另三层同一套按键识别方式：用 IMGUI 事件而不是 <c>UnityEngine.Input</c>。
        /// F3 共用（都是「收起面板」），F5／F9 是这一层独有的。
        /// </remarks>
        private void HandleKeys()
        {
            var current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (current.keyCode == KeyCode.F3)
            {
                IsVisible = false;
                current.Use();
                return;
            }

            if (!IsDiagnosticSaveSupported)
            {
                return;
            }

            if (current.keyCode == KeyCode.F5)
            {
                SaveToDiagnosticSlot();
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.F9)
            {
                LoadFromDiagnosticSlot();
                current.Use();
            }
        }

        private void EnsureServices()
        {
            if (!GameServices.IsReady)
            {
                // 服务被撤了（退出、或流程重跑）：攥着上一轮的引用只会画出已经不存在的槽位。
                DropServices();
                return;
            }

            var registry = GameServices.Registry;
            if (ReferenceEquals(registry, _registryRef) && _localization != null)
            {
                return;
            }

            // 注册表换了一张就说明服务重装过（组合根每次安装都会新建一张），
            // 此时旧的解析结果全部作废，必须重新解析。
            DropServices();

            if (!registry.TryResolve(out _coordinator) || _coordinator == null)
            {
                // 存档搬运要等组合根装完才在注册表里；此时只画得出一行「服务未就绪」。
                Rebuild();
                return;
            }

            if (!registry.TryResolve(out _saves) || _saves == null)
            {
                Rebuild();
                return;
            }

            if (!registry.TryResolve(out _localization) || _localization == null)
            {
                return;
            }

            _registryRef = registry;
            Rebuild();
            GameLog.Info(LogChannel.UI, "Save screen view bound to the service registry.");
        }

        private void DropServices()
        {
            _registryRef = null;
            _coordinator = null;
            _saves = null;
            _localization = null;
        }

        /// <summary>取词。服务缺失或键为 null 时返回 null，由调用方决定「不画这一行」。</summary>
        private string Get(string key)
        {
            if (_localization == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            return _localization.Get(key);
        }

        private static int PanelHeight() =>
            AccentHeight + Padding + FontSize + 12 + LineHeight + Gap + LineHeight + Padding;

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize + 2,
                fontStyle = FontStyle.Bold,
                richText = false,
            };
            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                richText = false,
            };
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize - 2,
                richText = false,
            };

            if (_font != null)
            {
                _titleStyle.font = _font;
                _lineStyle.font = _font;
                _hintStyle.font = _font;
            }

            _titleStyle.normal.textColor = LineColor;
            _lineStyle.normal.textColor = LineColor;
            _hintStyle.normal.textColor = HintColor;
        }

        private void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>
        /// 找一个有中文字形的系统字体，找不到就退回内置字体。
        /// </summary>
        /// <remarks>
        /// <b>只写 ASCII 字体名</b>：本文件在 UI 层，任何中文字面量都会被硬编码中文扫描拦下。
        /// 与另三层同一份候选表、同一条退路。
        /// </remarks>
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
            catch (Exception exception)
            {
                GameLog.Warn(LogChannel.UI, $"OS font lookup failed, falling back to builtin font: {exception.Message}");
                return null;
            }

            GameLog.Warn(LogChannel.UI, "No CJK OS font found, falling back to builtin font.");
            return null;
        }

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
    }
}
