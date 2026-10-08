using System;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 地图格坐标。原点在左下角，X 向右、Y 向上。
    /// </summary>
    /// <remarks>
    /// 刻意不用 <c>Vector2Int</c>：探索运行时不认识渲染层的世界坐标与浮点数，
    /// 一份格坐标就是两个整数。越界判定、占格索引与将来的存档位置读写只关心这两个数，
    /// 等界面层把格子换算成像素时再乘 <c>maps.csv</c> 里的瓦片尺寸即可。
    /// </remarks>
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public static GridPosition Origin => new GridPosition(0, 0);

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ Y;

        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);

        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);

        /// <summary>诊断串一律 ASCII：日志与界面脚本都不写中文字面量。</summary>
        public override string ToString() => "(" + X + "," + Y + ")";
    }

    /// <summary>
    /// 四方向。数值顺序即「上、右、下、左」，与 <see cref="GridPosition"/> 的 Y 向上口径一致。
    /// </summary>
    public enum MoveDirection
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    /// <summary>方向与格坐标之间的换算。界面画方向键、内核判落位都走这里，避免各处自己写 +1/−1。</summary>
    public static class MoveDirections
    {
        /// <summary>顺时针一圈，顺序与枚举一致。遍历四个方向时不要另抄一份。</summary>
        public static readonly MoveDirection[] All =
        {
            MoveDirection.North,
            MoveDirection.East,
            MoveDirection.South,
            MoveDirection.West,
        };

        public static int OffsetX(MoveDirection direction)
        {
            switch (direction)
            {
                case MoveDirection.East:
                    return 1;
                case MoveDirection.West:
                    return -1;
                default:
                    return 0;
            }
        }

        public static int OffsetY(MoveDirection direction)
        {
            switch (direction)
            {
                case MoveDirection.North:
                    return 1;
                case MoveDirection.South:
                    return -1;
                default:
                    return 0;
            }
        }

        public static MoveDirection Opposite(MoveDirection direction)
        {
            switch (direction)
            {
                case MoveDirection.North:
                    return MoveDirection.South;
                case MoveDirection.South:
                    return MoveDirection.North;
                case MoveDirection.East:
                    return MoveDirection.West;
                default:
                    return MoveDirection.East;
            }
        }

        /// <summary>从一格朝某方向走一步得到的坐标（不判定是否合法）。</summary>
        public static GridPosition Step(GridPosition from, MoveDirection direction) =>
            new GridPosition(from.X + OffsetX(direction), from.Y + OffsetY(direction));
    }

    /// <summary>这一步为什么没走成。</summary>
    public enum MoveRejection
    {
        /// <summary>走成了。</summary>
        None = 0,

        /// <summary>目标格在地图之外。</summary>
        OutOfBounds = 1,

        /// <summary>目标格被可见的交互物占着。</summary>
        Blocked = 2,

        /// <summary>上一场遭遇还没了结：战斗未结束，或结束时忘了调用 ResolveEncounter。</summary>
        EncounterPending = 3,
    }

    /// <summary>一次移动的结算结果。拒绝不是一个事件，而是返回值——调用方（界面）要立刻给出反馈。</summary>
    public readonly struct MoveResult
    {
        private MoveResult(bool moved, MoveDirection direction, GridPosition from, GridPosition to, MoveRejection rejection)
        {
            Moved = moved;
            Direction = direction;
            From = from;
            To = to;
            Rejection = rejection;
        }

        public bool Moved { get; }

        public MoveDirection Direction { get; }

        /// <summary>移动前的位置。被拒绝时它也指向玩家当前所在的那一格。</summary>
        public GridPosition From { get; }

        /// <summary>移动后的位置；被拒绝时等于 <see cref="From"/>。</summary>
        public GridPosition To { get; }

        public MoveRejection Rejection { get; }

        public static MoveResult Success(MoveDirection direction, GridPosition from, GridPosition to) =>
            new MoveResult(true, direction, from, to, MoveRejection.None);

        public static MoveResult Rejected(MoveDirection direction, GridPosition from, MoveRejection rejection) =>
            new MoveResult(false, direction, from, from, rejection);

        public override string ToString() =>
            Moved
                ? "move " + Direction + " " + From + " -> " + To
                : "move " + Direction + " rejected at " + From + " (" + Rejection + ")";
    }

    /// <summary>这次交互为什么没成。</summary>
    public enum InteractionRejection
    {
        /// <summary>触发了。</summary>
        None = 0,

        /// <summary>面朝的那一格上没有任何可见交互物。</summary>
        NoTarget = 1,

        /// <summary>条件没满足，且数据要求「条件满足前隐藏」——看不见的东西也不该能交互。</summary>
        Hidden = 2,

        /// <summary>条件没满足：物件看得见，但还不让动。</summary>
        RequirementNotMet = 3,

        /// <summary>一次性交互物已经用过了。</summary>
        AlreadyUsed = 4,

        /// <summary>上一场遭遇还没了结。</summary>
        EncounterPending = 5,
    }

    /// <summary>
    /// 一次交互的结算结果。
    /// </summary>
    /// <remarks>
    /// 触发只说明「这件事发生了」，不说明「做了什么」：具体效果由订阅
    /// <see cref="InteractionTriggeredEvent"/> 的模块决定（对话、开箱、换图）。
    /// 换图这一条因为完全由数据决定（<c>targetId</c> 是合法地图 ID），所以在这里带出来。
    /// </remarks>
    public readonly struct InteractionResult
    {
        private InteractionResult(
            InteractionRejection rejection,
            string interactableId,
            string interactionTypeKey,
            string targetId,
            GridPosition position,
            bool oneShot,
            bool requestsMapChange)
        {
            Rejection = rejection;
            InteractableId = interactableId;
            InteractionTypeKey = interactionTypeKey;
            TargetId = targetId;
            Position = position;
            OneShot = oneShot;
            RequestsMapChange = requestsMapChange;
        }

        public bool Triggered => Rejection == InteractionRejection.None;

        public InteractionRejection Rejection { get; }

        /// <summary>触发的交互物 ID；被拒绝时为空。</summary>
        public string InteractableId { get; }

        /// <summary>交互类型键（如 <c>interact.chest</c>）。</summary>
        public string InteractionTypeKey { get; }

        /// <summary>目标 ID：对话节点、任务 ID 或目标地图 ID。</summary>
        public string TargetId { get; }

        /// <summary>交互物所在的那一格（也就是玩家面朝的那一格）。</summary>
        public GridPosition Position { get; }

        /// <summary>这条交互物是否一次性（触发后已记入「用过了」）。</summary>
        public bool OneShot { get; }

        /// <summary><see cref="TargetId"/> 是一张合法地图 ⇒ 这是一次换图请求。</summary>
        public bool RequestsMapChange { get; }

        /// <summary>要换去的地图 ID；不是换图请求时为 null。</summary>
        public string TargetMapId => RequestsMapChange ? TargetId : null;

        public static InteractionResult Success(
            string interactableId,
            string interactionTypeKey,
            string targetId,
            GridPosition position,
            bool oneShot,
            bool requestsMapChange) =>
            new InteractionResult(
                InteractionRejection.None,
                interactableId,
                interactionTypeKey,
                targetId,
                position,
                oneShot,
                requestsMapChange);

        public static InteractionResult Rejected(InteractionRejection rejection, GridPosition position) =>
            new InteractionResult(rejection, null, null, null, position, false, false);

        public override string ToString() =>
            Triggered
                ? "interact " + InteractableId + " (" + InteractionTypeKey + ") at " + Position
                : "interact rejected at " + Position + " (" + Rejection + ")";
    }
}
