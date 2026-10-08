using System;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Economy;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「战斗结局 → 战利品结算」的接线：打赢一场就把掉落表掷掉，钱与物进真正的钱袋与背包。
    /// </summary>
    /// <remarks>
    /// <para>它接在 <see cref="BattleStoryLink"/> 旁边，读同一个事件（<c>BattleEndedEvent</c>），
    /// 但做的是另一件事：那条线记<b>事实</b>（这一场赢了），这条线发<b>东西</b>（给了多少钱与物）。
    /// 分开的理由是失败方式不同——账本键写重了只是记错一件事，战利品发重了是玩家凭空多拿到钱。</para>
    ///
    /// <para><b>只有一种收场会结算：<c>PlayerVictory</c></b>。战败、逃跑，以及剧情撤退
    /// （<c>ForcedRetreat</c>）都不发东西——《战斗内核 v1》第 11.2 节已经把「撤退不结算奖励」
    /// 写成了口径，这条线是它第一次真的落地。撤退如果也发奖励，玩家会发现「打不过就退」
    /// 比「打赢」更划算，而那正是设计上要避免的事。</para>
    ///
    /// <para><b>同一场可以刷</b>：这里<b>不</b>做「一个遭遇只结算一次」的限制。探索的遭遇本来就是
    /// 随机可重复触发的，重复打赢拿到重复的掉落是这类游戏的常态；「一次性奖励」属于任务奖励，
    /// 不是战利品。真正的一次性问题由内核保证——<c>BattleSession</c> 的结局只有一个出口、
    /// 且幂等，所以同一场仗不会广播两次结局，也就不会发两遍东西。</para>
    ///
    /// <para><b>掷骰用的是掉落流</b>（<c>RandomStreams.Loot</c>），不是战斗流：两件事互不干扰——
    /// 改掉落表不会挪动战斗的判定序列，反之亦然（ADR-005 的流分离）。代价是：读档时只有母种子被
    /// 灌回去（ADR-025），掉落流<b>从头开始</b>，所以奖励是可复现的、但不是「接着上次继续」。
    /// 要精确续流得把 <c>SnapshotAll</c> 一并搬进存档，那是另一件事。</para>
    /// </remarks>
    public sealed class LootBattleLink : IDisposable
    {
        private readonly IDefinitionRegistry _definitions;
        private readonly IEconomyService _economy;
        private readonly IRandomService _random;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="definitions">定义目录，用来查遭遇、敌人与掉落表。</param>
        /// <param name="economy">钱袋与背包；空表示这条线不接（战斗照旧打完，只是没人收钱）。</param>
        /// <param name="random">随机服务；空则掉落表里「按权重抽」的部分发不出来（固定赏金照发）。</param>
        public LootBattleLink(
            IEventBus bus,
            IDefinitionRegistry definitions,
            IEconomyService economy = null,
            IRandomService random = null)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _definitions = definitions;
            _economy = economy;
            _random = random;

            if (_economy == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "战利品结算线没有拿到经济服务，打赢之后不会有任何东西入账。");
            }
            else if (_definitions == null || _random == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "战利品结算线缺少定义目录或随机服务，掉落只能按最低限度发出。");
            }

            _subscriptions.Add(bus.Subscribe<BattleEndedEvent>(
                BattleEventChannel.Channel,
                OnBattleEnded));
        }

        /// <summary>真的结算过的胜仗数（诊断与测试用）。</summary>
        public int Settlements { get; private set; }

        /// <summary>收场不是胜利、因而一分钱没发的战斗数。</summary>
        public int SkippedOutcomes { get; private set; }

        /// <summary>没有遭遇 ID、因而无从查起该发什么的战斗数（手工构造的战斗会走到这里）。</summary>
        public int UnattributedOutcomes { get; private set; }

        /// <summary>遭遇查不到、或结算过程里撞上数据问题的战斗数。</summary>
        public int UnresolvedSpoils { get; private set; }

        /// <summary>累计发出去的钱。</summary>
        public int GoldSettled { get; private set; }

        /// <summary>累计发出去的东西件数（同一个 ID 抽到两次算两件）。</summary>
        public int ItemsSettled { get; private set; }

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
            if (_economy == null)
            {
                return;
            }

            if (gameEvent.Outcome != BattleOutcome.PlayerVictory)
            {
                // 败、逃、剧情撤退都不发东西（第 11.2 节的口径）。
                SkippedOutcomes++;
                return;
            }

            if (string.IsNullOrEmpty(gameEvent.EncounterId))
            {
                UnattributedOutcomes++;
                GameLog.Warn(
                    LogChannel.Flow,
                    "这场战斗赢了，但它没有遭遇 ID，不知道该按谁的掉落表发东西。");
                return;
            }

            if (_definitions == null
                || !_definitions.TryGet(gameEvent.EncounterId, out DefinitionBase found)
                || !(found is EncounterDefinition encounter))
            {
                UnresolvedSpoils++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"遭遇 '{gameEvent.EncounterId}' 在定义目录里找不到，这一场的战利品无处结算。",
                    gameEvent.EncounterId);
                return;
            }

            // 掉落流是独立的一条：改掉落表不会挪动战斗的判定序列（ADR-005）。
            var stream = _random?.GetStream(RandomStreams.Loot);

            var report = new ValidationReport();
            var spoils = LootResolver.Resolve(encounter, _definitions, stream, report);
            if (report.ErrorCount > 0)
            {
                UnresolvedSpoils++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"结算 '{gameEvent.EncounterId}' 的战利品时遇到 {report.ErrorCount} 处数据问题，"
                    + "坏掉的那几条已跳过，其余照常发放。",
                    gameEvent.EncounterId);
            }

            _economy.AddGold(spoils.Gold);
            for (var i = 0; i < spoils.Items.Count; i++)
            {
                _economy.AddItem(spoils.Items[i].ItemId, spoils.Items[i].Count);
                ItemsSettled += spoils.Items[i].Count;
            }

            GoldSettled += spoils.Gold;
            Settlements++;

            GameLog.Info(
                LogChannel.Economy,
                $"{gameEvent.EncounterId} 的战利品已入账：{spoils}（身上现有 {_economy.Gold} 钱）。",
                gameEvent.EncounterId);

            if (spoils.Experience != 0)
            {
                // 不做成长就别假装发了：敌人的 experienceReward 有值，但还没有等级系统收它。
                GameLog.Info(
                    LogChannel.Economy,
                    $"这一场本该给 {spoils.Experience} 点经验，但还没有等级系统收它，暂不落账。");
            }
        }
    }
}
