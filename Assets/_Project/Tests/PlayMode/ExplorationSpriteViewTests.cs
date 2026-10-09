using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Rendering;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 精灵层：探索画面真的按网格画出来了吗。
    /// </summary>
    /// <remarks>
    /// 坐标换算是纯数学，已经在 <c>GridGeometryTests</c> 里钉死；这里管的是另一半——
    /// 那套换算<b>确实接在了画面上</b>：人要站在格子的底边中点上，镜头要跟过去，闭上图要把东西收起来。
    ///
    /// <b>不依赖素材</b>：<c>Assets/_External</c> 是外挂目录、不在库里，所以这批用例只断言
    /// 「有素材就用、没素材就如实报缺」，不去断言具体贴图——那属于美术交付，不属于这一层。
    /// </remarks>
    public sealed class ExplorationSpriteViewTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        // 与 ExplorationScreenViewTests 同理：精灵层的自挂只在播放会话开头发生一次，
        // 销毁了就再也不会自己回来。这里只把它摆回默认开关，不销毁它。
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLog.DisableFileSink();
            GameLog.Reset();
            ExplorationSpriteView.Enabled = true;

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
            ExplorationSpriteView.Enabled = true;

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
        public IEnumerator SpriteLayer_StaysOutOfTheWayUntilAMapIsOpen()
        {
            yield return LoadBootstrapScene();

            var view = ExplorationSpriteView.EnsureCreated();
            Assert.IsNotNull(view, "精灵层必须自挂：工程里还没有任何流程会在开图时把它带起来。");

            view.Refresh();
            Assert.IsFalse(view.IsVisible, "没有会话时不该画任何地图。");
            Assert.IsFalse(
                ExplorationSpriteView.IsActive,
                "没有图可画时得让位，界面层的 ASCII 网格才是此刻唯一能看的东西。");

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            view.Refresh();
            Assert.IsTrue(view.IsVisible, "开了图精灵层就该接过来画。");
            Assert.AreEqual(
                ExplorationScreenView.DiagnosticMapId,
                view.MapId,
                "画的必须正是当前那一张图。");
            Assert.IsTrue(
                ExplorationSpriteView.IsActive,
                "精灵层接管画面后，界面层必须据此停掉 ASCII 网格，否则两层叠在一起。");
        }

        [UnityTest]
        public IEnumerator SpriteLayer_PutsThePlayerOnTheTilesBottomCenterAndBringsTheCamera()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var view = ExplorationSpriteView.EnsureCreated();
            view.Refresh();

            var map = MapOf(screen.Session.MapId);
            var expected = GridGeometry.TileBottomCenter(map, screen.Session.Position);

            Assert.AreEqual(
                expected,
                view.PlayerWorldPosition,
                "角色站的是格子的底边中点，不是格中心——否则人会陷进地里半格。");
            Assert.AreEqual(0f, view.PlayerWorldPosition.y - Mathf.Floor(view.PlayerWorldPosition.y), "人必须立在整格地面上。");

            Assert.IsNotNull(view.CameraRig, "镜头跟随器得挂起来，否则相机停在原点，人一走就出画。");
            Assert.IsTrue(view.CameraRig.HasTarget);
            Assert.AreSame(view.CameraRig, Camera.main.GetComponent<CameraFollowRig>(), "跟随器要挂在主相机上。");
            Assert.AreEqual(expected, view.CameraRig.Focus, "开图时镜头直接吸附到角色，不做从原点滑过来这种镜头飞行。");
        }

        [UnityTest]
        public IEnumerator SpriteLayer_KeepsUpWithThePlayerStepByStep()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var view = ExplorationSpriteView.EnsureCreated();
            view.Refresh();

            var map = MapOf(screen.Session.MapId);
            var before = view.PlayerWorldPosition;

            Assert.IsTrue(screen.Step(MoveDirection.North));
            view.Refresh();

            var expected = GridGeometry.TileBottomCenter(map, screen.Session.Position);
            Assert.AreEqual(expected, view.PlayerWorldPosition, "走了一步，画面里的人就得跟着挪一格。");
            Assert.AreNotEqual(before, view.PlayerWorldPosition, "挪了必须真的换位置，而不是原地重画。");

            // 镜头是在 LateUpdate 里摆的（同一帧内跑在 Update 之后），所以得让过一帧再验——
            // 在这里立刻断言等于要求镜头跑在角色前面。
            yield return null;

            Assert.AreEqual(expected, view.CameraRig.Focus, "镜头也要跟到新位置。");
            Assert.AreEqual(expected.x, view.CameraRig.transform.position.x, 0.0001f, "相机本体也要真的挪过去，而不只是记了个焦点。");
            Assert.AreEqual(expected.y, view.CameraRig.transform.position.y, 0.0001f);
        }

        [UnityTest]
        public IEnumerator SpriteLayer_ReportsTheMapsArtWithoutPretending()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var view = ExplorationSpriteView.EnsureCreated();
            view.Refresh();

            var map = MapOf(screen.Session.MapId);
            Assert.AreEqual(map.BackgroundSpriteKey, view.BackgroundKey, "底图用的素材键必须来自地图数据。");

            // 素材会随美术交付变化，所以这里断言的是「口径一致」而不是「有没有那张图」：
            // 报成缺口的必须真的取不出来；画上去的必须真的取得出来。缺了装作没缺、或没缺却报缺，都是错。
            foreach (var key in view.MissingKeys)
            {
                Assert.IsTrue(
                    view.Catalog == null || view.Catalog.Get(key) == null,
                    $"报成缺口的素材其实接上了：{key}");
            }

            foreach (var key in view.RenderedKeys)
            {
                Assert.IsNotNull(view.Catalog, "有东西被画出来，说明目录一定加载起来了。");
                Assert.IsNotNull(view.Catalog.Get(key), $"画上去的素材其实取不出来：{key}");
            }
        }

        [UnityTest]
        public IEnumerator SpriteLayer_StepsAsideWhenTheMapIsClosed()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var view = ExplorationSpriteView.EnsureCreated();
            view.Refresh();
            Assert.IsTrue(view.IsVisible);

            Assert.IsTrue(screen.LeaveMap());
            view.Refresh();

            Assert.IsFalse(view.IsVisible, "离了图就该把画面收起来。");
            Assert.IsFalse(ExplorationSpriteView.IsActive, "此时又轮到界面层的 ASCII 网格值班。");
        }

        private static MapDefinition MapOf(string mapId)
        {
            var definitions = GameServices.Registry.Resolve<IDefinitionRegistry>();
            Assert.IsTrue(definitions.TryGet(mapId, out MapDefinition map), $"地图定义里找不到 {mapId}。");
            return map;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
