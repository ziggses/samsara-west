using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 剧情强制撤退的行为锁定：已拍板口径是「<b>不算失败</b>，流程侧据此回到剧情前的节点」，
    /// 因此它必须是与战败、逃跑都不同的<b>单独结局</b>。
    /// </summary>
    /// <remarks>
    /// 它是第二条「不靠打死人收场」的路径（第一条是逃跑），所以同样两头都锁：
    /// 一头是结局本身的取值与幂等（重复调用不改写结局、不重复发事件）；
    /// 一头是它与回合阶段机的接缝（撤完之后还能不能动、双方还站着是不是就不算全灭）。
    /// </remarks>
    [TestFixture]
    public sealed class BattleForcedRetreatTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 强制撤退_写下单独结局而不是失败()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            Assert.IsTrue(session.ForceRetreat(), "战斗还在进行，这一次调用应当把它收场。");

            Assert.AreEqual(BattleOutcome.ForcedRetreat, session.Outcome);
            Assert.AreNotEqual(BattleOutcome.PlayerDefeat, session.Outcome, "剧情撤退不是打输了。");
            Assert.AreNotEqual(BattleOutcome.PlayerEscaped, session.Outcome, "它也不是掷骰跑掉的那一种。");
            Assert.IsTrue(session.IsFinished);
            Assert.AreEqual(TurnPhase.Finished, session.Phase);
        }

        [Test]
        public void 打到一半强制撤退_双方都还有人站着()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var enemy = session.EnemyUnits[0];
            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_HIT", enemy.RuntimeId).Success);
            Assert.Less(enemy.Health, enemy.MaxHealth, "先打一下，证明这确实是打到一半的战斗。");

            Assert.IsTrue(session.ForceRetreat());

            Assert.Greater(session.AliveCount(BattleSide.Player), 0);
            Assert.Greater(session.AliveCount(BattleSide.Enemy), 0, "双方都还站着：它不是靠谁全灭收场的。");
        }

        [Test]
        public void 强制撤退之后_这一手就不能再打了()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();
            session.ForceRetreat();

            var enemy = session.EnemyUnits[0];
            Assert.AreEqual(
                BattleCommandRejection.BattleFinished,
                session.UseSkill("SKL_HIT", enemy.RuntimeId).Rejection,
                "收场之后不能再出手。");
            Assert.IsNull(session.BeginNextTurn(), "收场之后不该再推进出任何行动者。");
        }

        [Test]
        public void 已经收场的战斗_再强制撤退返回false且不改写结局()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 50, 5, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();
            session.ForceRetreat();

            Assert.IsFalse(session.ForceRetreat(), "第二次调用不该再收一次场。");
            Assert.AreEqual(BattleOutcome.ForcedRetreat, session.Outcome, "结局是第一次写下的那个，不许被后一次覆盖。");
        }

        [Test]
        public void 战斗还没开始也能强制撤退_剧情可以在开场前撤()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));

            Assert.AreEqual(TurnPhase.Idle, session.Phase, "还没轮到任何人。");
            Assert.IsTrue(session.ForceRetreat(), "剧本在开场前判定「这仗不该打」也应当能撤。");
            Assert.AreEqual(BattleOutcome.ForcedRetreat, session.Outcome);
        }

        [Test]
        public void 强制撤退只发一条战斗结束事件()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");

            var bus = BattleLab.Bus();
            var endedCount = 0;
            BattleEndedEvent? ended = null;
            using var subscription = bus.Subscribe<BattleEndedEvent>(
                BattleEventChannel.Channel,
                e =>
                {
                    endedCount++;
                    ended = e;
                });

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            session.BeginNextTurn();

            session.ForceRetreat();
            session.ForceRetreat();

            Assert.AreEqual(1, endedCount, "重复调用不许重复发事件，否则流程侧会结算两次。");
            Assert.IsTrue(ended.HasValue);
            Assert.AreEqual(BattleOutcome.ForcedRetreat, ended.Value.Outcome);
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
