using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 光有代码正确还不够：启动场景里的引用一旦漏挂，运行期会一路静默降级。
    /// 这里直接加载真实的 Bootstrap 场景，把「资产已接上」变成可执行的验收。
    /// </summary>
    public sealed class BootstrapSceneWiringTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLog.DisableFileSink();
            GameLog.Reset();

            var existing = GameBootstrap.Instance;
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var bootstrap = GameBootstrap.Instance;
            if (bootstrap != null)
            {
                Object.Destroy(bootstrap.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        [UnityTest]
        public IEnumerator BootstrapScene_WiresCatalogLocalizationAndBattleConfig()
        {
            yield return LoadBootstrapScene();

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "启动场景里必须挂有 GameBootstrap。");
            Assert.IsTrue(bootstrap.IsReady);
            Assert.IsTrue(GameServices.IsReady);

            Assert.IsNotNull(bootstrap.DefinitionCatalog, "启动场景必须接上定义目录，否则运行期所有查询都会失败。");
            Assert.IsNotNull(bootstrap.BattleConfig, "启动场景必须接上战斗数值资产，否则会静默退回代码默认值。");

            var definitions = GameServices.Registry.Resolve<IDefinitionRegistry>();
            Assert.Greater(definitions.Count, 0, "定义目录里必须有数据。");
            Assert.AreEqual(0, definitions.Duplicates.Count, "重复 ID 必须为 0。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.Greater(localization.KeyCount, 0, "启动场景必须接上本地化文本表。");
            Assert.AreEqual(0, localization.MissingKeys.Count, "启动期不应有缺键。");
        }

        [UnityTest]
        public IEnumerator BootstrapScene_BattleConfigIsInternallyConsistent()
        {
            yield return LoadBootstrapScene();

            // 只断言「口径自洽」，不断言具体数值：调平衡改资产即可，不该被迫改测试。
            var config = GameBootstrap.Instance.BattleConfig;
            Assert.Greater(config.MinimumDamage, 0);
            Assert.Greater(config.RestrainMultiplier, config.RestrainedMultiplier);
            Assert.Greater(config.BrokenIncomingMultiplier, 1f);
            Assert.GreaterOrEqual(config.BrokenDurationTurns, 1);
            Assert.Greater(config.MultiHitDecay, 0f);
            Assert.LessOrEqual(config.MultiHitDecay, 1f);
            Assert.Less(config.MinSpeed, config.MaxSpeed);
            Assert.Greater(config.ActionValueBase, 0);
            Assert.Greater(config.GetActionValue(config.MaxSpeed), 0);
            Assert.Less(config.GetActionValue(config.MaxSpeed), config.GetActionValue(config.MinSpeed));
        }

        [UnityTest]
        public IEnumerator BootstrapScene_ProvidesReproducibleRandomStreams()
        {
            yield return LoadBootstrapScene();

            var random = GameServices.Registry.Resolve<IRandomService>();

            Assert.AreSame(random.GetStream(RandomStreams.Battle), random.GetStream(RandomStreams.Battle));

            var left = new RandomService(random.MasterSeed).GetStream(RandomStreams.Battle).NextUInt();
            var right = new RandomService(random.MasterSeed).GetStream(RandomStreams.Battle).NextUInt();

            Assert.AreEqual(left, right, "母种子来自场景配置：同种子同流必须给出同一结果，否则读档重放无从谈起。");
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
