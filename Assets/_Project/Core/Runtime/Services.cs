using System;
using System.Collections.Generic;

namespace SamsaraWest.Core
{
    /// <summary>
    /// 参与服务定位的模块级服务。生命周期由注册方显式管理，服务本身不依赖 Unity 场景，
    /// 因此场景切换不会导致服务丢失。
    /// </summary>
    public interface IService
    {
        /// <summary>注册完成后的回调，此时可以解析其它已注册服务。</summary>
        void OnRegistered(IServiceRegistry registry);

        /// <summary>注销前的回调，用于释放资源。</summary>
        void OnUnregistered();
    }

    /// <summary>显式注册 / 显式解析的服务注册表。禁止用 <c>FindObjectOfType</c> 代替服务定位。</summary>
    public interface IServiceRegistry
    {
        /// <summary>以 <typeparamref name="T"/> 为契约注册服务，同时以具体类型注册以便实现类自解析。</summary>
        void Register<T>(T service) where T : class, IService;

        bool TryResolve<T>(out T service) where T : class, IService;

        /// <summary>解析服务；未注册时抛出 <see cref="ServiceNotRegisteredException"/>。</summary>
        T Resolve<T>() where T : class, IService;

        bool IsRegistered<T>() where T : class, IService;

        void Unregister<T>() where T : class, IService;

        /// <summary>注销全部服务并逆序调用 <see cref="IService.OnUnregistered"/>。</summary>
        void Clear();

        IReadOnlyCollection<Type> RegisteredTypes { get; }
    }

    public sealed class ServiceNotRegisteredException : Exception
    {
        public ServiceNotRegisteredException(Type type)
            : base($"服务 {type.FullName} 尚未注册。请确认在 Composition Root（Flow.GameBootstrap）中已显式注册。")
        {
            ServiceType = type;
        }

        public Type ServiceType { get; }
    }

    public sealed class ServiceRegistry : IServiceRegistry
    {
        private readonly Dictionary<Type, IService> _services = new Dictionary<Type, IService>();
        private readonly List<Type> _installOrder = new List<Type>();

        public IReadOnlyCollection<Type> RegisteredTypes => _services.Keys;

        public void Register<T>(T service) where T : class, IService
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "不允许注册空服务。");
            }

            var contract = typeof(T);
            var isFirstBinding = !ContainsInstance(service);

            RegisterAs(contract, service);

            var concrete = service.GetType();
            if (concrete != contract)
            {
                RegisterAs(concrete, service);
            }

            if (isFirstBinding)
            {
                _installOrder.Add(contract);
                service.OnRegistered(this);
            }
        }

        public bool TryResolve<T>(out T service) where T : class, IService
        {
            if (_services.TryGetValue(typeof(T), out var found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public T Resolve<T>() where T : class, IService
        {
            if (TryResolve<T>(out var service))
            {
                return service;
            }

            throw new ServiceNotRegisteredException(typeof(T));
        }

        public bool IsRegistered<T>() where T : class, IService => _services.ContainsKey(typeof(T));

        public void Unregister<T>() where T : class, IService
        {
            if (!_services.TryGetValue(typeof(T), out var service))
            {
                return;
            }

            RemoveInstance(service);
        }

        public void Clear()
        {
            var instances = new List<IService>();
            foreach (var pair in _services)
            {
                if (!instances.Contains(pair.Value))
                {
                    instances.Add(pair.Value);
                }
            }

            _services.Clear();
            _installOrder.Clear();

            for (var i = instances.Count - 1; i >= 0; i--)
            {
                instances[i].OnUnregistered();
            }
        }

        private bool ContainsInstance(IService service)
        {
            foreach (var pair in _services)
            {
                if (ReferenceEquals(pair.Value, service))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveInstance(IService service)
        {
            var keys = new List<Type>();
            foreach (var pair in _services)
            {
                if (ReferenceEquals(pair.Value, service))
                {
                    keys.Add(pair.Key);
                }
            }

            foreach (var key in keys)
            {
                _services.Remove(key);
                _installOrder.Remove(key);
            }

            service.OnUnregistered();
        }

        private void RegisterAs(Type type, IService service)
        {
            if (_services.TryGetValue(type, out var existing))
            {
                if (ReferenceEquals(existing, service))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"服务契约 {type.FullName} 已被 {existing.GetType().Name} 占用，无法再注册 {service.GetType().Name}。");
            }

            _services[type] = service;
        }
    }

    /// <summary>
    /// 组合根（Composition Root）的单点入口。除 Flow 的启动流程外，任何位置都不应调用
    /// <see cref="Install"/>；<see cref="Uninstall"/> 只在应用退出或测试拆解时调用。
    /// </summary>
    public static class GameServices
    {
        private static IServiceRegistry _registry;

        public static bool IsReady => _registry != null;

        public static IServiceRegistry Registry =>
            _registry ?? throw new InvalidOperationException(
                "服务注册表尚未初始化。请先由 Flow.GameBootstrap 调用 GameServices.Install(registry)。");

        public static void Install(IServiceRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public static void Uninstall()
        {
            _registry = null;
        }
    }
}
