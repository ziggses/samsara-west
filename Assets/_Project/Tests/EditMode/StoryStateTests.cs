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
        private readonly List<StoryStateRestoredEvent> _restores = new List<StoryStateRestoredEvent>();

        [SetUp]
        public void SetUp()
        {
            _changes.Clear();
            _karmaChanges.Clear();
            _restores.Clear();

            _bus = new EventBus();
            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus);

            // 注册即触发 OnRegistered，账本借此拿到事件总线（和引导期同一条路）。
            _state = new StoryState();
            _registry.Register<IStoryState>(_state);

            _bus.Subscribe<StoryStateChangedEvent>(NarrativeEventChannel.Channel, _changes.Add);
            _bus.Subscribe<StoryKarmaChangedEvent>(NarrativeEventChannel.Channel, _karmaChanges.Add);
            _bus.Subscribe<StoryStateRestoredEvent>(NarrativeEventChannel.Channel, _restores.Add);
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

        [Test]
        public void Restore_ReplacesWholeLedger()
        {
            _state.SetValue(FlagKey, 1);
            _state.SetValue("flag.ch01.stale", 1);

            _state.Restore(new Dictionary<string, int> { { "flag.ch01.fresh", 2 } }, 1, 2, 3);

            Assert.AreEqual(1, _state.Count, "整本替换：上一局的键不该跟着过来。");
            Assert.AreEqual(0, _state.GetValue(FlagKey), "上一局写过的键必须没了，否则读档会留下没人查得出的残留。");
            Assert.AreEqual(2, _state.GetValue("flag.ch01.fresh"));
        }

        [Test]
        public void Restore_SetsKarmaFromArguments()
        {
            _state.AdjustKarma(KarmaAxis.Compassion, 7);

            _state.Restore(null, 3, -1, 0);

            Assert.AreEqual(3, _state.GetKarma(KarmaAxis.Compassion), "读档是恢复一份值，不是在当前值上累加。");
            Assert.AreEqual(-1, _state.GetKarma(KarmaAxis.Truth), "心念可正可负，负数也要如实恢复。");
            Assert.AreEqual(0, _state.GetKarma(KarmaAxis.Freedom));
        }

        [Test]
        public void Restore_SkipsInvalidAndKarmaKeys()
        {
            _state.Restore(
                new Dictionary<string, int>
                {
                    { "not_a_key", 5 },
                    { KarmaAxes.TruthChannel, 9 },
                    { "flag.ch01.ok", 1 },
                },
                0,
                0,
                0);

            Assert.AreEqual(1, _state.Count, "只有合法、且不是心念轴的键进得来。");
            Assert.AreEqual(1, _state.GetValue("flag.ch01.ok"));
            Assert.AreEqual(0, _state.GetKarma(KarmaAxis.Truth), "存档里的 karma.* 键不该盖过三个轴字段。");
        }

        [Test]
        public void Restore_SkipsZeroValues()
        {
            _state.Restore(new Dictionary<string, int> { { FlagKey, 0 } }, 0, 0, 0);

            Assert.AreEqual(0, _state.Count, "值为 0 等于没写过，与快照口径一致。");
        }

        [Test]
        public void Restore_NullValues_EmptiesLedger()
        {
            _state.SetValue(FlagKey, 1);

            _state.Restore(null, 0, 0, 0);

            Assert.AreEqual(0, _state.Count, "空快照也是一份快照：把账清空，而不是什么都不做。");
            Assert.AreEqual(1, _restores.Count);
            Assert.AreEqual(0, _restores[0].FlagCount);
        }

        [Test]
        public void Restore_AnnouncesOnceWithCount()
        {
            _state.SetValue("flag.ch01.previous", 1);

            // 铺场那一笔自己会发一条 changed 事件，基线先把它排除：
            // 这条用例要证的是「Restore 不逐键发」，不是「铺场不发」。
            var changesFromSeeding = _changes.Count;

            _state.Restore(
                new Dictionary<string, int> { { FlagKey, 1 }, { "flag.ch01.second", 2 } },
                4,
                5,
                6);

            Assert.AreEqual(1, _restores.Count, "整本换掉该只发一条事件，而不是每个键一条。");
            Assert.AreEqual(2, _restores[0].FlagCount);
            Assert.AreEqual(4, _restores[0].Compassion);
            Assert.AreEqual(5, _restores[0].Truth);
            Assert.AreEqual(6, _restores[0].Freedom);

            Assert.AreEqual(changesFromSeeding, _changes.Count, "整本替换不逐键发 StoryStateChangedEvent。");
            Assert.AreEqual(0, _karmaChanges.Count, "心念也不逐轴发：订阅方看这一条就够了。");
        }
    }
}
