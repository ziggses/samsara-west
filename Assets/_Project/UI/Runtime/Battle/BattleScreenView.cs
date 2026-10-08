using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Localization;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 战斗界面的诊断层视图：把 <see cref="BattleHudModel"/> 画出来，把点击原样交回
    /// <see cref="BattleScreenController"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是 IMGUI 诊断层</b>：与 <see cref="SkeletonBootScreen"/> 同一条理由——
    /// ADR-014 规定的正式界面（uGUI + TMP）需要一套带中文字形的字体资产，而工程里还没有。
    /// 若本层强依赖它，就会在「字体资产还没做」的现在画不出任何字，正好失去存在的意义。</para>
    /// <para><b>为什么自挂</b>：本层要回答的问题是「界面拿到的这一手，点下去到底会怎样」。
    /// 工程里目前没有任何流程会把它带起来（除 Bootstrap 外 0 个场景、0 个 prefab），
    /// 若还要靠场景接线，漏挂时它会静默缺席，等于没做。诊断层宁可自己出现。</para>
    /// <para><b>它不含战斗规则</b>：能点什么是 <see cref="BattleHudModel.Commands"/> 给的
    /// （而那是内核给的），点了算不算由内核说了算；这里只做「画」与「转发」，
    /// 连置灰理由也只是把 <see cref="BattleCommandOption.Rejection"/> 转述成文本键。</para>
    /// <para><b>不每帧拼文案</b>：快照只在相位、行动数、回合数或选择态变化时重建，
    /// <see cref="OnGUI"/> 只负责把已拼好的字符串画出去（同 <see cref="SkeletonBootScreen"/> 的取舍）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BattleScreenView : MonoBehaviour
    {
        /// <summary>与自检画面同一档字号。</summary>
        public const int FontSize = 18;

        /// <summary>诊断入口默认开的那一场遭遇：首章第一场。</summary>
        public const string DiagnosticEncounterId = "ENC_CH01_001";

        /// <summary>兜底刷新间隔。状态没变时不重算，但它保证「数字总归会跟上」。</summary>
        public const float RefreshIntervalSeconds = 0.5f;

        /// <summary>
        /// 诊断战斗发给队伍的每种道具各几个。
        /// </summary>
        /// <remarks>
        /// 这只是让诊断层「有道具可点」：正式流程的存货来自存档，不是这个常数。
        /// 给 3 个是为了能连着用几手，把「用光了按钮就消失」也看得见。
        /// </remarks>
        public const int DiagnosticItemStock = 3;

        /// <summary>「一场仗都没有」这个状态对应的快照键。</summary>
        private const int NoBattleSnapshotKey = -1;

        private const int Margin = 16;
        private const int PanelWidth = 760;
        private const int PanelHeight = 452;
        private const int AccentHeight = 3;
        private const int Padding = 12;
        private const int ButtonWidth = 132;
        private const int ButtonHeight = 28;
        private const int Gap = 6;

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.90f);
        private static readonly Color AccentColor = new Color(0.20f, 0.68f, 0.55f, 1f);
        private static readonly Color EndedAccentColor = new Color(0.60f, 0.60f, 0.62f, 1f);
        private static readonly Color PlayerColor = new Color(0.86f, 0.90f, 0.96f);
        private static readonly Color EnemyColor = new Color(0.94f, 0.78f, 0.72f);
        private static readonly Color CurrentActorColor = new Color(0.32f, 0.86f, 0.66f);
        private static readonly Color DownColor = new Color(0.50f, 0.52f, 0.56f);
        private static readonly Color LineColor = new Color(0.90f, 0.91f, 0.94f);
        private static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color WarnColor = new Color(0.95f, 0.66f, 0.30f);

        private static BattleScreenView _instance;

        /// <summary>界面上要填的一行（已拼好的字符串 + 决定配色的几个布尔）。</summary>
        public struct SnapshotRow
        {
            public string Text;
            public bool IsPlayer;
            public bool IsCurrentActor;
            public bool IsAlive;
        }

        private readonly List<SnapshotRow> _rows = new List<SnapshotRow>(FormationSlot.Capacity * 2);
        private readonly List<string> _commandLabels = new List<string>(8);
        private readonly List<bool> _commandEnabled = new List<bool>(8);
        private readonly List<string> _pickLabels = new List<string>(FormationSlot.Capacity);
        private readonly List<int> _pickKeys = new List<int>(FormationSlot.Capacity);

        private IBattleService _battle;
        private IDefinitionRegistry _definitions;
        private ILocalizationService _localization;

        /// <summary>已解析过的那张注册表。换了一张就说明服务重装过，缓存必须整个丢掉。</summary>
        private IServiceRegistry _registryRef;

        private BattleSession _boundSession;
        private BattleScreenController _controller;

        private string _titleLine;
        private string _statusLine;
        private string _promptLine;
        private string _noteLine;
        private string _hintLine;
        private int _snapshotKey = int.MinValue;
        private float _nextRefreshAt;

        private Font _font;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _hintStyle;

        /// <summary>正在选落点还是选同伴；<see cref="PickMode.None"/> 表示没有这类子选择。</summary>
        private enum PickMode
        {
            None = 0,
            Move = 1,
            Swap = 2,
        }

        private PickMode _pick;

        public static BattleScreenView Instance => _instance;

        /// <summary>关闭后连创建都不再创建。给「正式战斗界面接管」留的开关。</summary>
        public static bool Enabled { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 诊断用的「F2 开一场」是否允许。与错误面板同一条口径：只在编辑器与开发版里给，
        /// 正式包里不该有一个按键能凭空开战。公开出来是为了让用例不必猜这条规则。
        /// </summary>
        public static bool IsDiagnosticStartSupported =>
            Application.isEditor || Debug.isDebugBuild;

        /// <summary>当前驱动器；没有战斗时为 null。</summary>
        public BattleScreenController Controller => _controller;

        public bool HasBattle => _controller != null;

        /// <summary>最近一次画出去的那几行，供用例核对「确实画了文本表里的字」。</summary>
        public IReadOnlyList<SnapshotRow> Rows => _rows;

        public string TitleLine => _titleLine;

        public string StatusLine => _statusLine;

        public string HintLine => _hintLine;

        /// <summary>此刻画出来的指令按钮文案（与 <see cref="BattleHudModel.Commands"/> 一一对应）。</summary>
        public IReadOnlyList<string> CommandLabels => _commandLabels;

        /// <summary>此刻画出来的子选择按钮文案（选目标／落点／同伴）。</summary>
        public IReadOnlyList<string> PickLabels => _pickLabels;

        /// <summary>
        /// 子选择按钮对应的键，与 <see cref="PickLabels"/> 一一对应：
        /// 选目标／选同伴时是单位编号，选落点时是阵型序号。
        /// </summary>
        public IReadOnlyList<int> PickKeys => _pickKeys;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            EnsureCreated();
        }

        public static BattleScreenView EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (!Enabled)
            {
                return null;
            }

            var host = new GameObject("BattleScreenView");
            DontDestroyOnLoad(host);
            return host.AddComponent<BattleScreenView>();
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
        /// 一步：解析服务 → 认下当前战斗 → 推进驱动器 → 必要时重建快照。
        /// 每帧调它是安全的（驱动器重复调用幂等，快照按状态键跳过）。
        /// </summary>
        public void Tick()
        {
            EnsureServices();

            if (_battle == null)
            {
                return;
            }

            var current = _battle.Current;
            if (!ReferenceEquals(current, _boundSession))
            {
                _boundSession = current;
                _controller = current == null ? null : new BattleScreenController(current, _definitions);
                _pick = PickMode.None;
                _snapshotKey = int.MinValue;
            }

            if (_controller == null)
            {
                // 没有战斗也要把提示行拼出来：入口得先能被看见，才谈得上被使用。
                if (_snapshotKey != NoBattleSnapshotKey)
                {
                    _snapshotKey = NoBattleSnapshotKey;
                    Rebuild();
                }

                return;
            }

            // 幂等：已经在等我方输入时立即返回，因此这里不会替玩家做决定。
            _controller.Start();

            var key = ComputeSnapshotKey();
            if (key != _snapshotKey || Time.unscaledTime >= _nextRefreshAt)
            {
                _snapshotKey = key;
                Rebuild();
            }
        }

        /// <summary>立即重算画面文案。用例直接调它，不依赖等待。</summary>
        public void Rebuild()
        {
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            RebuildRows();
            RebuildCommands();
            RebuildPicks();
            RebuildHeaderAndPrompt();
        }

        /// <summary>
        /// 点第 <paramref name="index"/> 个指令按钮。
        /// </summary>
        /// <remarks>
        /// 置灰的按钮<b>不转发</b>内核：界面已经知道它必然被拒，转发过去只会把
        /// 「点了没用」也写进战斗日志里。
        /// </remarks>
        public bool ActivateCommand(int index)
        {
            if (_controller == null)
            {
                return false;
            }

            var commands = _controller.Hud.Commands;
            if (index < 0 || index >= commands.Count)
            {
                return false;
            }

            var option = commands[index];
            if (!option.Enabled)
            {
                Rebuild();
                return false;
            }

            switch (option.Id)
            {
                case BattleCommandId.Skill:
                    return Submit(_controller.ChooseSkill(option.SkillId));
                case BattleCommandId.Item:
                    // 道具的结算与技能共用同一条「选目标」界面：
                    // 之后玩家点的人由 ActivateTarget 转给 ChooseTarget。
                    return Submit(_controller.ChooseItem(option.ItemId));
                case BattleCommandId.Defend:
                    return Submit(_controller.ChooseDefend());
                case BattleCommandId.Flee:
                    return Submit(_controller.ChooseFlee());
                case BattleCommandId.EndTurn:
                    return Submit(_controller.ChooseEndTurn());
                case BattleCommandId.Move:
                    _pick = PickMode.Move;
                    Rebuild();
                    return true;
                case BattleCommandId.Swap:
                    _pick = PickMode.Swap;
                    Rebuild();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>在当前选目标提示下点一个单位。</summary>
        public bool ActivateTarget(int runtimeId)
        {
            if (_controller == null || _controller.Prompt != BattlePrompt.PlayerTarget)
            {
                return false;
            }

            return Submit(_controller.ChooseTarget(runtimeId));
        }

        /// <summary>在移动子选择下点一个落点。</summary>
        public bool ActivateMoveDestination(FormationSlot slot)
        {
            if (_controller == null || _pick != PickMode.Move)
            {
                return false;
            }

            return Submit(_controller.ChooseMove(slot));
        }

        /// <summary>在换位子选择下点一个同伴。</summary>
        public bool ActivateSwapPartner(int runtimeId)
        {
            if (_controller == null || _pick != PickMode.Swap)
            {
                return false;
            }

            return Submit(_controller.ChooseSwap(runtimeId));
        }

        /// <summary>撤销子选择：选目标退回指令栏，选落点／同伴退回指令栏。</summary>
        public bool CancelPick()
        {
            if (_controller == null)
            {
                return false;
            }

            if (_controller.Prompt == BattlePrompt.PlayerTarget)
            {
                return Submit(_controller.CancelTargeting());
            }

            if (_pick != PickMode.None)
            {
                _pick = PickMode.None;
                Rebuild();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 诊断入口：用真实遭遇与真实队伍开一场战斗，并把界面接上去。
        /// </summary>
        /// <remarks>
        /// 它存在的理由与「自挂」相同：工程里还没有探索流程会触发战斗，
        /// 没有一个入口的话这一层永远无法被人看到，也就无法被人核对。
        /// 已经有进行中的战斗时它会拒绝——不抢别人的战斗。
        /// </remarks>
        public bool StartDiagnosticBattle(string encounterId = null)
        {
            EnsureServices();

            if (_battle == null || _definitions == null)
            {
                GameLog.Warn(
                    LogChannel.UI,
                    "Diagnostic battle requested before the service registry was ready; ignored.");
                return false;
            }

            if (_battle.HasActiveBattle)
            {
                return false;
            }

            var id = string.IsNullOrEmpty(encounterId) ? DiagnosticEncounterId : encounterId;
            if (!_definitions.TryGet(id, out EncounterDefinition encounter) || encounter == null)
            {
                GameLog.Warn(LogChannel.UI, $"Diagnostic battle asked for unknown encounter '{id}'.", id);
                return false;
            }

            var party = BattleFactory.DefaultParty(_definitions);
            if (party.Count == 0)
            {
                GameLog.Warn(LogChannel.UI, "Diagnostic battle found no playable character definition.");
                return false;
            }

            var setup = BattleFactory.FromEncounter(encounter, party, BuildDiagnosticInventory());
            if (!setup.Validate(out var error))
            {
                GameLog.Warn(LogChannel.UI, $"Diagnostic battle setup rejected: {error}", id);
                return false;
            }

            _battle.StartBattle(setup);
            Tick();
            return HasBattle;
        }

        /// <summary>
        /// 诊断战斗的道具来源：数据表里每一件<b>战斗内可用</b>的道具各
        /// <see cref="DiagnosticItemStock"/> 个。
        /// </summary>
        /// <remarks>
        /// 不硬编码道具 ID——数据表加了新的战斗道具，诊断层自动就能点到，
        /// 这也顺带成了「新道具接进战斗通路了吗」的一根探针。
        /// </remarks>
        private BattleInventory BuildDiagnosticInventory()
        {
            var inventory = new BattleInventory();
            foreach (var item in _definitions.OfKind<ItemDefinition>())
            {
                if (item == null || string.IsNullOrEmpty(item.Id) || !item.UsableInBattle)
                {
                    continue;
                }

                inventory.Add(item.Id, DiagnosticItemStock);
            }

            return inventory;
        }

        private bool Submit(bool accepted)
        {
            _pick = PickMode.None;
            Rebuild();
            return accepted;
        }

        private void EnsureServices()
        {
            if (!GameServices.IsReady)
            {
                // 服务被撤了（退出、或流程重跑）：攥着上一轮的引用只会画出已经不存在的战局。
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

            if (!registry.TryResolve(out _battle) || _battle == null)
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

            _registryRef = registry;
            GameLog.Info(LogChannel.UI, "Battle screen view bound to the service registry.");
        }

        private void DropServices()
        {
            _registryRef = null;
            _battle = null;
            _definitions = null;
            _localization = null;
            _boundSession = null;
            _controller = null;
            _pick = PickMode.None;
            _snapshotKey = int.MinValue;
        }

        private int ComputeSnapshotKey()
        {
            var hud = _controller.Hud;
            var key = (int)_controller.Prompt;
            key = (key * 397) ^ hud.ActionCount;
            key = (key * 397) ^ hud.RoundNumber;
            key = (key * 397) ^ (int)_hudOutcome;
            key = (key * 397) ^ (int)_pick;
            return key;
        }

        private BattleOutcome _hudOutcome =>
            _controller == null ? BattleOutcome.Ongoing : _controller.Hud.Outcome;

        private void RebuildRows()
        {
            _rows.Clear();

            if (_controller == null)
            {
                return;
            }

            var hud = _controller.Hud;
            AppendSide(hud, hud.PlayerRows, isPlayer: true);
            AppendSide(hud, hud.EnemyRows, isPlayer: false);
        }

        private void AppendSide(BattleHudModel hud, IReadOnlyList<BattleUnitRow> source, bool isPlayer)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var row = source[i];
                _rows.Add(new SnapshotRow
                {
                    Text = BuildRowText(hud, row, isPlayer),
                    IsPlayer = isPlayer,
                    IsCurrentActor = row.IsCurrentActor,
                    IsAlive = row.IsAlive,
                });
            }
        }

        /// <summary>一行单位的文案：名字、气血、护体、状态、行动中标记，敌人再加一句意图。</summary>
        private string BuildRowText(BattleHudModel hud, BattleUnitRow row, bool isPlayer)
        {
            var builder = new StringBuilder();
            builder.Append(Get(row.NameKey));
            builder.Append("  ")
                .Append(_localization.Format(LocalizationKeys.UI_BATTLE_HP, row.Health, row.MaxHealth));

            // 不吃护体的单位（阈值 0）不画护体，免得读者以为它有一条空条。
            if (row.BreakThreshold > 0)
            {
                builder.Append("  ")
                    .Append(_localization.Format(
                        LocalizationKeys.UI_BATTLE_BREAK,
                        row.BreakValue,
                        row.BreakThreshold));
            }

            var statuses = row.Statuses;
            for (var i = 0; i < statuses.Count; i++)
            {
                var chip = statuses[i];
                builder.Append("  ").Append(Get(chip.NameKey));
                if (chip.Stacks > 1)
                {
                    builder.Append(" x").Append(chip.Stacks);
                }

                if (chip.RemainingTurns > 0)
                {
                    builder.Append(" (").Append(chip.RemainingTurns).Append(')');
                }
            }

            if (row.IsCurrentActor)
            {
                builder.Append("  ").Append(Get(BattleTextKeys.CurrentActor));
            }

            // 意图只在敌方那一行：这是我方决策的依据，也是「界面确实读到了预览」的凭据。
            if (!isPlayer)
            {
                var intent = hud.FindIntent(row.RuntimeId);
                if (intent != null && intent.HasIntent)
                {
                    builder.Append("  ").Append(_localization.Format(
                        LocalizationKeys.UI_BATTLE_INTENT_PREVIEW,
                        Get(intent.SkillNameKey)));
                }
            }

            return builder.ToString();
        }

        private void RebuildCommands()
        {
            _commandLabels.Clear();
            _commandEnabled.Clear();

            if (_controller == null)
            {
                return;
            }

            var commands = _controller.Hud.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                var option = commands[i];
                _commandLabels.Add(Get(option.LabelKey));
                _commandEnabled.Add(option.Enabled);
            }
        }

        private void RebuildPicks()
        {
            _pickLabels.Clear();
            _pickKeys.Clear();

            if (_controller == null)
            {
                return;
            }

            var hud = _controller.Hud;

            // 选目标来自驱动器（它才是「合法目标」的持有者），子选择来自界面自己的那次点击。
            if (_controller.Prompt == BattlePrompt.PlayerTarget)
            {
                var targets = _controller.TargetCandidateIds;
                for (var i = 0; i < targets.Count; i++)
                {
                    _pickKeys.Add(targets[i]);
                    _pickLabels.Add(RowName(hud, targets[i]));
                }

                return;
            }

            if (_pick == PickMode.Move)
            {
                var slots = hud.MoveCandidates;
                for (var i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    _pickKeys.Add(slot.Index);
                    _pickLabels.Add(_localization.Format(
                        LocalizationKeys.UI_BATTLE_MOVE_SLOT,
                        slot.Index + 1));
                }

                return;
            }

            if (_pick == PickMode.Swap)
            {
                var partners = hud.SwapCandidates;
                for (var i = 0; i < partners.Count; i++)
                {
                    _pickKeys.Add(partners[i]);
                    _pickLabels.Add(RowName(hud, partners[i]));
                }
            }
        }

        private void RebuildHeaderAndPrompt()
        {
            _titleLine = Get(BattleTextKeys.Title);

            if (_controller == null)
            {
                _statusLine = string.Empty;
                _promptLine = null;
                _noteLine = null;
                _hintLine = Get(LocalizationKeys.UI_BATTLE_VIEW_HINT);
                return;
            }

            var hud = _controller.Hud;
            var status = new StringBuilder();
            status.Append(_localization.Format(BattleTextKeys.Round, hud.RoundNumber));
            status.Append("  ").Append(_localization.Format(
                BattleTextKeys.EscapeChance,
                (hud.EscapeChance * 100f).ToString("F0", CultureInfo.InvariantCulture)));

            var outcomeKey = BattleTextKeys.Outcome(hud.Outcome);
            if (outcomeKey != null)
            {
                status.Append("  ").Append(Get(outcomeKey));
            }

            _statusLine = status.ToString();

            if (_controller.Prompt == BattlePrompt.PlayerTarget)
            {
                _promptLine = Get(BattleTextKeys.TargetPrompt);
            }
            else if (_pick == PickMode.Move)
            {
                _promptLine = Get(LocalizationKeys.UI_BATTLE_MOVE_PROMPT);
            }
            else if (_pick == PickMode.Swap)
            {
                _promptLine = Get(LocalizationKeys.UI_BATTLE_SWAP_PROMPT);
            }
            else
            {
                _promptLine = null;
            }

            // 拒绝理由只转述内核给的那两种（改操作能绕开的）。其余是界面本该拦住的，不画。
            _noteLine = null;
            var last = _controller.LastResult;
            if (last != null && !last.Success)
            {
                var rejectionKey = BattleTextKeys.Rejection(last.Rejection);
                if (rejectionKey != null)
                {
                    _noteLine = Get(rejectionKey);
                }
            }

            _hintLine = Get(LocalizationKeys.UI_BATTLE_VIEW_HINT);
        }

        private string RowName(BattleHudModel hud, int runtimeId)
        {
            var row = hud.FindRow(runtimeId);
            return row == null ? runtimeId.ToString(CultureInfo.InvariantCulture) : Get(row.NameKey);
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
        /// 与 <see cref="SkeletonBootScreen"/> 同一份候选表、同一条退路。
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

            // 与自检画面同一套按键识别方式：用 IMGUI 事件而不是 UnityEngine.Input，
            // 后者在「仅 Input System」的后端下会直接抛异常。
            var current = Event.current;
            if (current != null && current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.F3)
                {
                    IsVisible = false;
                    current.Use();
                    return;
                }

                if (current.keyCode == KeyCode.F2 && IsDiagnosticStartSupported)
                {
                    StartDiagnosticBattle();
                    current.Use();
                    return;
                }
            }

            // 文本服务没就绪时一个字都不画：界面层不允许自带中文兜底文案。
            if (_localization == null)
            {
                return;
            }

            EnsureStyles();

            var panel = new Rect(Margin, Screen.height - PanelHeight - Margin, PanelWidth, PanelHeight);
            DrawRect(panel, PanelColor);
            DrawRect(
                new Rect(panel.x, panel.y, panel.width, AccentHeight),
                _controller != null && _controller.Hud.IsFinished ? EndedAccentColor : AccentColor);

            var x = panel.x + Padding;
            var width = panel.width - Padding * 2f;
            var y = panel.y + AccentHeight + Padding;
            var lineHeight = FontSize + 8f;

            GUI.Label(new Rect(x, y, width, FontSize + 10f), _titleLine, _titleStyle);
            y += FontSize + 12f;
            GUI.Label(new Rect(x, y, width, lineHeight), _statusLine, _lineStyle);
            y += lineHeight + Gap;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                _lineStyle.normal.textColor = RowColor(row);
                GUI.Label(new Rect(x, y, width, lineHeight), row.Text, _lineStyle);
                y += lineHeight;
            }

            _lineStyle.normal.textColor = LineColor;
            y += Gap;

            if (!string.IsNullOrEmpty(_promptLine))
            {
                GUI.Label(new Rect(x, y, width, lineHeight), _promptLine, _hintStyle);
                y += lineHeight;
            }

            y = DrawButtons(x, y, width, _commandLabels, _commandEnabled, index => ActivateCommand(index));

            if (_pickLabels.Count > 0)
            {
                y = DrawButtons(x, y, width, _pickLabels, null, ActivatePick);
            }

            if (!string.IsNullOrEmpty(_noteLine))
            {
                _lineStyle.normal.textColor = WarnColor;
                GUI.Label(new Rect(x, y, width, lineHeight), _noteLine, _lineStyle);
                _lineStyle.normal.textColor = LineColor;
            }

            if (!string.IsNullOrEmpty(_hintLine))
            {
                GUI.Label(
                    new Rect(x, panel.yMax - Padding - FontSize - 6f, width, FontSize + 6f),
                    _hintLine,
                    _hintStyle);
            }
        }

        private void ActivatePick(int key)
        {
            if (_pick == PickMode.Move)
            {
                ActivateMoveDestination(FormationSlot.FromIndex(key));
                return;
            }

            ActivateSwapPartner(key);
        }

        private static Color RowColor(SnapshotRow row)
        {
            if (!row.IsAlive)
            {
                return DownColor;
            }

            if (row.IsCurrentActor)
            {
                return CurrentActorColor;
            }

            return row.IsPlayer ? PlayerColor : EnemyColor;
        }

        /// <summary>把一排按钮铺开，铺不下就换行，返回下一行的 y。</summary>
        private static float DrawButtons(
            float x,
            float y,
            float width,
            IReadOnlyList<string> labels,
            IReadOnlyList<bool> enabled,
            Action<int> onClick)
        {
            var originX = x;
            var cursorX = x;
            var cursorY = y;
            var wrapped = false;

            for (var i = 0; i < labels.Count; i++)
            {
                if (cursorX + ButtonWidth > originX + width && cursorX > originX)
                {
                    cursorX = originX;
                    cursorY += ButtonHeight + 4f;
                    wrapped = true;
                }

                var previous = GUI.enabled;
                if (enabled != null)
                {
                    GUI.enabled = enabled[i];
                }

                if (GUI.Button(new Rect(cursorX, cursorY, ButtonWidth, ButtonHeight), labels[i]))
                {
                    onClick?.Invoke(i);
                }

                GUI.enabled = previous;
                cursorX += ButtonWidth + 4f;
            }

            if (labels.Count == 0)
            {
                return wrapped ? cursorY + ButtonHeight + 4f : cursorY;
            }

            return cursorY + ButtonHeight + Gap;
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
