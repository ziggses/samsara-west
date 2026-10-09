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
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP02", new GridPosition(57, 4), MoveDirection.North));

            Assert.IsTrue(screen.Interact(), "前置条件：回前山的桃林入口就在面朝的那一格。");

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

        [UnityTest]
        public IEnumerator WalkingIntoTheGathererWithoutTheLedger_EndsAtTheGreeting()
        {
            var screen = EnterPeachGrove(new GridPosition(52, 13));

            Assert.IsTrue(screen.Interact(), "前置条件：采桃猴就在面朝的那一格。");
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_008_LINE_1), screen.DialogueLine);

            var advances = AdvanceToTheEnd(screen);

            Assert.AreEqual(
                5,
                advances,
                "账本还压在石桌底下时，招呼后面那一跳以 flag.ch01.ledger_found 为进入条件——挑不到人，会话就该到此为止。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.gatherer_met"), "招呼读过，采桃猴记住了这个人。");
            Assert.AreEqual(0, _story.GetKarma(KarmaAxis.Truth), "账本还没露面，就谈不上说真话。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheLedgerChain_PublishesTheTruthOnceAndRepeatsNothingElse()
        {
            // 先到旧石桌底下把账本翻出来。
            var screen = EnterPeachGrove(new GridPosition(44, 21));
            Assert.IsTrue(screen.Interact(), "前置条件：旧石桌就在面朝的那一格。");
            Assert.AreEqual(3, AdvanceToTheEnd(screen), "石桌那一段三行：账本压在桌子底下，被桃汁泡硬了。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.ledger_found"), "翻过一次，账本就算到手。");

            // 拿着账本去搭采桃猴的话：招呼 5 行 → 账本 3 行 → 公开账本 3 行。
            screen = EnterPeachGrove(new GridPosition(52, 13));
            Assert.IsTrue(screen.Interact(), "前置条件：采桃猴就在面朝的那一格。");
            Assert.AreEqual(11, AdvanceToTheEnd(screen), "三个节点串成一次会话：5 + 3 + 3。");
            Assert.AreEqual(1, _story.GetKarma(KarmaAxis.Truth), "公开账本那一段读完才记这一点真相。");

            // 再搭一次：账本内容还会念一遍，但公开那一段以「还没公开过」为进入条件，早该自己关上。
            Assert.IsTrue(screen.Interact(), "前置条件：采桃猴还在那儿，还能再搭一次话。");
            Assert.AreEqual(8, AdvanceToTheEnd(screen), "第二次只剩招呼 5 行与账本 3 行。");
            Assert.AreEqual(
                1,
                _story.GetKarma(KarmaAxis.Truth),
                "重复念账本不该再赚一点真相——否则站在桃筐边按 E 就能刷满心念。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeetingBaiMei_RecordsTheOneWhoRemembersEveryNight()
        {
            var screen = EnterBanquet(new GridPosition(68, 19));

            var missingBefore = _localization.MissingKeys.Count;
            Assert.IsTrue(screen.Interact(), "前置条件：白眉就在面朝的那一格。");

            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_022_LINE_1), screen.DialogueLine);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_022_LINE_1_WHO), screen.DialogueSpeaker);
            Assert.AreEqual(
                _localization.Format(
                    LocalizationKeys.UI_DIALOGUE_STATUS,
                    _localization.Get(LocalizationKeys.DLG_CH01_022_NAME),
                    1,
                    9),
                screen.DialogueStatusLine,
                "状态行要说清「这是哪一段、读到第几行」——9 这个行数得真的来自剧本里那条节点。");

            Assert.AreEqual(
                9,
                AdvanceToTheEnd(screen),
                "白眉那段一次念完九行：他记得每一夜，所以不敢告诉别人。");
            Assert.AreEqual(
                1,
                _story.GetValue("flag.ch01.bai_mei_met"),
                "搭过话，故事就记住「见过白眉」——MAP04 的守门猴要拿它当进入条件。");
            Assert.AreEqual(
                0,
                _story.GetKarma(KarmaAxis.Truth),
                "白眉还没把真相交出去，这一趟不该记心念。");
            Assert.AreEqual(
                missingBefore,
                _localization.MissingKeys.Count,
                "白眉那九行连说话人，必须一行不落地命中文本表。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheBanquetStations_EachRememberTheirOwnVisit()
        {
            // 主线「谁在摆宴」要求查三处：主桌、祠堂、鼓楼。它们是并列的——
            // 先查哪一处都不该影响另外两处，所以这里逐处各走一遍，看它们各写各的旗标。
            var screen = EnterBanquet(new GridPosition(48, 57));
            Assert.IsTrue(screen.Interact(), "前置条件：主桌就在面朝的那一格。");
            Assert.AreEqual(3, AdvanceToTheEnd(screen), "主桌那一段三行：一只空碗，碗底刻着「等大王回来再吃」。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.main_table_seen"));

            screen = EnterBanquet(new GridPosition(26, 63));
            Assert.IsTrue(screen.Interact(), "前置条件：祠堂就在面朝的那一格。");
            Assert.AreEqual(4, AdvanceToTheEnd(screen), "祠堂那一段四行：三块牌位，没有名字的那块香火最旺。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.shrine_seen"));

            screen = EnterBanquet(new GridPosition(76, 43));
            Assert.IsTrue(screen.Interact(), "前置条件：鼓楼就在面朝的那一格。");
            Assert.AreEqual(6, AdvanceToTheEnd(screen), "鼓楼那一段六行：鼓手抬了一千多次胳膊，同一段节奏，不敢停。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.drum_seen"));

            // 三处查完，旗标互不覆盖（上面每条都还在，没有被后来的冲掉）；
            // 厨房与宿舍是顺路的旁证，子时舞台是那一夜的落点，也各记一笔。
            Assert.AreEqual(1, _story.GetValue("flag.ch01.main_table_seen"), "查过祠堂不该把主桌那一笔抹掉。");

            screen = EnterBanquet(new GridPosition(22, 49));
            Assert.IsTrue(screen.Interact(), "前置条件：厨房就在面朝的那一格。");
            Assert.AreEqual(
                _localization.Get(LocalizationKeys.UI_DIALOGUE_NARRATOR),
                screen.DialogueSpeaker,
                "厨房那一段第一行是旁白，表里没有登记 .who——该画「旁白」，而不是那个键的占位符。");
            Assert.AreEqual(3, AdvanceToTheEnd(screen));
            Assert.AreEqual(1, _story.GetValue("flag.ch01.kitchen_seen"));

            screen = EnterBanquet(new GridPosition(16, 25));
            Assert.IsTrue(screen.Interact(), "前置条件：猴子宿舍就在面朝的那一格。");
            Assert.AreEqual(3, AdvanceToTheEnd(screen));
            Assert.AreEqual(1, _story.GetValue("flag.ch01.dorm_seen"));

            screen = EnterBanquet(new GridPosition(48, 65));
            Assert.IsTrue(screen.Interact(), "前置条件：子时舞台就在面朝的那一格。");
            Assert.AreEqual(4, AdvanceToTheEnd(screen), "子时舞台那一段四行：子时还没到，可桌上的食物已经在动。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.midnight_stage_seen"));

            Assert.AreEqual(0, _localization.MissingKeys.Count, "宴场八段新对白必须全部命中文本表。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeetingTheGateMonkey_HearsTheOneWhoForgotEveryNight()
        {
            var screen = EnterWaterCurtain(new GridPosition(52, 37));

            var missingBefore = _localization.MissingKeys.Count;
            Assert.IsTrue(screen.Interact(), "前置条件：守门猴就在面朝的那一格。");

            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(
                _localization.Get(LocalizationKeys.UI_DIALOGUE_NARRATOR),
                screen.DialogueSpeaker,
                "第一行是旁白，表里没有登记 .who——该画「旁白」，而不是那个键的占位符。");

            Assert.AreEqual(
                10,
                AdvanceToTheEnd(screen),
                "守门猴那段一次念完十行：他每晚都先杀他们，再坐在大王的位子上哭。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.gate_monkey_met"));
            Assert.AreEqual(
                0,
                _story.GetKarma(KarmaAxis.Truth),
                "听他说完不算真相——他手里没有答案，只有记得自己忘了这件事。");
            Assert.AreEqual(
                missingBefore,
                _localization.MissingKeys.Count,
                "守门猴那十行连说话人，必须一行不落地命中文本表。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRingOnlyOpensTheGateOnceItIsOutOfTheTomb()
        {
            var screen = EnterWaterCurtain(new GridPosition(46, 37));

            // 缺口石环出自隐藏墓穴 OPT_CH01_001，而墓穴那一批还没做——
            // 「出示石环」因此是本阶段唯一打不开的路线。这是有意的依赖，不是漏配。
            Assert.IsFalse(
                screen.Interact(),
                "信物还在墓里，出示石环这条路线的条件不该成立。");

            // 墓穴落地后由它写这个键；这里先替它写，证明「条件一成立，路线就亮」。
            _story.SetValue("flag.ch01.old_ring_held", 1);

            Assert.IsTrue(screen.Interact(), "信物到手之后，同一条路线就该亮起来。");
            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(_localization.Get(LocalizationKeys.DLG_CH01_028_LINE_1), screen.DialogueLine);
            Assert.AreEqual(
                3,
                AdvanceToTheEnd(screen),
                "出示石环那一段三行：取出石环、守门猴认出旧部、放行。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.gate_opened_by_relic"));
            Assert.AreEqual(0, _localization.MissingKeys.Count, "出示石环那三行也得全在文本表里。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PersuadingTheGateMonkey_NeedsTwoTruths()
        {
            var screen = EnterWaterCurtain(new GridPosition(58, 37));

            // 真相值来自前两张图：迎客猴那一问 +1，公开账本那一读 +1。
            Assert.AreEqual(0, _story.GetKarma(KarmaAxis.Truth), "刚开场，一点真相都还没攒下。");
            Assert.IsFalse(
                screen.Interact(),
                "一点真相都没有的人说服不了守门猴——这是三条路线里唯一要心念的那条。");

            _story.AdjustKarma(KarmaAxis.Truth, 1);
            Assert.IsFalse(screen.Interact(), "一点还不够：门槛是 2，表里写的是 GreaterOrEqual。");

            _story.AdjustKarma(KarmaAxis.Truth, 1);
            Assert.IsTrue(screen.Interact(), "攒到两点，守门猴就该愿意听了。");
            Assert.IsTrue(screen.IsDialogueActive);
            Assert.AreEqual(
                5,
                AdvanceToTheEnd(screen),
                "说服那一段五行：问出口、他沉默、他也想知道、让路、旁白记下那半步。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.gate_opened_by_words"));
            Assert.AreEqual(
                1,
                _story.GetKarma(KarmaAxis.Freedom),
                "让他开口的是「他也想知道答案」，这一笔记自由。");
            Assert.AreEqual(2, _story.GetKarma(KarmaAxis.Truth), "说服不该吃掉已经攒下的真相。");
            Assert.AreEqual(0, _localization.MissingKeys.Count, "说服那五行也得全在文本表里。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ForcingPastTheGateMonkey_CostsAPointOfCompassion()
        {
            var screen = EnterWaterCurtain(new GridPosition(52, 42));

            Assert.IsTrue(screen.Interact(), "前置条件：绕过去的落脚点就在面朝的那一格。");
            Assert.AreEqual(
                4,
                AdvanceToTheEnd(screen),
                "绕过那一段四行：让开、不让、从侧面过去、他又对着洞口念了一遍。");
            Assert.AreEqual(1, _story.GetValue("flag.ch01.gate_opened_by_force"));
            Assert.AreEqual(
                -1,
                _story.GetKarma(KarmaAxis.Compassion),
                "把他一个人晾在岗位上，慈悲要掉一点。");
            Assert.AreEqual(0, _localization.MissingKeys.Count, "绕过那四行也得全在文本表里。");
            yield return null;
        }

        private static ExplorationScreenView EnterBanquet(GridPosition approach)
        {
            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 同一个入口不允许抢会话：先离图，再按坐标进宴场。
            screen.LeaveMap();

            Assert.IsTrue(
                screen.EnterDiagnosticMap("CH01_MAP03", approach, MoveDirection.North),
                "前置条件：进得了宴场那张图。");
            return screen;
        }

        private static ExplorationScreenView EnterWaterCurtain(GridPosition approach)
        {
            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 同一个入口不允许抢会话：先离图，再按坐标进水帘洞外。
            screen.LeaveMap();

            Assert.IsTrue(
                screen.EnterDiagnosticMap("CH01_MAP04", approach, MoveDirection.North),
                "前置条件：进得了水帘洞外那张图。");
            return screen;
        }

        private static ExplorationScreenView EnterPeachGrove(GridPosition approach)
        {
            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 同一个入口不允许抢会话：先离图，再按坐标进桃林。
            screen.LeaveMap();

            Assert.IsTrue(
                screen.EnterDiagnosticMap("CH01_MAP02", approach, MoveDirection.North),
                "前置条件：进得了桃林那张图。");
            return screen;
        }

        private static int AdvanceToTheEnd(ExplorationScreenView screen)
        {
            var advances = 0;
            while (screen.IsDialogueActive && advances < 30)
            {
                screen.HandleKey(KeyCode.E);
                advances++;
            }

            Assert.IsFalse(screen.IsDialogueActive, "对白该自己收场，不该卡在最后一行上。");
            return advances;
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
