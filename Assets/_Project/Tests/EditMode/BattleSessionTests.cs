using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗流程内核的行为锁定：出手顺序、回合阶段机、指令被拒的原因。
    /// </summary>
    /// <remarks>
    /// 这一层刻意只断言「顺序、阶段、拒绝原因」，不碰伤害数字——
    /// 伤害口径有自己的用例，混在一起会让失败原因不可读。
    /// </remarks>
    [TestFixture]
    public sealed class BattleSessionTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 回合未开始时任何指令都被拒为没有行动者()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 100, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));

            Assert.AreEqual(TurnPhase.Idle, session.Phase);
            Assert.IsNull(session.CurrentActor, "还没开始回合，不该有当前行动者。");
            Assert.AreEqual(BattleCommandRejection.NoActiveTurn, session.UseSkill("SKL_HIT").Rejection);
            Assert.AreEqual(BattleCommandRejection.NoActiveTurn, session.MoveTo(new FormationSlot(1, 0)).Rejection);
            Assert.AreEqual(BattleCommandRejection.NoActiveTurn, session.SwapWith(0).Rejection);
        }

        [Test]
        public void 速度相同时按布阵顺序出手且我方先于敌方()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 100, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));

            var order = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var actor = session.BeginNextTurn();
                Assert.IsNotNull(actor, $"第 {i + 1} 手不该没有行动者。");
                order.Add(actor.DefinitionId);
                session.EndTurn();
            }

            CollectionAssert.AreEqual(new[] { "CHR_A", "CHR_B", "ENM_A" }, order);
            Assert.AreEqual(3, session.ActionCount);
        }

        [Test]
        public void 速度决定行动值_快者在自己第二个回合之前不会被超车()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_FAST", 200, 10, 0, 40, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_SLOW", 200, 10, 0, 14, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_FAST", "CHR_SLOW"), Line("ENM_A")));

            var order = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                order.Add(session.BeginNextTurn().DefinitionId);
                session.EndTurn();
            }

            // 行动值是行动间隔，出手后累加而不是重置：速度 40 的间隔是 250，速度 14 的是 714，速度 5 的是 2000。
            // 快者先动两次（250、500），它的第三手要等到 750，比慢者的 714 晚——所以第三手是慢者。
            // 速度差一旦拉开（比如慢者用 10，间隔 1000），快者的第三手 750 仍早于 1000，这里就锁不住了。
            CollectionAssert.AreEqual(new[] { "CHR_FAST", "CHR_FAST", "CHR_SLOW" }, order);
        }

        [Test]
        public void 主行动结算后进入移动阶段且不能第二次出手()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();
            Assert.AreEqual(TurnPhase.MainAction, session.Phase);

            var enemyId = session.EnemyUnits[0].RuntimeId;
            var first = session.UseSkill("SKL_HIT", enemyId);
            Assert.IsTrue(first.Success);
            Assert.AreEqual(TurnPhase.MoveOrSwap, session.Phase, "主行动用掉之后只该剩移动／换位。");

            var second = session.UseSkill("SKL_HIT", enemyId);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(BattleCommandRejection.WrongPhase, second.Rejection);
        }

        [Test]
        public void 主行动之后仍然可以移动()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var actor = session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success);

            var move = session.MoveTo(new FormationSlot(2, 1));
            Assert.IsTrue(move.Success);
            Assert.AreEqual(2, actor.Slot.Column);
            Assert.AreEqual(1, actor.Slot.Row);
        }

        [Test]
        public void 技能不在技能表里被拒为未掌握()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.AttackSkill("SKL_OTHER");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            var result = session.UseSkill("SKL_OTHER", session.EnemyUnits[0].RuntimeId);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.SkillNotOwned, result.Rejection);
        }

        [Test]
        public void 技能表里写了但数据里没有被拒为未知技能()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_MISSING");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            // 未掌握与「写了但没配」是两回事：前者是角色配错，后者是技能表漏行，分开报才好定位。
            var result = session.UseSkill("SKL_MISSING", session.EnemyUnits[0].RuntimeId);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.UnknownSkill, result.Rejection);
        }

        [Test]
        public void 灵力不够的技能被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.AttackSkill("SKL_COSTLY", power: 20, breakDamage: 0, spiritCost: 30);
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 10, "SKL_HIT", "SKL_COSTLY");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            var result = session.UseSkill("SKL_COSTLY", session.EnemyUnits[0].RuntimeId);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.NotEnoughSpirit, result.Rejection);
        }

        [Test]
        public void 单体攻击技能不给目标被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            var result = session.UseSkill("SKL_HIT");
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.NoValidTarget, result.Rejection);
        }

        [Test]
        public void 单体攻击技能指向同伴被拒为阵营不符()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            session.BeginNextTurn();

            var allyId = session.PlayerUnits[1].RuntimeId;
            var result = session.UseSkill("SKL_HIT", allyId);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.TargetSideMismatch, result.Rejection);
        }

        [Test]
        public void 冷却期内的技能不能再用_冷却回合数按自己的回合数递减()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.AttackSkill("SKL_CD", power: 10, breakDamage: 0, cooldownTurns: 2);
            lab.Character("CHR_A", 500, 10, 0, 20, FiveElement.None, 30, 50, "SKL_HIT", "SKL_CD");
            lab.Enemy("ENM_A", 9999, 1, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var actor = session.BeginNextTurn();
            var enemyId = session.EnemyUnits[0].RuntimeId;

            Assert.IsTrue(session.UseSkill("SKL_CD", enemyId).Success);
            Assert.AreEqual(2, actor.GetCooldown("SKL_CD"));
            session.EndTurn();
            Assert.AreEqual(2, actor.GetCooldown("SKL_CD"), "用掉的那一回合不该立刻递减冷却。");

            AdvanceUntilActor(session, "CHR_A");
            Assert.AreEqual(BattleCommandRejection.SkillOnCooldown, session.UseSkill("SKL_CD", enemyId).Rejection);
            session.EndTurn();
            Assert.AreEqual(1, actor.GetCooldown("SKL_CD"));

            AdvanceUntilActor(session, "CHR_A");
            Assert.AreEqual(BattleCommandRejection.SkillOnCooldown, session.UseSkill("SKL_CD", enemyId).Rejection);
            session.EndTurn();
            Assert.AreEqual(0, actor.GetCooldown("SKL_CD"));

            AdvanceUntilActor(session, "CHR_A");
            Assert.IsTrue(session.UseSkill("SKL_CD", enemyId).Success, "冷却走完之后应当可以再用。");
        }

        [Test]
        public void 移动改变站位且不消耗主行动()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            var actor = session.BeginNextTurn();

            var move = session.MoveTo(new FormationSlot(2, 1));
            Assert.IsTrue(move.Success);
            Assert.AreEqual(2, actor.Slot.Column);
            Assert.AreEqual(1, actor.Slot.Row);

            Assert.IsTrue(
                session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success,
                "移动不该吃掉主行动。");
        }

        [Test]
        public void 移动到已被同伴占用的格子被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            session.BeginNextTurn();

            var result = session.MoveTo(new FormationSlot(1, 0));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleCommandRejection.SlotOccupied, result.Rejection);
        }

        [Test]
        public void 一次移动用完之后再移动被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            Assert.IsTrue(session.MoveTo(new FormationSlot(2, 0)).Success);
            var second = session.MoveTo(new FormationSlot(2, 1));
            Assert.IsFalse(second.Success);
            Assert.AreEqual(BattleCommandRejection.MoveAlreadyUsed, second.Rejection);
        }

        [Test]
        public void 换位交换双方站位_换敌人被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            var actor = session.BeginNextTurn();
            var ally = session.PlayerUnits[1];

            var swap = session.SwapWith(ally.RuntimeId);
            Assert.IsTrue(swap.Success);
            Assert.AreEqual(1, actor.Slot.Column, "换位之后双方站位应当互换。");
            Assert.AreEqual(0, ally.Slot.Column);

            // 换位换的是「这一次机会」，主行动还在。
            Assert.IsTrue(session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success);
        }

        [Test]
        public void 换位目标不是存活同伴时被拒()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 100, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 999, 1, 0, 1, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            var actor = session.BeginNextTurn();

            Assert.AreEqual(
                BattleCommandRejection.SwapTargetInvalid,
                session.SwapWith(session.EnemyUnits[0].RuntimeId).Rejection,
                "换位对象必须是同伴。");

            Assert.AreEqual(
                BattleCommandRejection.SwapTargetInvalid,
                session.SwapWith(actor.RuntimeId).Rejection,
                "不能和自己换位。");
        }

        [Test]
        public void 敌方回合不需要指令_开始下一手就直接结算完毕()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 5, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 20, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var player = session.PlayerUnits[0];

            var actor = session.BeginNextTurn();
            Assert.AreEqual(BattleSide.Enemy, actor.Side);
            Assert.AreEqual(TurnPhase.Finished, session.Phase, "敌方回合应当在开始下一手时就自动走完。");
            Assert.Less(player.Health, player.MaxHealth, "敌方回合应当真的打到了人。");
            Assert.AreEqual(1, session.ActionCount);
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

        private static void AdvanceUntilActor(BattleSession session, string definitionId)
        {
            for (var guard = 0; guard < 32; guard++)
            {
                var actor = session.BeginNextTurn();
                Assert.IsNotNull(actor, "战斗提前结束了，用例设定的前提没有成立。");
                if (actor.DefinitionId == definitionId)
                {
                    return;
                }

                Assert.AreEqual(BattleSide.Enemy, actor.Side, "轮到的不是目标单位，说明用例的速度设定算错了。");
                session.EndTurn();
            }

            Assert.Fail($"连续 {32} 手都没轮到 {definitionId}。");
        }
    }
}
