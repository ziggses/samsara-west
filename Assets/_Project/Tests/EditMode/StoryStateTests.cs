using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Narrative;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 剧情状态账：未写的键读 0、只在真变化时发事件、心念只能累加、坏键进不来。
    /// </summary>
    /// <remarks>
    /// 这一组用例钉的是「账本自己」的规矩，与探索、界面无关——接线由
    /// <c>NarrativeStateLink</c> 单独负责，在 PlayMode 里用真数据端到端验证。
    /// </remarks>
    public sealed class StoryStateTests
    {
        private const string FlagKey = "flag.ch01.prologue_done";

        private EventBus _bus;
        private ServiceRegistry _registry;
        private StoryState _state;
        private readonly List<StoryStateChangedEvent> _changes = new List<StoryStateChangedEvent>();
        private readonly List<StoryKarmaChangedEvent> _karmaChanges = new List<StoryKarmaChangedEvent>();

        [SetUp]
        public void SetUp()
        {
            _changes.Clear();
            _karmaChanges.Clear();

            _bus = new EventBus();
            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus);

            // 注册即触发 OnRegistered，账本借此拿到事件总线（和引导期同一条路）。
            _state = new StoryState();
            _registry.Register<IStoryState>(_state);

            _bus.Subscribe<StoryStateChangedEvent>(NarrativeEventChannel.Channel, _changes.Add);
            _bus.Subscribe<StoryKarmaChangedEvent>(NarrativeEventChannel.Channel, _karmaChanges.Add);
        }

        [Test]
        public void UnsetKey_ReadsZero()
        {
            Assert.AreEqual(0, _state.GetValue(FlagKey), "没人写过的键必须读作 0，而不是抛异常。");
            Assert.AreEqual(0, _state.GetValue(null), "空键也读 0。");
            Assert.AreEqual(0, _state.Count);
        }

        [Test]
        public void SetValue_IsReadBack()
        {
            _state.SetValue(FlagKey, 7);

            Assert.AreEqual(7, _state.GetValue(FlagKey));
            Assert.AreEqual(1, _state.Count);
        }

        [Test]
        public void ZeroValue_LeavesNoEntry()
        {
            _state.SetValue(FlagKey, 3);
            _state.SetValue(FlagKey, 0);

            Assert.AreEqual(0, _state.GetValue(FlagKey));
            Assert.AreEqual(0, _state.Count, "写回 0 等于抹掉这条，别在快照里留一个没用的 0。");
        }

        [Test]
        public void InvalidKey_IsRejected()
        {
            _state.SetValue("ch01_prologue_done", 1);
            _state.SetValue("Flag.CH01.PROLOGUE_DONE", 1);

            Assert.AreEqual(0, _state.Count, "不符合 flag./relation./karma./ending. 规则的键一律进不来。");
        }

        [Test]
        public void SameValue_DoesNotPublishTwice()
        {
            _state.SetValue(FlagKey, 1);
            _state.SetValue(FlagKey, 1);

            Assert.AreEqual(1, _changes.Count, "值没变就不该发事件，否则一次开门会引出两条刷新。");
        }

        [Test]
        public void ChangedValue_PublishesPreviousAndCurrent()
        {
            _state.SetValue(FlagKey, 1);
            _state.SetValue(FlagKey, 4);

            Assert.AreEqual(2, _changes.Count);
            Assert.AreEqual(0, _changes[0].PreviousValue);
            Assert.AreEqual(1, _changes[0].CurrentValue);
            Assert.AreEqual(1, _changes[1].PreviousValue);
            Assert.AreEqual(4, _changes[1].CurrentValue);
            Assert.AreEqual(FlagKey, _changes[1].StateKey);
        }

        [Test]
        public void AdjustKarma_Accumulates()
        {
            _state.AdjustKarma(KarmaAxis.Compassion, 3);
            _state.AdjustKarma(KarmaAxis.Compassion, 2);
            _state.AdjustKarma(KarmaAxis.Compassion, -1);

            Assert.AreEqual(4, _state.GetKarma(KarmaAxis.Compassion));
        }

        [Test]
        public void KarmaChannel_ReadsThroughGetValue()
        {
            _state.AdjustKarma(KarmaAxis.Truth, 5);

            Assert.AreEqual(
                5,
                _state.GetValue(KarmaAxes.TruthChannel),
                "数据表允许把 karma.truth 填进条件键，读口必须认得它，否则那扇门永远打不开。");
        }

        [Test]
        public void SetValue_OnKarmaChannel_IsRejected()
        {
            _state.SetValue(KarmaAxes.CompassionChannel, 9);

            Assert.AreEqual(0, _state.GetKarma(KarmaAxis.Compassion), "心念只能累加，赋值语义会把累加值悄悄换掉。");
            Assert.AreEqual(0, _changes.Count, "被拒的写不该发事件。");
        }

        [Test]
        public void AdjustKarma_PublishesEvent()
        {
            _state.AdjustKarma(KarmaAxis.Freedom, 2);

            Assert.AreEqual(1, _karmaChanges.Count);
            Assert.AreEqual(KarmaAxes.FreedomChannel, _karmaChanges[0].KarmaChannel);
            Assert.AreEqual(0, _karmaChanges[0].PreviousValue);
            Assert.AreEqual(2, _karmaChanges[0].CurrentValue);
        }

        [Test]
        public void ZeroDelta_DoesNothing()
        {
            _state.AdjustKarma(KarmaAxis.Compassion, 0);

            Assert.AreEqual(0, _karmaChanges.Count, "没加没减就不算变化。");
        }

        [Test]
        public void Values_SnapshotExcludesZeroEntries()
        {
            _state.SetValue(FlagKey, 1);
            _state.SetValue("flag.ch01.truth_told", 2);

            Assert.AreEqual(2, _state.Values.Count);
            Assert.AreEqual(1, _state.Values[FlagKey]);
        }
    }
}
