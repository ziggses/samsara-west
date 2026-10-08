using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 「战斗结局 → 记进剧情账本」这条接线：四种收场各记各的键，撤退绝不记成战败，
    /// 没有遭遇 ID 的战斗不记账，记下来的键内容侧当条件就能用。
    /// </summary>
    public sealed class BattleStoryLinkTests
    {
        private const string EncounterId = "ENC_CH01_001";
        private const string OtherEncounterId = "ENC_CH01_002";

        private ServiceRegistry _registry;
        private EventBus _bus;
        private StoryState _state;
        private BattleStoryLink _link;
        private ExplorationLab _lab;

        [SetUp]
        public void SetUp()
        {
            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus = new EventBus());

            // 注册即触发 OnRegistered，账本借此拿到事件总线（和引导期同一条路）。
            _state = new StoryState();
            _registry.Register<IStoryState>(_state);

            _lab = new ExplorationLab();
            _link = new BattleStoryLink(_bus, _state);
        }

        [TearDown]
        public void TearDown()
        {
            _link.Dispose();
            _lab.Dispose();
            _registry.Clear();
        }

        [Test]
        public void Victory_LeavesWonKeyInTheLedger()
        {
            EndBattle(EncounterId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_001.won"));
            Assert.AreEqual(1, _link.OutcomesRecorded);
        }

        [Test]
        public void Defeat_LeavesLostKeyInTheLedger()
        {
            EndBattle(EncounterId, BattleOutcome.PlayerDefeat);

            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_001.lost"));
        }

        [Test]
        public void Escape_LeavesFledKeyInTheLedger()
        {
            EndBattle(EncounterId, BattleOutcome.PlayerEscaped);

            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_001.fled"));
        }

        [Test]
        public void ForcedRetreat_LeavesRetreatKeyAndNeverDefeat()
        {
            EndBattle(EncounterId, BattleOutcome.ForcedRetreat);

            Assert.AreEqual(
                1,
                _state.GetValue("flag.battle.enc_ch01_001.retreated"),
                "剧情撤退是事实，要记下来——流程侧据此回到战斗之前的剧情节点。");
            Assert.AreEqual(
                0,
                _state.GetValue("flag.battle.enc_ch01_001.lost"),
                "撤退不是失败。把它记成战败，剧本里「输过一次」这类条件会判错，而且错得很安静。");
        }

        [Test]
        public void OneBattle_LeavesExactlyOneKey()
        {
            EndBattle(EncounterId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(1, _state.Count, "一场仗只有一个收场，账本里也只该有一个键。");
        }

        [Test]
        public void TwoDifferentBattles_EachGetTheirOwnKey()
        {
            EndBattle(EncounterId, BattleOutcome.PlayerVictory);
            EndBattle(OtherEncounterId, BattleOutcome.PlayerDefeat);

            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_001.won"));
            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_002.lost"));
            Assert.AreEqual(2, _link.OutcomesRecorded);
        }

        [Test]
        public void SameBattleTwice_IsRecordedOnce()
        {
            var ended = new BattleEndedEvent(EncounterId, BattleOutcome.PlayerVictory, 4, 9);

            _bus.Publish(BattleEventChannel.Channel, ended);
            _bus.Publish(BattleEventChannel.Channel, ended);

            Assert.AreEqual(1, _state.GetValue("flag.battle.enc_ch01_001.won"));
            Assert.AreEqual(
                1,
                _link.OutcomesRecorded,
                "同一场再说一次「打完了」不该被数成两条战果（比如存档刚灌回来又收到一次广播）。");
        }

        [Test]
        public void BattleWithoutEncounterId_IsNotRecorded()
        {
            // 手工构造的战斗没有遭遇 ID（测试、将来的剧情战斗）。它没法归因，
            // 所以宁可什么都不记，也不要写一个归不到任何遭遇的键。
            EndBattle(null, BattleOutcome.PlayerVictory);

            Assert.AreEqual(0, _state.Count, "归不了因的战果不该往账本里塞键。");
            Assert.AreEqual(1, _link.UnattributedOutcomes);
            Assert.AreEqual(0, _link.OutcomesRecorded);
        }

        [Test]
        public void UnfinishedBattle_IsNotRecorded()
        {
            EndBattle(EncounterId, BattleOutcome.Ongoing);

            Assert.AreEqual(0, _state.Count, "仗还没打完就没有战果可记。");
            Assert.AreEqual(1, _link.UnrecognisedOutcomes);
            Assert.AreEqual(0, _link.OutcomesRecorded);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            // 退订夹具里这条线（Dispose 是幂等的，TearDown 再调一次也无妨）：
            // 这样账本里留下的任何键都只能来自「还在听的那条线」，而它已经不在了。
            _link.Dispose();

            EndBattle(EncounterId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(0, _state.Count, "退订之后这条线不该再听到任何战果。");
            Assert.AreEqual(0, _link.OutcomesRecorded);
        }

        [Test]
        public void ExistingLedgerEntries_AreLeftAlone()
        {
            _state.SetValue("flag.ch01.prologue_done", 1);

            EndBattle(EncounterId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(1, _state.GetValue("flag.ch01.prologue_done"), "记账是加一条，不是换一本。");
            Assert.AreEqual(2, _state.Count);
        }

        [Test]
        public void OutcomeKey_SpeaksTheLedgerLanguage()
        {
            // 内容作者要在 interactables.csv / 对话条件里原样写这些键，
            // 所以键必须先过数据层的格式规则（flag. 前缀、全小写、点分）。
            var keys = new[]
            {
                BattleFlags.OutcomeKey(EncounterId, BattleOutcome.PlayerVictory),
                BattleFlags.OutcomeKey(EncounterId, BattleOutcome.PlayerDefeat),
                BattleFlags.OutcomeKey(EncounterId, BattleOutcome.PlayerEscaped),
                BattleFlags.OutcomeKey(EncounterId, BattleOutcome.ForcedRetreat),
            };

            for (var i = 0; i < keys.Length; i++)
            {
                Assert.IsTrue(IdRules.IsValidStateKey(keys[i]), $"{keys[i]} 得是合法的状态键，内容侧才写得出来。");
            }

            Assert.AreEqual("flag.battle.enc_ch01_001.won", keys[0]);
            Assert.AreEqual(
                "flag.battle.enc_ch01_003.retreated",
                BattleFlags.OutcomeKey("ENC_CH01_003", BattleOutcome.ForcedRetreat),
                "遭遇 ID 在表里是大写，进账本必须转成小写。");

            Assert.IsEmpty(BattleFlags.OutcomeKey(null, BattleOutcome.PlayerVictory), "没有遭遇 ID 就没有键。");
            Assert.IsEmpty(BattleFlags.OutcomeKey(EncounterId, BattleOutcome.Ongoing), "没打完不是收场。");
        }

        [Test]
        public void Victory_OpensTheConditionalPath()
        {
            // 这一条是「键名与条件语言对得上」的证据：战果写进真账本之后，
            // 内容侧照 requiredStateKey 写出来的条件必须当场满足。
            var map = _lab.Map();
            var gate = _lab.Interactable(
                "INT_TEST_GATE",
                "CH01_MAP01",
                2,
                1,
                requiredStateKey: BattleFlags.OutcomeKey(EncounterId, BattleOutcome.PlayerVictory),
                requiredValue: 1,
                hiddenUntilConditionMet: true);

            var session = _lab.Session(map, new NarrativeStateSourceAdapter(_state), interactables: gate);

            Assert.IsFalse(session.Grid.IsVisible(gate), "还没打赢，这条路不该出现。");

            EndBattle(EncounterId, BattleOutcome.PlayerVictory);

            // 真装配里这一步由 NarrativeStateLink 替我们做（PlayMode 的 NarrativeStateLinkTests
            // 管的是那一半：账本一变，没有任何人手动调它，格也照样重建）。
            session.RefreshVisibility();

            Assert.IsTrue(session.Grid.IsVisible(gate), "打赢之后，按战果设条件的路必须当场出现。");
        }

        private void EndBattle(string encounterId, BattleOutcome outcome) =>
            _bus.Publish(BattleEventChannel.Channel, new BattleEndedEvent(encounterId, outcome, 4, 9));
    }
}
