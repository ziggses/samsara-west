using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 经文的每回合效果接进回合回路：自己的回合收尾时回灵、付苦修的生命代价。
    /// </summary>
    /// <remarks>
    /// <para>这里锁三件事：<b>时机</b>（只在「自己」的回合收尾发生，不替别人结算）、
    /// <b>夹上限</b>（回灵不溢出灵力上限）、以及<b>苦修不致死</b>（生命代价最多扣到剩 1 点）。</para>
    /// <para>回多少灵、付多少代价<b>不住在代码里</b>：它们跟着 <c>sutras.csv</c> 的
    /// <c>spiritRegenPerTurn</c> / <c>healthCostPerTurn</c> 走，用例只证明内核把这些数用在了对的地方、
    /// 对的时刻。算术本身在 <c>CharacterStatsTests</c> 里逐项锁过。</para>
    /// <para>建场时敌人只给 <c>power = 0</c> 的攻击技能并刻意更慢：这样在我方前两手之内敌人一次都不出手，
    /// 于是「生命只被苦修扣过」这条前提成立，断言不必再去抵消敌人那几下手。</para>
    /// </remarks>
    [TestFixture]
    public sealed class BattleSutraTurnTests
    {
        private const string Encounter = "ENC_TEST_SUTRA";
        private const int SpiritRegen = 3;
        private const int HealthCost = 4;

        [Test]
        public void 自己的回合收尾才回灵_别人的回合不算()
        {
            using var lab = new BattleLab();

            // 行动值是行动间隔：我方速度 10 → 1000，敌人 12 → 833，于是敌人只先手一次。
            lab.AttackSkill("SKL_COST", power: 10, breakDamage: 0, spiritCost: 20);
            lab.AttackSkill("SKL_TAP", power: 0, breakDamage: 0);
            lab.Character("CHR_A", 500, 10, 0, 10, FiveElement.None, 999, 50, "SKL_COST");
            lab.Enemy("ENM_A", 5000, 0, 0, 12, FiveElement.None, 999, false, "SKL_TAP");
            lab.Sutra("SUT_TEST_STILLNESS", spiritRegenPerTurn: SpiritRegen);
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_STILLNESS");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];
            var enemy = session.EnemyUnits[0];

            Assert.AreEqual(SpiritRegen, unit.SpiritRegenPerTurn, "经文把这个每回合读数带到了单位身上。");
            Assert.AreEqual(0, enemy.SpiritRegenPerTurn, "敌人不穿经文，这个读数恒为 0。");

            Assert.AreEqual(BattleSide.Enemy, session.BeginNextTurn().Side, "敌人更快，先走一手。");
            var spiritBefore = unit.Spirit;
            session.EndTurn();

            Assert.AreEqual(spiritBefore, unit.Spirit, "别人的回合收尾不该替我回灵。");

            Assert.AreEqual(BattleSide.Player, session.BeginNextTurn().Side);
            Assert.IsTrue(session.UseSkill("SKL_COST", enemy.RuntimeId).Success);
            Assert.AreEqual(spiritBefore - 20, unit.Spirit, "技能先按灵力消耗扣掉。");

            session.EndTurn();

            Assert.AreEqual(spiritBefore - 20 + SpiritRegen, unit.Spirit, "自己这一手收尾才回灵。");
        }

        [Test]
        public void 回灵被灵力上限夹住_满灵时不会溢出()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 0, breakDamage: 0);
            lab.Character("CHR_A", 500, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 5000, 0, 0, 1, FiveElement.None, 999, false, "SKL_HIT");
            lab.Sutra("SUT_TEST_STILLNESS", spiritRegenPerTurn: SpiritRegen);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_STILLNESS");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(unit.MaxSpirit, unit.Spirit, "开局满灵，于是这一手的回灵全是溢出的。");
            Assert.AreEqual(BattleSide.Player, session.BeginNextTurn().Side, "速度 30 对 1，我方先手。");

            session.EndTurn();

            Assert.AreEqual(unit.MaxSpirit, unit.Spirit, "回灵照旧被灵力上限夹住，不会溢出。");
        }

        [Test]
        public void 苦修的失血在自己的回合收尾结算()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 0, breakDamage: 0);
            lab.Character("CHR_A", 500, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 5000, 0, 0, 1, FiveElement.None, 999, false, "SKL_HIT");
            lab.Sutra("SUT_TEST_ASCETIC", healthBonus: 20, healthCostPerTurn: HealthCost);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_ASCETIC");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(HealthCost, unit.HealthCostPerTurn);
            Assert.AreEqual(520, unit.MaxHealth, "生命加成照旧先并进上限。");
            Assert.AreEqual(520, unit.Health, "开满血。");

            session.BeginNextTurn();
            session.EndTurn();

            Assert.AreEqual(520 - HealthCost, unit.Health, "自己这一手收尾付一次代价。");
        }

        [Test]
        public void 苦修不会把自己扣死_生命停在下限一()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 0, breakDamage: 0);
            lab.Character("CHR_A", 6, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 5000, 0, 0, 1, FiveElement.None, 999, false, "SKL_HIT");
            lab.Sutra("SUT_TEST_ASCETIC", healthCostPerTurn: HealthCost);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_ASCETIC");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(6, unit.Health);

            session.BeginNextTurn();
            session.EndTurn();
            Assert.AreEqual(2, unit.Health, "6 - 4：代价按实数扣。");

            session.BeginNextTurn();
            session.EndTurn();

            Assert.AreEqual(1, unit.Health, "只剩 2 点时这一手最多扣到剩 1 点。");
            Assert.IsTrue(unit.IsAlive, "苦修可以把自己逼到濒死，但不该替对手收人头。");
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome, "战斗没有因为苦修而结束。");
        }

        [Test]
        public void 敌人不带走灵与苦修_清单里写了也不生效()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 0, 10, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 300, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");
            lab.Sutra("SUT_TEST_ASCETIC", spiritRegenPerTurn: SpiritRegen, healthCostPerTurn: HealthCost);

            var loadout = new BattleLoadout();
            loadout.Equip("ENM_A", "Sutra", "SUT_TEST_ASCETIC");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));

            Assert.AreEqual(0, session.EnemyUnits[0].SpiritRegenPerTurn, "经文只挂在我方成员身上。");
            Assert.AreEqual(0, session.EnemyUnits[0].HealthCostPerTurn);
            Assert.AreEqual(0, session.PlayerUnits[0].SpiritRegenPerTurn, "顺带确认这份清单也没串到我方。");
        }

        private static BattleSetup Setup(
            List<BattleUnitBlueprint> party,
            List<BattleUnitBlueprint> enemies,
            IBattleLoadout loadout = null) =>
            new BattleSetup(Encounter, party, enemies, loadout: loadout);

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
            new BattleSession(lab.Config, lab.Registry(), BattleLab.Stream(), setup, BattleLab.Bus());
    }
}
