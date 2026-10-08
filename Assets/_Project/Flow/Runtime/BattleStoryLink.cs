using System;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Narrative;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「战斗结局 → 剧情账本」的接线：打完一场就把战果记下来。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要这一层：战斗内核会写下结局并广播 <see cref="BattleEndedEvent"/>，
    /// 但它<b>不认识账本</b>（连 Narrative 都不引用）；账本也<b>不认识战斗</b>。
    /// 两边都认识的地方只有组合根，所以这条订阅落在这里，与另四条 Link 同一种写法。</para>
    ///
    /// <para>记的是<b>事实</b>，不是奖励：账本里写下「这一场赢了」就到此为止。
    /// 掉什么、给多少钱属于战利品与经济，那两块还没有运行期真源——这一层<b>不发明数值</b>，
    /// 需要奖励时由内容侧（战利品表、任务奖励）去读这个键。</para>
    ///
    /// <para>刻意<b>不</b>在这里丢弃战斗会话（<c>IBattleService.EndBattle()</c>）：内核的注释说得很清楚，
    /// 那是「战后结算完成、<b>进入下一段剧情时</b>」该做的事，而现在还没有「下一段剧情」；
    /// 战斗面板也还在读 <c>Current</c> 显示结局，提前丢掉会让玩家看不到自己是怎么赢的。</para>
    ///
    /// <para>这条线同样<b>只写不读</b>：读它的是内容表里的条件，以及将来的对话／任务。</para>
    /// </remarks>
    public sealed class BattleStoryLink : IDisposable
    {
        private readonly IStoryState _state;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="state">剧情状态账；空表示这条线不接账本（战斗照旧打完，只是记不下来）。</param>
        public BattleStoryLink(IEventBus bus, IStoryState state)
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
                    "战果回写线没有拿到剧情状态账，战斗结局将只在本次运行内有效。");
            }

            _subscriptions.Add(bus.Subscribe<BattleEndedEvent>(
                BattleEventChannel.Channel,
                OnBattleEnded));
        }

        /// <summary>记进账本几条战果。诊断与测试用（同一场重复广播不会重复计数）。</summary>
        public int OutcomesRecorded { get; private set; }

        /// <summary>没有遭遇 ID、因而无处记账的战果条数（手工构造的战斗会走到这里）。</summary>
        public int UnattributedOutcomes { get; private set; }

        /// <summary>广播来的结局不是一个收场（例如仗还没打完）的条数。</summary>
        public int UnrecognisedOutcomes { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnBattleEnded(BattleEndedEvent gameEvent)
        {
            if (_state == null)
            {
                return;
            }

            var key = BattleFlags.OutcomeKey(gameEvent.EncounterId, gameEvent.Outcome);
            if (key.Length == 0)
            {
                if (string.IsNullOrEmpty(gameEvent.EncounterId))
                {
                    UnattributedOutcomes++;
                    GameLog.Warn(
                        LogChannel.Flow,
                        $"这场战斗没有遭遇 ID（结局 {gameEvent.Outcome}），战果无处记账。");
                }
                else
                {
                    UnrecognisedOutcomes++;
                    GameLog.Error(
                        LogChannel.Flow,
                        $"战斗以 {gameEvent.Outcome} 收场，它不是四种收场之一，战果不入账。",
                        gameEvent.EncounterId);
                }

                return;
            }

            if (_state.GetValue(key) != 0)
            {
                // 账本里已经有了：这一场在别的会话里打过，或者存档刚灌回来。
                return;
            }

            _state.SetValue(key, 1);
            OutcomesRecorded++;

            GameLog.Info(
                LogChannel.Flow,
                $"战果已记进剧情账本：{key}（第 {gameEvent.Rounds} 回合、累计 {gameEvent.ActionCount} 手）。",
                gameEvent.EncounterId);
        }
    }
}
