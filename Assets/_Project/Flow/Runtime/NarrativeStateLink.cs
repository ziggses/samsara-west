using System;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Narrative;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「剧情状态变了 → 重建探索可行格」的接线。
    /// </summary>
    /// <remarks>
    /// 为什么必须有这一层：探索的可见性依赖剧情状态，而网格是一件<b>可重建的快照</b>
    /// （见 <c>ExplorationGrid</c> 与 <c>ExplorationSession.RefreshVisibility</c>）。
    /// 账本换了值，探索自己<b>收不到</b>那条事件——它不许认识 <c>Narrative</c>。
    /// 于是这条订阅落在组合根：Flow 两边都认识，像 <c>ExplorationBattleLink</c> 一样做那条唯一同时看得见两端的线。
    ///
    /// 三条事件都要听：交互物的条件键平时是 <c>flag.*</c>，但键的格式规则允许 <c>karma.*</c>
    /// （「慈悲 ≥ 30 才开的门」），所以心念变化同样要重建；读档那一下是<b>整本换掉</b>，
    /// 逐键发不现实（一次几百条），所以账本另发一条「整本换掉了」。
    ///
    /// 不在图上时什么都不做：账本可以在进图前就被改（对话发生在哪都行），
    /// 而进图那一刻 <c>ExplorationService.EnterMap</c> 本来就会按当前账本建格，不必在这里补。
    /// </remarks>
    public sealed class NarrativeStateLink : IDisposable
    {
        private readonly IExplorationService _exploration;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="exploration">探索服务；空表示这条接线不接探索（状态变化只记账，不重建格）。</param>
        public NarrativeStateLink(IEventBus bus, IExplorationService exploration)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _exploration = exploration;
            if (_exploration == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "剧情状态接线没有拿到探索服务，状态变化将不会重建可行格。");
            }

            _subscriptions.Add(bus.Subscribe<StoryStateChangedEvent>(
                NarrativeEventChannel.Channel,
                OnStateChanged));
            _subscriptions.Add(bus.Subscribe<StoryKarmaChangedEvent>(
                NarrativeEventChannel.Channel,
                OnKarmaChanged));
            _subscriptions.Add(bus.Subscribe<StoryStateRestoredEvent>(
                NarrativeEventChannel.Channel,
                OnStateRestored));
        }

        /// <summary>因状态变化而重建过几次可行格。诊断与测试用。</summary>
        public int Refreshes { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnStateChanged(StoryStateChangedEvent gameEvent) => Refresh($"{gameEvent.StateKey} 变化");

        private void OnKarmaChanged(StoryKarmaChangedEvent gameEvent) => Refresh($"{gameEvent.KarmaChannel} 变化");

        private void OnStateRestored(StoryStateRestoredEvent gameEvent) =>
            Refresh($"整本换掉（{gameEvent.FlagCount} 个键）");

        private void Refresh(string cause)
        {
            var session = _exploration == null ? null : _exploration.Current;
            if (session == null)
            {
                // 不在图上：这次变化与可行格无关，等下次进图自会按新账重建。
                return;
            }

            session.RefreshVisibility();
            Refreshes++;

            GameLog.Info(
                LogChannel.Flow,
                $"剧情状态 {cause}，已重建地图 {session.MapId} 的可行格（可见交互物 {session.Grid.VisibleInteractables.Count} 个）。",
                session.MapId);
        }
    }
}
