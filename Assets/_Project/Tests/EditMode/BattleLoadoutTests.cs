using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 在身装备接进战斗回路：基础值 + 装备 + 经文 → 登场数值。
    /// </summary>
    /// <remarks>
    /// 算术本身在 <c>CharacterStatsTests</c> 里逐项锁过，这里只证明<b>战斗真的用上了那份结果</b>：
    /// 装上加成之后单位数值变大、血量按新上限开满；没接在身清单时行为与从前逐字一致；
    /// 加成在建单位那一刻定型，之后换装不会回头改动场上的单位。
    /// </remarks>
    [TestFixture]
    public sealed class BattleLoadoutTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 装上加成之后_战斗单位按新数值登场()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                speedBonus: 1,
                healthBonus: 20);
            lab.Sutra("SUT_TEST_RAGE", attackBonus: 6, healthBonus: 20, breakThresholdBonus: 4);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_STAFF");
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_RAGE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(140, unit.MaxHealth, "100 + 20（武器）+ 20（经文）。");
            Assert.AreEqual(140, unit.Health, "加成先并进上限，再按新上限开满血。");
            Assert.AreEqual(40, unit.Spirit, "40：两件都没加灵力，基础值原样留着。");
            Assert.AreEqual(21, unit.BaseAttack, "10 + 5 + 6。");
            Assert.AreEqual(21, unit.EffectiveAttack, "身上还没有状态，有效攻击力就是基础值。");
            Assert.AreEqual(11, unit.BaseSpeed, "10 + 1：速度从装备来。");
            Assert.AreEqual(34, unit.BreakThreshold, "30 + 4。");
        }

        [Test]
        public void 没接在身清单时_数值与从前逐字一致()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5, healthBonus: 20);

            var bare = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout: null));
            var withEmptyLoadout = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), new BattleLoadout()));
            var unit = bare.PlayerUnits[0];
            var untouched = withEmptyLoadout.PlayerUnits[0];

            Assert.AreEqual(100, unit.MaxHealth, "没传装备来源就是裸装：直接取角色基础值。");
            Assert.AreEqual(10, unit.BaseAttack);
            Assert.AreEqual(10, unit.BaseSpeed);
            Assert.AreEqual(30, unit.BreakThreshold);

            Assert.AreEqual(unit.MaxHealth, untouched.MaxHealth, "传一个空的清单与不传，结果必须一样。");
            Assert.AreEqual(unit.BaseAttack, untouched.BaseAttack);
        }

        [Test]
        public void 在身清单按成员分配_没登记的人吃不到加成()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Character("CHR_B", 100, 10, 5, 10, FiveElement.None, breakThreshold: 30, maxSpirit: 40, "SKL_HIT");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_STAFF");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A"), loadout));
            var equipped = FindPlayer(session, "CHR_A");
            var bare = FindPlayer(session, "CHR_B");

            Assert.AreEqual(15, equipped.BaseAttack, "10 + 5。");
            Assert.AreEqual(10, bare.BaseAttack, "清单里没有 CHR_B，一分加成也不该沾到。");
        }

        [Test]
        public void 装备配错的那一件只跳过自己_战斗照打()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            // 武器挂在护甲栏：聚合层会记一条错误码并跳过，但战斗必须打得下去。
            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Armor", "EQP_TEST_STAFF");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));

            Assert.AreEqual(1, session.PlayerUnits.Count, "坏装备不该让人无法开战。");
            Assert.AreEqual(1, session.EnemyUnits.Count);
            Assert.AreEqual(10, session.PlayerUnits[0].BaseAttack, "跳过的件不参与数值。");
            Assert.AreEqual(100, session.PlayerUnits[0].MaxHealth);
        }

        [Test]
        public void 敌人不穿装备_清单里写了也不生效()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            var loadout = new BattleLoadout();
            loadout.Equip("ENM_A", "Weapon", "EQP_TEST_STAFF");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));

            Assert.AreEqual(10, session.EnemyUnits[0].BaseAttack, "敌人的数值只由敌人定义决定，装备栏不认敌方。");
            Assert.AreEqual(10, session.PlayerUnits[0].BaseAttack, "顺带确认这份清单也没串到我方身上。");
        }

        [Test]
        public void 加成在建单位那一刻定型_之后换装不改场上单位()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment("EQP_TEST_IRON", EquipmentSlot.Weapon, attackBonus: 5);
            lab.Equipment("EQP_TEST_RUYI", EquipmentSlot.Weapon, attackBonus: 50);

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_IRON");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];
            Assert.AreEqual(15, unit.BaseAttack, "10 + 5。");

            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_RUYI");

            Assert.AreEqual(15, unit.BaseAttack, "换装只影响下一场：进行中的战斗读的是进场快照。");
        }

        /// <summary>一名基础值刻意取「一眼能算」的角色与一个不会立刻结束战斗的敌人。</summary>
        private static void RegisterCombatants(BattleLab lab)
        {
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 5, 10, FiveElement.None, breakThreshold: 30, maxSpirit: 40, "SKL_HIT");
            lab.Enemy("ENM_A", 300, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");
        }

        private static BattleUnit FindPlayer(BattleSession session, string definitionId)
        {
            for (var i = 0; i < session.PlayerUnits.Count; i++)
            {
                if (session.PlayerUnits[i].DefinitionId == definitionId)
                {
                    return session.PlayerUnits[i];
                }
            }

            Assert.Fail($"我方阵容里没有 '{definitionId}'。");
            return null;
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
