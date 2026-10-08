using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 探索服务：按地图 ID 开一次探索会话，并记住当前这一次。
    /// </summary>
    /// <remarks>
    /// 与 <c>IBattleService</c> 同一种分工：服务长期存活、只负责「开一次 / 记住当前这次」，
    /// 换地图就丢掉旧会话。它<b>不认识</b>战斗、界面与场景（见 ADR-021 的依赖方向），
    /// 遇敌只发事件，开战由 Flow 侧的接线决定。
    /// </remarks>
    public interface IExplorationService : IService
    {
        /// <summary>当前使用的剧情状态读取源。没有注入时是「全部读作 0」的替身。</summary>
        IExplorationStateSource StateSource { get; }

        /// <summary>当前这次探索会话；不在任何地图上时为 null。</summary>
        ExplorationSession Current { get; }

        bool IsExploring { get; }

        /// <summary>
        /// 进一张地图，落在 <paramref name="start"/>。地图 ID 非法或不在定义目录里时返回 null 并记错误日志——
        /// 调用方是流程与界面，让它们拿到 null 比抛异常更好处理。
        /// <para>
        /// 落点由调用方负责算准。读档恢复用这个方法：存档里的坐标是玩家自己走出来的，
        /// 越界或被占住说明数据坏了，那时该留下痕迹而不是悄悄替玩家挪个地方。
        /// </para>
        /// </summary>
        ExplorationSession EnterMap(string mapId, GridPosition start, MoveDirection facing = MoveDirection.South);

        /// <summary>
        /// 从锚点<b>附近</b>进图：目标图里同坐标不可走（越界、或站着别的交互物）时，
        /// 就近取离它最近的可行走格（见 <see cref="ExplorationGrid.NearestWalkable"/>）。
        /// </summary>
        /// <remarks>
        /// 换图用它。锚点通常是「来路那扇门在上一张图上的格子」，
        /// 于是进门的效果就是「从这扇门进来，出现在对面那扇门附近」，
        /// 而两张图尺寸不同、同一格被别的物件占住这类事都不必由调用方操心。
        /// </remarks>
        ExplorationSession EnterMapNear(string mapId, GridPosition anchor, MoveDirection facing = MoveDirection.South);

        /// <summary>离开当前地图（换图前、或进入战斗演出时调用）。</summary>
        void LeaveMap();
    }

    /// <inheritdoc cref="IExplorationService" />
    public sealed class ExplorationService : IExplorationService
    {
        private IDefinitionRegistry _registry;
        private IRandomService _random;
        private IEventBus _eventBus;
        private readonly IExplorationStateSource _stateSource;

        public ExplorationService(IExplorationStateSource stateSource = null)
        {
            _stateSource = stateSource ?? MissingExplorationStateSource.Instance;
        }

        public IExplorationStateSource StateSource => _stateSource;

        public ExplorationSession Current { get; private set; }

        public bool IsExploring => Current != null;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // 数据与随机是硬依赖：没有地图表就无处可走，没有随机流就无法复现遭遇。
            if (!registry.TryResolve(out _registry) || _registry == null)
            {
                throw new ServiceNotRegisteredException(typeof(IDefinitionRegistry));
            }

            if (!registry.TryResolve(out _random) || _random == null)
            {
                throw new ServiceNotRegisteredException(typeof(IRandomService));
            }

            // 事件总线是软依赖：没有总线时照样能走能交互，只是没人知道（测试里就不接总线）。
            registry.TryResolve(out _eventBus);

            GameLog.Info(
                LogChannel.Exploration,
                _stateSource is MissingExplorationStateSource
                    ? "Exploration 模块已注册；剧情状态源未注入，条件类交互物按「条件未满足」处理。"
                    : "Exploration 模块已注册，已注入剧情状态源。");
        }

        public void OnUnregistered()
        {
            Current = null;
            _registry = null;
            _random = null;
            _eventBus = null;
        }

        public ExplorationSession EnterMap(
            string mapId,
            GridPosition start,
            MoveDirection facing = MoveDirection.South) =>
            EnterMapCore(mapId, start, facing, resolveNearest: false);

        public ExplorationSession EnterMapNear(
            string mapId,
            GridPosition anchor,
            MoveDirection facing = MoveDirection.South) =>
            EnterMapCore(mapId, anchor, facing, resolveNearest: true);

        /// <summary>两条进图路径共用的壳：查表、收交互物、建格、开会话。差别只有「落点算不算」。</summary>
        private ExplorationSession EnterMapCore(
            string mapId,
            GridPosition requested,
            MoveDirection facing,
            bool resolveNearest)
        {
            if (_registry == null || _random == null)
            {
                throw new ServiceNotRegisteredException(typeof(IExplorationService));
            }

            if (string.IsNullOrWhiteSpace(mapId))
            {
                GameLog.Error(LogChannel.Exploration, "进图时地图 ID 为空，已忽略。");
                return null;
            }

            if (!IdRules.IsValidId(DefinitionKind.Map, mapId))
            {
                GameLog.Error(LogChannel.Exploration, $"地图 ID '{mapId}' 不符合命名规则（CH01_MAP01），已忽略。");
                return null;
            }

            if (!_registry.TryGet(mapId, out MapDefinition map) || map == null)
            {
                GameLog.Error(LogChannel.Exploration, $"定义目录里没有地图 {mapId}，无法进图。", mapId);
                return null;
            }

            var interactables = new List<InteractableDefinition>();
            foreach (var interactable in _registry.OfKind<InteractableDefinition>())
            {
                if (interactable != null && string.Equals(interactable.MapId, mapId, StringComparison.Ordinal))
                {
                    interactables.Add(interactable);
                }
            }

            var start = requested;
            if (resolveNearest)
            {
                // 先建一份格只为算出落点。会话随后会按同一份定义建自己那一张快照，
                // 这里不把探路的那份递进去：网格的归属只有一处，才不会有两份可见性各说各话。
                var probe = ExplorationGrid.Build(map, interactables, _stateSource);
                start = probe.NearestWalkable(requested);

                if (start != requested)
                {
                    GameLog.Info(
                        LogChannel.Exploration,
                        $"地图 {mapId} 的 {requested} 站不住（越界或被占），就近落到 {start}。",
                        mapId);
                }
            }

            // 遭遇走「exploration」这条命名流：调战斗数值不会连带改掉路边遇敌的随机序列。
            var stream = _random.GetStream(RandomStreams.Exploration);
            Current = new ExplorationSession(map, interactables, _stateSource, stream, _eventBus, start, facing);

            GameLog.Info(
                LogChannel.Exploration,
                $"进入地图 {mapId}（{map.GridWidth}x{map.GridHeight}），落点 {start}，交互物 {interactables.Count} 条。",
                mapId);

            return Current;
        }

        public void LeaveMap()
        {
            if (Current == null)
            {
                return;
            }

            GameLog.Info(
                LogChannel.Exploration,
                $"离开地图 {Current.MapId}（本次走了 {Current.StepCount} 步）。",
                Current.MapId);
            Current = null;
        }
    }

    /// <summary>Exploration 层的组合入口，与 <c>DataModule.Install</c>／<c>BattleModule.Install</c> 同一种写法。</summary>
    public static class ExplorationModule
    {
        /// <param name="registry">服务注册表。</param>
        /// <param name="stateSource">
        /// 剧情状态读取源。省略表示「所有条件键都是 0」——带条件的交互物会保持隐藏或不可交互，
        /// 这是诚实的退化：状态那本账还没人写，探索不该替它编。
        /// </param>
        public static void Install(IServiceRegistry registry, IExplorationStateSource stateSource = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IExplorationService>(new ExplorationService(stateSource));
        }
    }
}
