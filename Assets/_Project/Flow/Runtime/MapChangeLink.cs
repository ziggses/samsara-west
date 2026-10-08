using System;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「走到一扇门 → 换一张图」的接线。探索里那扇门点了之后，真正换图的是这里。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有这一层</b>：探索只依赖 Core 与 Data（ADR-021），它发
    /// <see cref="MapChangeRequestedEvent"/> 但无权换图——「谁来加载、落在哪」既有场景的事，
    /// 也有流程的事。于是这条链落在组合根，而不是落进探索内部，
    /// 否则探索就要开始认识场景与流程，模块边界会从编译期约束退化成一句口号。</para>
    ///
    /// <para><b>落点口径</b>：把门在<b>来源图</b>上的格子当锚点，落在目标图上离它最近的可行走格
    /// （<see cref="IExplorationService.EnterMapNear"/>）。效果就是「从这扇门进来，出现在对面那扇门附近」。
    /// 两张图尺寸不同、同一格被别的物件占住，都不必在这里分辨：那是网格的事。</para>
    ///
    /// <para><b>先验证再离图</b>：目标图不在定义目录里时<b>不动当前会话</b>。
    /// 先离图再发现目标查不到，玩家就会掉进「哪张图都不在」的状态，
    /// 那比留在原地并明确记一条错误难查得多。</para>
    ///
    /// <para><b>换图发生在事件派发途中</b>：本方法是被 <c>ExplorationSession.TryInteract</c> 同步调起的，
    /// 也就是说换图时那一次交互还没返回。这条是安全的——<c>TryInteract</c> 在发出事件之后
    /// 不再读会话自身任何状态，只把早已算好的结果返回。改动那个方法时请把这条一并考虑。</para>
    ///
    /// <para><b>还没做的两件事</b>（登记在 <c>Docs/待拍板清单.md</c>，不在这里偷偷兜底）：
    /// 进门后的朝向现在一律朝下（门的数据里没有「出口朝向」这一列，场地规则另行排期）；
    /// 一次性交互物的「用过了」是记在会话上的，换图会丢——它该记进剧情账本，等账本进存档时一并处理。</para>
    /// </remarks>
    public sealed class MapChangeLink : IDisposable
    {
        private readonly IDefinitionRegistry _definitions;
        private readonly IExplorationService _exploration;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="definitions">定义目录，用来确认目标图真的存在。</param>
        /// <param name="exploration">
        /// 探索服务；空表示这条接线不接探索（换图请求只记数，谁都不动）。
        /// </param>
        public MapChangeLink(
            IEventBus bus,
            IDefinitionRegistry definitions,
            IExplorationService exploration = null)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _exploration = exploration;
            if (_exploration == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "换图接线没有拿到探索服务，换图请求将只会被忽略。");
            }

            _subscriptions.Add(bus.Subscribe<MapChangeRequestedEvent>(
                ExplorationEventChannel.Channel,
                OnMapChangeRequested));
        }

        /// <summary>这条接线一共真的换过几次图。诊断与测试用。</summary>
        public int MapChanges { get; private set; }

        /// <summary>忽略掉的换图请求次数（没有探索服务、目标图查不到、门指向自己这张图）。</summary>
        public int IgnoredRequests { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnMapChangeRequested(MapChangeRequestedEvent gameEvent)
        {
            if (_exploration == null)
            {
                IgnoredRequests++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"地图 {gameEvent.FromMapId} 的 {gameEvent.InteractableId} 要求前往 {gameEvent.ToMapId}，" +
                    "但换图接线没有探索服务，已忽略。",
                    gameEvent.InteractableId);
                return;
            }

            if (string.Equals(gameEvent.FromMapId, gameEvent.ToMapId, StringComparison.Ordinal))
            {
                IgnoredRequests++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"交互物 {gameEvent.InteractableId} 的 targetId 指向自己所在的 {gameEvent.ToMapId}，" +
                    "换图没有意义，已忽略。请核对 interactables.csv。",
                    gameEvent.InteractableId);
                return;
            }

            if (!_definitions.TryGet(gameEvent.ToMapId, out MapDefinition target) || target == null)
            {
                IgnoredRequests++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"目标地图 {gameEvent.ToMapId} 不在定义目录里，这次换图作废，玩家留在 {gameEvent.FromMapId}。" +
                    "请核对交互物表的 targetId。",
                    gameEvent.ToMapId);
                return;
            }

            // 目标确认存在之后才离图：中途失败会让玩家哪张图都不在。
            _exploration.LeaveMap();
            var session = _exploration.EnterMapNear(gameEvent.ToMapId, gameEvent.Position);

            if (session == null)
            {
                // 走到这里说明目标图在目录里却进不去（ID 不合规、或服务缺依赖）。
                // 玩家此刻确实不在图上了，所以这条必须是错误日志，不能只记一个数。
                IgnoredRequests++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"目标地图 {gameEvent.ToMapId} 在目录里但进不去，玩家已离开 {gameEvent.FromMapId} 且未进入新图。",
                    gameEvent.ToMapId);
                return;
            }

            MapChanges++;

            GameLog.Info(
                LogChannel.Flow,
                $"从 {gameEvent.FromMapId} 的 {gameEvent.InteractableId}（{gameEvent.Position}）换图到 " +
                $"{gameEvent.ToMapId}，落点 {session.Position}。",
                gameEvent.ToMapId);
        }
    }
}
