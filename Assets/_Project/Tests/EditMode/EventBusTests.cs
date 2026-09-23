using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 事件总线是模块之间唯一的通信方式，因此它的语义（频道隔离、退订、弱引用）必须稳定。
    /// </summary>
    public sealed class EventBusTests
    {
        private readonly struct DamageDealtEvent : IGameEvent
        {
            public DamageDealtEvent(int amount)
            {
                Amount = amount;
            }

            public int Amount { get; }
        }

        private readonly struct FlagChangedEvent : IGameEvent
        {
            public FlagChangedEvent(string key)
            {
                Key = key;
            }

            public string Key { get; }
        }

        /// <summary>普通实例订阅者：走弱引用分支（非编译器生成类型）。</summary>
        private sealed class Listener
        {
            public int Received;

            public int LastAmount = -1;

            public void OnDamage(DamageDealtEvent gameEvent)
            {
                Received++;
                LastAmount = gameEvent.Amount;
            }

            public void OnFlag(FlagChangedEvent gameEvent)
            {
                Received++;
            }
        }

        private EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus();
        }

        [Test]
        public void Publish_InvokesSubscriberOfSameChannel()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);

            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(7));

            Assert.AreEqual(1, listener.Received);
            Assert.AreEqual(7, listener.LastAmount);
        }

        [Test]
        public void Publish_OnOtherChannel_DoesNotInvoke()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);

            _bus.Publish(EventChannel.Narrative, new DamageDealtEvent(7));

            Assert.AreEqual(0, listener.Received);
        }

        [Test]
        public void Publish_WithoutSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.Publish(EventChannel.Core, new FlagChangedEvent("flag.ch01.truth_told")));
        }

        [Test]
        public void Publish_OnlyDeliversToMatchingEventType()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);

            _bus.Publish(EventChannel.Battle, new FlagChangedEvent("flag.ch01.truth_told"));

            Assert.AreEqual(0, listener.Received);
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, null));
        }

        [Test]
        public void Dispose_UnsubscribesHandler()
        {
            var listener = new Listener();
            var handle = _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);

            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(1));
            handle.Dispose();
            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(2));

            Assert.AreEqual(1, listener.Received);
            Assert.AreEqual(0, _bus.SubscriberCount);
        }

        [Test]
        public void LambdaSubscription_RequiresExplicitDispose()
        {
            // 闭包是编译器生成类型，总线对它持强引用：不 Dispose 就会一直被调用（也一直泄漏）。
            var received = 0;
            var handle = _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, _ => received++);

            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(1));
            Assert.AreEqual(1, received);

            handle.Dispose();
            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(2));
            Assert.AreEqual(1, received);
        }

        [Test]
        public void ClearChannel_RemovesOnlyThatChannel()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);
            _bus.Subscribe<FlagChangedEvent>(EventChannel.Narrative, listener.OnFlag);

            var removed = _bus.ClearChannel(EventChannel.Battle);

            Assert.AreEqual(1, removed);
            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(1));
            _bus.Publish(EventChannel.Narrative, new FlagChangedEvent("flag.ch01.truth_told"));
            Assert.AreEqual(1, listener.Received, "只有剧情频道的订阅应当还在。");
        }

        [Test]
        public void ClearAll_DropsEverySubscription()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);
            _bus.Subscribe<FlagChangedEvent>(EventChannel.Narrative, listener.OnFlag);

            _bus.ClearAll();

            Assert.AreEqual(0, _bus.SubscriberCount);
            _bus.Publish(EventChannel.Battle, new DamageDealtEvent(1));
            Assert.AreEqual(0, listener.Received);
        }

        [Test]
        public void SubscriberCount_TracksLiveSubscriptions()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);
            _bus.Subscribe<FlagChangedEvent>(EventChannel.Narrative, listener.OnFlag);

            Assert.AreEqual(3, _bus.SubscriberCount);

            _bus.ClearChannel(EventChannel.Battle);

            Assert.AreEqual(1, _bus.SubscriberCount);
        }

        [Test]
        public void SubscriberThrowing_DoesNotBreakOtherSubscribers()
        {
            var received = 0;
            var handle = _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, _ => throw new InvalidOperationException("订阅者内部异常"));
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, _ => received++);

            Assert.DoesNotThrow(() => _bus.Publish(EventChannel.Battle, new DamageDealtEvent(3)));

            handle.Dispose();
            Assert.AreEqual(1, received, "一个订阅者抛异常不应影响其它订阅者收到事件。");
        }

        [Test]
        public void SubscriptionBag_DisposesEveryHandle()
        {
            var listener = new Listener();
            using (var bag = new SubscriptionBag())
            {
                bag.Add(_bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage));
                bag.Add(_bus.Subscribe<FlagChangedEvent>(EventChannel.Narrative, listener.OnFlag));
                Assert.AreEqual(2, bag.Count);
            }

            Assert.AreEqual(0, _bus.SubscriberCount);
        }

        [Test]
        public void SubscriptionBag_AddAfterDispose_Throws()
        {
            var bag = new SubscriptionBag();
            bag.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => bag.Add(_bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, _ => { })));
        }

        [Test]
        public void OnUnregistered_ClearsSubscriptions()
        {
            var listener = new Listener();
            _bus.Subscribe<DamageDealtEvent>(EventChannel.Battle, listener.OnDamage);

            _bus.OnUnregistered();

            Assert.AreEqual(0, _bus.SubscriberCount);
        }
    }
}
