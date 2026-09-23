using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SamsaraWest.Core
{
    /// <summary>事件频道。用于隔离不同系统的订阅，也便于按频道清理。</summary>
    public enum EventChannel
    {
        Core = 0,
        Data = 1,
        Flow = 2,
        Battle = 3,
        Exploration = 4,
        Narrative = 5,
        Progression = 6,
        Economy = 7,
        UI = 8,
        Save = 9,
        Localization = 10,
        Audio = 11,
        Tools = 12,
    }

    /// <summary>所有游戏事件的标记接口。事件应为不可变类型（推荐 readonly struct）。</summary>
    public interface IGameEvent
    {
    }

    /// <summary>
    /// 类型化事件总线。模块之间只通过事件通信，不允许直接改写对方内部状态。
    /// </summary>
    /// <remarks>
    /// 订阅策略：
    /// <list type="bullet">
    /// <item>目标是普通对象（服务、自定义类）时使用<b>弱引用</b>，对象被回收后回调自动失效，避免悬挂。</item>
    /// <item>目标是 lambda / 闭包（编译器生成类型）时使用<b>强引用</b>，因为闭包没有其它强引用会被立刻回收；
    /// 这类订阅必须由调用方负责 <c>Dispose</c>。</item>
    /// <item>静态方法使用强引用。</item>
    /// </list>
    /// </remarks>
    public interface IEventBus : IService
    {
        IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent;

        IDisposable Subscribe<T>(EventChannel channel, Action<T> handler) where T : IGameEvent;

        void Publish<T>(T gameEvent) where T : IGameEvent;

        void Publish<T>(EventChannel channel, T gameEvent) where T : IGameEvent;

        /// <summary>注销某频道下的全部订阅，返回被移除的数量。</summary>
        int ClearChannel(EventChannel channel);

        void ClearAll();

        /// <summary>当前仍有效的订阅数（会顺带清理已死亡的弱引用）。</summary>
        int SubscriberCount { get; }
    }

    public sealed class EventBus : IEventBus, IService
    {
        private readonly Dictionary<Type, List<Subscription>> _subscriptions = new Dictionary<Type, List<Subscription>>();
        private readonly List<Subscription> _scratch = new List<Subscription>();

        public void OnRegistered(IServiceRegistry registry)
        {
        }

        public void OnUnregistered() => ClearAll();

        public int SubscriberCount
        {
            get
            {
                PruneDead();
                var total = 0;
                foreach (var pair in _subscriptions)
                {
                    total += pair.Value.Count;
                }

                return total;
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent =>
            Subscribe(EventChannel.Core, handler);

        public IDisposable Subscribe<T>(EventChannel channel, Action<T> handler) where T : IGameEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var subscription = new Subscription(typeof(T), channel, handler);
            if (!_subscriptions.TryGetValue(typeof(T), out var list))
            {
                list = new List<Subscription>(4);
                _subscriptions[typeof(T)] = list;
            }

            list.Add(subscription);
            return subscription;
        }

        public void Publish<T>(T gameEvent) where T : IGameEvent => Publish(EventChannel.Core, gameEvent);

        public void Publish<T>(EventChannel channel, T gameEvent) where T : IGameEvent
        {
            if (!_subscriptions.TryGetValue(typeof(T), out var list) || list.Count == 0)
            {
                return;
            }

            _scratch.Clear();
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var subscription = list[i];
                if (!subscription.IsAlive)
                {
                    list.RemoveAt(i);
                    continue;
                }

                if (subscription.Channel == channel)
                {
                    _scratch.Add(subscription);
                }
            }

            try
            {
                for (var i = 0; i < _scratch.Count; i++)
                {
                    _scratch[i].Invoke(
                        gameEvent,
                        channel,
                        typeof(T).Name);
                }
            }
            finally
            {
                _scratch.Clear();
            }
        }

        public int ClearChannel(EventChannel channel)
        {
            var removed = 0;
            foreach (var pair in _subscriptions)
            {
                var list = pair.Value;
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Channel == channel)
                    {
                        list[i].MarkDisposed();
                        list.RemoveAt(i);
                        removed++;
                    }
                }
            }

            return removed;
        }

        public void ClearAll()
        {
            foreach (var pair in _subscriptions)
            {
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    pair.Value[i].MarkDisposed();
                }
            }

            _subscriptions.Clear();
            _scratch.Clear();
        }

        private void PruneDead()
        {
            foreach (var pair in _subscriptions)
            {
                var list = pair.Value;
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (!list[i].IsAlive)
                    {
                        list.RemoveAt(i);
                    }
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly bool _isWeak;
            private readonly WeakReference _weakTarget;
            private readonly object _strongTarget;
            private readonly MethodInfo _method;
            private Action<object> _compiled;
            private bool _disposed;

            public Subscription(Type eventType, EventChannel channel, Delegate handler)
            {
                EventType = eventType;
                Channel = channel;

                var target = handler.Target;
                _isWeak = ShouldUseWeakReference(target);

                if (_isWeak)
                {
                    _weakTarget = new WeakReference(target);
                    _method = handler.Method;
                    _strongTarget = null;
                    _compiled = null;
                }
                else
                {
                    _strongTarget = target;
                    _method = null;
                    _weakTarget = null;
                    _compiled = payload => handler.DynamicInvoke(payload);
                }
            }

            public Type EventType { get; }

            public EventChannel Channel { get; }

            public bool IsAlive
            {
                get
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    return !_isWeak || _weakTarget.IsAlive;
                }
            }

            public void Invoke(object gameEvent, EventChannel channel, string eventName)
            {
                if (!IsAlive)
                {
                    return;
                }

                try
                {
                    if (_isWeak)
                    {
                        var target = _weakTarget.Target;
                        if (target == null)
                        {
                            return;
                        }

                        _method.Invoke(target, new[] { gameEvent });
                    }
                    else
                    {
                        _compiled(gameEvent);
                    }
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    GameLog.Error(LogChannel.Core, $"事件 {eventName} 的订阅者抛出异常：{exception.InnerException.Message}", exception.InnerException);
                }
                catch (Exception exception)
                {
                    GameLog.Error(LogChannel.Core, $"事件 {eventName} 的订阅者抛出异常：{exception.Message}", exception);
                }
            }

            public void MarkDisposed() => _disposed = true;

            public void Dispose()
            {
                _disposed = true;
                if (!_isWeak && _strongTarget == null)
                {
                    _compiled = null;
                }
            }

            private static bool ShouldUseWeakReference(object target)
            {
                if (target == null)
                {
                    return false;
                }

                var type = target.GetType();
                return !type.IsDefined(typeof(CompilerGeneratedAttribute), false);
            }
        }
    }

    /// <summary>批量持有订阅句柄，一次性释放。适合 MonoBehaviour 在 OnDestroy 时统一清理。</summary>
    public sealed class SubscriptionBag : IDisposable
    {
        private readonly List<IDisposable> _handles = new List<IDisposable>();
        private bool _disposed;

        public int Count => _handles.Count;

        public void Add(IDisposable handle)
        {
            if (handle == null)
            {
                return;
            }

            if (_disposed)
            {
                handle.Dispose();
                throw new ObjectDisposedException(nameof(SubscriptionBag));
            }

            _handles.Add(handle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var i = _handles.Count - 1; i >= 0; i--)
            {
                _handles[i].Dispose();
            }

            _handles.Clear();
        }
    }
}
