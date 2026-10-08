using System;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「探索遇敌 → 进战斗」的接线。整条链只有这里同时认识探索与战斗。
    /// </summary>
    /// <remarks>
    /// 为什么必须有一个接线处：探索只依赖 Core 与 Data（ADR-021），它发
    /// <see cref="EncounterTriggeredEvent"/> 但无权开战；战斗也不该反过来知道路边遇敌。
    /// Flow 是组合根，两边都认识，于是这条链落在这里，而不是落进任何一个模块内部——
    /// 否则探索就要引用战斗，模块边界会从编译期约束退化成一句口号。
    ///
    /// 两件事值得单独记住：
    /// <list type="bullet">
    /// <item><description><b>遭遇 ID 当场失效就不开战</b>：查不到遭遇定义、或队伍为空、或入场清单不合法时，
    /// 记错误日志并且<b>不了结</b>那次遭遇——留着它，玩家能看见「这里卡住了」，
    /// 而不是带着一个静默消失的遇敌继续走。</description></item>
    /// <item><description><b>战斗结束才清遭遇</b>：订阅 <see cref="BattleEndedEvent"/> 回调
    /// <see cref="ExplorationSession.ResolveEncounter"/>。漏了这一步，玩家会在原地一步都走不动。</description></item>
    /// </list>
    /// </remarks>
    public sealed class ExplorationBattleLink : IDisposable
    {
        private readonly IBattleService _battle;
        private readonly IDefinitionRegistry _definitions;
        private readonly IExplorationService _exploration;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="battle">战斗服务。</param>
        /// <param name="definitions">定义目录，用来查遭遇与取队伍。</param>
        /// <param name="bus">事件总线。</param>
        /// <param name="exploration">
        /// 探索服务，用来在战斗结束后了结遭遇。允许为空（不接探索时这条链只做「遇敌 → 开战」）。
        /// </param>
        public ExplorationBattleLink(
            IBattleService battle,
            IDefinitionRegistry definitions,
            IEventBus bus,
            IExplorationService exploration = null)
        {
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _exploration = exploration;

            _subscriptions.Add(bus.Subscribe<EncounterTriggeredEvent>(
                ExplorationEventChannel.Channel,
                OnEncounterTriggered));
            _subscriptions.Add(bus.Subscribe<BattleEndedEvent>(
                BattleEventChannel.Channel,
                OnBattleEnded));
        }

        /// <summary>这条接线一共开出过几场战斗。诊断与测试用。</summary>
        public int BattlesStarted { get; private set; }

        /// <summary>忽略掉的遇敌次数（已有战斗在进行、或数据查不到）。</summary>
        public int IgnoredEncounters { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnEncounterTriggered(EncounterTriggeredEvent gameEvent)
        {
            if (_battle.HasActiveBattle)
            {
                IgnoredEncounters++;
                GameLog.Warn(
                    LogChannel.Flow,
                    $"地图 {gameEvent.MapId} 触发遭遇 {gameEvent.EncounterId} 时已有一场战斗在进行，本次遇敌不予处理（战斗结束后请了结挂在探索上的遭遇）。",
                    gameEvent.EncounterId);
                return;
            }

            if (!_definitions.TryGet(gameEvent.EncounterId, out EncounterDefinition encounter) || encounter == null)
            {
                IgnoredEncounters++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"遭遇 {gameEvent.EncounterId} 不在定义目录里，无法开战。请核对 maps.csv 的 encounterTableId。",
                    gameEvent.EncounterId);
                return;
            }

            // 骨架期的队伍口径：目录里全部可操作角色（BattleFactory.DefaultParty）。
            // 存档的队伍与背包接上之后，换的是这一行，不是这条链。
            var party = BattleFactory.DefaultParty(_definitions);
            if (party.Count == 0)
            {
                IgnoredEncounters++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"定义目录里没有可操作角色，遭遇 {gameEvent.EncounterId} 无法开战。",
                    gameEvent.EncounterId);
                return;
            }

            var setup = BattleFactory.FromEncounter(encounter, party);
            if (!setup.Validate(out var error))
            {
                IgnoredEncounters++;
                GameLog.Error(
                    LogChannel.Flow,
                    $"遭遇 {gameEvent.EncounterId} 的入场清单不合法（{error}），无法开战。",
                    gameEvent.EncounterId);
                return;
            }

            _battle.StartBattle(setup);
            BattlesStarted++;

            GameLog.Info(
                LogChannel.Flow,
                $"地图 {gameEvent.MapId} 的遇敌 {gameEvent.EncounterId} 已开战：我方 {setup.Party.Count} 人、敌方 {setup.Enemies.Count} 人。",
                gameEvent.EncounterId);
        }

        private void OnBattleEnded(BattleEndedEvent gameEvent)
        {
            var session = _exploration == null ? null : _exploration.Current;
            if (session == null || !session.IsEncounterPending)
            {
                return;
            }

            session.ResolveEncounter();
        }
    }
}
