using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 整场战斗层面的行为锁定：可复现性、胜负判定、敌方预告是否守信。
    /// </summary>
    [TestFixture]
    public sealed class BattleRunTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 同种子跑两遍_每一手的行动数与逐单位状态完全一致()
        {
            using var lab = new BattleLab();
            BuildAttritionFight(lab);

            var first = NewSession(lab, Setup(Line("CHR_A", "CHR_B", "CHR_C"), Line("ENM_X", "ENM_Y")));
            var second = NewSession(lab, Setup(Line("CHR_A", "CHR_B", "CHR_C"), Line("ENM_X", "ENM_Y")));

            var firstActions = first.RunToEnd();
            var secondActions = second.RunToEnd();

            Assert.AreEqual(firstActions, secondActions, "同种子的两场战斗不该走出不同的手数。");
            Assert.AreEqual(first.Outcome, second.Outcome);
            Assert.AreEqual(first.RoundNumber, second.RoundNumber);
            Assert.AreEqual(first.Units.Count, second.Units.Count);

            for (var i = 0; i < first.Units.Count; i++)
            {
                var a = first.Units[i];
                var b = second.Units[i];
                Assert.AreEqual(a.DefinitionId, b.DefinitionId);
                Assert.AreEqual(a.Health, b.Health, $"{a.DefinitionId} 的残余血量在两场里不一致。");
                Assert.AreEqual(a.BreakValue, b.BreakValue, $"{a.DefinitionId} 的护体值在两场里不一致。");
                Assert.AreEqual(a.ActionValue, b.ActionValue, $"{a.DefinitionId} 的行动值在两场里不一致。");
                Assert.AreEqual(a.TurnsTaken, b.TurnsTaken, $"{a.DefinitionId} 的出手次数在两场里不一致。");
            }
        }

        [Test]
        public void 战斗能在有限手数内跑完并判出胜负()
        {
            using var lab = new BattleLab();
            BuildAttritionFight(lab);

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B", "CHR_C"), Line("ENM_X", "ENM_Y")));
            var actions = session.RunToEnd();

            Assert.AreNotEqual(BattleOutcome.Ongoing, session.Outcome, "RunToEnd 返回后不该还是未分胜负。");
            Assert.AreEqual(BattleOutcome.PlayerVictory, session.Outcome);
            Assert.Greater(actions, 0);
            Assert.LessOrEqual(actions, 512, "手数上限是防死循环的兜底，正常战斗不该撞到它。");
            Assert.AreEqual(0, session.AliveCount(BattleSide.Enemy));
        }

        [Test]
        public void 敌方预告的技能就是它真的放出来的技能()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_BITE", power: 18, breakDamage: 5);
            lab.Character("CHR_A", 600, 5, 0, 5, FiveElement.None, 30, 50, "SKL_BITE");
            lab.Enemy("ENM_A", 999, 20, 0, 30, FiveElement.None, 20, false, "SKL_BITE");

            var bus = BattleLab.Bus();
            var usedSkills = new List<string>();
            using var subscription = bus.Subscribe<BattleDamagedEvent>(
                BattleEventChannel.Channel,
                e => usedSkills.Add(e.SkillId));

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            var enemy = session.EnemyUnits[0];
            var player = session.PlayerUnits[0];

            var intent = session.GetIntent(enemy.RuntimeId);
            Assert.IsTrue(intent.HasIntent, "轮到敌方出手之前，预告就该可读。");
            Assert.AreEqual("SKL_BITE", intent.SkillId);
            Assert.IsFalse(intent.TargetIsRandom);
            CollectionAssert.AreEqual(new[] { player.RuntimeId }, intent.TargetRuntimeIds);

            var actor = session.BeginNextTurn();
            Assert.AreEqual(enemy.RuntimeId, actor.RuntimeId);
            CollectionAssert.Contains(usedSkills, "SKL_BITE", "预告的技能必须就是实际放出来的技能。");
            Assert.Less(player.Health, player.MaxHealth);
        }

        [Test]
        public void 随机目标的技能只预告随机_不预告具体目标()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_WILD", power: 18, breakDamage: 5, target: TargetRule.RandomEnemy);
            lab.Character("CHR_A", 600, 5, 0, 5, FiveElement.None, 30, 50, "SKL_WILD");
            lab.Character("CHR_B", 600, 5, 0, 4, FiveElement.None, 30, 50, "SKL_WILD");
            lab.Enemy("ENM_A", 999, 20, 0, 30, FiveElement.None, 20, false, "SKL_WILD");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            var intent = session.GetIntent(session.EnemyUnits[0].RuntimeId);

            // 落点既然是随机抽的，就不该假装知道要打谁：预告宁可不给，也不能给错。
            Assert.IsTrue(intent.HasIntent);
            Assert.AreEqual("SKL_WILD", intent.SkillId);
            Assert.IsTrue(intent.TargetIsRandom);
            Assert.AreEqual(0, intent.TargetRuntimeIds.Count);
        }

        [Test]
        public void 被控住的敌方不再有预告_轮到它时直接跳过并掉一层状态()
        {
            using var lab = new BattleLab();
            // 预告窗口要盖住两手：第一手是我方（负责控住），第二手才是敌方（负责被跳过）。
            BattleLab.SetPrivate(lab.Config, "_intentPreviewLead", 2);
            lab.AttackSkill("SKL_BITE", power: 18, breakDamage: 5);
            lab.Status("STS_STUN", durationTurns: 1, preventsAction: true);
            lab.StatusSkill("SKL_STUN", "STS_STUN");
            lab.Character("CHR_STUN", 600, 5, 0, 30, FiveElement.None, 30, 50, "SKL_STUN");
            // 敌方要真的在第二手轮到，就必须和我方同速（同速按布阵我方先手）：
            // 行动值是累加的间隔，速度 5（间隔 2000）的话敌方要等到第 7 手才会动。
            lab.Enemy("ENM_A", 999, 20, 0, 30, FiveElement.None, 20, false, "SKL_BITE");

            var bus = BattleLab.Bus();
            var skipped = 0;
            using var subscription = bus.Subscribe<BattleTurnSkippedEvent>(
                BattleEventChannel.Channel,
                _ => skipped++);

            var session = NewSession(lab, Setup(Line("CHR_STUN"), Line("ENM_A")), bus: bus);
            var enemy = session.EnemyUnits[0];
            var player = session.PlayerUnits[0];

            Assert.IsTrue(session.GetIntent(enemy.RuntimeId).HasIntent);

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_STUN", enemy.RuntimeId).Success);
            Assert.IsNotNull(enemy.FindStatus("STS_STUN"), "控住之后敌方身上应当有该状态。");
            Assert.IsFalse(
                session.GetIntent(enemy.RuntimeId).HasIntent,
                "已经动不了的单位不该再给预告。");
            session.EndTurn();

            var actor = session.BeginNextTurn();
            Assert.AreEqual(enemy.RuntimeId, actor.RuntimeId);
            Assert.AreEqual(TurnPhase.Finished, session.Phase);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(player.MaxHealth, player.Health, "被控住的敌方不该打出任何伤害。");
            Assert.IsNull(enemy.FindStatus("STS_STUN"), "持续 1 回合的状态应当在这次跳过之后自然到期。");
        }

        private static void BuildAttritionFight(BattleLab lab)
        {
            // 刻意塞进「随机目标」「治疗」两类技能：它们分别压到随机流和分支选择，
            // 只要流程里有一处顺序不稳，同种子重放就会散架。
            lab.AttackSkill("SKL_SLASH", power: 40, breakDamage: 12);
            lab.HealingSkill("SKL_MEND", healPower: 30);
            lab.AttackSkill("SKL_WILD", power: 22, breakDamage: 6, target: TargetRule.RandomEnemy);
            lab.AttackSkill("SKL_BITE", power: 18, breakDamage: 5);

            lab.Character("CHR_A", 260, 34, 12, 12, FiveElement.None, 30, 50, "SKL_SLASH", "SKL_MEND");
            lab.Character("CHR_B", 240, 30, 10, 11, FiveElement.None, 30, 50, "SKL_SLASH");
            lab.Character("CHR_C", 300, 28, 14, 9, FiveElement.None, 30, 50, "SKL_SLASH", "SKL_MEND");

            lab.Enemy("ENM_X", 150, 16, 6, 8, FiveElement.None, 20, false, "SKL_WILD");
            lab.Enemy("ENM_Y", 130, 14, 5, 10, FiveElement.None, 20, false, "SKL_BITE");
        }

        private static BattleSetup Setup(List<BattleUnitBlueprint> party, List<BattleUnitBlueprint> enemies) =>
            new BattleSetup(Encounter, party, enemies);

        private static List<BattleUnitBlueprint> Line(params string[] definitionIds)
        {
            var line = new List<BattleUnitBlueprint>(definitionIds.Length);
            for (var i = 0; i < definitionIds.Length; i++)
            {
                line.Add(new BattleUnitBlueprint(definitionIds[i], BattleFormation.SlotForIndex(i)));
            }

            return line;
        }

        private static BattleSession NewSession(
            BattleLab lab,
            BattleSetup setup,
            ulong seed = BattleLab.DefaultSeed,
            IEventBus bus = null) =>
            new BattleSession(lab.Config, lab.Registry(), BattleLab.Stream(seed), setup, bus);
    }
}
