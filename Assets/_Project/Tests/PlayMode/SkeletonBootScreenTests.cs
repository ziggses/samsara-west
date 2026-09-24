using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 骨架期工程没有任何画面内容，运行起来就是一块纯黑：在屏幕上，「一切正常」与「一启动就挂了」
    /// 长得完全一样。这组断言把「包能自证自己没坏」变成可执行验收 —— 自检画面必须不依赖场景接线
    /// 就能出现、必须真的把文本表里的键画出来、且服务缺一个就要当场报缺，而不是继续显示一片祥和。
    /// </summary>
    public sealed class SkeletonBootScreenTests
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
        public IEnumerator BootScreen_AppearsWithoutSceneWiring()
        {
            yield return LoadBootstrapScene();

            var screen = SkeletonBootScreen.Instance;
            Assert.IsNotNull(screen, "自检画面必须自挂到运行期：场景接线漏挂时，正是它最该出现的时候。");
            Assert.IsTrue(screen.IsVisible, "启动后自检画面默认可见，否则黑屏依然无法分辨。");

            screen.Rebuild();
            var report = screen.Report;
            Assert.IsNotNull(report);
            Assert.IsTrue(report.ServicesReady, "启动场景加载完成后，服务注册表应当已安装。");
            Assert.IsTrue(report.LocalizationAvailable, "启动场景加载完成后，文本服务应当可用。");
            Assert.IsTrue(report.IsHealthy, "接好线的启动场景不该缺服务：" + string.Join(", ", report.MissingServices));
            Assert.GreaterOrEqual(report.Lines.Count, 8, "自检至少要写出服务、数据、文本、种子、管线、分辨率与操作提示。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(0, localization.MissingKeys.Count, "画面上的文案必须全部命中文本表，不允许靠缺键兜底。");
        }

        [UnityTest]
        public IEnumerator BootScreen_DisplaysTheSkeletonGreetingKey()
        {
            yield return LoadBootstrapScene();

            var screen = SkeletonBootScreen.Instance;
            screen.Rebuild();

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            var greeting = localization.Get(LocalizationKeys.UI_SKELETON_GREETING);

            // 这条键在文本表里登记了很久却没有任何代码显示它。断言它确实出现在画面上，
            // 防止「文案登记了、常量生成了、玩家永远看不到」这种死键重新出现。
            CollectionAssert.Contains(
                screen.Report.Lines,
                greeting,
                "自检画面必须把 ui.skeleton.greeting 真正画出来，否则它只是一条死键。");
        }

        [UnityTest]
        public IEnumerator BootScreen_ReportsMissingServicesInsteadOfStayingSilent()
        {
            yield return LoadBootstrapScene();

            var screen = SkeletonBootScreen.Instance;
            screen.Rebuild();
            Assert.IsTrue(screen.Report.IsHealthy, "前置条件：接好线的场景应当健康。");

            GameServices.Registry.Unregister<IDefinitionRegistry>();
            screen.Rebuild();

            var report = screen.Report;
            Assert.IsFalse(report.IsHealthy, "定义目录已经没了却仍报健康，说明这个自检并没有在自检。");
            CollectionAssert.Contains(report.MissingServices, nameof(IDefinitionRegistry));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            var expected = localization.Format(LocalizationKeys.UI_SKELETON_MISSING, nameof(IDefinitionRegistry));
            CollectionAssert.Contains(report.Lines, expected, "缺失的契约必须写在画面上，而不是只存在于日志里。");
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
