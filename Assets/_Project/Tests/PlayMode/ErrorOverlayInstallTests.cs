using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Flow;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// FND-08 的运行期异常面板必须真的被挂起来。它与自检画面是同一个病根：
    /// 组件写好了、场景里却没人放它，于是 <c>GameLog.AddSink</c> 永远不执行，
    /// 「日志里记过的错」和「屏幕上看到的错」再也不是同一份数据。
    /// </summary>
    public sealed class ErrorOverlayInstallTests
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
        public IEnumerator ErrorOverlay_IsInstalledWithoutSceneWiring()
        {
            yield return LoadBootstrapScene();

            Assert.IsTrue(ErrorOverlay.IsSupportedInThisBuild, "PlayMode 测试跑在编辑器里，面板应当受支持。");
            Assert.IsNotNull(
                ErrorOverlay.Instance,
                "错误面板必须自挂到运行期：场景接线漏挂时，正是最需要看到错误的时候。");
        }

        [UnityTest]
        public IEnumerator ErrorOverlay_CapturesErrorLogsAsScreenContent()
        {
            yield return LoadBootstrapScene();

            var overlay = ErrorOverlay.Instance;
            Assert.IsNotNull(overlay, "前置条件：面板应当已经挂起来。");

            var before = overlay.Records.Count;
            GameLog.Error(LogChannel.UI, "error overlay install probe");

            Assert.AreEqual(
                before + 1,
                overlay.Records.Count,
                "错误级日志必须进面板：面板是 GameLog 的一个 sink，两边看的应当是同一份数据。");
            Assert.IsTrue(overlay.IsVisible, "收到错误之后面板必须可见，否则等于没报。");
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
