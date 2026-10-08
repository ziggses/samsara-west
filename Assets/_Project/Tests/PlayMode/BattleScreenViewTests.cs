using System.Collections;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 界面回路（<see cref="BattleHudModel"/> + <see cref="BattleScreenController"/>）在 EditMode 里已经被逐条锁死，
    /// 但那是一层「能点的接口」，不是「能点的界面」。这一组断言管的是后者：它必须不依赖场景接线就能出现、
    /// 必须真的把文本表里的字画出来、并且<b>只靠点它就能把一场真仗打完</b>——
    /// 否则所谓「界面」只是一段没人调用过的代码。
    /// </summary>
    public sealed class BattleScreenViewTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>点击循环的保险丝。它不是玩法限制，是「界面卡住」这个缺陷的探针。</summary>
        private const int MaxClicks = 5000;

        // 注意：这里不销毁 BattleScreenView。它的自挂只在播放会话开头发生一次，
        // 销毁了就再也不会自己回来——那等于把被测对象弄没了，后面的用例只能测空气。
        // 它自己会在服务重装后重新解析，所以换一场用例不需要换它。
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

            var screen = BattleScreenView.Instance;
            Assert.IsNotNull(
                screen,
                "战斗界面层必须自挂到运行期：工程里还没有任何流程会在一场仗开始时把它带起来。");
            Assert.IsTrue(screen.IsVisible, "默认可见，否则它等于没出现。");

            screen.Tick();
            Assert.IsFalse(screen.HasBattle, "引导流程不该擅自开战：没有战斗时要老实说没有。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Get(LocalizationKeys.UI_BATTLE_VIEW_HINT),
                screen.HintLine,
                "没有战斗时也要能告诉人怎么开一场，否则这个诊断入口无法被发现。");
        }

        [UnityTest]
        public IEnumerator Screen_StartsADiagnosticBattleFromRealEncounter()
        {
            yield return LoadBootstrapScene();

            var screen = BattleScreenView.Instance;
            screen.Tick();

            Assert.IsTrue(screen.StartDiagnosticBattle(), "诊断入口必须能用真实遭遇开一场仗。");

            var controller = screen.Controller;
            Assert.IsNotNull(controller);
            Assert.AreEqual(BattleScreenView.DiagnosticEncounterId, controller.Session.Setup.EncounterId);
            Assert.AreEqual(
                BattlePrompt.PlayerCommand,
                controller.Prompt,
                "开完场必须停在我方下令，而不是停在别的相位上等人猜。");

            var expectedRows = controller.Session.Setup.Party.Count + controller.Session.Setup.Enemies.Count;
            Assert.AreEqual(expectedRows, screen.Rows.Count, "每个场上单位都该有一行。");
            Assert.Greater(screen.CommandLabels.Count, 0, "轮到我方时指令栏不能是空的。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_BATTLE_TITLE), screen.TitleLine);

            // 按钮文案必须逐条来自文本表，而不是键名或空白。
            var commands = controller.Hud.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                Assert.AreEqual(localization.Get(commands[i].LabelKey), screen.CommandLabels[i]);
            }

            Assert.AreEqual(0, localization.MissingKeys.Count, "画出来的文案必须全部命中文本表。");
        }

        [UnityTest]
        public IEnumerator Screen_RefusesToReplaceARunningBattle()
        {
            yield return LoadBootstrapScene();

            var screen = BattleScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.StartDiagnosticBattle());

            var running = screen.Controller.Session;
            Assert.IsFalse(screen.StartDiagnosticBattle(), "已经在打的那一场不该被诊断入口顶掉。");
            Assert.AreSame(running, screen.Controller.Session, "拒绝之后必须还是原来那一场。");
        }

        [UnityTest]
        public IEnumerator Screen_ClickingButtonsAloneCanFinishTheBattle()
        {
            yield return LoadBootstrapScene();

            var screen = BattleScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.StartDiagnosticBattle());

            var clicks = 0;
            BattleOutcome outcome;

            while (true)
            {
                var controller = screen.Controller;
                outcome = controller.Hud.Outcome;
                if (controller.Hud.IsFinished)
                {
                    break;
                }

                Assert.Less(clicks++, MaxClicks, "点了这么多次还没打完，说明界面卡住或按钮是死的。");

                switch (controller.Prompt)
                {
                    case BattlePrompt.PlayerTarget:
                        Assert.Greater(
                            controller.TargetCandidateIds.Count,
                            0,
                            "进到选目标却一个候选都没有，玩家会卡在这里。");
                        Assert.IsTrue(screen.ActivateTarget(controller.TargetCandidateIds[0]));
                        break;

                    case BattlePrompt.PlayerCommand:
                    case BattlePrompt.PlayerMoveOrSwap:
                        Assert.IsTrue(
                            ClickOneResolvingCommand(screen, controller),
                            $"停在 {controller.Prompt} 时没有一个按钮能推进局面，玩家会卡在这里。");
                        break;

                    default:
                        Assert.Fail($"循环停在了 {controller.Prompt}，界面无法自行往下走。");
                        break;
                }
            }

            Assert.AreNotEqual(BattleOutcome.Ongoing, outcome, "循环退出时必须已经分出结果。");

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(0, localization.MissingKeys.Count, "打完之后画出来的文案同样不许缺键。");
        }

        [UnityTest]
        public IEnumerator Screen_MoveButtonActuallyMovesTheActor()
        {
            yield return LoadBootstrapScene();

            var screen = BattleScreenView.Instance;
            screen.Tick();
            Assert.IsTrue(screen.StartDiagnosticBattle());

            var controller = screen.Controller;
            var moveIndex = FindCommandIndex(controller, BattleCommandId.Move);
            Assert.GreaterOrEqual(moveIndex, 0, "开局阵型有空位，移动按钮就该给得出来。");

            var actorId = controller.Hud.CurrentActorRuntimeId;
            Assert.GreaterOrEqual(actorId, 0);
            var before = controller.Hud.FindRow(actorId).Slot;

            Assert.IsTrue(screen.ActivateCommand(moveIndex), "点移动应当进入选落点。");
            Assert.Greater(screen.PickLabels.Count, 0, "进了选落点就必须画出落点按钮。");

            // 子选择必须能撤回：否则点开移动又改主意的人会被困在这里。
            Assert.IsTrue(screen.CancelPick(), "选落点必须能撤回。");
            Assert.AreEqual(0, screen.PickLabels.Count, "撤回之后不该还留着落点按钮。");
            Assert.IsTrue(screen.ActivateCommand(moveIndex), "撤回之后必须还能重新点开移动。");

            var destination = FormationSlot.FromIndex(screen.PickKeys[0]);
            Assert.AreNotEqual(before, destination, "落点候选不该包含脚下这一格。");
            Assert.IsTrue(screen.ActivateMoveDestination(destination), "点第一个落点应当被接受。");

            Assert.AreEqual(
                destination,
                controller.Hud.FindRow(actorId).Slot,
                "点了落点，单位就必须真的挪过去——否则按钮只是画着好看。");
        }

        private static int FindCommandIndex(BattleScreenController controller, BattleCommandId id)
        {
            var commands = controller.Hud.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 点一个「点了就会推进局面」的按钮：相位或行动数至少有一个变。
        /// </summary>
        /// <remarks>
        /// 移动／换位只是打开子选择、自己什么都不消耗，循环里点它们等于空转，
        /// 所以这里跳过（它们由 <see cref="Screen_MoveButtonActuallyMovesTheActor"/> 专门盯）。
        /// 置灰的按钮与「点了没变化」的按钮同样跳过，继续试下一个；
        /// 一个都找不到时返回 false——那正是「玩家卡住了」，用例必须红。
        /// </remarks>
        private static bool ClickOneResolvingCommand(
            BattleScreenView screen,
            BattleScreenController controller)
        {
            var commands = controller.Hud.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                var id = commands[i].Id;
                if (id == BattleCommandId.Move || id == BattleCommandId.Swap)
                {
                    continue;
                }

                var promptBefore = controller.Prompt;
                var actionsBefore = controller.Hud.ActionCount;

                if (!screen.ActivateCommand(i))
                {
                    continue;
                }

                if (controller.Prompt != promptBefore || controller.Hud.ActionCount != actionsBefore)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
