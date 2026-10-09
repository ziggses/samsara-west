using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.Narrative;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 从「站在迎客猴面前按一下 E」到「对白念完、人又能走」的整条路。
    /// </summary>
    /// <remarks>
    /// EditMode 那边分别验过推进器与接线，这里补的是它们<b>接起来之后的后果</b>：
    /// <list type="bullet">
    /// <item>文本键真的落在文本表里（推出来的键名与表里的同名，不是两套各说各话）；</item>
    /// <item>对白开着时面板真的是<b>模态</b>的——探索内核不认对白，挡住方向键的只有这一层；</item>
    /// <item>两段节点串成一次会话时，面板要跟着换标题与行数；</item>
    /// <item>走过一扇门不会开对白，而搭第二次话时「问过的问题不再问」。</item>
    /// </list>
    /// </remarks>
    public sealed class ExplorationDialogueTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        private ILocalizationService _localization;
        private IStoryState _story;

        // 注意：这里不销毁 ExplorationScreenView。它的自挂只在播放会话开头发生一次，
        // 销毁了就再也不会自己回来——它会在服务重装后自行重新解析。
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

            yield return LoadBootstrapScene();

            _localization = GameServices.Registry.Resolve<ILocalizationService>();
            _story = GameServices.Registry.Resolve<IStoryState>();
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

            _localization = null;
            _story = null;
        }

        [UnityTest]
        public IEnumerator Bootstrap_InstallsTheDialogueLink()
        {
            Assert.IsNotNull(
                GameBootstrap.Instance.DialogueLink,
                "组合根要自己把「按 E → 开对白」这条线接上；靠某个面板记得去 new 一个，迟早会漏。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator InteractOnTheGreeter_OpensTheDialoguePanelOnItsFirstLine()
        {
            var screen = EnterFrontHill();

            Assert.IsFalse(screen.IsDialogueActive, "前置条件：还没搭话。");
            Assert.IsNull(screen.DialogueLine);

            var missingBefore = _localization.MissingKeys.Count;
            Assert.IsTrue(screen.Interact(), "前置条件：迎客猴就在面朝的那一格。");

            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(_localization.Get(LocalizationKeys.UI_DIALOGUE_TITLE), screen.DialogueTitleLine);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_002_LINE_1), screen.DialogueLine);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_002_LINE_1_WHO), screen.DialogueSpeaker);
            Assert.AreEqual(
                _localization.Format(
                    LocalizationKeys.UI_DIALOGUE_STATUS,
                    _localization.Get(LocalizationKeys.DLG_CH01_002_NAME),
                    1,
                    7),
                screen.DialogueStatusLine,
                "状态行要说清「这是哪一段、读到第几行」——7 行那一版的行数得真的来自剧本。");
            Assert.AreEqual(
                missingBefore,
                _localization.MissingKeys.Count,
                "推进器给的键与表里的键必须同名；差一个字符，玩家看到的就是兜底的错位提示。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DialogueIsModal_ArrowsDoNotMoveAndTheInteractKeyContinues()
        {
            var screen = EnterFrontHill();
            screen.Interact();
            var position = screen.Session.Position;
            var line = screen.DialogueLine;

            Assert.IsTrue(screen.HandleKey(KeyCode.RightArrow), "对白开着时按键要算被吃掉，不能再传给探索。");
            Assert.IsTrue(screen.HandleKey(KeyCode.D));
            Assert.AreEqual(position, screen.Session.Position, "对白期间走不动——挡住它的只有这一层。");
            Assert.AreEqual(line, screen.DialogueLine, "方向键不是「继续」，不许翻页。");

            Assert.IsTrue(screen.HandleKey(KeyCode.E));

            Assert.AreNotEqual(line, screen.DialogueLine);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_002_LINE_2_WHO), screen.DialogueSpeaker);
            Assert.IsTrue(
                screen.HandleKey(KeyCode.F5),
                "对白开着时整块面板是模态的，别的键也不许穿到探索去——漏掉一个，玩家就能一边看对白一边走动。");
            Assert.AreEqual(position, screen.Session.Position);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReadingTheGreeterToTheEnd_ChainsIntoTheQuestion_ThenGivesBackControl()
        {
            var screen = EnterFrontHill();
            screen.Interact();
            var position = screen.Session.Position;

            var seen = new HashSet<string>();
            var advances = 0;
            while (screen.IsDialogueActive && advances < 20)
            {
                seen.Add(screen.DialogueStatusLine);
                screen.HandleKey(KeyCode.E);
                advances++;
            }

            Assert.IsFalse(screen.IsDialogueActive, "念完要自己收场。");
            Assert.AreEqual(9, advances, "招呼 7 行接问伤口 2 行：两个节点串成一次会话，共 9 次推进。");
            CollectionAssert.Contains(
                seen,
                _localization.Format(
                    LocalizationKeys.UI_DIALOGUE_STATUS,
                    _localization.Get(LocalizationKeys.DLG_CH01_003_NAME),
                    1,
                    2),
                "串到第二段时面板必须跟过去，包括行数从 7 变成 2。");
            Assert.AreEqual(1, _story.GetKarma(KarmaAxis.Truth), "问伤口那一段读完才记这一点真相。");

            Assert.IsTrue(screen.HandleKey(KeyCode.UpArrow), "收场之后方向键要交还给探索。");
            Assert.AreEqual(new GridPosition(position.X, position.Y + 1), screen.Session.Position, "人要真的能走起来。");
            Assert.IsNull(screen.DialogueLine, "对白块的文案要清干净，否则会留一屏上一段的话。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AskingAgain_OnlyHearsTheGreeting_AndDoesNotFarmTruth()
        {
            var screen = EnterFrontHill();

            screen.Interact();
            var first = 0;
            while (screen.IsDialogueActive && first < 20)
            {
                screen.HandleKey(KeyCode.E);
                first++;
            }

            Assert.AreEqual(9, first);
            Assert.AreEqual(1, _story.GetKarma(KarmaAxis.Truth));

            Assert.IsTrue(screen.Interact(), "前置条件：迎客猴还在那儿，还能再搭一次话。");

            var second = 0;
            while (screen.IsDialogueActive && second < 20)
            {
                screen.HandleKey(KeyCode.E);
                second++;
            }

            Assert.AreEqual(
                7,
                second,
                "第二次只剩招呼 7 行：问伤口那一段以「还没问过」为进入条件，问过就该自己关上。");
            Assert.AreEqual(
                1,
                _story.GetKarma(KarmaAxis.Truth),
                "重复搭话不该再赚一点真相——否则站在原地按 E 就能刷满心念。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkingThroughADoor_DoesNotOpenADialogue()
        {
            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP02", new GridPosition(15, 4), MoveDirection.North));

            Assert.IsTrue(screen.Interact(), "前置条件：迎客的城门就在面朝的那一格。");

            var interaction = screen.LastInteraction;
            Assert.IsTrue(interaction.HasValue);
            Assert.IsTrue(interaction.Value.RequestsMapChange, "门就是门：走的还是换图那条路。");
            Assert.IsFalse(
                screen.IsDialogueActive,
                "门与箱子发的交互事件不该被拿去问对话表。判据交给类型键与目标数据，接线不自己维护名单。");
            Assert.AreEqual(0, GameBootstrap.Instance.DialogueLink.Started);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LeavingAndComingBack_DropsTheOpenDialogueInsteadOfResumingIt()
        {
            var screen = EnterFrontHill();
            screen.Interact();
            var line = screen.DialogueLine;

            Assert.IsTrue(screen.HandleKey(KeyCode.F4), "F4 排在模态判定之前——「对白开着一半直接切图」这条路真的存在。");
            Assert.IsTrue(screen.EnterDiagnosticMap(), "再进来一次，就是换了张图。");

            Assert.IsFalse(
                screen.IsDialogueActive,
                "换图之后对白必须收掉，否则回到图上的第一下按键是在推进上一张图的那段话。");
            Assert.IsNull(screen.DialogueLine);
            Assert.AreEqual(1, GameBootstrap.Instance.DialogueLink.Interrupted);

            Assert.IsTrue(screen.HandleKey(KeyCode.E), "而搭话本身照旧能用——收场不是把这条线关掉。");
            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(
                line,
                screen.DialogueLine,
                "重新搭话是重开一段，不是接着上次停的那行——「读到哪里」不该跨图留着。");
            yield return null;
        }

        private static ExplorationScreenView EnterFrontHill()
        {
            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap(), "前置条件：进得了首章那张野外图。");
            return screen;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
