using System;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Narrative;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「一次性交互物用过了 → 记进剧情账本」的接线。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要这一层：探索在会话里记下「这条用过了」，但会话是<b>一次进图</b>的产物，
    /// 换一张图就丢了——开过的箱子出镇再回来会复活。要让它跨会话、还进存档，
    /// 就得记进剧情账本，而账本<b>不认识</b>交互物、探索也<b>不认识</b>账本（ADR-021／023 的边界）。
    /// 两边都认识的地方只有组合根，所以这条订阅落在这里，与另三条 Link 同一种写法。</para>
    ///
    /// <para>只记一次性交互物：可重复触发的（查看、对话）不记——它们的语义是「每次来都能用」，
    /// 记下来会让第二次交互被当成「已用过」而拒掉。判据用事件自带的
    /// <see cref="InteractionTriggeredEvent.OneShot"/>，不让这一层各自去查数据表。</para>
    ///
    /// <para>读那一侧不在这里：<see cref="ExplorationSession.HasUsed"/> 自己往账本上问一句
    /// （经 <see cref="IExplorationStateSource"/>），所以这条线是单向的——只写不读。
    /// 也因此它不需要知道「哪些键该恢复」，读档时账本整本灌回来，下次进图的判定自然就对了。</para>
    /// </remarks>
    public sealed class InteractionFlagLink : IDisposable
    {
        private readonly IStoryState _state;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="state">剧情状态账；空表示这条线不接账本（交互照旧发生，只是记不下来）。</param>
        public InteractionFlagLink(IEventBus bus, IStoryState state)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _state = state;
            if (_state == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "交互记账线没有拿到剧情状态账，一次性交互物将只在本次进图内有效。");
            }

            _subscriptions.Add(bus.Subscribe<InteractionTriggeredEvent>(
                ExplorationEventChannel.Channel,
                OnInteraction));
        }

        /// <summary>往账本里写进去几个键。诊断与测试用（重复交互不会重复计数）。</summary>
        public int FlagsWritten { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnInteraction(InteractionTriggeredEvent gameEvent)
        {
            if (_state == null || !gameEvent.OneShot)
            {
                return;
            }

            var key = InteractableFlags.KeyOf(gameEvent.InteractableId);
            if (key.Length == 0)
            {
                return;
            }

            if (_state.GetValue(key) != 0)
            {
                // 账本里已经有了：这条交互物在别的会话里被用过，或者存档刚灌回来。
                return;
            }

            _state.SetValue(key, 1);
            FlagsWritten++;

            GameLog.Info(
                LogChannel.Flow,
                $"一次性交互物 {gameEvent.InteractableId} 已记进剧情账本（{key}）。");
        }
    }
}
