using System;
using System.Collections.Generic;
using System.Globalization;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Narrative
{
    /// <summary>
    /// 剧情节点推进器的读写契约：开始一段对白、一行一行往前推、按条件挑后继。
    /// </summary>
    /// <remarks>
    /// <b>它不产文本</b>：推进器只说「现在该念 <c>dlg.ch01.002.line.3</c> 这一行」，
    /// 文本由本地化表给出（<c>ILocalizationService</c> 在 UI 层解析）。这样 Narrative 不必认识 Localization，
    /// 而「哪一行」这件事是可断言的数据，不必去比对满屏汉字。
    /// </remarks>
    public interface IDialogueService : IService
    {
        /// <summary>当前有没有对白开着。</summary>
        bool IsActive { get; }

        /// <summary>当前对白的 ID（例如 <c>DLG_CH01_002</c>）；没开时为 null。</summary>
        string DialogueId { get; }

        /// <summary>当前所在剧本节点名（例如 <c>CH01_N02_GREETER_TALK</c>）；没开时为 null。</summary>
        string KnotName { get; }

        /// <summary>当前节点的名字键，界面拿它当标题。</summary>
        string NodeDisplayNameKey { get; }

        /// <summary>当前行号，从 1 数起。</summary>
        int LineNumber { get; }

        /// <summary>当前节点的总行数。</summary>
        int LineCount { get; }

        /// <summary>当前这一行正文的文本键，例如 <c>dlg.ch01.002.line.3</c>。</summary>
        string LineKey { get; }

        /// <summary>当前这一行说话人的文本键，例如 <c>dlg.ch01.002.line.3.who</c>。</summary>
        string SpeakerKey { get; }

        /// <summary>本次会话已经播了多少行（跨节点累计）。</summary>
        int LinesPlayed { get; }

        /// <summary>开始一段对白。被拒时带理由回来，不抛异常——玩家对着空气按 E 不该是崩溃。</summary>
        DialogueStartResult TryStart(string dialogueId);

        /// <summary>推进一行；走到节点末尾就结算写入、按条件挑后继，没有后继就收场。</summary>
        DialogueAdvanceResult Advance();

        /// <summary>强行收场（换图、读档、退出探索时用）。没有对白开着时返回 false。</summary>
        bool Close();
    }

    /// <inheritdoc cref="IDialogueService" />
    /// <remarks>
    /// <b>节点之间怎么走</b>：<c>nextNodeIds</c> 里<b>按顺序取第一个条件满足的</b>节点。
    /// 条件不满足就跳过，全都满足不了就到此为止——这正是「问过一次就不再问」的实现手段：
    /// 问伤口那条把自己挂在 <c>flag.ch01.monkey_questioned == 0</c> 上，问过之后条件不再满足，
    /// 于是重复搭话只听到招呼，不会再赚一点真相值。
    ///
    /// <b>为什么走 key 名而不是资产 ID</b>：<c>nextNodeIds</c> 登记的是剧本节点名（<c>CH01_N03_ASK_THE_WOUND</c>），
    /// 与 <c>inkKnotName</c> 同一命名空间——剧本里怎么称呼这些段，表里就怎么称呼。
    /// 于是启动时按 <c>inkKnotName</c> 建一张索引，运行时只在索引里查，不必每步都扫全表。
    ///
    /// <b>重名怎么办</b>：两个对话占了同一个节点名时保留先建的、把后来者记成错误日志。
    /// 静默取一个会让「数据改错了」表现为「某段对白莫名其妙消失」。
    /// </remarks>
    public sealed class DialogueService : IDialogueService
    {
        private readonly IDefinitionRegistry _definitions;
        private readonly Dictionary<string, DialogueDefinition> _byKnot =
            new Dictionary<string, DialogueDefinition>(StringComparer.Ordinal);

        private IStoryState _state;
        private IEventBus _eventBus;
        private bool _indexed;

        private DialogueDefinition _node;
        private int _lineIndex;
        private int _linesPlayed;

        public DialogueService(IDefinitionRegistry definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public bool IsActive => _node != null;

        public string DialogueId => _node?.Id;

        public string KnotName => _node?.InkKnotName;

        public string NodeDisplayNameKey => _node?.DisplayNameKey;

        public int LineNumber => _node == null ? 0 : _lineIndex + 1;

        public int LineCount => _node?.LineCount ?? 0;

        public string LineKey => _node == null ? null : LineKeyOf(_node, LineNumber);

        public string SpeakerKey => _node == null ? null : SpeakerKeyOf(_node, LineNumber);

        public int LinesPlayed => _linesPlayed;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // 账本是硬依赖：没有它，进入条件与节点写入就都成了空转，而且是那种「看着能跑」的空转。
            // 它由 NarrativeModule 在同一个安装过程里先于本服务注册，所以解析不到就是装配错了。
            _state = registry.Resolve<IStoryState>();

            // 事件总线是软依赖，与 StoryState 同一口径：没有总线的测试里照旧能推进，只是没人听得见。
            registry.TryResolve(out _eventBus);

            var nodes = BuildIndex();
            GameLog.Info(
                LogChannel.Narrative,
                $"剧情节点推进器就位，已按 inkKnotName 索引 {nodes} 个节点。");
        }

        public void OnUnregistered()
        {
            _state = null;
            _eventBus = null;
            _node = null;
            _lineIndex = 0;
            _linesPlayed = 0;
            _byKnot.Clear();
            _indexed = false;
        }

        public DialogueStartResult TryStart(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                return DialogueStartResult.Rejected(dialogueId, DialogueStartRejection.UnknownDialogue);
            }

            if (IsActive)
            {
                return DialogueStartResult.Rejected(dialogueId, DialogueStartRejection.AlreadyActive);
            }

            EnsureIndex();

            // 这里认的是「这个 ID 是不是一条对话」。门的 targetId 是一张地图、箱子的 targetId 是一张掉落表，
            // 它们在这句话上返回 false，于是接线那边不必自己维护一份「哪些交互算对白」的白名单。
            if (!_definitions.TryGet(dialogueId, out DialogueDefinition node) || node == null)
            {
                GameLog.Warn(LogChannel.Narrative, $"找不到对话定义 '{dialogueId}'，对白没有开始。", dialogueId);
                return DialogueStartResult.Rejected(dialogueId, DialogueStartRejection.UnknownDialogue);
            }

            if (!MeetsRequirement(node))
            {
                GameLog.Info(
                    LogChannel.Narrative,
                    $"对话 '{dialogueId}' 的进入条件没满足（{node.RequiredStateKey} {node.RequiredOperator} {node.RequiredValue}），对白没有开始。",
                    dialogueId);
                return DialogueStartResult.Rejected(dialogueId, DialogueStartRejection.RequirementNotMet);
            }

            _node = node;
            _lineIndex = 0;
            _linesPlayed = 0;

            Publish(new DialogueStartedEvent(node.Id, node.InkKnotName, node.LineCount));

            if (node.LineCount <= 0)
            {
                // 没有文本的节点（山门出口那一类）进来就当场收场，免得界面停在一条空行上等玩家按键。
                FinishNode();
            }
            else
            {
                ShowCurrentLine();
            }

            return new DialogueStartResult(true, DialogueStartRejection.None, node.Id, node.InkKnotName);
        }

        public DialogueAdvanceResult Advance()
        {
            if (!IsActive)
            {
                return DialogueAdvanceResult.Inactive();
            }

            _lineIndex++;

            if (_lineIndex < _node.LineCount)
            {
                ShowCurrentLine();
                return new DialogueAdvanceResult(true, false, false, _node.InkKnotName, _linesPlayed);
            }

            return FinishNode();
        }

        public bool Close()
        {
            if (!IsActive)
            {
                return false;
            }

            // 中断不等于读完：本节点的写入不结算（话没说完就不该记这一次的账），
            // 也不沿 nextNodeIds 续跑——「强行收场」要挡的正是这两件事。
            // 若借 FinishNode 收场，一段挂在条件上的后继会在这里接着开起来，
            // 于是「换图时收场」变成「换图后对白还挂着」，而且要走完一整段才看得出来。
            EndSession(_node, completed: false);
            return true;
        }

        /// <summary>节点第 <paramref name="lineNumber"/> 行正文的文本键（从 1 数起）。</summary>
        /// <remarks>
        /// 键从节点的 <c>displayNameKey</c> 去尾推出来（<c>dlg.ch01.002.name</c> → <c>dlg.ch01.002</c>），
        /// 不另立一列。理由：<c>lineCount</c> 本来就是「资产与剧本是否同步」的对账口径，
        /// 行键跟着同一处走，才不会出现「行数改了、键名没改」那种谁也看不出来的错位。
        /// 万一有人把 displayNameKey 写成了别的形状，行键会当场落空——本地化服务会把缺的键报出来，
        /// 比悄悄画出一片空白要好。
        /// </remarks>
        public static string LineKeyOf(DialogueDefinition node, int lineNumber)
        {
            var stem = NodeKeyStem(node);
            return string.IsNullOrEmpty(stem)
                ? string.Empty
                : stem + ".line." + lineNumber.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>节点第 <paramref name="lineNumber"/> 行说话人的文本键。</summary>
        public static string SpeakerKeyOf(DialogueDefinition node, int lineNumber)
        {
            var lineKey = LineKeyOf(node, lineNumber);
            return string.IsNullOrEmpty(lineKey) ? string.Empty : lineKey + ".who";
        }

        /// <summary>节点文本键的「词干」，即 <c>displayNameKey</c> 去掉结尾的 <c>.name</c>。</summary>
        public static string NodeKeyStem(DialogueDefinition node)
        {
            var displayKey = node?.DisplayNameKey;
            if (string.IsNullOrWhiteSpace(displayKey))
            {
                return string.Empty;
            }

            const string suffix = ".name";
            return displayKey.EndsWith(suffix, StringComparison.Ordinal)
                ? displayKey.Substring(0, displayKey.Length - suffix.Length)
                : displayKey;
        }

        /// <summary>当前节点读完了：结算写入、按条件挑后继；挑不到就收场。</summary>
        private DialogueAdvanceResult FinishNode()
        {
            var finished = _node;
            ApplyEffects(finished);

            var next = finished.IsTerminal ? null : ResolveNext(finished);

            if (next == null)
            {
                var played = EndSession(finished, completed: true);
                return new DialogueAdvanceResult(true, false, true, finished.InkKnotName, played);
            }

            if (next.LineCount <= 0)
            {
                // 中途撞上一个没有文本的节点：它的写入照算，但会话到此为止。
                // 「无文本路由节点」要做成能穿过去，得先有循环保护与跳数上限，登记为未做。
                GameLog.Warn(
                    LogChannel.Narrative,
                    $"后继节点 '{next.InkKnotName}' 没有文本行，对白在此收场。",
                    next.Id);
                ApplyEffects(next);
                var played = EndSession(next, completed: true);
                return new DialogueAdvanceResult(true, true, true, next.InkKnotName, played);
            }

            _node = next;
            _lineIndex = 0;
            ShowCurrentLine();
            return new DialogueAdvanceResult(true, true, false, next.InkKnotName, _linesPlayed);
        }

        /// <summary>在 <c>nextNodeIds</c> 里按顺序取第一个条件满足的节点；没有就返回 null。</summary>
        private DialogueDefinition ResolveNext(DialogueDefinition node)
        {
            var candidates = node.NextNodeIds;
            if (candidates.Length == 0)
            {
                return null;
            }

            var sawAnyNode = false;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (!_byKnot.TryGetValue(candidates[i], out var candidate) || candidate == null)
                {
                    GameLog.Warn(
                        LogChannel.Narrative,
                        $"剧本节点 '{node.InkKnotName}' 的后继 '{candidates[i]}' 没有对应定义，已跳过。",
                        node.Id);
                    continue;
                }

                sawAnyNode = true;

                if (MeetsRequirement(candidate))
                {
                    return candidate;
                }
            }

            if (sawAnyNode)
            {
                GameLog.Info(
                    LogChannel.Narrative,
                    $"剧本节点 '{node.InkKnotName}' 的后继条件都没满足，对白到此为止。",
                    node.Id);
            }

            return null;
        }

        /// <summary>结算节点的写入：置位状态键、累加心念。</summary>
        private void ApplyEffects(DialogueDefinition node)
        {
            if (_state == null || node == null)
            {
                return;
            }

            var sets = node.SetsStateKeys;
            for (var i = 0; i < sets.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(sets[i]))
                {
                    continue;
                }

                _state.SetValue(sets[i], 1);
            }

            if (node.KarmaDelta == 0)
            {
                return;
            }

            if (KarmaAxes.TryParseChannel(node.KarmaChannel, out var axis))
            {
                _state.AdjustKarma(axis, node.KarmaDelta);
                return;
            }

            GameLog.Error(
                LogChannel.Narrative,
                $"对话 '{node.Id}' 的心念频道 '{node.KarmaChannel}' 不是三轴之一，{node.KarmaDelta} 点心念没有记上。",
                node.Id);
        }

        /// <summary>收场：发结束事件、清掉会话。返回本次一共播了多少行（清会话之前取）。</summary>
        private int EndSession(DialogueDefinition lastNode, bool completed)
        {
            var dialogueId = DialogueId;
            var knotName = lastNode?.InkKnotName;
            var played = _linesPlayed;

            _node = null;
            _lineIndex = 0;
            _linesPlayed = 0;

            Publish(new DialogueEndedEvent(dialogueId, knotName, played, completed));

            GameLog.Info(
                LogChannel.Narrative,
                completed
                    ? $"对白 '{dialogueId}' 读完（{played} 行，收在 {knotName}）。"
                    : $"对白 '{dialogueId}' 被中断（已读 {played} 行）。",
                dialogueId);

            return played;
        }

        private void ShowCurrentLine()
        {
            _linesPlayed++;
            Publish(new DialogueLineChangedEvent(DialogueId, KnotName, _lineIndex, LineKey, SpeakerKey));
        }

        private bool MeetsRequirement(DialogueDefinition node)
        {
            if (string.IsNullOrWhiteSpace(node.RequiredStateKey))
            {
                return true;
            }

            var current = _state == null ? 0 : _state.GetValue(node.RequiredStateKey);
            return Compare(current, node.RequiredValue, node.RequiredOperator);
        }

        /// <summary>
        /// 按 <c>requiredOperator</c> 比较。未知运算符一律判不满足——坏数据不该被当成放行。
        /// </summary>
        /// <remarks>
        /// 与 <c>ExplorationGrid.Compare</c> 同一条规则，但在这里又写了一份：
        /// Narrative 只允许依赖 Core 与 Data（<c>ProjectSkeletonTests</c> 锁住），
        /// 为了复用一行比较去反向依赖 Exploration 是笔亏本买卖。两处规则若有一天分叉，
        /// 「条件语义」本来就该由数据层来说，那时再把它挪到 Data 里去。
        /// </remarks>
        private static bool Compare(int value, int target, CompareOperator comparison)
        {
            switch (comparison)
            {
                case CompareOperator.Equal:
                    return value == target;
                case CompareOperator.NotEqual:
                    return value != target;
                case CompareOperator.Greater:
                    return value > target;
                case CompareOperator.GreaterOrEqual:
                    return value >= target;
                case CompareOperator.Less:
                    return value < target;
                case CompareOperator.LessOrEqual:
                    return value <= target;
                default:
                    return false;
            }
        }

        /// <summary>按 <c>inkKnotName</c> 建索引，只建一次。</summary>
        private int BuildIndex()
        {
            _indexed = true;
            _byKnot.Clear();

            var count = 0;
            foreach (var definition in _definitions.OfKind<DialogueDefinition>())
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.InkKnotName))
                {
                    continue;
                }

                if (_byKnot.ContainsKey(definition.InkKnotName))
                {
                    GameLog.Error(
                        LogChannel.Narrative,
                        $"剧本节点名 '{definition.InkKnotName}' 被多个对话占用，后一个被忽略：{definition.Id}。",
                        definition.Id);
                    continue;
                }

                _byKnot.Add(definition.InkKnotName, definition);
                count++;
            }

            return count;
        }

        private void EnsureIndex()
        {
            if (_indexed)
            {
                return;
            }

            BuildIndex();
        }

        private void Publish<T>(T gameEvent) where T : IGameEvent =>
            _eventBus?.Publish(NarrativeEventChannel.Channel, gameEvent);
    }
}
