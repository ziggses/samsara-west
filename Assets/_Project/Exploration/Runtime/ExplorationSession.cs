using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 一次进图的探索会话：玩家在哪、面朝哪、走一步会发生什么。
    /// </summary>
    /// <remarks>
    /// 会话是<b>有始有终的产物</b>（和 <c>BattleSession</c> 同一个理由）：换地图就换一个会话，
    /// 长期存活的是服务（<see cref="IExplorationService"/>）。这里不碰渲染、不加载场景、
    /// 不认识战斗——它只做三件事：走一格、交互一次、掷一次遭遇，然后把发生的事发成事件。
    ///
    /// 两条口径值得单独记住：
    /// <list type="bullet">
    /// <item><description><b>转身只随移动成功</b>：撞墙不改变朝向。于是「面朝哪」永远等于「上一次走成的方向」，
    /// 界面与交互判定不会出现「人没动但朝向变了」的错觉。</description></item>
    /// <item><description><b>遭遇挂着时不许走</b>：掷中之后到 <see cref="ResolveEncounter"/> 之前，
    /// 移动与交互都会被拒绝（<see cref="MoveRejection.EncounterPending"/>）。
    /// 否则玩家连点两下会连开两场战斗。</description></item>
    /// </list>
    /// </remarks>
    public sealed class ExplorationSession
    {
        private readonly IReadOnlyList<InteractableDefinition> _interactables;
        private readonly IExplorationStateSource _state;
        private readonly IRandomStream _stream;
        private readonly IEventBus _bus;
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="map">地图定义。为 null 时不抛异常，只是得到一张空图（服务侧已先查过表）。</param>
        /// <param name="interactables">这张图名下的全部交互物，含条件未满足而不可见的那些。</param>
        /// <param name="state">剧情状态读取源；null 等价于所有条件键都是 0。</param>
        /// <param name="stream">探索命名流（<c>RandomStreams.Exploration</c>）。为 null 时不掷遭遇。</param>
        /// <param name="bus">事件总线；null 表示只算结果不发事件（测试里常见）。</param>
        /// <param name="start">进入时的位置。落点非法时记错误日志并夹到原点，不抛异常。</param>
        /// <param name="facing">进入时的朝向。缺省朝下——俯视视角里那是「面朝镜头」。</param>
        public ExplorationSession(
            MapDefinition map,
            IReadOnlyList<InteractableDefinition> interactables,
            IExplorationStateSource state,
            IRandomStream stream,
            IEventBus bus = null,
            GridPosition start = default,
            MoveDirection facing = MoveDirection.South)
        {
            Map = map;
            MapId = map == null ? null : map.Id;
            _interactables = interactables ?? Array.Empty<InteractableDefinition>();
            _state = state;
            _stream = stream;
            _bus = bus;
            Facing = facing;

            Grid = ExplorationGrid.Build(Map, _interactables, _state);

            Position = start;
            if (!Grid.IsInside(Position))
            {
                GameLog.Error(
                    LogChannel.Exploration,
                    $"进入地图 {MapId} 的落点 {Position} 越界，已夹到 (0,0)。请核对存档位置或传送点配置。",
                    MapId);
                Position = Grid.Width > 0 && Grid.Height > 0 ? GridPosition.Origin : Position;
            }
            else if (!Grid.IsWalkable(Position))
            {
                // 落在被交互物占住的格子上：不强行挪人（那会把「数据错」伪装成「位置被纠正」），
                // 但必须留下痕迹，否则玩家会站在一个不该站的地方而没人知道。
                GameLog.Warn(
                    LogChannel.Exploration,
                    $"进入地图 {MapId} 的落点 {Position} 被交互物占着，位置保持不动，请核对表。",
                    MapId);
            }

            Publish(new MapEnteredEvent(MapId, Position, Grid.Width, Grid.Height, Map == null ? null : Map.EncounterTableId));
        }

        public MapDefinition Map { get; }

        public string MapId { get; }

        /// <summary>可行走格快照。剧情状态变了要调 <see cref="RefreshVisibility"/> 重建。</summary>
        public ExplorationGrid Grid { get; private set; }

        public GridPosition Position { get; private set; }

        public MoveDirection Facing { get; private set; }

        /// <summary>本次进图后走成的步数。</summary>
        public int StepCount { get; private set; }

        /// <summary>本次进图后掷过的遭遇骰次数。诊断用：次数应当等于走成的步数（安全区与无遭遇表的地图除外）。</summary>
        public int EncounterRollCount { get; private set; }

        /// <summary>已经掷中、还没了结的遭遇 ID；没有则为 null。</summary>
        public string PendingEncounterId { get; private set; }

        public bool IsEncounterPending => !string.IsNullOrEmpty(PendingEncounterId);

        /// <summary>玩家面朝的那一格。交互判定看的就是它。</summary>
        public GridPosition FacingPosition => MoveDirections.Step(Position, Facing);

        /// <summary>这条一次性交互物是不是已经用过了。</summary>
        /// <remarks>
        /// 两个来源：本次会话记下的（<c>_used</c>），与剧情账本里的
        /// <see cref="InteractableFlags"/> 记录。账本那一份跨会话、跨地图、进存档——
        /// 没有它，开过的箱子出镇再回来就会复活。
        ///
        /// 账本说用过就把 ID 并进 <c>_used</c>：同一条交互物在一次会话里只问账本一次，
        /// 没有账本时（<see cref="MissingExplorationStateSource"/> 每个键只警告一次）也不会反复刷日志。
        /// 没有账本时读回 0，行为与从前一致：一次性只在本次进图内有效。
        /// </remarks>
        public bool HasUsed(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId))
            {
                return false;
            }

            if (_used.Contains(interactableId))
            {
                return true;
            }

            if (_state == null || _state.GetValue(InteractableFlags.KeyOf(interactableId)) == 0)
            {
                return false;
            }

            _used.Add(interactableId);
            return true;
        }

        /// <summary>
        /// 朝某方向走一格。
        /// </summary>
        /// <remarks>
        /// 顺序是固定的：先判遭遇挂起，再判越界，再判占格，走成之后更新朝向与步数、发事件，
        /// <b>最后</b>才掷遭遇。也就是「这一步先发生，再决定要不要遇敌」。
        /// </remarks>
        public MoveResult TryMove(MoveDirection direction)
        {
            var from = Position;

            if (IsEncounterPending)
            {
                return MoveResult.Rejected(direction, from, MoveRejection.EncounterPending);
            }

            var target = MoveDirections.Step(from, direction);
            if (!Grid.IsInside(target))
            {
                return MoveResult.Rejected(direction, from, MoveRejection.OutOfBounds);
            }

            if (!Grid.IsWalkable(target))
            {
                return MoveResult.Rejected(direction, from, MoveRejection.Blocked);
            }

            Position = target;
            Facing = direction;
            StepCount++;

            Publish(new PlayerMovedEvent(MapId, from, target, StepCount));
            RollEncounter();

            return MoveResult.Success(direction, from, target);
        }

        /// <summary>
        /// 对面朝的那一格交互。
        /// </summary>
        /// <remarks>
        /// 玩家永远站在交互物的<b>旁边</b>而不是上面：交互物占格且不可进入，所以「面向」是必要条件，
        /// 这也是 <see cref="Facing"/> 存在的理由。
        /// </remarks>
        public InteractionResult TryInteract()
        {
            var cell = FacingPosition;

            if (IsEncounterPending)
            {
                return InteractionResult.Rejected(InteractionRejection.EncounterPending, cell);
            }

            var interactable = Grid.InteractableAt(cell);
            if (interactable == null)
            {
                // 分两种「没反应」：那一格确实空着，还是有个条件未满足而藏起来的物件。
                // 原因不同，界面给的反馈也不同（没东西 vs 打不开），所以这里多查一次数据。
                var hidden = FindHiddenAt(cell);
                return InteractionResult.Rejected(
                    hidden == null ? InteractionRejection.NoTarget : InteractionRejection.Hidden,
                    cell);
            }

            if (!InteractableConditions.MeetsRequirement(interactable, _state))
            {
                return InteractionResult.Rejected(InteractionRejection.RequirementNotMet, cell);
            }

            if (interactable.OneShot && HasUsed(interactable.Id))
            {
                return InteractionResult.Rejected(InteractionRejection.AlreadyUsed, cell);
            }

            if (interactable.OneShot)
            {
                // 先记账再发事件：订阅方（开箱、发奖励）不必自己防重入。
                _used.Add(interactable.Id);
            }

            // 判据是数据不是类型键：targetId 只要是合法地图 ID，这次交互就是换图请求。
            var requestsMapChange = IdRules.IsValidId(DefinitionKind.Map, interactable.TargetId);

            Publish(new InteractionTriggeredEvent(
                interactable.Id,
                interactable.InteractionTypeKey,
                interactable.TargetId,
                cell,
                interactable.OneShot));

            if (requestsMapChange)
            {
                Publish(new MapChangeRequestedEvent(MapId, interactable.TargetId, interactable.Id, cell));
            }

            return InteractionResult.Success(
                interactable.Id,
                interactable.InteractionTypeKey,
                interactable.TargetId,
                cell,
                interactable.OneShot,
                requestsMapChange);
        }

        /// <summary>
        /// 了结挂在身上的那次遭遇。战斗结束（胜、负、逃）之后必须调一次，否则玩家走不动。
        /// </summary>
        public void ResolveEncounter()
        {
            if (!IsEncounterPending)
            {
                return;
            }

            GameLog.Info(
                LogChannel.Exploration,
                $"地图 {MapId} 的遭遇 {PendingEncounterId} 已了结，玩家可以在 {Position} 继续走动。",
                MapId);

            PendingEncounterId = null;
        }

        /// <summary>
        /// 按当前剧情状态重建可行走格。
        /// </summary>
        /// <remarks>
        /// 触发点是「条件键变了」（剧情侧发的那条事件），界面在同一条事件里自行重画，
        /// 所以这里不发新事件——否则一次开门会引出两条刷新。
        /// </remarks>
        public void RefreshVisibility()
        {
            var before = Grid;
            Grid = ExplorationGrid.Build(Map, _interactables, _state);

            if (!Grid.IsWalkable(Position))
            {
                GameLog.Warn(
                    LogChannel.Exploration,
                    $"地图 {MapId} 的 {Position} 在新一层可见性下被交互物占住（玩家站在了刚出现的物件上），位置保持不变。",
                    MapId);
            }

            if (before.VisibleInteractables.Count != Grid.VisibleInteractables.Count)
            {
                GameLog.Info(
                    LogChannel.Exploration,
                    $"地图 {MapId} 的可见交互物 {before.VisibleInteractables.Count} -> {Grid.VisibleInteractables.Count}。",
                    MapId);
            }
        }

        /// <summary>掷一次遭遇。掷中只挂起并报告，开战由 Flow 侧接线决定。</summary>
        private void RollEncounter()
        {
            if (Map == null || _stream == null)
            {
                return;
            }

            // 安全区与城镇是「不该有野外战斗」的地方，即便表里填了概率也不掷——
            // 这条比数据校验更硬：校验只警告，运行期必须真的不发生。
            if (Map.IsSafeZone)
            {
                return;
            }

            var rate = Map.EncounterRate;
            var encounterId = Map.EncounterTableId;
            if (rate <= 0f || string.IsNullOrEmpty(encounterId))
            {
                return;
            }

            EncounterRollCount++;

            if (!_stream.Chance(rate))
            {
                return;
            }

            PendingEncounterId = encounterId;

            GameLog.Info(
                LogChannel.Exploration,
                $"地图 {MapId} 在 {Position} 触发遭遇 {encounterId}（走了 {StepCount} 步，遭遇率 {rate}）。",
                MapId);

            Publish(new EncounterTriggeredEvent(MapId, encounterId, Position, StepCount));
        }

        private InteractableDefinition FindHiddenAt(GridPosition cell)
        {
            for (var i = 0; i < _interactables.Count; i++)
            {
                var interactable = _interactables[i];
                if (interactable == null || interactable.GridX != cell.X || interactable.GridY != cell.Y)
                {
                    continue;
                }

                if (!string.Equals(interactable.MapId, MapId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!InteractableConditions.IsVisible(interactable, _state))
                {
                    return interactable;
                }
            }

            return null;
        }

        private void Publish<T>(T gameEvent) where T : IGameEvent
        {
            _bus?.Publish(ExplorationEventChannel.Channel, gameEvent);
        }

        public override string ToString() =>
            "explore " + MapId + " at " + Position + " facing " + Facing + " steps=" + StepCount +
            (IsEncounterPending ? " (pending " + PendingEncounterId + ")" : string.Empty);
    }
}
