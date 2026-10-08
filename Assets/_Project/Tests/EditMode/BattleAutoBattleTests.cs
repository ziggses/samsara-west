using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 自动战斗（<see cref="BattleSession.RunToEnd"/>）的行为锁定。
    /// </summary>
    /// <remarks>
    /// 口径见 Docs/战斗内核-v1.md 第 11.1 节第 6 条：它<b>就是</b>自动战斗，
    /// 我方与敌方走同一套规划器，只产出 <see cref="BattleOutcome"/>，
    /// <c>maxActions</c> 用尽即封顶（封顶之后战局仍然可以由玩家接手）。
    /// </remarks>
    [TestFixture]
    public sealed class BattleAutoBattleTests
    {
        private const string Encounter = "ENC_TEST_AUTO";

        [Test]
        public void 自动战斗_双方都由规划器出手()
        {
            using var lab = new BattleLab();
            BuildLongFight(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var player = session.PlayerUnits[0];
            var enemy = session.EnemyUnits[0];

            session.RunToEnd();

            Assert.AreNotEqual(BattleOutcome.Ongoing, session.Outcome);
            Assert.Less(enemy.Health, enemy.MaxHealth, "我方没有任何人下过指令，也该由规划器代打出去。");
            Assert.Less(player.Health, player.MaxHealth, "敌方那一侧本来就由规划器出手。");
        }

        [Test]
        public void 自动战斗_单次调用最多推进maxActions手()
        {
            using var lab = new BattleLab();
            BuildLongFight(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));

            var first = session.RunToEnd(2);

            Assert.AreEqual(2, first, "返回值是累计手数，封顶时正好等于上限。");
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome, "两手的战斗不该已经分出胜负。");
            Assert.AreEqual(TurnPhase.Finished, session.Phase, "封顶停下时不该把回合停在半途。");

            // 封顶不是「战斗卡死」：接着跑仍然能跑完。
            Assert.Greater(session.RunToEnd(), first);
            Assert.AreNotEqual(BattleOutcome.Ongoing, session.Outcome);
        }

        [Test]
        public void 自动战斗_分出胜负之后再调用不再推进()
        {
            using var lab = new BattleLab();
            BuildLongFight(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var actions = session.RunToEnd();
            var outcome = session.Outcome;

            Assert.AreEqual(actions, session.RunToEnd(), "已经有结局的战斗不该再被推进。");
            Assert.AreEqual(outcome, session.Outcome, "重复调用不该改写结局。");
        }

        [Test]
        public void 同种子的自动战斗_每一手仍完全一致()
        {
            using var lab = new BattleLab();
            BuildLongFight(lab);

            var party = Line("CHR_A", "CHR_B");
            var enemies = Line("ENM_A", "ENM_B");
            var first = NewSession(lab, Setup(party, enemies));
            var second = NewSession(lab, Setup(party, enemies));

            Assert.AreEqual(first.RunToEnd(), second.RunToEnd());
            Assert.AreEqual(first.Outcome, second.Outcome);
            Assert.AreEqual(first.RoundNumber, second.RoundNumber);
        }

        private static void BuildLongFight(BattleLab lab)
        {
            // 双方都够肉、伤害都够小：确保「两手封顶」那一条不会被意外打完。
            lab.AttackSkill("SKL_JAB", power: 20, breakDamage: 0);
            lab.Character("CHR_A", 600, 5, 0, 30, FiveElement.None, 30, 50, "SKL_JAB");
            lab.Character("CHR_B", 600, 5, 0, 30, FiveElement.None, 30, 50, "SKL_JAB");
            lab.Enemy("ENM_A", 3000, 5, 0, 30, FiveElement.None, 200, false, "SKL_JAB");
            lab.Enemy("ENM_B", 3000, 5, 0, 30, FiveElement.None, 200, false, "SKL_JAB");
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

        private static BattleSession NewSession(BattleLab lab, BattleSetup setup) =>
            new BattleSession(lab.Config, lab.Registry(), BattleLab.Stream(), setup);
    }
}
