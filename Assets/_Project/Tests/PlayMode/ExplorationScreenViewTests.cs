using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 探索运行时（<see cref="ExplorationSession"/>）在 EditMode 里已经被逐条锁死，但那时它<b>只有测试在用</b>。
    /// 这一组断言管的是另一半：它必须不靠场景接线就能出现、必须真的把文本表里的字画出来、
    /// 并且<b>只靠按方向键与 E 就能走进一张真图、撞上真交互物、掷出真遭遇</b>——
    /// 否则「能在游戏里走」这句话没有证据。
    /// </summary>
    public sealed class ExplorationScreenViewTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>
        /// 找遭遇的保险丝。首章野外图的遭遇率是 0.12，来回走这么多步还掷不中的概率可以忽略；
        /// 它测的不是运气，是「掷骰到底有没有接在移动上」。
        /// </summary>
        private const int MaxEncounterWalkSteps = 400;

        // 注意：这里不销毁 ExplorationScreenView。它的自挂只在播放会话开头发生一次，
        // 销毁了就再也不会自己回来——那等于把被测对象弄没了。它会在服务重装后自行重新解析。
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
        public IEnumerator Screen_AppearsWithoutSceneWiring()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            Assert.IsNotNull(
                screen,
                "探索界面层必须自挂到运行期：工程里还没有任何流程会在一张图被打开时把它带起来。");
            Assert.IsTrue(screen.IsVisible, "默认可见，否则它等于没出现。");

            screen.Tick();
            Assert.IsFalse(screen.IsExploring, "引导流程不该擅自开图：没有会话时要老实说没有。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_EXPLORE_TITLE), screen.TitleLine);
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_NO_MAP),
                screen.StatusLine,
                "没有会话时必须明说怎么开一张图，否则这个诊断入口无法被发现。");
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_EXPLORE_VIEW_HINT), screen.HintLine);
            Assert.AreEqual(0, localization.MissingKeys.Count, "画出来的文案必须全部命中文本表。");
        }

        [UnityTest]
        public IEnumerator Screen_DiagnosticEntryOpensARealMap()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            Assert.IsTrue(screen.EnterDiagnosticMap(), "诊断入口必须能用真实地图数据开一张图。");

            var session = screen.Session;
            Assert.IsNotNull(session);
            Assert.AreEqual(ExplorationScreenView.DiagnosticMapId, session.MapId);
            Assert.AreEqual(ExplorationScreenView.DiagnosticStart, session.Position);
            Assert.AreEqual(ExplorationScreenView.DiagnosticFacing, session.Facing);
            Assert.AreEqual(0, session.StepCount, "刚进图还没走过。");

            var grid = session.Grid;
            Assert.AreEqual(60, grid.Width, "宽度必须来自 maps.csv，而不是界面自己编一个。");
            Assert.AreEqual(40, grid.Height);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_EXPLORE_GRID_LEGEND), screen.LegendLine);
            StringAssert.Contains(
                ExplorationScreenView.DiagnosticMapId,
                screen.StatusLine,
                "状态行必须报出当前是哪张图。");

            // 落点与朝向是挑过的：正前方应当正好是 (20,8) 的迎客猴，连名字一起画出来。
            var villager = grid.InteractableAt(session.FacingPosition);
            Assert.IsNotNull(villager, "诊断落点正前方应当有一个交互物，否则这个入口演示不了交互。");
            Assert.AreEqual("INT_CH01_001_GREETER_MONKEY", villager.Id);
            Assert.AreEqual(
                localization.Format(
                    LocalizationKeys.UI_EXPLORE_FACING_TARGET,
                    localization.Get(villager.DisplayNameKey),
                    localization.Get(villager.PromptKey)),
                screen.FacingLine,
                "前方有什么必须报名字与提示，而不是报键名。");
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_RefusesToReplaceARunningMap()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var running = screen.Session;
            Assert.IsFalse(screen.EnterDiagnosticMap(), "已经开着的那张图不该被诊断入口顶掉。");
            Assert.AreSame(running, screen.Session, "拒绝之后必须还是原来那一张。");

            Assert.IsTrue(screen.LeaveMap());
            Assert.IsFalse(screen.IsExploring);
            Assert.IsNull(screen.Session);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_EXPLORE_NO_MAP), screen.StatusLine);

            Assert.IsTrue(screen.EnterDiagnosticMap(), "离图之后必须还能再进一张，否则入口只能开一次。");
        }

        [UnityTest]
        public IEnumerator Screen_StepIntoTheVillagerIsRefusedAndSaysWhy()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            var before = screen.Position.Value;
            Assert.IsFalse(screen.Step(MoveDirection.East), "迎客猴占着那一格，走不过去。");

            Assert.AreEqual(MoveRejection.Blocked, screen.LastMove.Value.Rejection);
            Assert.AreEqual(before, screen.Position.Value, "被拒之后人必须在原地。");
            Assert.AreEqual(0, screen.StepCount);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_MOVE_BLOCKED),
                screen.NoteLine,
                "按了方向键没反应，必须有一行字解释——否则玩家会先怀疑按键丢了。");
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_StepIntoTheEdgeIsRefusedAndSaysSo()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // (0,0) 朝西：正前方一步就出图。
            Assert.IsTrue(screen.EnterDiagnosticMap(
                ExplorationScreenView.DiagnosticMapId,
                GridPosition.Origin,
                MoveDirection.West));

            Assert.IsFalse(screen.Step(MoveDirection.West));
            Assert.AreEqual(MoveRejection.OutOfBounds, screen.LastMove.Value.Rejection);
            Assert.AreEqual(GridPosition.Origin, screen.Position.Value);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_MOVE_OUT_OF_BOUNDS),
                screen.NoteLine);
        }

        [UnityTest]
        public IEnumerator Screen_StepActuallyMovesAndClearsTheNote()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());
            Assert.IsFalse(screen.Step(MoveDirection.East));
            Assert.IsNotNull(screen.NoteLine, "先留一行拒绝理由，好确认走成之后它会被清掉。");

            Assert.IsTrue(screen.Step(MoveDirection.North));
            Assert.AreEqual(
                new GridPosition(ExplorationScreenView.DiagnosticStart.X, ExplorationScreenView.DiagnosticStart.Y + 1),
                screen.Position.Value,
                "走成了人就必须真的挪过去，否则按键只是画着好看。");
            Assert.AreEqual(1, screen.StepCount);
            Assert.AreEqual(MoveDirection.North, screen.Facing, "朝向必须跟着走的方向。");
            Assert.IsNull(screen.NoteLine, "走成了就不该还挂着上一次的拒绝理由。");
        }

        [UnityTest]
        public IEnumerator Screen_InteractTriggersTheVillagerAndNamesIt()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap());

            Assert.IsTrue(screen.Interact(), "对着迎客猴按 E 必须触发。");

            var result = screen.LastInteraction.Value;
            Assert.AreEqual("INT_CH01_001_GREETER_MONKEY", result.InteractableId);
            Assert.AreEqual("interact.dialogue", result.InteractionTypeKey);
            Assert.AreEqual("DLG_CH01_002", result.TargetId, "对话目标必须原样带出来，交给剧情侧去认。");
            Assert.IsFalse(result.RequestsMapChange);

            var definitions = GameServices.Registry.Resolve<IDefinitionRegistry>();
            Assert.IsTrue(definitions.TryGet(result.InteractableId, out InteractableDefinition villager));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Format(
                    LocalizationKeys.UI_EXPLORE_INTERACT_TRIGGERED,
                    localization.Get(villager.DisplayNameKey)),
                screen.NoteLine,
                "触发之后必须报出触发的是什么东西，而不是只报一个 ID。");
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_InteractWithNothingAheadExplainsItself()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // (10,6) 朝北：那一片没有交互物。
            Assert.IsTrue(screen.EnterDiagnosticMap(
                ExplorationScreenView.DiagnosticMapId,
                new GridPosition(10, 6),
                MoveDirection.North));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_FACING_EMPTY),
                screen.FacingLine,
                "格内空着就要明说空着——玩家按 E 之前该知道会不会有反应。");

            Assert.IsFalse(screen.Interact());
            Assert.AreEqual(InteractionRejection.NoTarget, screen.LastInteraction.Value.Rejection);
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_INTERACT_NO_TARGET),
                screen.NoteLine);
        }

        [UnityTest]
        public IEnumerator Screen_HiddenInteractableGetsNoWordAtAll()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 桃林 (26,38) 的灵石墓穴入口要求 flag.ch01.broken_bridge_seen == 1 才现身。
            // 引导期没有存档，旗标读出来是 0，所以它现在是「藏着的」。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP02", new GridPosition(26, 37), MoveDirection.North));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_EXPLORE_FACING_EMPTY),
                screen.FacingLine,
                "看不见的东西在画面上就该像没有一样。");

            Assert.IsFalse(screen.Interact());
            Assert.AreEqual(
                InteractionRejection.Hidden,
                screen.LastInteraction.Value.Rejection,
                "条件未满足且数据要求隐藏，就不该让它被交互。");
            Assert.IsNull(
                screen.NoteLine,
                "这一行字等于替数据宣布「这里有个机关，只是现在看不见」，把隐藏想藏的意图当场泄掉。");
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_EncounterStopsTheWalkUntilItIsSettled()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // (0,0) 与 (0,1) 都是空地，来回走每一步都必定走成，也就必定掷一次遭遇骰。
            Assert.IsTrue(screen.EnterDiagnosticMap(
                ExplorationScreenView.DiagnosticMapId,
                GridPosition.Origin,
                MoveDirection.North));

            var session = screen.Session;
            var steps = 0;
            while (!session.IsEncounterPending && steps < MaxEncounterWalkSteps)
            {
                var direction = steps % 2 == 0 ? MoveDirection.North : MoveDirection.South;
                Assert.IsTrue(screen.Step(direction), "来回的两格都是空地，这一步不该被拒。");
                steps++;
            }

            Assert.IsTrue(
                session.IsEncounterPending,
                "首章野外图的遭遇率是 0.12，走这么多步还掷不中，只可能是掷骰没接在移动上。");
            Assert.AreEqual(
                session.StepCount,
                session.EncounterRollCount,
                "走成的每一步都该掷一次骰。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            var pending = session.PendingEncounterId;
            StringAssert.Contains(pending, screen.StatusLine, "遭遇挂着了就得报出来，藏着一个未了结的遭遇最让人困惑。");

            // 往回走一步：那一格刚刚站过，必定是能走的空地。
            // 方向不能写死——遭遇在哪一步掷中取决于运行期种子（引导场景的 _masterSeed 是 0），
            // 走到 (0,0) 时朝南的一步落在图外，拒绝理由就变成了越界，这条用例会时红时绿。
            var back = session.Position.Y == 0 ? MoveDirection.North : MoveDirection.South;

            Assert.IsFalse(screen.Step(back), "遭遇没了结之前不该还能走。");
            Assert.AreEqual(MoveRejection.EncounterPending, screen.LastMove.Value.Rejection);
            Assert.AreEqual(
                localization.Format(LocalizationKeys.UI_EXPLORE_ENCOUNTER_PENDING, pending),
                screen.NoteLine,
                "拒绝的理由必须说清楚是「先打完这一场」，而不是含糊的走不动。");
            Assert.IsFalse(screen.Interact(), "遭遇没了结之前同样不该还能交互。");
            Assert.AreEqual(InteractionRejection.EncounterPending, screen.LastInteraction.Value.Rejection);

            session.ResolveEncounter();

            Assert.IsTrue(screen.Step(back), "了结之后必须能立刻继续走。");
            Assert.AreEqual(MoveRejection.None, screen.LastMove.Value.Rejection);
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [Test]
        public void Screen_KeyBindingsCoverArrowsAndWasd()
        {
            Assert.AreEqual(MoveDirection.North, ExplorationScreenView.DirectionOf(KeyCode.UpArrow));
            Assert.AreEqual(MoveDirection.North, ExplorationScreenView.DirectionOf(KeyCode.W));
            Assert.AreEqual(MoveDirection.East, ExplorationScreenView.DirectionOf(KeyCode.RightArrow));
            Assert.AreEqual(MoveDirection.East, ExplorationScreenView.DirectionOf(KeyCode.D));
            Assert.AreEqual(MoveDirection.South, ExplorationScreenView.DirectionOf(KeyCode.DownArrow));
            Assert.AreEqual(MoveDirection.South, ExplorationScreenView.DirectionOf(KeyCode.S));
            Assert.AreEqual(MoveDirection.West, ExplorationScreenView.DirectionOf(KeyCode.LeftArrow));
            Assert.AreEqual(MoveDirection.West, ExplorationScreenView.DirectionOf(KeyCode.A));

            Assert.IsNull(ExplorationScreenView.DirectionOf(KeyCode.F4), "F4 是入口键，不该被当成方向。");
            Assert.IsNull(ExplorationScreenView.DirectionOf(KeyCode.E));

            Assert.IsTrue(ExplorationScreenView.IsInteractKey(KeyCode.E));
            Assert.IsTrue(ExplorationScreenView.IsInteractKey(KeyCode.Space));
            Assert.IsTrue(ExplorationScreenView.IsInteractKey(KeyCode.Return));
            Assert.IsFalse(ExplorationScreenView.IsInteractKey(KeyCode.Q));
        }

        [UnityTest]
        public IEnumerator Screen_WalkingThroughTheGateChangesTheMap()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(
                bootstrap.MapChange,
                "换图接线得由引导期装起来：探索只会发出换图请求，没人接的话首章走到门口照样出不去。");

            // 前山东侧的山门在 (57,6)：站到 (56,6) 朝东，正前方就是它。
            Assert.IsTrue(screen.EnterDiagnosticMap(
                ExplorationScreenView.DiagnosticMapId,
                new GridPosition(56, 6),
                MoveDirection.East));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            StringAssert.Contains(
                localization.Get(LocalizationKeys.INT_CH01_004_NAME),
                screen.FacingLine,
                "面朝的那一格是门，就该报出它的名字。");
            StringAssert.Contains(
                localization.Get(LocalizationKeys.UI_PROMPT_ENTER),
                screen.FacingLine,
                "门上的提示是「进入」，不是「查看」也不是「打开」。");

            Assert.IsTrue(screen.Interact(), "门是可以交互的。");

            var session = screen.Session;
            Assert.IsNotNull(session, "换图不是离图：走完门必须还在某张图上。");
            Assert.AreEqual("CH01_MAP02", session.MapId, "走了门就必须真的换图——这正是这条接线的全部理由。");
            Assert.AreEqual(
                new GridPosition(57, 6),
                session.Position,
                "门在前山的 (57,6)，桃林 70x50 装得下这个坐标：落点就是它自己，不必再夹。");
            Assert.IsTrue(
                session.Grid.IsWalkable(session.Position),
                "落点必须是能站的格子，否则进门第一步就走不动。");
            Assert.AreEqual(70, session.Grid.Width, "换图之后网格得重建，不然画出来的还是上一张图。");
            Assert.AreEqual(50, session.Grid.Height);
            Assert.AreEqual(1, bootstrap.MapChange.MapChanges);
            StringAssert.Contains("CH01_MAP02", screen.StatusLine, "状态行得跟着报出新图。");
            Assert.AreEqual(0, localization.MissingKeys.Count, "新加的门与提示文案必须都在文本表里。");
        }

        [UnityTest]
        public IEnumerator Screen_ArrivingFromTheGateLandsInFrontOfTheOneThatLeadsBack()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap(
                ExplorationScreenView.DiagnosticMapId,
                new GridPosition(56, 6),
                MoveDirection.East));

            Assert.IsTrue(screen.Interact());

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP02", session.MapId);

            // 两条门在各自图上的位置是对着选的：从前山山门 (57,6) 出门，就该落在桃林图上与它相对的地方，
            // 回来时才不至于被丢到地图另一头。这里锁的就是「落点在对面那扇门跟前」。
            Assert.AreEqual(
                new GridPosition(57, 5),
                session.FacingPosition,
                "落点应当就在回程门跟前，而不是被丢到图的角落。");

            var gateBack = session.Grid.InteractableAt(session.FacingPosition);
            Assert.IsNotNull(gateBack, "回程门应当就在落点正前方。");
            Assert.AreEqual("INT_CH01_008_PEACH_GROVE_GATE", gateBack.Id);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            StringAssert.Contains(localization.Get(LocalizationKeys.INT_CH01_008_NAME), screen.FacingLine);
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_TheReturnGateLeadsBackUpTheHill()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 反过来走一遍：桃林 (57,4) 朝北，正前方是 (57,5) 的桃林入口。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP02", new GridPosition(57, 4), MoveDirection.North));
            Assert.IsTrue(screen.Interact());

            var session = screen.Session;
            Assert.IsTrue(
                session.MapId == "CH01_MAP01",
                "门是双向的：数据里两张图的 connections 互相指着对方，走回去就该回到前山图。");
            Assert.AreEqual(
                new GridPosition(57, 5),
                session.Position,
                "回程门在 (57,5)，前山图装得下这个坐标，落点就是它自己，不必搜索。");
            Assert.AreEqual(1, GameBootstrap.Instance.MapChange.MapChanges);
        }

        [UnityTest]
        public IEnumerator Screen_TheBanquetGateLeadsIntoTheBanquetGround()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 桃林北缘的宴场门在 (52,47)：站到 (52,46) 朝北，正前方就是它。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP02", new GridPosition(52, 46), MoveDirection.North));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            StringAssert.Contains(
                localization.Get(LocalizationKeys.INT_CH01_016_NAME),
                screen.FacingLine,
                "面朝的那一格是宴场门，就该报出它的名字。");

            Assert.IsTrue(screen.Interact(), "门是可以交互的。");

            var session = screen.Session;
            Assert.IsNotNull(session, "换图不是离图：走完门必须还在某张图上。");
            Assert.AreEqual("CH01_MAP03", session.MapId, "走了宴场门就必须真的换到宴场那张图。");
            Assert.AreEqual(90, session.Grid.Width, "换图之后网格得重建，宴场尺寸来自 maps.csv。");
            Assert.AreEqual(70, session.Grid.Height);
            Assert.IsTrue(session.Grid.IsWalkable(session.Position), "落点必须是能站的格子。");

            // 两张图的门嵌在同一个坐标上：锚点 (52,47) 本身就是可行走格，落点就在原地；
            // 进门一律面朝南，正前方正好是摆在 (52,46) 的回程门。
            // 这是桃林那两扇门定下的口径——落点要在对面那扇门跟前，而不是被丢到图另一头。
            Assert.AreEqual(new GridPosition(52, 47), session.Position);
            Assert.AreEqual(new GridPosition(52, 46), session.FacingPosition, "落点应当就在回程门跟前。");

            var gateBack = session.Grid.InteractableAt(session.FacingPosition);
            Assert.IsNotNull(gateBack, "回程门应当就在落点正前方。");
            Assert.AreEqual("INT_CH01_017_BANQUET_EXIT", gateBack.Id);
            StringAssert.Contains("CH01_MAP03", screen.StatusLine, "状态行得跟着报出新图。");
            Assert.AreEqual(0, localization.MissingKeys.Count, "新加的门与提示文案必须都在文本表里。");
        }

        [UnityTest]
        public IEnumerator Screen_TheBanquetExitLeadsBackIntoThePeachGrove()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 反过来走一遍：宴场 (52,45) 朝北，正前方是 (52,46) 的回程门。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP03", new GridPosition(52, 45), MoveDirection.North));
            Assert.IsTrue(screen.Interact());

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP02", session.MapId, "门是双向的：两张图的 connections 互相指着对方。");
            Assert.AreEqual(
                new GridPosition(52, 46),
                session.Position,
                "回程门在 (52,46)，桃林 70x50 装得下这个坐标，落点就是它自己，不必搜索。");
            Assert.AreEqual(1, GameBootstrap.Instance.MapChange.MapChanges);
        }

        [UnityTest]
        public IEnumerator Screen_TheBanquetGroundCarriesAllEightStations()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP03", new GridPosition(52, 47), MoveDirection.North));

            var grid = screen.Session.Grid;

            // 剧本给了宴场八个节点（入口、厨房、主桌、鼓楼、祠堂、宿舍、白眉、子时舞台），
            // 这里按 ID 逐个点名，而不是数总数：将来往宴场上加一件装饰，
            // 这条用例就不该跟着变红——它守的是「八个节点一个不落」，不是「图上正好有八件东西」。
            var stations = new (string Id, int X, int Y)[]
            {
                ("INT_CH01_017_BANQUET_EXIT", 52, 46),
                ("INT_CH01_018_KITCHEN", 22, 50),
                ("INT_CH01_019_MAIN_TABLE", 48, 58),
                ("INT_CH01_020_DRUM_TOWER", 76, 44),
                ("INT_CH01_021_SHRINE", 26, 64),
                ("INT_CH01_022_MONKEY_DORM", 16, 26),
                ("INT_CH01_023_BAI_MEI", 68, 20),
                ("INT_CH01_024_MIDNIGHT_STAGE", 48, 66),
            };

            foreach (var station in stations)
            {
                var found = grid.InteractableAt(new GridPosition(station.X, station.Y));
                Assert.IsNotNull(
                    found,
                    $"宴场缺了 {station.Id}：剧本那八个节点要一个不落地画在图上，落位也不能漂。");
                Assert.AreEqual(station.Id, found.Id);
            }
        }

        [UnityTest]
        public IEnumerator Screen_TheWaterfallPathLeadsOutsideTheWaterCurtain()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 宴场北缘的石径在 (52,22)：站到 (52,21) 朝北，正前方就是它。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP03", new GridPosition(52, 21), MoveDirection.North));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            StringAssert.Contains(
                localization.Get(LocalizationKeys.INT_CH01_025_NAME),
                screen.FacingLine,
                "面朝的那一格是通往水帘洞的石径，就该报出它的名字。");

            Assert.IsTrue(screen.Interact(), "门是可以交互的。");

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP04", session.MapId);
            Assert.AreEqual(80, session.Grid.Width, "换图之后网格得重建，水帘洞外的尺寸来自 maps.csv。");
            Assert.AreEqual(55, session.Grid.Height);
            Assert.IsTrue(session.Grid.IsWalkable(session.Position), "落点必须是能站的格子。");

            // 门位口径跟桃林那两扇一样：025 与 026 嵌在 (52,22)/(52,21) 这一对上。
            // 锚点自己就是可行走格，落点留在原地；进门一律面朝南，正前方正好是回程门。
            Assert.AreEqual(new GridPosition(52, 22), session.Position);
            Assert.AreEqual(new GridPosition(52, 21), session.FacingPosition, "落点应当就在回程门跟前。");

            var gateBack = session.Grid.InteractableAt(session.FacingPosition);
            Assert.IsNotNull(gateBack, "回程门应当就在落点正前方。");
            Assert.AreEqual("INT_CH01_026_WATERFALL_EXIT", gateBack.Id);
            StringAssert.Contains("CH01_MAP04", screen.StatusLine, "状态行得跟着报出新图。");
            Assert.AreEqual(0, localization.MissingKeys.Count, "新加的门与提示文案必须都在文本表里。");
        }

        [UnityTest]
        public IEnumerator Screen_TheWaterfallExitLeadsBackIntoTheBanquetGround()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 反过来走一遍：水帘洞外 (52,22) 朝南，正前方是 (52,21) 的回程门。
            // 朝南正是进门时被摆好的朝向（ADR-024），所以从宴场过来的人一落地就能原路按回去。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP04", new GridPosition(52, 22), MoveDirection.South));
            Assert.IsTrue(screen.Interact());

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP03", session.MapId, "门是双向的：两张图的 connections 互相指着对方。");
            Assert.AreEqual(
                new GridPosition(52, 21),
                session.Position,
                "回程门在 (52,21)，宴场 90x70 装得下这个坐标，落点就是它自己，不必搜索。");
            Assert.AreEqual(1, GameBootstrap.Instance.MapChange.MapChanges);
        }

        [UnityTest]
        public IEnumerator Screen_TheWaterCurtainCarriesItsFourInvestigations()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP04", new GridPosition(52, 22), MoveDirection.North));

            var grid = screen.Session.Grid;

            // 剧本给了水帘洞外五站，第五站「魔王洞门」通到 CH01_MAP05——本批那张图落地了，
            // 门就真的画上了，不再是一扇指着空处的口子。
            // 这里守的是「四段调查 + 兜底路线 + 那扇洞门」一个不落，按 ID 点名，不数总数。
            var stations = new (string Id, int X, int Y)[]
            {
                ("INT_CH01_026_WATERFALL_EXIT", 52, 21),
                ("INT_CH01_027_BROKEN_WATERFALL", 52, 30),
                ("INT_CH01_028_GATE_MONKEY", 52, 38),
                ("INT_CH01_029_WEAPON_RACK", 40, 42),
                ("INT_CH01_030_GREAT_SAGE_SEAT", 64, 42),
                ("INT_CH01_033_ENTER_BY_FORCE", 52, 43),
                ("INT_CH01_034_STRONGHOLD_GATE", 52, 50),
            };

            foreach (var station in stations)
            {
                var found = grid.InteractableAt(new GridPosition(station.X, station.Y));
                Assert.IsNotNull(
                    found,
                    $"水帘洞外缺了 {station.Id}：剧本那几站要一个不落地画在图上，落位也不能漂。");
                Assert.AreEqual(station.Id, found.Id);
            }
        }

        [UnityTest]
        public IEnumerator Screen_TheStrongholdPathLeadsBeyondTheDeepestWaterCurtain()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 水帘洞外最深处 (52,50) 是通往魔王洞窟的口子：站到 (52,49) 朝北，正前方就是它。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP04", new GridPosition(52, 49), MoveDirection.North));

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            StringAssert.Contains(
                localization.Get(LocalizationKeys.INT_CH01_034_NAME),
                screen.FacingLine,
                "面朝的那一格是通往魔王洞窟的口子，就该报出它的名字。");

            Assert.IsTrue(screen.Interact(), "门是可以交互的。");

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP05", session.MapId);
            Assert.AreEqual(100, session.Grid.Width, "换图之后网格得重建，魔王洞窟的尺寸来自 maps.csv。");
            Assert.AreEqual(70, session.Grid.Height);
            Assert.IsTrue(session.Grid.IsWalkable(session.Position), "落点必须是能站的格子。");

            // 门位口径跟水帘洞那两扇一样：034 与 035 嵌在 (52,50)/(52,49) 这一对上。
            // 锚点自己就是可行走格，落点留在原地；进门一律面朝南，正前方正好是回程门。
            Assert.AreEqual(new GridPosition(52, 50), session.Position);
            Assert.AreEqual(new GridPosition(52, 49), session.FacingPosition, "落点应当就在回程门跟前。");

            var gateBack = session.Grid.InteractableAt(session.FacingPosition);
            Assert.IsNotNull(gateBack, "回程门应当就在落点正前方。");
            Assert.AreEqual("INT_CH01_035_STRONGHOLD_EXIT", gateBack.Id);
            StringAssert.Contains("CH01_MAP05", screen.StatusLine, "状态行得跟着报出新图。");
            Assert.AreEqual(0, localization.MissingKeys.Count, "新加的门与提示文案必须都在文本表里。");
        }

        [UnityTest]
        public IEnumerator Screen_TheStrongholdExitLeadsBackIntoTheWaterCurtain()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();

            // 反过来走一遍：魔王洞窟 (52,50) 朝南，正前方是 (52,49) 的回程门。
            // 朝南正是进门时被摆好的朝向（ADR-024），所以从水帘洞过来的人一落地就能原路按回去。
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP05", new GridPosition(52, 50), MoveDirection.South));
            Assert.IsTrue(screen.Interact());

            var session = screen.Session;
            Assert.AreEqual("CH01_MAP04", session.MapId, "门是双向的：两张图的 connections 互相指着对方。");
            Assert.AreEqual(
                new GridPosition(52, 49),
                session.Position,
                "回程门在 (52,49)，水帘洞外 80x55 装得下这个坐标，落点就是它自己，不必搜索。");
            Assert.AreEqual(1, GameBootstrap.Instance.MapChange.MapChanges);
        }

        [UnityTest]
        public IEnumerator Screen_TheStrongholdCarriesItsFiveStations()
        {
            yield return LoadBootstrapScene();

            var screen = ExplorationScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.EnterDiagnosticMap("CH01_MAP05", new GridPosition(52, 50), MoveDirection.North));

            var grid = screen.Session.Grid;

            // 剧本给了魔王洞窟五站：断兵器、空酒坛、壁上字、断掉的石像，加魔王本人。
            // 战后那五件（空王座 + 杀封放 + 出洞的光）以战果为门槛，没打仗之前不该出现，
            // 由 ExplorationDialogueTests 那几条单独守着。
            var stations = new (string Id, int X, int Y)[]
            {
                ("INT_CH01_035_STRONGHOLD_EXIT", 52, 49),
                ("INT_CH01_036_SHATTERED_WEAPONS", 34, 44),
                ("INT_CH01_037_EMPTY_WINE_JARS", 70, 44),
                ("INT_CH01_038_WALL_INSCRIPTION", 52, 33),
                ("INT_CH01_039_MONKEY_KING", 52, 26),
                ("INT_CH01_040_BROKEN_SAGE_STATUE", 70, 26),
                ("INT_CH01_041_REINCARNATION_ALTAR", 52, 12),
            };

            foreach (var station in stations)
            {
                var found = grid.InteractableAt(new GridPosition(station.X, station.Y));
                Assert.IsNotNull(
                    found,
                    $"魔王洞窟缺了 {station.Id}：剧本那几站要一个不落地画在图上，落位也不能漂。");
                Assert.AreEqual(station.Id, found.Id);
            }
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
