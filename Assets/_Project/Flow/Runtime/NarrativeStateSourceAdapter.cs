using System;
using SamsaraWest.Exploration;
using SamsaraWest.Narrative;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 把剧情状态账转述成探索要的只读口 <see cref="IExplorationStateSource"/>。
    /// </summary>
    /// <remarks>
    /// 为什么要一个适配器，而不是让账本直接实现探索的接口：
    /// <c>Narrative</c> 只依赖 Core 与 Data（<c>ProjectSkeletonTests</c> 锁住），
    /// 它<b>不许</b>认识 <c>Exploration</c>；反过来 <c>Exploration</c> 也不许认识 <c>Narrative</c>。
    /// 两边都认识的那个地方是组合根，所以这层转述落在 <c>Flow</c>——
    /// 与「遇敌 → 开战」的 <c>ExplorationBattleLink</c> 是同一条理由。
    /// 适配器只转一个方法，探索侧因此看不到账本还有心念、快照这些别的东西。
    /// </remarks>
    public sealed class NarrativeStateSourceAdapter : IExplorationStateSource
    {
        private readonly IStoryState _state;

        public NarrativeStateSourceAdapter(IStoryState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public int GetValue(string stateKey) => _state.GetValue(stateKey);
    }
}
