using SamsaraWest.Core;

namespace SamsaraWest.Narrative
{
    /// <summary>
    /// 剧情事件频道。与 <c>ExplorationEventChannel</c> 同一种写法：
    /// 订阅方只认频道上的事件，不反向读账本内部状态。
    /// </summary>
    public static class NarrativeEventChannel
    {
        public const EventChannel Channel = EventChannel.Narrative;
    }

    /// <summary>
    /// 一个剧情状态键的值真的变了。
    /// </summary>
    /// <remarks>
    /// <b>只在值真的变化时发</b>（把同一个键写成同一个数不发）。探索侧靠这条事件重建可行格，
    /// 界面靠它重画；若重复发，一次开门会引出两条刷新，而且日志里看不出哪次是真变化。
    /// </remarks>
    public readonly struct StoryStateChangedEvent : IGameEvent
    {
        public StoryStateChangedEvent(string stateKey, int previousValue, int currentValue)
        {
            StateKey = stateKey;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        /// <summary>发生变化的状态键，例如 <c>flag.ch01.prologue_done</c>。</summary>
        public string StateKey { get; }

        public int PreviousValue { get; }

        public int CurrentValue { get; }
    }

    /// <summary>心念某轴的值真的变了。频道名见 <see cref="KarmaAxes"/>。</summary>
    public readonly struct StoryKarmaChangedEvent : IGameEvent
    {
        public StoryKarmaChangedEvent(string karmaChannel, int previousValue, int currentValue)
        {
            KarmaChannel = karmaChannel;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        /// <summary>心念轴状态键，例如 <c>karma.compassion</c>。</summary>
        public string KarmaChannel { get; }

        public int PreviousValue { get; }

        public int CurrentValue { get; }
    }

    /// <summary>
    /// 整本账被换掉了（读档，见 <see cref="IStoryState.Restore"/>）。
    /// </summary>
    /// <remarks>
    /// <see cref="StoryStateChangedEvent"/> 说的是「一个键变了」，这条说的是「整本换了一份」。
    /// 为什么不逐键发：读档会一次换掉几百个键，逐键发会让每个订阅方各跑几百遍，
    /// 而它们要做的其实是同一件事——按新账重建一次。载荷只够报出「换成了什么规模」，
    /// 想细看就该去读账本，而不是从事件里拼一份影子账。
    /// </remarks>
    public readonly struct StoryStateRestoredEvent : IGameEvent
    {
        public StoryStateRestoredEvent(int flagCount, int compassion, int truth, int freedom)
        {
            FlagCount = flagCount;
            Compassion = compassion;
            Truth = truth;
            Freedom = freedom;
        }

        /// <summary>换进来多少个非 0 的状态键。单看它就能区分「读了一份空档」与「读了一份真档」。</summary>
        public int FlagCount { get; }

        public int Compassion { get; }

        public int Truth { get; }

        public int Freedom { get; }
    }

    /// <summary>一段对白开始了。</summary>
    /// <remarks>
    /// 与 <c>DialogueLineChangedEvent</c> 分开，是因为「开了一段」与「翻了一行」对订阅方的意思不同：
    /// 前者要出现面板，后者只是重画一行。合成一条会让首行出现两次。
    /// </remarks>
    public readonly struct DialogueStartedEvent : IGameEvent
    {
        public DialogueStartedEvent(string dialogueId, string knotName, int lineCount)
        {
            DialogueId = dialogueId;
            KnotName = knotName;
            LineCount = lineCount;
        }

        public string DialogueId { get; }

        /// <summary>起始剧本节点名，例如 <c>CH01_N02_GREETER_TALK</c>。</summary>
        public string KnotName { get; }

        /// <summary>起始节点的总行数。为 0 表示「进来就收场」的那种节点（出口、占位）。</summary>
        public int LineCount { get; }
    }

    /// <summary>对白翻到了新的一行（也可能是翻到了一个新节点）。</summary>
    public readonly struct DialogueLineChangedEvent : IGameEvent
    {
        public DialogueLineChangedEvent(string dialogueId, string knotName, int lineIndex, string lineKey, string speakerKey)
        {
            DialogueId = dialogueId;
            KnotName = knotName;
            LineIndex = lineIndex;
            LineKey = lineKey;
            SpeakerKey = speakerKey;
        }

        public string DialogueId { get; }

        public string KnotName { get; }

        /// <summary>行号，<b>从 0 数起</b>；界面上显示时加一。</summary>
        public int LineIndex { get; }

        /// <summary>这一行正文的文本键，例如 <c>dlg.ch01.002.line.3</c>。正文本身不在这里。</summary>
        public string LineKey { get; }

        /// <summary>这一行说话人的文本键，例如 <c>dlg.ch01.002.line.3.who</c>。</summary>
        public string SpeakerKey { get; }
    }

    /// <summary>一段对白结束了（读完、或者被中断）。</summary>
    public readonly struct DialogueEndedEvent : IGameEvent
    {
        public DialogueEndedEvent(string dialogueId, string lastKnotName, int linesPlayed, bool completed)
        {
            DialogueId = dialogueId;
            LastKnotName = lastKnotName;
            LinesPlayed = linesPlayed;
            Completed = completed;
        }

        public string DialogueId { get; }

        /// <summary>最后读到的剧本节点名。</summary>
        public string LastKnotName { get; }

        /// <summary>本次会话一共播了多少行。</summary>
        public int LinesPlayed { get; }

        /// <summary>是读完自然收场，还是被中断（换图、读档、界面上按了退出）。</summary>
        public bool Completed { get; }
    }
}
