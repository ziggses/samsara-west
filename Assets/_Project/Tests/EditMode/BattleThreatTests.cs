using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 敌方规划器的威胁评估口径锁定。
    /// </summary>
    /// <remarks>
    /// 口径见 Docs/战斗数值-v1.md 第 7.5 节与 Docs/战斗内核-v1.md 第 4 节：
    /// 威胁 = 伤害期望 × 权重 + 治疗量 × 权重 + 命中集合里敌对单位的有效速度 × 权重，
    /// 三项权重都在 <see cref="BattleConfig"/> 里，速度那一项默认关着。
    /// 平局取主目标 <c>RuntimeId</c> 最小，<b>不</b>看血条长短。
    /// </remarks>
    [TestFixture]
    public sealed class BattleThreatTests
    {
        private const string Encounter = "ENC_TEST_THREAT";

        [Test]
        public void 威胁值最高的技能获胜_而不是技能表里排第一的()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_WEAK", power: 20, breakDamage: 0);
            lab.AttackSkill("SKL_STRONG", power: 40, breakDamage: 0);
            lab.Character("CHR_A", 600, 5, 0, 30);
            lab.Enemy("ENM_A", 300, 10, 0, 30, FiveElement.None, 20, false, "SKL_WEAK", "SKL_STRONG");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var intent = session.GetIntent(session.EnemyUnits[0].RuntimeId);

            // 承伤 30 / 50，选招必须按威胁值而不是技能表顺序。
            Assert.AreEqual("SKL_STRONG", intent.SkillId);
            CollectionAssert.AreEqual(
                new[] { session.PlayerUnits[0].RuntimeId },
                intent.TargetRuntimeIds);
        }

        [Test]
        public void 按实际承伤挑目标_不按血条长短()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 30, breakDamage: 0);
            lab.Character("CHR_SOFT", 100, 5, 0, 30);
            lab.Character("CHR_TOUGH", 900, 5, 40, 30);
            lab.Enemy("ENM_A", 300, 20, 0, 30, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_SOFT", "CHR_TOUGH"), Line("ENM_A")));
            var intent = session.GetIntent(session.EnemyUnits[0].RuntimeId);

            // 软的吃 30 + 20 − 0 = 50，硬的吃 30 + 20 − 20 = 30。
            // 「不挑残血」说的是不看当前血量，不是「专挑防御高的」。
            Assert.AreEqual(0, intent.TargetRuntimeIds[0], "ENM_A 该打承伤最高的那个。");
            Assert.AreEqual("CHR_SOFT", session.PlayerUnits[0].DefinitionId);
        }

        [Test]
        public void 伤害打平时按目标编号破平局()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 30, breakDamage: 0);
            lab.Character("CHR_SMALL", 100, 5, 10, 30);
            lab.Character("CHR_BIG", 900, 5, 10, 30);
            lab.Enemy("ENM_A", 300, 20, 0, 30, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_SMALL", "CHR_BIG"), Line("ENM_A")));
            var intent = session.GetIntent(session.EnemyUnits[0].RuntimeId);

            // 两个目标同防同元素 → 承伤都是 45 → 平局按 RuntimeId 最小，不按血条最长。
            Assert.AreEqual(
                session.PlayerUnits[0].RuntimeId,
                intent.TargetRuntimeIds[0],
                "威胁打平之后仍然要有一个确定答案，口径是取编号最小的。");
        }

        [Test]
        public void 速度权重默认关_调高之后才优先打快的()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 30, breakDamage: 0);
            lab.Character("CHR_SLOW", 500, 5, 0, 3);
            lab.Character("CHR_FAST", 500, 5, 0, 99);
            lab.Enemy("ENM_A", 300, 20, 0, 30, FiveElement.None, 20, false, "SKL_HIT");

            var byTieBreak = NewSession(lab, Setup(Line("CHR_SLOW", "CHR_FAST"), Line("ENM_A")));
            var slow = byTieBreak.PlayerUnits[0].RuntimeId;
            Assert.AreEqual(
                slow,
                byTieBreak.GetIntent(byTieBreak.EnemyUnits[0].RuntimeId).TargetRuntimeIds[0],
                "速度权重默认是 0，威胁打平就该回到编号破平局。");

            BattleLab.SetPrivate(lab.Config, "_threatWeightSpeed", 1f);

            var bySpeed = NewSession(lab, Setup(Line("CHR_SLOW", "CHR_FAST"), Line("ENM_A")));
            var fast = bySpeed.PlayerUnits[1].RuntimeId;
            Assert.AreEqual(
                fast,
                bySpeed.GetIntent(bySpeed.EnemyUnits[0].RuntimeId).TargetRuntimeIds[0],
                "把速度权重打开之后，先挨打的该是跑得快的那个。");
        }

        [Test]
        public void 治疗量能压过小额伤害_治疗权重默认是一()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 200, breakDamage: 0);
            lab.AttackSkill("SKL_TAP", power: 5, breakDamage: 0);
            lab.HealingSkill("SKL_MEND", healPower: 100);
            lab.Character("CHR_A", 500, 10, 0, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_TANK", 300, 10, 0, 30);
            lab.Enemy("ENM_HEALER", 300, 5, 0, 30, FiveElement.None, 20, false, "SKL_TAP", "SKL_MEND");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_TANK", "ENM_HEALER")));
            var tank = session.EnemyUnits[0];
            var healer = session.EnemyUnits[1];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_HIT", tank.RuntimeId).Success);
            session.EndTurn();
            Assert.Less(tank.Health, tank.MaxHealth);

            var intent = session.GetIntent(healer.RuntimeId);
            Assert.AreEqual("SKL_MEND", intent.SkillId, "治疗 100 与伤害 10 比，威胁是前者高。");
            CollectionAssert.AreEqual(new[] { tank.RuntimeId }, intent.TargetRuntimeIds);
        }

        [Test]
        public void 威胁全为零时仍退回第一个能用的技能()
        {
            using var lab = new BattleLab();
            lab.Status("STS_WEAK", durationTurns: 2);
            lab.StatusSkill("SKL_DEBUFF1", "STS_WEAK");
            lab.StatusSkill("SKL_DEBUFF2", "STS_WEAK");
            lab.Character("CHR_A", 600, 5, 0, 30);
            lab.Enemy("ENM_A", 300, 10, 0, 30, FiveElement.None, 20, false, "SKL_DEBUFF1", "SKL_DEBUFF2");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var intent = session.GetIntent(session.EnemyUnits[0].RuntimeId);

            // 纯增益／纯减益算不出威胁，但不该让「还有牌可打」的单位呆站着。
            Assert.AreEqual("SKL_DEBUFF1", intent.SkillId);
            CollectionAssert.AreEqual(
                new[] { session.PlayerUnits[0].RuntimeId },
                intent.TargetRuntimeIds);
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
