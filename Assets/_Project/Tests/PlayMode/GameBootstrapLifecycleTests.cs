using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 生命周期验收：引导一次即装齐服务、重复启动不重复安装、
    /// 以及最关键的一条——服务不挂在场景对象上，因此切场景不会丢。
    /// 本组故意不挂任何数据资产，用来验证「缺资产时优雅降级而不是崩」。
    /// </summary>
    public sealed class GameBootstrapLifecycleTests
    {
        private const ulong TestSeed = 20260923UL;

        private static int _sceneCounter;

        private GameObject _host;
        private string _saveRoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLog.DisableFileSink();
            GameLog.Reset();

            yield return DestroyExistingBootstrap();

            _saveRoot = Path.Combine(Path.GetTempPath(), "samsara-west-playmode", Guid.NewGuid().ToString("N"));

            _host = new GameObject("TestGameBootstrap");
            _host.SetActive(false);
            var bootstrap = _host.AddComponent<GameBootstrap>();
            SetPrivateField(bootstrap, "_masterSeed", TestSeed);
            SetPrivateField(bootstrap, "_saveDirectoryOverride", _saveRoot);
            _host.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return DestroyExistingBootstrap();

            if (_saveRoot != null && Directory.Exists(_saveRoot))
            {
                Directory.Delete(_saveRoot, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Bootstrap_InstallsCoreDataLocalizationAndSaveModules()
        {
            yield return null;

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "创建启动器后应当存在实例。");
            Assert.IsTrue(bootstrap.IsReady);
            CollectionAssert.AreEqual(
                new[] { "Core", "Data", "Localization", "Save" },
                bootstrap.InstalledModules,
                "模块安装顺序即依赖顺序，改动顺序要同步改这里。");

            Assert.AreEqual(TestSeed, bootstrap.MasterSeed);
            Assert.IsTrue(GameServices.IsReady);

            var registry = GameServices.Registry;
            Assert.IsNotNull(registry.Resolve<IEventBus>());
            Assert.IsNotNull(registry.Resolve<ITimeService>());
            Assert.IsNotNull(registry.Resolve<IPoolService>());
            Assert.IsNotNull(registry.Resolve<IInputService>());
            Assert.IsNotNull(registry.Resolve<IDefinitionRegistry>());
            Assert.IsNotNull(registry.Resolve<ILocalizationService>());
            Assert.IsNotNull(registry.Resolve<ISaveService>());
            Assert.AreEqual(TestSeed, registry.Resolve<IRandomService>().MasterSeed, "母种子必须一路传到随机服务，否则读档后无法复现。");
        }

        [UnityTest]
        public IEnumerator Bootstrap_WithoutDataAssets_DegradesGracefullyAndLogsError()
        {
            yield return null;

            var registry = GameServices.Registry;
            Assert.IsNull(GameBootstrap.Instance.DefinitionCatalog);
            Assert.AreEqual(0, registry.Resolve<IDefinitionRegistry>().Count);
            Assert.AreEqual(0, registry.Resolve<ILocalizationService>().KeyCount);
            Assert.IsTrue(GameServices.IsReady, "缺数据资产也必须把服务装上，让上层能显示错误界面。");

            var errors = GameLog.Snapshot(LogLevel.Error);
            var found = false;
            for (var i = 0; i < errors.Length; i++)
            {
                if (errors[i].Channel == LogChannel.Flow && errors[i].Message.Contains("未指定 DefinitionCatalog"))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "缺数据目录必须留下可追溯的错误日志，而不是静默降级。");
        }

        [UnityTest]
        public IEnumerator Bootstrap_UsesSaveDirectoryOverride()
        {
            yield return null;

            var save = GameServices.Registry.Resolve<ISaveService>();
            Assert.AreEqual(_saveRoot, save.SaveDirectory);
            Assert.IsFalse(save.Exists(1));
            Assert.IsTrue(save.TrySave(1, new SaveData { Version = SaveService.LatestVersion }));
            Assert.IsTrue(File.Exists(Path.Combine(_saveRoot, "slot_01.json")));
        }

        [UnityTest]
        public IEnumerator Bootstrap_SecondCall_IsIgnored()
        {
            yield return null;

            var before = GameBootstrap.BootstrapCount;
            var bootstrap = GameBootstrap.Instance;

            bootstrap.Bootstrap();

            Assert.AreEqual(before, GameBootstrap.BootstrapCount, "重复引导不得再装一遍服务。");
            Assert.AreEqual(4, bootstrap.InstalledModules.Count);
        }

        [UnityTest]
        public IEnumerator DuplicateBootstrap_IsDestroyedAndKeepsExistingServices()
        {
            yield return null;

            var original = GameBootstrap.Instance;
            var bus = GameServices.Registry.Resolve<IEventBus>();

            var duplicate = new GameObject("DuplicateBootstrap");
            duplicate.SetActive(false);
            duplicate.AddComponent<GameBootstrap>();
            duplicate.SetActive(true);

            yield return null;

            Assert.IsTrue(duplicate == null, "场景重载后出现的第二个启动器应当自我销毁。");
            Assert.AreSame(original, GameBootstrap.Instance);
            Assert.AreSame(bus, GameServices.Registry.Resolve<IEventBus>(), "既有服务实例必须保持，否则订阅会全部丢掉。");
        }

        [UnityTest]
        public IEnumerator Services_SurviveSceneSwap()
        {
            yield return null;

            var bootstrap = GameBootstrap.Instance;
            var bus = GameServices.Registry.Resolve<IEventBus>();
            var time = GameServices.Registry.Resolve<ITimeService>();
            var save = GameServices.Registry.Resolve<ISaveService>();

            var temp = SceneManager.CreateScene($"SamsaraWestPlayModeTemp{_sceneCounter++}");
            SceneManager.SetActiveScene(temp);
            yield return null;

            Assert.AreSame(bootstrap, GameBootstrap.Instance);
            Assert.AreEqual("DontDestroyOnLoad", bootstrap.gameObject.scene.name, "启动器必须常驻，否则场景切换会连带销毁服务。");
            Assert.AreSame(bus, GameServices.Registry.Resolve<IEventBus>());
            Assert.AreSame(time, GameServices.Registry.Resolve<ITimeService>());
            Assert.AreSame(save, GameServices.Registry.Resolve<ISaveService>());

            yield return SceneManager.UnloadSceneAsync(temp);

            Assert.IsTrue(GameServices.IsReady, "卸载场景后服务仍应可用。");
            Assert.AreSame(bus, GameServices.Registry.Resolve<IEventBus>());
        }

        [UnityTest]
        public IEnumerator Bootstrap_SecondCall_ReportsWarningAndKeepsRegistryUnchanged()
        {
            yield return null;

            var before = GameBootstrap.Instance.InstalledModules.Count;

            GameBootstrap.Instance.Bootstrap();

            Assert.AreEqual(before, GameBootstrap.Instance.InstalledModules.Count);

            var warnings = GameLog.Snapshot(LogLevel.Warning);
            var found = false;
            for (var i = 0; i < warnings.Length; i++)
            {
                if (warnings[i].Message.Contains("忽略重复调用"))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "重复引导应是可追溯的警告，而不是无声无息。");
        }

        private static IEnumerator DestroyExistingBootstrap()
        {
            var existing = GameBootstrap.Instance;
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }

            yield return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"找不到字段 {fieldName}：启动器字段更名后测试会失效，请同步更新。");
            field.SetValue(target, value);
        }
    }
}
