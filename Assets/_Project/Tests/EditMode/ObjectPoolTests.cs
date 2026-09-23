using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 对象池存在的唯一理由是不在战斗期反复分配实例。这里验证复用、上限淘汰与清理三条语义。
    /// </summary>
    public sealed class ObjectPoolTests
    {
        private sealed class Payload
        {
            public int Id;
        }

        [Test]
        public void Prewarm_CreatesInactiveInstances()
        {
            var pool = new ObjectPool<Payload>(() => new Payload(), prewarm: 4);

            Assert.AreEqual(4, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
            Assert.AreEqual(4L, pool.TotalCreated);
        }

        [Test]
        public void Get_ReusesInactiveInstanceInsteadOfAllocating()
        {
            var pool = new ObjectPool<Payload>(() => new Payload(), prewarm: 1);
            var instance = pool.Get();

            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(1, pool.CountActive);

            pool.Release(instance);
            var again = pool.Get();

            Assert.AreSame(instance, again);
            Assert.AreEqual(1L, pool.TotalCreated, "池中有实例时不应再分配新对象。");
        }

        [Test]
        public void Get_WhenEmpty_Allocates()
        {
            var pool = new ObjectPool<Payload>(() => new Payload());

            pool.Get();
            pool.Get();

            Assert.AreEqual(2L, pool.TotalCreated);
            Assert.AreEqual(2, pool.CountActive);
        }

        [Test]
        public void Release_Null_IsIgnored()
        {
            var pool = new ObjectPool<Payload>(() => new Payload());

            Assert.DoesNotThrow(() => pool.Release(null));
            Assert.AreEqual(0, pool.CountInactive);
        }

        [Test]
        public void Release_BeyondMaxSize_DestroysInstance()
        {
            var destroyed = new List<Payload>();
            var pool = new ObjectPool<Payload>(
                () => new Payload(),
                onDestroy: item => destroyed.Add(item),
                maxSize: 2);

            var items = new List<Payload> { pool.Get(), pool.Get(), pool.Get() };
            for (var i = 0; i < items.Count; i++)
            {
                pool.Release(items[i]);
            }

            Assert.AreEqual(2, pool.CountInactive);
            Assert.AreEqual(1, destroyed.Count, "超出上限的实例应被销毁而不是无限堆积。");
        }

        [Test]
        public void Prewarm_CalledTwice_KeepsAccumulating()
        {
            var pool = new ObjectPool<Payload>(() => new Payload());

            pool.Prewarm(2);
            pool.Prewarm(3);

            Assert.AreEqual(5, pool.CountInactive);
            Assert.AreEqual(5L, pool.TotalCreated);
        }

        [Test]
        public void Clear_DestroysAllInactiveAndResetsActiveCount()
        {
            var destroyed = new List<Payload>();
            var pool = new ObjectPool<Payload>(
                () => new Payload(),
                onDestroy: item => destroyed.Add(item),
                prewarm: 3);

            pool.Get();
            pool.Clear();

            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive, "Clear 应把活跃计数一并归零，作为战斗结束时的兜底。");
            Assert.AreEqual(2, destroyed.Count, "Clear 只销毁池内闲置实例：取走的那一个已由业务方负责。");
        }

        [Test]
        public void GetAndRelease_Callbacks_AreInvoked()
        {
            var got = 0;
            var released = 0;
            var pool = new ObjectPool<Payload>(
                () => new Payload(),
                onGet: _ => got++,
                onRelease: _ => released++);

            var item = pool.Get();
            pool.Release(item);

            Assert.AreEqual(1, got);
            Assert.AreEqual(1, released);
        }

        [Test]
        public void PoolService_GetOrCreate_ReturnsSamePoolPerType()
        {
            var service = new PoolService();

            var first = service.GetOrCreate(() => new Payload());
            var second = service.GetOrCreate(() => new Payload());

            Assert.AreSame(first, second);
            Assert.IsTrue(service.TryGet<Payload>(out var found));
            Assert.AreSame(first, found);
        }

        [Test]
        public void PoolService_TryGet_UnknownType_ReturnsFalse()
        {
            var service = new PoolService();

            Assert.IsFalse(service.TryGet<Payload>(out var pool));
            Assert.IsNull(pool);
        }

        [Test]
        public void PoolService_ClearAll_DropsEveryPool()
        {
            var service = new PoolService();
            var pool = service.GetOrCreate(() => new Payload(), prewarm: 2);

            service.ClearAll();

            Assert.AreEqual(0, pool.CountInactive);
            Assert.IsFalse(service.TryGet<Payload>(out _));
        }

        [Test]
        public void PoolService_GetOrCreatePrefab_NullPrefab_Throws()
        {
            var service = new PoolService();

            Assert.Throws<System.ArgumentNullException>(() => service.GetOrCreatePrefab(null));
        }
    }
}
