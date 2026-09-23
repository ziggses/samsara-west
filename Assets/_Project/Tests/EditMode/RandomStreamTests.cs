using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 可复现随机是「战斗与测试能被精确重放」的前提：
    /// 同种子同流必然同序列，不同流互不干扰，且能整体快照/恢复。
    /// </summary>
    public sealed class RandomStreamTests
    {
        private static uint[] Draw(PcgRandomStream stream, int count)
        {
            var values = new uint[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = stream.NextUInt();
            }

            return values;
        }

        [Test]
        public void SameSeedAndName_ProducesIdenticalSequence()
        {
            var left = new PcgRandomStream(RandomStreams.Battle, 20260923UL);
            var right = new PcgRandomStream(RandomStreams.Battle, 20260923UL);

            CollectionAssert.AreEqual(Draw(left, 64), Draw(right, 64));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var left = new PcgRandomStream(RandomStreams.Battle, 1UL);
            var right = new PcgRandomStream(RandomStreams.Battle, 2UL);

            CollectionAssert.AreNotEqual(Draw(left, 64), Draw(right, 64));
        }

        [Test]
        public void DifferentStreamSameSeed_ProducesDifferentSequence()
        {
            var battle = new PcgRandomStream(RandomStreams.Battle, 42UL);
            var loot = new PcgRandomStream(RandomStreams.Loot, 42UL);

            CollectionAssert.AreNotEqual(Draw(battle, 8), Draw(loot, 8), "改动抽卡逻辑不得影响战斗判定，因此两条流必须独立。");
        }

        [Test]
        public void EmptyName_IsNormalizedName()
        {
            var stream = new PcgRandomStream(null, 1UL);
            Assert.AreEqual("unnamed", stream.Name);
        }

        [Test]
        public void DrawCount_TracksEveryDraw()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 1UL);
            Assert.AreEqual(0UL, stream.DrawCount);

            stream.NextUInt();
            stream.NextInt(10);
            stream.NextFloat();

            Assert.AreEqual(3UL, stream.DrawCount);
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 7UL);
            for (var i = 0; i < 4096; i++)
            {
                var value = stream.NextFloat();
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }
        }

        [Test]
        public void NextInt_StaysWithinRequestedRange()
        {
            var stream = new PcgRandomStream(RandomStreams.Loot, 7UL);
            var sawMin = false;
            var sawMax = false;

            for (var i = 0; i < 4096; i++)
            {
                var value = stream.NextInt(3, 8);
                Assert.GreaterOrEqual(value, 3);
                Assert.Less(value, 8);
                sawMin |= value == 3;
                sawMax |= value == 7;
            }

            Assert.IsTrue(sawMin && sawMax, "范围内两端都应被取到，否则说明取模有偏。");
        }

        [Test]
        public void NextInt_EmptyRange_Throws()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(5, 5));
        }

        [Test]
        public void Chance_AtBoundaries_IsDeterministic()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 1UL);
            Assert.IsFalse(stream.Chance(0f));
            Assert.IsTrue(stream.Chance(1f));
            Assert.AreEqual(0UL, stream.DrawCount, "边界概率不应消耗随机数，否则会改变后续序列。");
        }

        [Test]
        public void Pick_EmptySource_Throws()
        {
            var stream = new PcgRandomStream(RandomStreams.Loot, 1UL);
            Assert.Throws<ArgumentException>(() => stream.Pick(new List<string>()));
        }

        [Test]
        public void Shuffle_PreservesMultiset()
        {
            var stream = new PcgRandomStream(RandomStreams.Loot, 99UL);
            var source = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            stream.Shuffle(source);

            source.Sort();
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, source);
        }

        [Test]
        public void SnapshotAndRestore_ResumesIdenticalSequence()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 12345UL);
            Draw(stream, 10);

            var state = stream.Snapshot();
            var expected = Draw(stream, 10);

            stream.Restore(state);
            var actual = Draw(stream, 10);

            CollectionAssert.AreEqual(expected, actual);
            Assert.AreEqual(20UL, stream.DrawCount);
        }

        [Test]
        public void Restore_WithMismatchedName_Throws()
        {
            var stream = new PcgRandomStream(RandomStreams.Battle, 1UL);
            var state = stream.Snapshot();
            state.Name = RandomStreams.Loot;

            Assert.Throws<ArgumentException>(() => stream.Restore(state));
        }

        [Test]
        public void RandomService_GetStream_IsStablePerName()
        {
            var service = new RandomService(1UL);

            var first = service.GetStream(RandomStreams.Battle);
            var second = service.GetStream(RandomStreams.Battle);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, service.StreamNames.Count);
        }

        [Test]
        public void RandomService_EmptyStreamName_Throws()
        {
            var service = new RandomService(1UL);
            Assert.Throws<ArgumentException>(() => service.GetStream(null));
        }

        [Test]
        public void RandomService_Reseed_DiscardsExistingStreams()
        {
            var service = new RandomService(1UL);
            var before = service.GetStream(RandomStreams.Battle);
            Draw((PcgRandomStream)before, 4);

            service.Reseed(2UL);
            var after = service.GetStream(RandomStreams.Battle);

            Assert.AreNotSame(before, after);
            Assert.AreEqual(2UL, service.MasterSeed);
            Assert.AreEqual(0UL, ((PcgRandomStream)after).DrawCount);
        }

        [Test]
        public void RandomService_SnapshotAll_IsSortedByName()
        {
            var service = new RandomService(5UL);
            service.GetStream(RandomStreams.Loot);
            service.GetStream(RandomStreams.Battle);
            service.GetStream(RandomStreams.Narrative);

            var states = service.SnapshotAll();

            Assert.AreEqual(3, states.Length);
            Assert.AreEqual(RandomStreams.Battle, states[0].Name);
            Assert.AreEqual(RandomStreams.Loot, states[1].Name);
            Assert.AreEqual(RandomStreams.Narrative, states[2].Name);
        }

        [Test]
        public void RandomService_RestoreAll_ContinuesOriginalSequence()
        {
            var service = new RandomService(77UL);
            var stream = service.GetStream(RandomStreams.Battle);
            Draw((PcgRandomStream)stream, 5);
            var states = service.SnapshotAll();
            var expected = Draw((PcgRandomStream)stream, 5);

            service.Reseed(0UL);
            service.RestoreAll(states);
            var actual = Draw((PcgRandomStream)service.GetStream(RandomStreams.Battle), 5);

            CollectionAssert.AreEqual(expected, actual);
        }
    }
}
