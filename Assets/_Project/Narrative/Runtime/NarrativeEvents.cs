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
}
