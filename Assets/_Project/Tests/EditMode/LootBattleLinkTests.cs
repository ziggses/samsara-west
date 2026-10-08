using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Economy;
using SamsaraWest.Flow;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 结算接线：哪种收场发东西、发到哪去、什么情况下什么都不发。
    /// </summary>
    /// <remarks>
    /// 这里最关键的一条是<b>剧情撤退不发奖励</b>——《战斗内核 v1》第 11.2 节早就把
    /// 「撤退不结算奖励」写成了口径，但在本轮之前它只是一句话。若撤退也发东西，
    /// 玩家会发现「打不过就退」比「打赢」更划算。
    /// </remarks>
    internal sealed class LootBattleLinkTests
    {
        private BattleLab _lab;
        private IEventBus _bus;
        private EconomyService _economy;
        private RandomService _random;
        private LootBattleLink _link;

        [SetUp]
        public void SetUp()
        {
            _lab = new BattleLab();
            _bus = BattleLab.Bus();
            _economy = new EconomyService();
            _random = new RandomService(BattleLab.DefaultSeed);
        }

        [TearDown]
        public void TearDown()
        {
            _link?.Dispose();
            _link = null;
            _lab.Dispose();
        }

        [Test]
        public void Victory_SettlesTheBountyAndTheDropsIntoTheVault()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");

            Assert.AreEqual(12, _economy.Gold, "赏金 8 加掉落表的 4。");
            Assert.AreEqual(2, _economy.CountOf("ITM_HERB"));
            Assert.AreEqual(1, _link.Settlements);
            Assert.AreEqual(12, _link.GoldSettled);
            Assert.AreEqual(2, _link.ItemsSettled);
        }

        [Test]
        public void Victory_AccumulatesAcrossBattles()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");
            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");

            Assert.AreEqual(24, _economy.Gold, "重复打赢就该重复拿——探索的遭遇本来就随机可重复触发。");
            Assert.AreEqual(4, _economy.CountOf("ITM_HERB"));
            Assert.AreEqual(2, _link.Settlements);
        }

        [Test]
        public void Defeat_SettlesNothing()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerDefeat, "ENC_ROAD");

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(0, _economy.Items.Count);
            Assert.AreEqual(0, _link.Settlements);
            Assert.AreEqual(1, _link.SkippedOutcomes);
        }

        [Test]
        public void Escape_SettlesNothing()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerEscaped, "ENC_ROAD");

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(1, _link.SkippedOutcomes);
        }

        [Test]
        public void ForcedRetreat_SettlesNothingEvenThoughTheStoryMovesOn()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.ForcedRetreat, "ENC_ROAD");

            Assert.AreEqual(
                0,
                _economy.Gold,
                "剧情撤退不算失败，但也不算打赢——它一分钱都不该发（第 11.2 节的口径）。");
            Assert.AreEqual(0, _economy.Items.Count);
            Assert.AreEqual(0, _link.Settlements);
            Assert.AreEqual(1, _link.SkippedOutcomes);
        }

        [Test]
        public void MissingEncounterId_SettlesNothingAndIsCountedSeparately()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerVictory, null);

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(1, _link.UnattributedOutcomes);
            Assert.AreEqual(0, _link.UnresolvedSpoils);
            Assert.AreEqual(0, _link.SkippedOutcomes, "「不知道是谁」与「打赢了但不发」不是一回事。");
        }

        [Test]
        public void UnknownEncounter_SettlesNothingAndIsCounted()
        {
            ArrangeStandardEncounter();
            Wire();

            Publish(BattleOutcome.PlayerVictory, "ENC_NOT_THERE");

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(1, _link.UnresolvedSpoils);
            Assert.AreEqual(0, _link.Settlements);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            ArrangeStandardEncounter();
            Wire();

            _link.Dispose();
            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(0, _link.Settlements);
        }

        [Test]
        public void WithoutEconomy_TheLinkStaysQuietInsteadOfThrowing()
        {
            ArrangeStandardEncounter();
            Wire(withEconomy: false);

            Assert.DoesNotThrow(() => Publish(BattleOutcome.PlayerVictory, "ENC_ROAD"));
            Assert.AreEqual(0, _link.Settlements);
        }

        [Test]
        public void WithoutRandomService_FixedRewardsStillArrive()
        {
            ArrangeStandardEncounter();
            Wire(withRandom: false);

            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");

            Assert.AreEqual(12, _economy.Gold, "赏金与必掉的东西不需要掷骰。");
            Assert.AreEqual(2, _economy.CountOf("ITM_HERB"));
        }

        [Test]
        public void DamagedEnemyData_IsCountedButDoesNotStopTheRestOfTheSettlement()
        {
            var known = _lab.Enemy("ENM_BANDIT", 30, 8, 3, 6);
            _lab.LootTable(
                "LUT_BANDIT",
                guaranteedItemIds: new[] { "ITM_HERB" },
                guaranteedItemCounts: new[] { 1 });
            BattleLab.SetLoot(known, "LUT_BANDIT", goldReward: 8);
            _lab.Encounter("ENC_ROAD", "ENM_BANDIT", "ENM_GHOST");
            Wire();

            Publish(BattleOutcome.PlayerVictory, "ENC_ROAD");

            Assert.AreEqual(8, _economy.Gold, "认得的那一只照发。");
            Assert.AreEqual(1, _economy.CountOf("ITM_HERB"));
            Assert.AreEqual(1, _link.UnresolvedSpoils, "坏数据要被记下来，而不是静默吞掉。");
            Assert.AreEqual(1, _link.Settlements);
        }

        /// <summary>一场「赏金 8 + 掉落表 4 + 必掉两份药草」的常规遭遇。</summary>
        private void ArrangeStandardEncounter()
        {
            var enemy = _lab.Enemy("ENM_BANDIT", 30, 8, 3, 6);
            _lab.LootTable(
                "LUT_BANDIT",
                guaranteedItemIds: new[] { "ITM_HERB" },
                guaranteedItemCounts: new[] { 2 },
                goldMin: 4,
                goldMax: 4);
            BattleLab.SetLoot(enemy, "LUT_BANDIT", goldReward: 8, experienceReward: 12);
            _lab.Encounter("ENC_ROAD", "ENM_BANDIT");
        }

        private void Wire(bool withEconomy = true, bool withRandom = true)
        {
            _link = new LootBattleLink(
                _bus,
                _lab.Registry(),
                withEconomy ? _economy : null,
                withRandom ? _random : null);
        }

        private void Publish(BattleOutcome outcome, string encounterId) =>
            _bus.Publish(
                BattleEventChannel.Channel,
                new BattleEndedEvent(encounterId, outcome, rounds: 3, actionCount: 9));
    }
}
