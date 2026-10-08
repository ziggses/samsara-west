using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 交互物的条件判定。抽成静态类是因为三处都要问同一个问题：
    /// 网格（挡不挡路）、会话（让不让交互）、界面（画不画提示）。
    /// </summary>
    public static class InteractableConditions
    {
        /// <summary>
        /// 条件是否满足。没有条件键的交互物恒为满足。
        /// </summary>
        public static bool MeetsRequirement(InteractableDefinition interactable, IExplorationStateSource state)
        {
            if (interactable == null)
            {
                return false;
            }

            var key = interactable.RequiredStateKey;
            if (string.IsNullOrEmpty(key))
            {
                return true;
            }

            var value = state == null ? 0 : state.GetValue(key);
            return Compare(value, interactable.RequiredValue, interactable.RequiredOperator);
        }

        /// <summary>
        /// 是否可见。条件不满足时：标了「条件满足前隐藏」的看不见，没标的照旧看得见（只是不让交互）。
        /// </summary>
        public static bool IsVisible(InteractableDefinition interactable, IExplorationStateSource state)
        {
            if (interactable == null)
            {
                return false;
            }

            if (MeetsRequirement(interactable, state))
            {
                return true;
            }

            return !interactable.IsHiddenUntilConditionMet;
        }

        /// <summary>按 <c>requiredOperator</c> 比较。未知运算符一律判不满足——坏数据不该被当成放行。</summary>
        public static bool Compare(int value, int target, CompareOperator comparison)
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
    }

    /// <summary>
    /// 一张地图的可行走格。
    /// </summary>
    /// <remarks>
    /// <b>可行走格从哪来</b>：地图矩形范围内的格子，减去被可见交互物占住的格子。
    /// 本轮不引入地形／路障字段（<c>maps.csv</c> 没有这类列，场地规则的字段设计另行排期，
    /// 见 <c>Docs/待拍板清单.md</c>），所以「断桥缺格」「裂痕不可跨越」这类需求今天表达不出来，
    /// 登记为未决，而不是先在代码里挖一个没人填的口子。
    ///
    /// 可见性依赖剧情状态，而状态会变，因此网格是一件<b>可重建的快照</b>：
    /// 找 <see cref="ExplorationSession.RefreshVisibility"/> 重建它，而不是让它可以被改。
    /// </remarks>
    public sealed class ExplorationGrid
    {
        private static readonly InteractableDefinition[] NoInteractables = Array.Empty<InteractableDefinition>();

        private readonly bool[] _blocked;
        private readonly List<InteractableDefinition>[] _byCell;
        private readonly List<InteractableDefinition> _visible = new List<InteractableDefinition>();
        private readonly IExplorationStateSource _state;

        private ExplorationGrid(
            MapDefinition map,
            int width,
            int height,
            IExplorationStateSource state)
        {
            MapId = map == null ? null : map.Id;
            Width = width;
            Height = height;
            _state = state;
            _blocked = new bool[width * height];
            _byCell = new List<InteractableDefinition>[width * height];
        }

        /// <summary>地图 ID。地图定义缺失时为空串。</summary>
        public string MapId { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>当前可见的交互物，按数据给的顺序。被条件藏起来的、以及越界的坏数据都不在其中。</summary>
        public IReadOnlyList<InteractableDefinition> VisibleInteractables => _visible;

        /// <summary>被可见交互物占住的格子数。诊断用：它应当等于可见交互物里去重后的占格数。</summary>
        public int BlockedCellCount { get; private set; }

        /// <summary>建网格时被忽略的坏数据条数（越界、或 <c>mapId</c> 对不上）。</summary>
        public int IgnoredInteractableCount { get; private set; }

        /// <summary>
        /// 按地图定义与它名下的交互物建一张网格。
        /// </summary>
        /// <param name="map">地图定义。</param>
        /// <param name="interactables">候选交互物；不属于这张图的会被忽略并计入 <see cref="IgnoredInteractableCount"/>。</param>
        /// <param name="state">剧情状态读取源；传 null 等价于「所有条件键都是 0」。</param>
        public static ExplorationGrid Build(
            MapDefinition map,
            IEnumerable<InteractableDefinition> interactables,
            IExplorationStateSource state)
        {
            var width = map == null ? 0 : Math.Max(0, map.GridWidth);
            var height = map == null ? 0 : Math.Max(0, map.GridHeight);

            if (map == null)
            {
                GameLog.Error(LogChannel.Exploration, "建网格时地图定义为空，得到一张空网格。");
            }
            else if (width < 1 || height < 1)
            {
                GameLog.Error(
                    LogChannel.Exploration,
                    $"地图 {map.Id} 的网格尺寸为 {width}x{height}，没有任何可走格。",
                    map.Id);
            }

            var grid = new ExplorationGrid(map, width, height, state);

            if (interactables != null)
            {
                foreach (var interactable in interactables)
                {
                    grid.Place(interactable);
                }
            }

            return grid;
        }

        public bool IsInside(GridPosition position) =>
            position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;

        /// <summary>能不能走进去：在界内，且没有被可见交互物占着。</summary>
        public bool IsWalkable(GridPosition position) =>
            IsInside(position) && !_blocked[Index(position)];

        /// <summary>
        /// 离 <paramref name="anchor"/> 最近的可行走格。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么要有它</b>：换图的落点口径是「从这扇门进来，就出现在对面那扇门附近」——
        /// 也就是把门在<b>来源图</b>的坐标，拿到<b>目标图</b>上找最近的一个能站的地方。
        /// 目标图比来源图小的时候那个坐标会越界，所以先夹进边界，再从内向外搜。</para>
        /// <para><b>先夹再夹</b>：夹过的格若本身可走就直接用它，这是绝大多数情况的答案，
        /// 也让「两张图同坐标都空着」这种最常见的门不必进搜索。</para>
        /// <para><b>为什么是切比雪夫环</b>：环上任意一格到锚点的「走几步」都相同（八方向计数），
        /// 内外层天然有序，不必算浮点距离。环内取格的顺序是固定的
        /// （先四个正方向：上 → 右 → 下 → 左；再扫环上其余格：上边左到右 → 右边上到下 → 下边右到左 → 左边下到上），
        /// 所以同一张网格配同一个锚点永远给同一个答案：测试与存档都靠这条确定性。</para>
        /// <para>整张图没有任何可走格时记错误日志并退回夹过的锚点——那已经不是「就近」能解决的问题，
        /// 而返回原点会让错误更难看出来。</para>
        /// </remarks>
        public GridPosition NearestWalkable(GridPosition anchor)
        {
            if (Width < 1 || Height < 1)
            {
                return GridPosition.Origin;
            }

            var clamped = new GridPosition(
                Clamp(anchor.X, Width),
                Clamp(anchor.Y, Height));

            if (IsWalkable(clamped))
            {
                return clamped;
            }

            var maxRing = Math.Max(Width, Height);
            for (var ring = 1; ring <= maxRing; ring++)
            {
                var found = FindOnRing(clamped, ring);
                if (found.HasValue)
                {
                    return found.Value;
                }
            }

            GameLog.Error(
                LogChannel.Exploration,
                $"地图 {MapId} 上没有任何可走格（{Width}x{Height} 全被交互物占着），换图落点退回 {clamped}。",
                MapId);
            return clamped;
        }

        /// <summary>把锚点夹进 [0, size-1]。锚点为负或越界都落到最近的边界格上。</summary>
        private static int Clamp(int value, int size) => value < 0 ? 0 : (value >= size ? size - 1 : value);

        /// <summary>
        /// 在距 <paramref name="center"/> 切比雪夫距离为 <paramref name="ring"/> 的那一圈上找第一个可走格。
        /// 环上取格顺序固定（见 <see cref="NearestWalkable"/> 的说明），越界的格由 <see cref="IsWalkable"/> 挡掉。
        /// </summary>
        private GridPosition? FindOnRing(GridPosition center, int ring)
        {
            // 先看四个正方向。「站在门正上方」比「站在门口斜角」更像玩家预期的落点，
            // 而斜角那一圈是过一遍筛选就能拿到的，不值得为它让正方向排在后面。
            var up = new GridPosition(center.X, center.Y + ring);
            if (IsWalkable(up))
            {
                return up;
            }

            var rightward = new GridPosition(center.X + ring, center.Y);
            if (IsWalkable(rightward))
            {
                return rightward;
            }

            var down = new GridPosition(center.X, center.Y - ring);
            if (IsWalkable(down))
            {
                return down;
            }

            var leftward = new GridPosition(center.X - ring, center.Y);
            if (IsWalkable(leftward))
            {
                return leftward;
            }

            var left = center.X - ring;
            var right = center.X + ring;
            var bottom = center.Y - ring;
            var top = center.Y + ring;

            // 再扫环上剩下的格（斜角，以及 ring > 1 时的中间格）。
            // 上边：左 → 右
            for (var x = left; x <= right; x++)
            {
                var position = new GridPosition(x, top);
                if (IsWalkable(position))
                {
                    return position;
                }
            }

            // 右边：上 → 下（角已在上边查过）
            for (var y = top - 1; y >= bottom; y--)
            {
                var position = new GridPosition(right, y);
                if (IsWalkable(position))
                {
                    return position;
                }
            }

            // 下边：右 → 左
            for (var x = right - 1; x >= left; x--)
            {
                var position = new GridPosition(x, bottom);
                if (IsWalkable(position))
                {
                    return position;
                }
            }

            // 左边：下 → 上（两端角已查过）
            for (var y = bottom + 1; y <= top - 1; y++)
            {
                var position = new GridPosition(left, y);
                if (IsWalkable(position))
                {
                    return position;
                }
            }

            return null;
        }

        /// <summary>这一格上的可见交互物；没有则返回 null。同格多条时给第一条（建场时会警告）。</summary>
        public InteractableDefinition InteractableAt(GridPosition position)
        {
            if (!IsInside(position))
            {
                return null;
            }

            var list = _byCell[Index(position)];
            return list == null || list.Count == 0 ? null : list[0];
        }

        /// <summary>这一格上的全部可见交互物；没有则返回空数组。</summary>
        public IReadOnlyList<InteractableDefinition> InteractablesAt(GridPosition position)
        {
            if (!IsInside(position))
            {
                return NoInteractables;
            }

            var list = _byCell[Index(position)];
            return list == null || list.Count == 0 ? NoInteractables : list;
        }

        /// <summary>重建后的可见性判定，供诊断与测试复用。</summary>
        public bool IsVisible(InteractableDefinition interactable) =>
            InteractableConditions.IsVisible(interactable, _state);

        public override string ToString() =>
            "grid " + MapId + " " + Width + "x" + Height + " visible=" + _visible.Count + " blocked=" + BlockedCellCount;

        private void Place(InteractableDefinition interactable)
        {
            if (interactable == null)
            {
                return;
            }

            if (MapId != null && !string.Equals(interactable.MapId, MapId, StringComparison.Ordinal))
            {
                IgnoredInteractableCount++;
                GameLog.Warn(
                    LogChannel.Exploration,
                    $"交互物 {interactable.Id} 属于地图 {interactable.MapId}，不属于 {MapId}，建网格时已忽略。",
                    interactable.Id);
                return;
            }

            var position = new GridPosition(interactable.GridX, interactable.GridY);
            if (!IsInside(position))
            {
                IgnoredInteractableCount++;
                GameLog.Error(
                    LogChannel.Exploration,
                    $"交互物 {interactable.Id} 落在 {position}，超出地图 {MapId} 的 {Width}x{Height} 范围，已忽略。",
                    interactable.Id);
                return;
            }

            if (!InteractableConditions.IsVisible(interactable, _state))
            {
                // 条件没满足且要求隐藏：它既不该被画出来，也不该挡路。
                return;
            }

            var index = Index(position);
            var list = _byCell[index];
            if (list == null)
            {
                list = new List<InteractableDefinition>(1);
                _byCell[index] = list;
            }
            else
            {
                GameLog.Warn(
                    LogChannel.Exploration,
                    $"地图 {MapId} 的 {position} 上已有交互物 {list[0].Id}，又放下 {interactable.Id}：两个都会留着，交互取第一条，请核对表。",
                    interactable.Id);
            }

            if (!_blocked[index])
            {
                _blocked[index] = true;
                BlockedCellCount++;
            }

            list.Add(interactable);
            _visible.Add(interactable);
        }

        private int Index(GridPosition position) => (position.Y * Width) + position.X;
    }
}
