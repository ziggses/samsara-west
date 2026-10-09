using System;
using System.Globalization;
using System.Text;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Localization;
using SamsaraWest.Narrative;
using SamsaraWest.Rendering;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 探索界面的诊断层视图：把 <see cref="IExplorationService"/> 当前这一手画成一张格子图，
    /// 把方向键原样交给 <see cref="ExplorationSession.TryMove"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么又是 IMGUI 诊断层</b>：与 <see cref="SkeletonBootScreen"/>、
    /// <see cref="BattleScreenView"/> 同一条理由——ADR-014 的正式界面（uGUI + TMP）要一套带中文字形的
    /// 字体资产，工程里还没有；正式场景、瓦片图与角色图也还没有。格子图用字符画出来，
    /// 代价是丑，收益是<b>今天就能真的走</b>。</para>
    /// <para><b>自挂的理由与战斗面板相同</b>：工程里没有任何流程会把探索带起来，靠场景接线的话
    /// 漏挂时会静默缺席。诊断层宁可自己出现。</para>
    /// <para><b>它不含探索规则</b>：能走到哪、能不能交互、遭遇掷没掷中，全是
    /// <see cref="ExplorationSession"/> 说了算。它<b>不认识战斗</b>——遭遇怎么变成一场仗是 Flow
    /// 侧接线的事，本层只把 <c>PendingEncounterId</c> 如实画出来。</para>
    /// <para><b>输入只用 IMGUI 事件</b>：与另两层同一取舍，用 <c>UnityEngine.Input</c> 会在
    /// 「仅 Input System」的后端下直接抛异常。<c>IInputService</c>（<c>Gameplay/Move</c>）
    /// 仍是未绑定状态——正式输入接线与本层无关，见 <c>Docs/待拍板清单.md</c>。</para>
    /// <para><b>不每帧拼文案</b>：快照只在动作后与兜底间隔到点时重建。格子本身每帧按会话现算
    /// （它没有文案，只有坐标与字符）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ExplorationScreenView : MonoBehaviour
    {
        /// <summary>与自检／战斗面板同一档字号。</summary>
        public const int FontSize = 18;

        /// <summary>格子里那一个字符的字号。比正文小一号，避免 20 列铺不下。</summary>
        public const int CellFontSize = 14;

        /// <summary>单格边长（像素）。</summary>
        public const int CellSize = 18;

        /// <summary>诊断入口默认开的那张图：首章第一张野外图（有遭遇率，走几步就能看到遇敌）。</summary>
        public const string DiagnosticMapId = "CH01_MAP01";

        /// <summary>
        /// 诊断入口的落点：迎客猴左边一格、面朝东。
        /// </summary>
        /// <remarks>
        /// 挑这个落点不是随手写的：<c>CH01_MAP01</c> 在 (20,8) 有一个迎客猴交互物，
        /// 从 (19,8) 朝东进图，面板一出现就同时有「脚下是自己、旁边是交互物」两件事可核对，
        /// 按一下 E 又能把交互通路走通。
        /// </remarks>
        public static readonly GridPosition DiagnosticStart = new GridPosition(19, 8);

        /// <summary>诊断入口的朝向。与 <see cref="DiagnosticStart"/> 配套。</summary>
        public const MoveDirection DiagnosticFacing = MoveDirection.East;

        /// <summary>兜底刷新间隔。动作后必定立即重算，它保证「别人改的状态总归会跟上」。</summary>
        public const float RefreshIntervalSeconds = 0.5f;

        private const int Margin = 16;
        private const int PanelWidth = 620;
        private const int AccentHeight = 3;
        private const int Padding = 12;
        private const int Gap = 6;

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.90f);
        private static readonly Color AccentColor = new Color(0.36f, 0.62f, 0.90f, 1f);
        private static readonly Color GridBackColor = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color PlayerColor = new Color(0.32f, 0.86f, 0.66f);
        private static readonly Color FacingColor = new Color(0.42f, 0.68f, 0.94f);
        private static readonly Color InteractableColor = new Color(0.95f, 0.72f, 0.38f);
        private static readonly Color FloorColor = new Color(0.45f, 0.48f, 0.54f);
        private static readonly Color LineColor = new Color(0.90f, 0.91f, 0.94f);
        private static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color WarnColor = new Color(0.95f, 0.66f, 0.30f);

        private const string PlayerChar = "@";
        private const string FacingChar = "*";
        private const string InteractableChar = "#";
        private const string FloorChar = ".";

        private static ExplorationScreenView _instance;

        private IExplorationService _exploration;
        private IDefinitionRegistry _definitions;
        private ILocalizationService _localization;

        /// <summary>
        /// 剧情节点推进器。
        /// </summary>
        /// <remarks>
        /// 与<b>账本</b>不同，它可能是<b>缺席</b>的：组合根不给定义目录时不装这个服务，
        /// 那时面板照旧要能探索，只是没有对白块。所以按软依赖解析，缺席不报警。
        /// </remarks>
        private IDialogueService _dialogue;

        /// <summary>已解析过的那张注册表。换了一张就说明服务重装过，缓存必须整个丢掉。</summary>
        private IServiceRegistry _registryRef;

        private string _titleLine;
        private string _statusLine;
        private string _facingLine;
        private string _legendLine;
        private string _noteLine;
        private string _hintLine;

        /// <summary>对白块的四行。没有对白开着时全部为 null，面板就一像素也不多占。</summary>
        private string _dialogueTitleLine;
        private string _dialogueStatusLine;
        private string _dialogueSpeakerLine;
        private string _dialogueTextLine;

        private MoveResult? _lastMove;
        private InteractionResult? _lastInteraction;

        private float _nextRefreshAt;

        private Font _font;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _cellStyle;

        public static ExplorationScreenView Instance => _instance;

        /// <summary>关闭后连创建都不再创建。给「正式探索界面接管」留的开关。</summary>
        public static bool Enabled { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 诊断用的「F4 进图／离图」是否允许。与错误面板、战斗面板同一条口径：
        /// 只在编辑器与开发版里给，正式包里不该有一个按键能凭空传送。
        /// </summary>
        public static bool IsDiagnosticStartSupported =>
            Application.isEditor || Debug.isDebugBuild;

        /// <summary>当前会话；不在图上时为 null。</summary>
        public ExplorationSession Session => _exploration == null ? null : _exploration.Current;

        public bool IsExploring => _exploration != null && _exploration.IsExploring;

        public string MapId => Session == null ? null : Session.MapId;

        public GridPosition? Position => Session == null ? (GridPosition?)null : Session.Position;

        public MoveDirection? Facing => Session == null ? (MoveDirection?)null : Session.Facing;

        public int StepCount => Session == null ? 0 : Session.StepCount;

        public string PendingEncounterId => Session == null ? null : Session.PendingEncounterId;

        /// <summary>最近一次移动的结算结果；本层还没走过一步时为 null。</summary>
        public MoveResult? LastMove => _lastMove;

        /// <summary>最近一次交互的结算结果；本层还没交互过时为 null。</summary>
        public InteractionResult? LastInteraction => _lastInteraction;

        /// <summary>最近一次画出去的那几行，供用例核对「确实画了文本表里的字」。</summary>
        public string TitleLine => _titleLine;

        public string StatusLine => _statusLine;

        public string FacingLine => _facingLine;

        public string LegendLine => _legendLine;

        public string NoteLine => _noteLine;

        public string HintLine => _hintLine;

        /// <summary>对白块的标题行；没有对白开着时为 null。</summary>
        public string DialogueTitleLine => _dialogueTitleLine;

        /// <summary>对白块当前展示的正文；没有对白开着时为 null。</summary>
        public string DialogueLine => _dialogueTextLine;

        /// <summary>对白块当前展示的说话人；没有对白开着时为 null。</summary>
        public string DialogueSpeaker => _dialogueSpeakerLine;

        /// <summary>对白块的状态行（节点名 · 第 n/N 行）；没有对白开着时为 null。</summary>
        public string DialogueStatusLine => _dialogueStatusLine;

        /// <summary>
        /// 当前有没有对白开着。
        /// </summary>
        /// <remarks>开着的时候本面板是<b>模态</b>的：方向键不再挪人，交互键变成「继续」。</remarks>
        public bool IsDialogueActive => _dialogue != null && _dialogue.IsActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            EnsureCreated();
        }

        public static ExplorationScreenView EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (!Enabled)
            {
                return null;
            }

            var host = new GameObject("ExplorationScreenView");
            DontDestroyOnLoad(host);
            return host.AddComponent<ExplorationScreenView>();
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

        /// <summary>
        /// 一步：解析服务 → 必要时重建快照。每帧调它是安全的。
        /// </summary>
        /// <remarks>
        /// 保留兜底间隔而不是只按状态键跳过，是因为<b>遭遇是被别人了结的</b>：
        /// 战斗结束时 Flow 侧的接线会调 <see cref="ExplorationSession.ResolveEncounter"/>，
        /// 那一刻本层看不见，只能靠下个间隔把「可以继续走了」画出来。
        /// </remarks>
        public void Tick()
        {
            EnsureServices();

            if (_localization == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextRefreshAt)
            {
                Rebuild();
            }
        }

        /// <summary>立即重算画面文案。用例直接调它，不依赖等待。</summary>
        public void Rebuild()
        {
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;

            _titleLine = Get(ExplorationTextKeys.Title);
            _hintLine = Get(ExplorationTextKeys.Hint);
            _legendLine = Get(ExplorationTextKeys.GridLegend);
            RebuildDialogue();

            var session = Session;
            if (session == null)
            {
                _statusLine = Get(ExplorationTextKeys.NoMap);
                _facingLine = null;
                _noteLine = null;
                return;
            }

            RebuildStatus(session);
            RebuildFacing(session);
            RebuildNote(session);
        }

        /// <summary>
        /// 重建对白块的四行。
        /// </summary>
        /// <remarks>
        /// 分工是刻意的：<b>「该念哪一行」由推进器给</b>（<c>dlg.ch01.002.line.3</c> 这种键），
        /// <b>「那一行长什么样」才在这里解析</b>。Narrative 因此不必认识本地化服务，
        /// 本层也不必认识剧本节点——两侧各跨半步，靠一条文本键对接。
        /// 缺键时把「缺的是哪一个」写进面板：本层的规矩是不给兜底正文，
        /// 但一次数据错位在玩家眼里会表现成「对白面板上一片空白」，那比丑更糟。
        /// </remarks>
        private void RebuildDialogue()
        {
            if (!IsDialogueActive)
            {
                _dialogueTitleLine = null;
                _dialogueStatusLine = null;
                _dialogueSpeakerLine = null;
                _dialogueTextLine = null;
                return;
            }

            _dialogueTitleLine = Get(ExplorationTextKeys.DialogueTitle);
            _dialogueStatusLine = _localization.Format(
                ExplorationTextKeys.DialogueStatus,
                Get(_dialogue.NodeDisplayNameKey),
                _dialogue.LineNumber,
                _dialogue.LineCount);

            // 旁白行在文本表里压根没有 .who 这一条，所以得先问「表里有没有这个键」，再取词。
            // 直接取词会拿到缺失键的占位符 [[dlg...line.n.who]]——它非空，于是「没有登记说话人就画旁白」
            // 那一路永远走不到，面板上会顶着一串键名，运行时缺失键清单也被旁白行灌满。
            var speakerKey = _dialogue.SpeakerKey;
            var speaker = _localization != null && _localization.HasKey(speakerKey) ? Get(speakerKey) : null;
            _dialogueSpeakerLine = string.IsNullOrEmpty(speaker)
                ? Get(ExplorationTextKeys.DialogueNarrator)
                : speaker;

            var text = Get(_dialogue.LineKey);
            _dialogueTextLine = string.IsNullOrEmpty(text)
                ? _localization.Format(ExplorationTextKeys.DialogueMissing, _dialogue.DialogueId, _dialogue.LineKey)
                : text;
        }

        /// <summary>
        /// 诊断入口：进首章第一张野外图（或指定的图）。已经有会话时拒绝——不抢别人的图。
        /// </summary>
        /// <remarks>
        /// 它存在的理由与「自挂」相同：工程里还没有任何流程会开一张图，
        /// 没有一个入口的话这一层永远无法被人看到，也就无法被人核对。
        /// </remarks>
        public bool EnterDiagnosticMap(
            string mapId = null,
            GridPosition? start = null,
            MoveDirection facing = DiagnosticFacing)
        {
            EnsureServices();

            if (_exploration == null)
            {
                GameLog.Warn(
                    LogChannel.UI,
                    "Diagnostic map requested before the service registry was ready; ignored.");
                return false;
            }

            if (_exploration.IsExploring)
            {
                return false;
            }

            var id = string.IsNullOrEmpty(mapId) ? DiagnosticMapId : mapId;
            var entered = _exploration.EnterMap(id, start ?? DiagnosticStart, facing);
            if (entered == null)
            {
                GameLog.Warn(LogChannel.UI, $"Diagnostic map asked for unknown map '{id}'.", id);
                Rebuild();
                return false;
            }

            _lastMove = null;
            _lastInteraction = null;
            Rebuild();
            return true;
        }

        /// <summary>诊断入口的开关：不在图上就进图，在图上就离图。</summary>
        public bool ToggleDiagnosticMap()
        {
            EnsureServices();

            if (_exploration == null)
            {
                GameLog.Warn(
                    LogChannel.UI,
                    "Diagnostic map requested before the service registry was ready; ignored.");
                return false;
            }

            if (_exploration.IsExploring)
            {
                LeaveMap();
                return true;
            }

            return EnterDiagnosticMap();
        }

        /// <summary>离开当前地图。把这一次的结算结果一起忘掉，免得离图后还挂着上一张图的拒绝理由。</summary>
        public bool LeaveMap()
        {
            EnsureServices();

            if (_exploration == null || !_exploration.IsExploring)
            {
                return false;
            }

            _exploration.LeaveMap();
            _lastMove = null;
            _lastInteraction = null;
            Rebuild();
            return true;
        }

        /// <summary>
        /// 朝某方向走一格。
        /// </summary>
        /// <remarks>
        /// 返回值是「走成了没有」——被拒时<b>照样</b>记下这次结算并重画，
        /// 因为那一行「为什么没走成」正是本层要给出的反馈。
        /// </remarks>
        public bool Step(MoveDirection direction)
        {
            EnsureServices();

            var session = Session;
            if (session == null)
            {
                GameLog.Warn(LogChannel.UI, "Exploration step requested before entering a map; ignored.");
                return false;
            }

            var result = session.TryMove(direction);
            _lastMove = result;
            _lastInteraction = null;
            Rebuild();
            return result.Moved;
        }

        /// <summary>对面朝的那一格交互一次。返回值是「触发了没有」。</summary>
        public bool Interact()
        {
            EnsureServices();

            var session = Session;
            if (session == null)
            {
                GameLog.Warn(LogChannel.UI, "Exploration interaction requested before entering a map; ignored.");
                return false;
            }

            var result = session.TryInteract();
            _lastMove = null;
            _lastInteraction = result;
            Rebuild();
            return result.Triggered;
        }

        /// <summary>推进对白一行。没有对白开着时返回 false，什么也不做。</summary>
        public bool AdvanceDialogue()
        {
            EnsureServices();

            if (_dialogue == null || !_dialogue.IsActive)
            {
                return false;
            }

            var result = _dialogue.Advance();
            Rebuild();
            return result.Advanced;
        }

        private void RebuildStatus(ExplorationSession session)
        {
            var status = new StringBuilder();
            status.Append(_localization.Format(
                ExplorationTextKeys.Status,
                session.MapId,
                session.Position.ToString(),
                Get(ExplorationTextKeys.Facing(session.Facing)),
                session.StepCount.ToString(CultureInfo.InvariantCulture)));

            if (session.IsEncounterPending)
            {
                status.Append("  ").Append(_localization.Format(
                    ExplorationTextKeys.EncounterPending,
                    session.PendingEncounterId));
            }

            _statusLine = status.ToString();
        }

        /// <summary>
        /// 「前方是什么」这一行。
        /// </summary>
        /// <remarks>
        /// 面朝格在图外时<b>不画这一行</b>：图边在格子图上一眼可见，再写一句反而像在报错。
        /// 格内空着则明说「前方空着」——玩家按 E 之前该知道会不会有反应。
        /// </remarks>
        private void RebuildFacing(ExplorationSession session)
        {
            var cell = session.FacingPosition;
            var grid = session.Grid;
            var target = grid.InteractableAt(cell);

            if (target == null)
            {
                _facingLine = grid.IsInside(cell) ? Get(ExplorationTextKeys.FacingEmpty) : null;
                return;
            }

            var name = NameOf(target);
            var prompt = Get(target.PromptKey);
            _facingLine = string.IsNullOrEmpty(prompt)
                ? _localization.Format(ExplorationTextKeys.FacingTargetPlain, name)
                : _localization.Format(ExplorationTextKeys.FacingTarget, name, prompt);
        }

        private void RebuildNote(ExplorationSession session)
        {
            _noteLine = null;

            if (_lastMove.HasValue && !_lastMove.Value.Moved)
            {
                _noteLine = FormatRejection(
                    ExplorationTextKeys.MoveRejectionKey(_lastMove.Value.Rejection),
                    session);
                return;
            }

            if (!_lastInteraction.HasValue)
            {
                return;
            }

            var interaction = _lastInteraction.Value;
            if (!interaction.Triggered)
            {
                _noteLine = FormatRejection(
                    ExplorationTextKeys.InteractionRejectionKey(interaction.Rejection),
                    session);
                return;
            }

            var builder = new StringBuilder();
            builder.Append(_localization.Format(
                ExplorationTextKeys.InteractTriggered,
                NameOfId(interaction.InteractableId)));

            if (interaction.RequestsMapChange)
            {
                builder.Append("  ").Append(_localization.Format(
                    ExplorationTextKeys.InteractMapChange,
                    interaction.TargetMapId));
            }

            _noteLine = builder.ToString();
        }

        /// <summary>把一条拒绝理由转成可画的一行。「遭遇还挂着」这句要带上遭遇 ID，所以要单独补参数。</summary>
        private string FormatRejection(string key, ExplorationSession session)
        {
            if (key == null)
            {
                return null;
            }

            return key == ExplorationTextKeys.EncounterPending
                ? _localization.Format(key, session.PendingEncounterId)
                : Get(key);
        }

        /// <summary>交互物的显示名。键缺失时退回 ID：画一个丑但真的东西，好过画一个空。</summary>
        private string NameOf(InteractableDefinition interactable) =>
            interactable == null ? null : NameOfId(interactable.Id);

        private string NameOfId(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId))
            {
                return null;
            }

            if (_definitions != null &&
                _definitions.TryGet(interactableId, out InteractableDefinition definition) &&
                definition != null)
            {
                var name = Get(definition.DisplayNameKey);
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return interactableId;
        }

        private void EnsureServices()
        {
            if (!GameServices.IsReady)
            {
                // 服务被撤了（退出、或流程重跑）：攥着上一轮的引用只会画出已经不存在的会话。
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

            if (!registry.TryResolve(out _exploration) || _exploration == null)
            {
                return;
            }

            if (!registry.TryResolve(out _definitions) || _definitions == null)
            {
                return;
            }

            if (!registry.TryResolve(out _localization) || _localization == null)
            {
                return;
            }

            // 推进器是软依赖：组合根只装账本（没给定义目录）时不装它，
            // 那时界面照旧要探索，只是按 E 只会发一条交互事件，读不出对白。
            registry.TryResolve(out _dialogue);

            _registryRef = registry;
            Rebuild();
            GameLog.Info(LogChannel.UI, "Exploration screen view bound to the service registry.");
        }

        private void DropServices()
        {
            _registryRef = null;
            _exploration = null;
            _definitions = null;
            _localization = null;
            _dialogue = null;
            _dialogueTitleLine = null;
            _dialogueStatusLine = null;
            _dialogueSpeakerLine = null;
            _dialogueTextLine = null;
            _lastMove = null;
            _lastInteraction = null;
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

        /// <summary>
        /// 找一个有中文字形的系统字体，找不到就退回内置字体。
        /// </summary>
        /// <remarks>
        /// <b>只写 ASCII 字体名</b>：本文件在 UI 层，任何中文字面量都会被硬编码中文扫描拦下。
        /// 与另两层同一份候选表、同一条退路。
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

            var session = Session;

            // 精灵层已经把地图画成像素画时，这块 ASCII 格子图就该让位：既不再画网格，
            // 也不再把面板撑成网格那么大——60×40 格的网格面板会把整个屏幕盖住，正好挡掉美术。
            var spriteLayerUp = ExplorationSpriteView.IsActive;
            var gridWidth = session == null || spriteLayerUp ? 0 : session.Grid.Width;
            var gridHeight = session == null || spriteLayerUp ? 0 : session.Grid.Height;
            var panel = new Rect(
                Margin,
                Margin,
                PanelWidthFor(gridWidth),
                PanelHeight(gridHeight, IsDialogueActive));
            DrawRect(panel, PanelColor);
            DrawRect(new Rect(panel.x, panel.y, panel.width, AccentHeight), AccentColor);

            var x = panel.x + Padding;
            var width = panel.width - Padding * 2f;
            var y = panel.y + AccentHeight + Padding;

            GUI.Label(new Rect(x, y, width, FontSize + 10f), _titleLine, _titleStyle);
            y += FontSize + 12f;

            GUI.Label(new Rect(x, y, width, LineHeight), _statusLine, _lineStyle);

            if (session == null)
            {
                DrawHintLine(panel, x, width);
                return;
            }

            y += LineHeight;
            GUI.Label(new Rect(x, y, width, LineHeight), _facingLine, _lineStyle);

            y += LineHeight;
            _lineStyle.normal.textColor = HintColor;
            GUI.Label(new Rect(x, y, width, LineHeight), _legendLine, _lineStyle);
            _lineStyle.normal.textColor = LineColor;

            y += LineHeight + Gap;

            if (!spriteLayerUp)
            {
                var gridHeightPixels = session.Grid.Height * (float)CellSize;
                var gridArea = new Rect(x, y, session.Grid.Width * (float)CellSize, gridHeightPixels);
                DrawGrid(gridArea, session);

                y += gridHeightPixels + Gap;
            }
            if (!string.IsNullOrEmpty(_noteLine))
            {
                _lineStyle.normal.textColor = WarnColor;
                GUI.Label(new Rect(x, y, width, LineHeight), _noteLine, _lineStyle);
                _lineStyle.normal.textColor = LineColor;
            }

            y += LineHeight;

            if (IsDialogueActive)
            {
                DrawDialogueBlock(x, y, width);
                DrawHintLine(panel, x, width, Get(ExplorationTextKeys.DialogueHint));
                return;
            }

            DrawHintLine(panel, x, width);
        }

        /// <summary>画对白块：标题、进度、说话人、正文。位置由调用方给。</summary>
        private void DrawDialogueBlock(float x, float y, float width)
        {
            _lineStyle.normal.textColor = InteractableColor;
            GUI.Label(new Rect(x, y, width, LineHeight), _dialogueTitleLine, _lineStyle);
            _lineStyle.normal.textColor = LineColor;
            y += LineHeight;

            _lineStyle.normal.textColor = HintColor;
            GUI.Label(new Rect(x, y, width, LineHeight), _dialogueStatusLine, _lineStyle);
            GUI.Label(new Rect(x, y + LineHeight, width, LineHeight), _dialogueSpeakerLine, _lineStyle);
            _lineStyle.normal.textColor = LineColor;

            GUI.Label(new Rect(x, y + (LineHeight * 2f), width, LineHeight), _dialogueTextLine, _lineStyle);
        }

        /// <summary>
        /// 按键处理。
        /// </summary>
        /// <remarks>
        /// 与另两层同一套按键识别方式：用 IMGUI 事件而不是 <c>UnityEngine.Input</c>，
        /// 后者在「仅 Input System」的后端下会直接抛异常。
        /// F3 与战斗面板共用（都是「收起面板」），两个面板会一起收起来，这是合意的。
        /// </remarks>
        private void HandleKeys()
        {
            var current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (HandleKey(current.keyCode))
            {
                current.Use();
            }
        }

        /// <summary>
        /// 处理一个按键，返回「吃掉了没有」。
        /// </summary>
        /// <remarks>
        /// 判定之所以从 <see cref="HandleKeys"/> 里拆出来，是因为 IMGUI 事件在用例里不好造：
        /// 拆开之后「对白开着时方向键不许穿过去」这种事直接喂一个 <c>KeyCode</c> 就能断言，
        /// 不必去伪造 <c>Event.current</c>。行为与拆之前逐条一致。
        /// </remarks>
        public bool HandleKey(KeyCode keyCode)
        {
            if (keyCode == KeyCode.F3)
            {
                IsVisible = false;
                return true;
            }

            if (keyCode == KeyCode.F4 && IsDiagnosticStartSupported)
            {
                ToggleDiagnosticMap();
                return true;
            }

            if (IsDialogueActive)
            {
                // 对白开着时本面板模态：方向键不许穿过去挪人，交互键变成「继续」。
                // 这是「对白期间走不动」的唯一实现处——探索内核不认对白，不会替我们挡住。
                if (IsInteractKey(keyCode))
                {
                    AdvanceDialogue();
                }

                return true;
            }

            var direction = DirectionOf(keyCode);
            if (direction.HasValue)
            {
                Step(direction.Value);
                return true;
            }

            if (IsInteractKey(keyCode))
            {
                Interact();
                return true;
            }

            return false;
        }

        /// <summary>方向键与 WASD 等价。IMGUI 里字母键一律报大写 <c>KeyCode</c>。</summary>
        public static MoveDirection? DirectionOf(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.W:
                    return MoveDirection.North;
                case KeyCode.DownArrow:
                case KeyCode.S:
                    return MoveDirection.South;
                case KeyCode.LeftArrow:
                case KeyCode.A:
                    return MoveDirection.West;
                case KeyCode.RightArrow:
                case KeyCode.D:
                    return MoveDirection.East;
                default:
                    return null;
            }
        }

        public static bool IsInteractKey(KeyCode keyCode) =>
            keyCode == KeyCode.E ||
            keyCode == KeyCode.Space ||
            keyCode == KeyCode.Return ||
            keyCode == KeyCode.KeypadEnter;

        private static float LineHeight => FontSize + 8f;

        /// <summary>
        /// 面板宽度跟着地图宽度走。
        /// </summary>
        /// <remarks>
        /// 定宽会在宽过上界的图上把格子切掉，而「切掉一半的图」比「丑一点的宽面板」更糟：
        /// 玩家会以为右边那片走不过去。<see cref="PanelWidth"/> 是下限，屏幕宽是上限。
        /// </remarks>
        private static float PanelWidthFor(int gridWidth)
        {
            var needed = (Padding * 2f) + (gridWidth * (float)CellSize);
            return Mathf.Max(PanelWidth, Mathf.Min(needed, Screen.width - (Margin * 2f)));
        }

        /// <summary>面板高度按内容算：图高不同面板就不同，不留一大片空。</summary>
        /// <param name="dialogueOpen">
        /// 对白块开着没有。它画在备注行下方，所以面板得跟着长高——
        /// 不预留的话对白正文会被底部那行提示压住，而「压住一半的字」比丑更难读。
        /// 没进图时必然没有对白（没有会话就没有交互），那一支不看这个参数。
        /// </param>
        private static float PanelHeight(int gridHeight, bool dialogueOpen)
        {
            var titleHeight = FontSize + 12f;
            var hintHeight = FontSize + 6f;
            var dialogueHeight = dialogueOpen ? (LineHeight * 5f) + (Gap * 2f) : 0f;

            if (gridHeight <= 0)
            {
                // 没进图时只画三行：标题、一行说明、底部提示。
                return AccentHeight + (Padding * 2f) + titleHeight + LineHeight + Gap + hintHeight;
            }

            // 正文四行（状态／前方／图例／备注）+ 三段间隔 + 格子 + 对白块 + 底部提示。
            return AccentHeight + (Padding * 2f) + titleHeight + (LineHeight * 4f) + (Gap * 3f) +
                (gridHeight * (float)CellSize) + dialogueHeight + hintHeight;
        }

        /// <summary>
        /// 把网格画出来。
        /// </summary>
        /// <remarks>
        /// 行序必须反过来画：内核的 Y 向上，屏幕的 Y 向下。若照着内存顺序从上往下画，
        /// 玩家会觉得自己在倒着走北。
        /// </remarks>
        private void DrawGrid(Rect area, ExplorationSession session)
        {
            var grid = session.Grid;
            DrawRect(area, GridBackColor);

            for (var row = 0; row < grid.Height; row++)
            {
                var y = grid.Height - 1 - row;
                for (var column = 0; column < grid.Width; column++)
                {
                    var rect = new Rect(
                        area.x + (column * CellSize),
                        area.y + (row * CellSize),
                        CellSize,
                        CellSize);
                    DrawCell(rect, new GridPosition(column, y), session);
                }
            }
        }

        /// <summary>
        /// 画一格。
        /// </summary>
        /// <remarks>
        /// 优先级是「自己 &gt; 交互物 &gt; 面朝 &gt; 空地」：面朝的那一格如果站着交互物，
        /// 就画交互物——「前面有东西」比「我在看那边」更有用，朝向另有状态行在说。
        /// </remarks>
        private void DrawCell(Rect rect, GridPosition cell, ExplorationSession session)
        {
            if (cell == session.Position)
            {
                _cellStyle.normal.textColor = PlayerColor;
                GUI.Label(rect, PlayerChar, _cellStyle);
                return;
            }

            if (session.Grid.InteractableAt(cell) != null)
            {
                _cellStyle.normal.textColor = InteractableColor;
                GUI.Label(rect, InteractableChar, _cellStyle);
                return;
            }

            if (cell == session.FacingPosition)
            {
                _cellStyle.normal.textColor = FacingColor;
                GUI.Label(rect, FacingChar, _cellStyle);
                return;
            }

            _cellStyle.normal.textColor = FloorColor;
            GUI.Label(rect, FloorChar, _cellStyle);
        }

        /// <param name="hint">要画的提示文字。不传就用探索那条；对白开着时传对白那条。</param>
        private void DrawHintLine(Rect panel, float x, float width, string hint = null)
        {
            var text = hint ?? _hintLine;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GUI.Label(
                new Rect(x, panel.yMax - Padding - FontSize - 6f, width, FontSize + 6f),
                text,
                _hintStyle);
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
            _lineStyle.normal.textColor = LineColor;

            if (_font != null)
            {
                _lineStyle.font = _font;
            }

            _titleStyle = new GUIStyle(_lineStyle)
            {
                fontSize = FontSize + 6,
                fontStyle = FontStyle.Bold,
            };

            _hintStyle = new GUIStyle(_lineStyle);
            _hintStyle.normal.textColor = HintColor;

            _cellStyle = new GUIStyle(_lineStyle)
            {
                fontSize = CellFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
            _cellStyle.normal.textColor = FloorColor;
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
