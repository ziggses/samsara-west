using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 破防、状态叠加与纯辅助技能的三条规则锁定。
    /// </summary>
    /// <remarks>
    /// 前两条走完整流程（护体是「行动的结果」），
    /// 「更强的顶掉旧的」这一条只能直接驱动 <see cref="BattleUnit"/>：
    /// 一个状态 Id 在数据里只有一份定义，同 Id 不同强度在正式数据里不存在，
    /// 走流程永远进不了那条分支。
    /// </remarks>
    [TestFixture]
    public sealed class BattleBreakAndStatusTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 护体被削到零即破防_破防期间承伤更高_到期回满护体()
        {
            using var lab = new BattleLab();
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);
            lab.AttackSkill("SKL_BREAK", power: 10, breakDamage: 20);
            lab.AttackSkill("SKL_BITE", power: 5, breakDamage: 0);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 30, 50, "SKL_BREAK");
            // 破防计时按「被打者自己的回合」推进，所以敌人必须也走得动：
            // 同为速度 30、同速按布阵我方先手，敌人正好夹在我方两手之间行动。
            // 若把敌人调成速度 5（间隔 2000），它前三手都轮不到，破防永远不会到期。
            lab.Enemy("ENM_A", 400, 5, 0, 30, FiveElement.None, 20, false, "SKL_BITE");

            var bus = BattleLab.Bus();
            var recovered = 0;
            using var subscription = bus.Subscribe<BattleBreakRecoveredEvent>(
                BattleEventChannel.Channel,
                _ => recovered++);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            var first = session.UseSkill("SKL_BREAK", enemy.RuntimeId);

            Assert.IsTrue(first.Success);
            Assert.AreEqual(1, first.BrokenCount);
            // 护体 20、这一击正好削 20：断言「削到零即破防」，不是「削到负数才破防」。
            Assert.AreEqual(20, first.Effects[0].BreakDamage);
            Assert.IsTrue(enemy.IsBroken);
            Assert.AreEqual(0, enemy.BreakValue);
            Assert.AreEqual(lab.Config.BrokenDurationTurns, enemy.BrokenTurnsRemaining);
            session.EndTurn();

            AdvanceToPlayer(session);
            var second = session.UseSkill("SKL_BREAK", enemy.RuntimeId);

            Assert.IsTrue(second.Success);
            Assert.Greater(second.TotalDamage, first.TotalDamage, "破防期间同一次攻击应当更疼。");
            Assert.AreEqual(0, second.Effects[0].BreakDamage, "已经破防的单位不该被重复削护体。");
            session.EndTurn();

            AdvanceToPlayer(session);

            Assert.AreEqual(1, recovered, "破防到期只该报一次恢复。");
            Assert.IsFalse(enemy.IsBroken);
            Assert.AreEqual(enemy.BreakThreshold, enemy.BreakValue, "恢复之后护体应当回满。");
        }

        [Test]
        public void 可叠加状态按层数累积并放大效果()
        {
            using var lab = new BattleLab();
            lab.Status("STS_CURSE", durationTurns: 3, stackRule: StackRule.Stackable, maxStacks: 3, attackModifier: -0.2f);
            lab.StatusSkill("SKL_CURSE", "STS_CURSE");
            lab.AttackSkill("SKL_BITE", power: 5, breakDamage: 0);
            lab.Character("CHR_A", 600, 5, 0, 30, FiveElement.None, 30, 50, "SKL_CURSE");
            lab.Enemy("ENM_A", 999, 40, 0, 5, FiveElement.None, 20, false, "SKL_BITE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            var first = session.UseSkill("SKL_CURSE", enemy.RuntimeId);
            Assert.IsTrue(first.Success);
            Assert.AreEqual(StatusChangeKind.Applied, first.Effects[0].StatusChange);
            Assert.AreEqual(1, enemy.FindStatus("STS_CURSE").Stacks);
            var oneStackAttack = enemy.EffectiveAttack;
            session.EndTurn();

            AdvanceToPlayer(session);
            var second = session.UseSkill("SKL_CURSE", enemy.RuntimeId);

            var cursed = enemy.FindStatus("STS_CURSE");
            Assert.AreEqual(StatusChangeKind.Stacked, second.Effects[0].StatusChange);
            Assert.AreEqual(2, cursed.Stacks, "可叠加状态第二次施加应当叠一层，而不是覆盖。");
            Assert.AreEqual(-0.4f, enemy.AttackModifier, 1e-4f, "两层负面应当各算各的。");
            Assert.Less(enemy.EffectiveAttack, oneStackAttack, "层数越高，攻击压得越低。");
            Assert.AreEqual(3, cursed.RemainingTurns, "叠层时也应把持续时间刷回满。");
        }

        [Test]
        public void 刷新型状态只延长时长不叠层()
        {
            using var lab = new BattleLab();
            lab.Status("STS_MARK", durationTurns: 2, stackRule: StackRule.Refresh, maxStacks: 1, defenseModifier: -0.3f);
            lab.StatusSkill("SKL_MARK", "STS_MARK");
            lab.AttackSkill("SKL_BITE", power: 5, breakDamage: 0);
            lab.Character("CHR_A", 600, 5, 0, 30, FiveElement.None, 30, 50, "SKL_MARK");
            lab.Enemy("ENM_A", 999, 40, 0, 5, FiveElement.None, 20, false, "SKL_BITE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            session.UseSkill("SKL_MARK", enemy.RuntimeId);
            Assert.AreEqual(1, enemy.FindStatus("STS_MARK").Stacks);
            session.EndTurn();

            AdvanceToPlayer(session);
            var again = session.UseSkill("SKL_MARK", enemy.RuntimeId);

            var marked = enemy.FindStatus("STS_MARK");
            Assert.AreEqual(StatusChangeKind.Refreshed, again.Effects[0].StatusChange);
            Assert.AreEqual(1, marked.Stacks, "不可叠加的状态即使反复施加也只能是一层。");
            Assert.AreEqual(2, marked.RemainingTurns);
            Assert.AreEqual(-0.3f, enemy.DefenseModifier, 1e-4f);
        }

        [Test]
        public void 纯辅助技能只回血不倒扣血()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 5, breakDamage: 0);
            lab.HealingSkill("SKL_MEND", healPower: 60);
            lab.Character("CHR_TANK", 200, 5, 20, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_MEDIC", 120, 5, 5, 30, FiveElement.None, 30, 50, "SKL_HIT", "SKL_MEND");
            lab.Enemy("ENM_A", 999, 40, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_TANK", "CHR_MEDIC"), Line("ENM_A")));
            var tank = session.PlayerUnits[0];

            // 先挨一下，否则治疗会被满血上限吃掉，断言就成了空转。
            AdvanceUntilDamaged(session, tank);

            var before = tank.Health;
            var result = session.UseSkill("SKL_MEND", tank.RuntimeId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.TotalDamage, "治疗技能不该造成任何伤害。");
            Assert.AreEqual(0, result.Effects[0].Damage);
            Assert.Greater(result.TotalHealing, 0);
            Assert.AreEqual(before + result.TotalHealing, tank.Health);
        }

        [Test]
        public void 只保留最强的一次_更强的一次会顶掉旧的弱状态()
        {
            using var lab = new BattleLab();
            var weak = lab.Status("STS_MARK", durationTurns: 3, stackRule: StackRule.StrongestOnly, defenseModifier: -0.2f);
            var strong = lab.Status("STS_MARK", durationTurns: 3, stackRule: StackRule.StrongestOnly, defenseModifier: -0.6f);

            var unit = NewUnit(BattleSide.Enemy, "ENM_A");

            Assert.AreEqual(StatusChangeKind.Applied, unit.ApplyStatus(weak));
            Assert.AreEqual(-0.2f, unit.DefenseModifier, 1e-4f);
            Assert.AreEqual(StatusChangeKind.Refreshed, unit.ApplyStatus(weak), "同一个弱状态再来一次只该刷新时长。");

            Assert.AreEqual(StatusChangeKind.Replaced, unit.ApplyStatus(strong), "更强的一次应当顶掉旧的。");
            Assert.AreEqual(1, unit.Statuses.Count);
            Assert.AreEqual(-0.6f, unit.DefenseModifier, 1e-4f);

            Assert.AreEqual(StatusChangeKind.Refreshed, unit.ApplyStatus(weak), "更弱的一次不该把强的顶回来。");
            Assert.AreEqual(-0.6f, unit.DefenseModifier, 1e-4f);
        }

        private static BattleUnit NewUnit(BattleSide side, string definitionId) =>
            new BattleUnit(
                0,
                side,
                0,
                BattleFormation.SlotForIndex(0),
                definitionId,
                "test.battle.unit.name",
                side == BattleSide.Player ? DefinitionKind.Character : DefinitionKind.Enemy,
                200,
                50,
                20,
                5,
                10,
                FiveElement.None,
                30,
                Array.Empty<string>(),
                false);

        /// <summary>一直推进到「指定单位活着且已掉血」为止，再交还控制权。</summary>
        private static void AdvanceUntilDamaged(BattleSession session, BattleUnit target)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                var actor = session.BeginNextTurn();
                Assert.IsNotNull(actor, "战斗提前结束了，用例设定的前提没有成立。");

                if (actor.Side == BattleSide.Player)
                {
                    // 只要目标已经掉血就交还控制权：治疗者与被打的人不是同一个单位，
                    // 这里等的是「治疗者的回合 + 有人已经受伤」这个组合。
                    if (target.Health < target.MaxHealth)
                    {
                        return;
                    }

                    session.EndTurn();
                    continue;
                }

                session.EndTurn();
            }

            Assert.Fail("连续 64 手都没等到目标掉血。");
        }

        private static void AdvanceToPlayer(BattleSession session)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                var actor = session.BeginNextTurn();
                Assert.IsNotNull(actor, "战斗提前结束了，用例设定的前提没有成立。");
                if (actor.Side == BattleSide.Player)
                {
                    return;
                }

                session.EndTurn();
            }

            Assert.Fail("连续 64 手都没轮到我方。");
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
