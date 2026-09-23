using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 服务定位是全局的骨架约定：场景切换不丢服务、模块只通过事件通信。
    /// 这里把「注册/解析/注销」的语义钉死，避免后续有人把它改成隐式查找。
    /// </summary>
    public sealed class ServiceRegistryTests
    {
        private interface IProbeService : IService
        {
            string Label { get; }
        }

        private sealed class ProbeService : IProbeService
        {
            public ProbeService(string label)
            {
                Label = label;
            }

            public string Label { get; }

            public int RegisteredCount { get; private set; }

            public int UnregisteredCount { get; private set; }

            public List<string> ContractsAtRegisterTime { get; } = new List<string>();

            public void OnRegistered(IServiceRegistry registry)
            {
                RegisteredCount++;
                foreach (var type in registry.RegisteredTypes)
                {
                    ContractsAtRegisterTime.Add(type.Name);
                }
            }

            public void OnUnregistered()
            {
                UnregisteredCount++;
            }
        }

        private interface ISecondaryService : IService
        {
            int UnregisteredCount { get; }
        }

        private sealed class SecondaryService : ISecondaryService
        {
            public int UnregisteredCount { get; private set; }

            public void OnRegistered(IServiceRegistry registry)
            {
            }

            public void OnUnregistered()
            {
                UnregisteredCount++;
            }
        }

        private ServiceRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new ServiceRegistry();
        }

        [Test]
        public void Register_ExposesService_ByContractAndConcreteType()
        {
            var probe = new ProbeService("alpha");
            _registry.Register<IProbeService>(probe);

            Assert.IsTrue(_registry.IsRegistered<IProbeService>());
            Assert.IsTrue(_registry.IsRegistered<ProbeService>(), "实现类自身也应可解析，方便实现类内部做自检。");
            Assert.AreSame(probe, _registry.Resolve<IProbeService>());
            Assert.AreSame(probe, _registry.Resolve<ProbeService>());
        }

        [Test]
        public void Register_NullService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.Register<IProbeService>(null));
        }

        [Test]
        public void Register_DuplicateContract_Throws()
        {
            _registry.Register<IProbeService>(new ProbeService("first"));

            Assert.Throws<InvalidOperationException>(() => _registry.Register<IProbeService>(new ProbeService("second")));
        }

        [Test]
        public void Register_SameInstanceTwice_KeepsSingleBindingAndSkipsSecondCallback()
        {
            var probe = new ProbeService("alpha");
            _registry.Register<IProbeService>(probe);
            _registry.Register<IProbeService>(probe);

            Assert.AreEqual(1, probe.RegisteredCount, "同一实例重复注册不应重复触发 OnRegistered。");
            Assert.AreEqual(2, _registry.RegisteredTypes.Count);
        }

        [Test]
        public void OnRegistered_CanSeeItselfInRegistry()
        {
            var probe = new ProbeService("alpha");
            _registry.Register<IProbeService>(probe);

            CollectionAssert.Contains(probe.ContractsAtRegisterTime, nameof(IProbeService));
        }

        [Test]
        public void TryResolve_UnknownContract_ReturnsFalse()
        {
            Assert.IsFalse(_registry.TryResolve<IProbeService>(out var service));
            Assert.IsNull(service);
        }

        [Test]
        public void Resolve_UnknownContract_ThrowsWithTypeName()
        {
            var exception = Assert.Throws<ServiceNotRegisteredException>(() => _registry.Resolve<IProbeService>());
            Assert.AreSame(typeof(IProbeService), exception.ServiceType);
        }

        [Test]
        public void Unregister_RemovesBothBindingsAndNotifiesOnce()
        {
            var probe = new ProbeService("alpha");
            _registry.Register<IProbeService>(probe);

            _registry.Unregister<IProbeService>();

            Assert.IsFalse(_registry.IsRegistered<IProbeService>());
            Assert.IsFalse(_registry.IsRegistered<ProbeService>(), "注销契约时应同时清掉实现类绑定，否则会残留半活的服务。");
            Assert.AreEqual(1, probe.UnregisteredCount);
        }

        [Test]
        public void Unregister_UnknownContract_IsNoOp()
        {
            Assert.DoesNotThrow(() => _registry.Unregister<IProbeService>());
        }

        [Test]
        public void Clear_NotifiesEveryServiceExactlyOnce()
        {
            var first = new ProbeService("first");
            var second = new SecondaryService();
            _registry.Register<IProbeService>(first);
            _registry.Register<ISecondaryService>(second);
            _registry.Register<IEventBus>(new EventBus());

            _registry.Clear();

            Assert.AreEqual(1, first.UnregisteredCount);
            Assert.AreEqual(1, second.UnregisteredCount);
            Assert.AreEqual(0, _registry.RegisteredTypes.Count);
        }

        [Test]
        public void GameServices_RegistryBeforeInstall_Throws()
        {
            GameServices.Uninstall();

            Assert.IsFalse(GameServices.IsReady);
            Assert.Throws<InvalidOperationException>(() => { var _ = GameServices.Registry; });
        }

        [Test]
        public void GameServices_InstallThenUninstall_TogglesIsReady()
        {
            try
            {
                GameServices.Install(_registry);

                Assert.IsTrue(GameServices.IsReady);
                Assert.AreSame(_registry, GameServices.Registry);
            }
            finally
            {
                GameServices.Uninstall();
            }

            Assert.IsFalse(GameServices.IsReady);
        }
    }
}
