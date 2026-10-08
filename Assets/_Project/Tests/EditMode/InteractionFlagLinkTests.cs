using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 「一次性交互物用过了 → 记进剧情账本」这条接线：只记一次性的，重复触发不重复写，
    /// 记下的记录反过来让会话判定「这条已经用过了」。
    /// </summary>
    public sealed class InteractionFlagLinkTests
    {
        private const string ChestId = "INT_CH01_CHEST";
        private const string ChestKey = "flag.interact.int_ch01_chest";

        private ServiceRegistry _registry;
        private EventBus _bus;
        private StoryState _state;
        private InteractionFlagLink _link;

        [SetUp]
        public void SetUp()
        {
            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus = new EventBus());

            // 注册即触发 OnRegistered，账本借此拿到事件总线（和引导期同一条路）。
            _state = new StoryState();
            _registry.Register<IStoryState>(_state);

            _link = new InteractionFlagLink(_bus, _state);
        }

        [TearDown]
        public void TearDown()
        {
            _link.Dispose();
            _registry.Clear();
        }

        [Test]
        public void OneShotInteraction_LeavesARecordInTheLedger()
        {
            _bus.Publish(
                ExplorationEventChannel.Channel,
                new InteractionTriggeredEvent(ChestId, "interact.chest", null, GridPosition.Origin, true));

            Assert.AreEqual(1, _state.GetValue(ChestKey), "一次性交互物用过之后，账本里该留下一条记录。");
            Assert.AreEqual(1, _link.FlagsWritten);
        }

        [Test]
        public void RepeatableInteraction_IsNotRecorded()
        {
            _bus.Publish(
                ExplorationEventChannel.Channel,
                new InteractionTriggeredEvent("INT_CH01_SIGN", "interact.read", null, GridPosition.Origin, false));

            Assert.AreEqual(0, _state.Count, "可重复触发的交互物语义是「每次来都能用」，记下来会让第二次被拒。");
            Assert.AreEqual(0, _link.FlagsWritten);
        }

        [Test]
        public void SameInteractionTwice_WritesOnce()
        {
            var triggered = new InteractionTriggeredEvent(ChestId, "interact.chest", null, GridPosition.Origin, true);

            _bus.Publish(ExplorationEventChannel.Channel, triggered);
            _bus.Publish(ExplorationEventChannel.Channel, triggered);

            Assert.AreEqual(1, _state.GetValue(ChestKey));
            Assert.AreEqual(1, _link.FlagsWritten, "同一条交互物第二次触发不该再写一遍账本。");
        }

        [Test]
        public void WithoutLedger_DoesNotThrow()
        {
            using var link = new InteractionFlagLink(_bus, null);

            Assert.DoesNotThrow(() => _bus.Publish(
                ExplorationEventChannel.Channel,
                new InteractionTriggeredEvent(ChestId, "interact.chest", null, GridPosition.Origin, true)));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            using var link = new InteractionFlagLink(_bus, _state);

            link.Dispose();
            link.Dispose();

            _bus.Publish(
                ExplorationEventChannel.Channel,
                new InteractionTriggeredEvent(ChestId, "interact.chest", null, GridPosition.Origin, true));

            Assert.AreEqual(0, link.FlagsWritten, "退订之后这条线不该再听到任何交互。");
        }

        [Test]
        public void InteractableId_IsLowercasedIntoTheKey()
        {
            Assert.AreEqual(ChestKey, InteractableFlags.KeyOf(ChestId));
            Assert.AreEqual(string.Empty, InteractableFlags.KeyOf(null), "空 ID 宁可返回空串，也不该拼出一个残缺的键。");
        }

        [Test]
        public void Session_ReadsTheLedger_SoAUsedChestStaysUsed()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map("CH01_MAP01", 4, 3);
            var chest = lab.Interactable(ChestId, "CH01_MAP01", 1, 1, oneShot: true);

            // 账本里记着这条用过了——模拟「出镇再回来」或「读了一份存档」。
            var session = lab.Session(
                map,
                ExplorationLab.State((ChestKey, 1)),
                start: new GridPosition(0, 1),
                facing: MoveDirection.East,
                interactables: chest);

            Assert.IsTrue(session.HasUsed(ChestId), "账本里记过的交互物，换个会话也该算用过了。");

            var result = session.TryInteract();

            Assert.AreEqual(InteractionRejection.AlreadyUsed, result.Rejection, "读档回来再开同一个箱子，应当被拒。");
        }

        [Test]
        public void Session_WithoutLedger_BehavesAsBefore()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map("CH01_MAP01", 4, 3);
            var chest = lab.Interactable(ChestId, "CH01_MAP01", 1, 1, oneShot: true);

            var session = lab.Session(
                map,
                ExplorationLab.State(),
                start: new GridPosition(0, 1),
                facing: MoveDirection.East,
                interactables: chest);

            Assert.IsFalse(session.HasUsed(ChestId));
            Assert.AreEqual(InteractionRejection.None, session.TryInteract().Rejection);
        }
    }
}
