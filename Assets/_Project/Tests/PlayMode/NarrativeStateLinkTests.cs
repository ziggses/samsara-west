using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 「剧情状态变了 → 探索可行格重建」的端到端证据。
    /// </summary>
    /// <remarks>
    /// EditMode 用例已经证明账本自己守规矩（见 <c>StoryStateTests</c>），但那时账本与探索
    /// <b>互不认识</b>，也不在真实装配里。这一组管的是另一半：在引导器装出来的真服务、
    /// 真数据表、真适配器之下，往账本写一个键，藏在首章桃林里的灵石墓穴入口必须<b>当场现身</b>——
    /// 而且<b>没有任何人手动调 RefreshVisibility</b>，全靠 Flow 侧那条接线。
    /// </remarks>
    public sealed class NarrativeStateLinkTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>桃林里那个墓穴入口的旗标；它要求 flag.ch01.broken_bridge_seen == 1 才现身。</summary>
        private const string TombFlagKey = "flag.ch01.broken_bridge_seen";
        private const string TombMapId = "CH01_MAP02";
        private static readonly GridPosition TombCell = new GridPosition(26, 38);

        /// <summary>墓穴入口正南面那一格，站在这里朝北正对着它。</summary>
        private static readonly GridPosition TombApproach = new GridPosition(26, 37);

        /// <summary>
        /// 那个墓穴入口的定义 ID。
        /// </summary>
        /// <remarks>
        /// 断言「它有没有被画出来」时要按 ID 找，不能数总数：这张图上还有七件东西
        /// （桃林入口、采桃猴、三块木牌、旧石桌、断桥藤根）本来就该画出来，总数一变这条断言就会冤枉接线。
        /// 内容会一直加，能数总数的图只有测试自己搭的那张。
        /// </remarks>
        private const string TombEntranceId = "INT_CH01_015_TOMB_ENTRANCE";

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
            // 账本是场景级共享的：写过的键必须写回去，否则会污染同一场的其它用例。
            // 用 TryResolve 而不是 Resolve：用例中途失败时服务可能没装上，清理不该再抛一次。
            if (GameServices.IsReady)
            {
                if (GameServices.Registry.TryResolve(out IStoryState story))
                {
                    story.SetValue(TombFlagKey, 0);
                }

                if (GameServices.Registry.TryResolve(out IExplorationService exploration))
                {
                    exploration.LeaveMap();
                }
            }

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
        public IEnumerator StoryFlagUnlocksTheTombEntrance_WithoutManualRefresh()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var exploration = registry.Resolve<IExplorationService>();
            var story = registry.Resolve<IStoryState>();

            Assert.IsNotNull(
                GameBootstrap.Instance.NarrativeLink,
                "剧情状态接线要装在引导末尾，否则账本变了没人去重建可行格。");

            var session = exploration.EnterMap(TombMapId, TombApproach, MoveDirection.North);
            Assert.IsNotNull(session);

            Assert.IsTrue(session.Grid.IsWalkable(TombCell), "条件没满足时墓穴入口藏着，那一格该是通的。");
            Assert.IsFalse(IsDrawn(session, TombEntranceId), "藏着的墓穴入口不该被画出来。");

            // 剧情把键改了。这里刻意不碰探索——由 NarrativeStateLink 去重建。
            story.SetValue(TombFlagKey, 1);

            Assert.IsFalse(
                session.Grid.IsWalkable(TombCell),
                "账本一变，墓穴入口必须当场现身并开始占格——没有任何人手动调 RefreshVisibility。");
            Assert.IsTrue(IsDrawn(session, TombEntranceId), "现身之后它就该被画出来。");

            Assert.AreEqual(1, GameBootstrap.Instance.NarrativeLink.Refreshes, "重建的次数要能被数出来。");
        }

        [UnityTest]
        public IEnumerator StoryChange_WhileNotOnMap_RebuildsOnNextEntry()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var exploration = registry.Resolve<IExplorationService>();
            var story = registry.Resolve<IStoryState>();

            // 不在图上写账：接线不该崩，也不该去碰一个不存在的会话。
            story.SetValue(TombFlagKey, 1);

            var session = exploration.EnterMap(TombMapId, TombApproach, MoveDirection.North);

            Assert.IsFalse(
                session.Grid.IsWalkable(TombCell),
                "进图那一刻就该按当前账本建格；不必等人再写一次账。");
        }

        [UnityTest]
        public IEnumerator SameValueAgain_DoesNotRebuildAgain()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var exploration = registry.Resolve<IExplorationService>();
            var story = registry.Resolve<IStoryState>();

            exploration.EnterMap(TombMapId, TombApproach, MoveDirection.North);
            var link = GameBootstrap.Instance.NarrativeLink;

            story.SetValue(TombFlagKey, 1);
            var afterFirst = link.Refreshes;

            story.SetValue(TombFlagKey, 1);

            Assert.AreEqual(
                afterFirst,
                link.Refreshes,
                "把同一个键写成同一个数不算变化，不该再引来一次重建。");
        }

        /// <summary>这张网格上现在画没画这个交互物。按 ID 找，不数总数——图上的内容会一直加。</summary>
        private static bool IsDrawn(ExplorationSession session, string interactableId)
        {
            var visible = session.Grid.VisibleInteractables;
            for (var i = 0; i < visible.Count; i++)
            {
                var interactable = visible[i];
                if (interactable != null && interactable.Id == interactableId)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            // 无条件重载：SetUp 刚把场景里的引导器拆掉，只有重新加载才会再装一次服务。
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
