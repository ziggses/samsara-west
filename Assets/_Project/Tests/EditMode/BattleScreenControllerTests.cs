using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.UI;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗循环驱动器（<see cref="BattleScreenController"/>）的行为锁定。
    /// </summary>
    /// <remarks>
    /// 这一层把内核的回合推进收拢成「等我方下令 / 等选目标 / 已结束」三态，
    /// 因此整个最小可玩回路可以在 EditMode 里跑到结局，不必先有场景与界面。
    /// </remarks>
    [TestFixture]
    public sealed class BattleScreenControllerTests
    {
        private const string Encounter = "ENC_TEST_SCREEN";

        [Test]
        public void 开工之前_什么指令都不接()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out _);

            Assert.AreEqual(BattlePrompt.NotStarted, screen.Prompt);
            Assert.IsFalse(screen.ChooseSkill("SKL_HIT"), "还没开工就不该接指令。");
            Assert.IsFalse(screen.ChooseTarget(1));
            Assert.IsFalse(screen.ChooseFlee());
            Assert.IsFalse(screen.ChooseEndTurn());
        }

        [Test]
        public void 开局推进_停在等我方下令()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase);
            CollectionAssert.IsNotEmpty(screen.Hud.Commands, "轮到我方时界面必须有按钮可画。");
        }

        [Test]
        public void 单体技_先要求选目标_候选只有敌人()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt);
            Assert.AreEqual("SKL_HIT", screen.PendingSkillId);
            CollectionAssert.AreEqual(
                new[] { session.EnemyUnits[0].RuntimeId },
                screen.TargetCandidateIds,
                "单体攻击的候选只有敌方存活单位。");
        }

        [Test]
        public void 辅助技_候选是自己人()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.HealingSkill("SKL_HEAL", healPower: 20);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HEAL");
            lab.Character("CHR_B", 300, 10, 5, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 10, FiveElement.None, 900, false, "SKL_HIT");

            var screen = BuildScreen(lab, Line("CHR_A", "CHR_B"), Line("ENM_A"), out var session);
            screen.Start();

            Assert.IsTrue(screen.ChooseSkill("SKL_HEAL"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt);
            CollectionAssert.AreEquivalent(
                new[] { session.PlayerUnits[0].RuntimeId, session.PlayerUnits[1].RuntimeId },
                screen.TargetCandidateIds,
                "治疗技的候选是同阵营存活单位，包括自己。");

            // 升序是破平局口径的一部分，界面据此决定默认选中谁。
            for (var i = 1; i < screen.TargetCandidateIds.Count; i++)
            {
                Assert.Less(screen.TargetCandidateIds[i - 1], screen.TargetCandidateIds[i]);
            }
        }

        [Test]
        public void 取消选目标_退回下令()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out _);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");

            Assert.IsTrue(screen.CancelTargeting());
            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.IsNull(screen.PendingSkillId);
            CollectionAssert.IsEmpty(screen.TargetCandidateIds);
        }

        [Test]
        public void 候选之外的目标_不提交给内核()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");

            var before = session.EnemyUnits[0].Health;
            Assert.IsFalse(screen.ChooseTarget(session.PlayerUnits[0].RuntimeId), "自己不是这个技能的合法目标。");
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt, "非法点击不该改变状态。");
            Assert.AreEqual(before, session.EnemyUnits[0].Health);
        }

        [Test]
        public void 用完主行动_敌方自己走完再轮回我方()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");
            Assert.IsTrue(screen.ChooseTarget(session.EnemyUnits[0].RuntimeId));

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt, "中间不该停在敌方回合上。");
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
            Assert.Greater(session.ActionCount, 2, "我方一手 + 敌方一手 + 再轮到我方。");
            Assert.Less(screen.Hud.EnemyRows[0].Health, 3000, "敌方掉血了，说明攻击确实打进了内核。");
        }

        [Test]
        public void 结束回合_也会把敌方走完再轮回我方()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            Assert.IsTrue(screen.ChooseEndTurn());

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
        }

        [Test]
        public void 打光敌人_结局是胜利()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 200, breakDamage: 0);
            lab.Character("CHR_A", 300, 300, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 40, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 64)
            {
                Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
                Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
                Assert.IsTrue(screen.ChooseTarget(session.EnemyUnits[0].RuntimeId));
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerVictory, session.Outcome);
            Assert.AreEqual(BattleOutcome.PlayerVictory, screen.Hud.Outcome);
            CollectionAssert.IsEmpty(screen.Hud.Commands, "打完了就不该再有按钮。");
        }

        [Test]
        public void 我方全灭_结局是失败()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 1, breakDamage: 0);
            lab.Character("CHR_A", 20, 1, 0, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 400, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 64)
            {
                Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
                Assert.IsTrue(screen.ChooseEndTurn());
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerDefeat, session.Outcome);
        }

        [Test]
        public void 逃跑成功_结局是逃脱()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 1, breakDamage: 0);
            lab.Character("CHR_A", 3000, 1, 50, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 100, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.Greater(screen.Hud.EscapeChance, 0.5f, "我方想跑，成功率该偏向高的一端。");

            // 掷骰是确定性的（固定种子），但「第几次才成功」不该被写死，
            // 因此循环到挣脱为止，并给一个远超需要的上限。
            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 500)
            {
                Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
                Assert.IsTrue(screen.ChooseFlee());
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerEscaped, session.Outcome);
            Assert.IsTrue(screen.LastResult.Escaped);
        }

        private static BattleScreenController NewScreen(BattleLab lab, out BattleSession session) =>
            BuildScreen(lab, Line("CHR_A"), Line("ENM_A"), out session);

        private static BattleScreenController BuildScreen(
            BattleLab lab,
            List<BattleUnitBlueprint> party,
            List<BattleUnitBlueprint> enemies,
            out BattleSession session)
        {
            // 会话与界面共用同一个目录实例：各取一个虽然结果相同，但那是两处会各自漂移的来源。
            var registry = lab.Registry();
            session = new BattleSession(
                lab.Config,
                registry,
                BattleLab.Stream(),
                new BattleSetup(Encounter, party, enemies));
            return new BattleScreenController(session, registry);
        }

        private static void BuildDuel(BattleLab lab)
        {
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");
        }

        private static List<BattleUnitBlueprint> Line(params string[] definitionIds)
        {
            var line = new List<BattleUnitBlueprint>(definitionIds.Length);
            for (var i = 0; i < definitionIds.Length; i++)
            {
                line.Add(new BattleUnitBlueprint(definitionIds[i], BattleFormation.SlotForIndex(i)));
            }

            return line;
        }
    }
}
