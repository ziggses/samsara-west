using SamsaraWest.Core;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 探索事件。全部走 <see cref="EventChannel.Exploration"/>，
    /// 界面／音频／Flow 只订阅，不反向读会话内部状态。
    /// </summary>
    public static class ExplorationEventChannel
    {
        public const EventChannel Channel = EventChannel.Exploration;
    }

    /// <summary>进入一张地图：地图换掉的那一刻，位置、尺寸与遭遇编排一起给出。</summary>
    public readonly struct MapEnteredEvent : IGameEvent
    {
        public MapEnteredEvent(string mapId, GridPosition position, int gridWidth, int gridHeight, string encounterId)
        {
            MapId = mapId;
            Position = position;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            EncounterId = encounterId;
        }

        public string MapId { get; }

        public GridPosition Position { get; }

        public int GridWidth { get; }

        public int GridHeight { get; }

        /// <summary>这张图的野外遭遇 ID；没有配置时为空。</summary>
        public string EncounterId { get; }
    }

    /// <summary>玩家走了一格。</summary>
    /// <remarks>只带位置与步数，不带「走完发生了什么」——遇敌另有事件，两者顺序是固定的。</remarks>
    public readonly struct PlayerMovedEvent : IGameEvent
    {
        public PlayerMovedEvent(string mapId, GridPosition from, GridPosition to, int stepCount)
        {
            MapId = mapId;
            From = from;
            To = to;
            StepCount = stepCount;
        }

        public string MapId { get; }

        public GridPosition From { get; }

        public GridPosition To { get; }

        /// <summary>本次进图后的累计步数（含这一步）。</summary>
        public int StepCount { get; }
    }

    /// <summary>
    /// 触发了一条交互物。
    /// </summary>
    /// <remarks>
    /// 它只报告「谁、在哪、朝向什么目标」，不含效果：开箱给什么、对话说什么、换图去哪，
    /// 分别由经济／剧情／Flow 侧订阅后决定。一次性交互物在事件发出<b>之前</b>就已记入「用过了」，
    /// 因此订阅方不必自己防重入。
    /// </remarks>
    public readonly struct InteractionTriggeredEvent : IGameEvent
    {
        public InteractionTriggeredEvent(
            string interactableId,
            string interactionTypeKey,
            string targetId,
            GridPosition position,
            bool oneShot)
        {
            InteractableId = interactableId;
            InteractionTypeKey = interactionTypeKey;
            TargetId = targetId;
            Position = position;
            OneShot = oneShot;
        }

        public string InteractableId { get; }

        public string InteractionTypeKey { get; }

        public string TargetId { get; }

        public GridPosition Position { get; }

        public bool OneShot { get; }
    }

    /// <summary>
    /// 交互的结果是「换一张地图」。
    /// </summary>
    /// <remarks>
    /// 判据是数据而非类型键：<c>targetId</c> 只要是合法地图 ID（<c>CH01_MAP01</c>），
    /// 这次交互就是换图请求。这样加一种「传送」的写法不必改内核。
    /// 探索自己<b>不</b>换图——谁来加载场景、位置落在哪，是 Flow 的事。
    /// <para>
    /// <b>为什么带上门的坐标</b>：Flow 定的落点口径是「目标图里离这扇门最近的可行走格」，
    /// 不知道门在哪就算不出落点。发事件的一方知道这件事而订阅方不知道，
    /// 那就该由发的一方带上——少带这一个字段，订阅方就只剩「随便落在哪」一种选择。
    /// </para>
    /// </remarks>
    public readonly struct MapChangeRequestedEvent : IGameEvent
    {
        public MapChangeRequestedEvent(
            string fromMapId,
            string toMapId,
            string interactableId,
            GridPosition position)
        {
            FromMapId = fromMapId;
            ToMapId = toMapId;
            InteractableId = interactableId;
            Position = position;
        }

        public string FromMapId { get; }

        public string ToMapId { get; }

        public string InteractableId { get; }

        /// <summary>这扇门在<b>来源图</b>上的格子（也就是玩家面朝的那一格）。</summary>
        public GridPosition Position { get; }
    }

    /// <summary>
    /// 野外遭遇被掷中了。
    /// </summary>
    /// <remarks>
    /// 探索只掷骰与报告，<b>不开战</b>：探索模块只依赖 Core 与 Data（见 ADR-021），
    /// 它不认识 <c>IBattleService</c>。开战由 Flow 的 <c>ExplorationBattleLink</c> 订阅本事件后发起，
    /// 战斗结束后再回来调 <c>ExplorationSession.ResolveEncounter()</c> 了结这次遭遇。
    /// </remarks>
    public readonly struct EncounterTriggeredEvent : IGameEvent
    {
        public EncounterTriggeredEvent(string mapId, string encounterId, GridPosition position, int stepCount)
        {
            MapId = mapId;
            EncounterId = encounterId;
            Position = position;
            StepCount = stepCount;
        }

        public string MapId { get; }

        public string EncounterId { get; }

        public GridPosition Position { get; }

        /// <summary>触发时本次进图的累计步数，用于校算「走了几步撞上」。</summary>
        public int StepCount { get; }
    }
}
