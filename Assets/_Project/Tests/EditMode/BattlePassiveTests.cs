using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.UI;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 被动接进战斗：在身装备与经文带来的被动，在建单位那一刻成了一条常驻状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 聚合层（<c>PassiveIds</c> 的顺序、去重、坏引用错误码）在 <c>CharacterStatsTests</c> 里锁过，
    /// 这里只证明战斗真的用上了那份清单：进场就生效、常驻不到期、每回合量照旧结算、只作用携带者本人、
    /// 与别的状态同权（进得了状态清单，也被修正聚合读到）。
    /// </para>
    /// <para>
    /// 被动的身份在 passives.csv，效果在它指向的那条状态里（ADR-020）：所以「查不到」有两种——
    /// 被动本身查不到（聚合层就挡下来了），与被动指向的状态查不到（这里的两跳查表挡下来）。
    /// 两种都只跳过那一条，战斗照打。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BattlePassiveTests
    {
        private const string Encounter = "ENC_TEST_001";

        /// <summary>被动挂上来的那条状态：常驻、增益、攻击翻倍，数值刻意取「一眼能算」。</summary>
        private const string PassiveStatus = "STS_TEST_FORCE";

        [Test]
        public void 装备带来的被动_进场时成了身上的一条常驻状态()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            var status = unit.FindStatus(PassiveStatus);
            Assert.IsNotNull(status, "被动没进场，装备上那一列就是白填。");
            Assert.IsTrue(status.IsPermanent, "被动挂上来的状态就该是常驻的。");
            Assert.AreEqual(15, unit.BaseAttack, "装备的数值加成照旧生效，两者各走各的。");
            Assert.AreEqual(30, unit.EffectiveAttack, "被动是货真价实的加成，不是只挂个图标：15 × (1 + 100%)。");
        }

        [Test]
        public void 经文带来的被动_同样进场生效()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Sutra("SUT_TEST_MIND", spiritRegenPerTurn: 3, passiveId: "PSV_TEST_FORCE");

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_MIND");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.IsNotNull(unit.FindStatus(PassiveStatus), "经文与装备带来的被动走同一条通路。");
            Assert.AreEqual(3, unit.SpiritRegenPerTurn, "经文的每回合效果与它的被动各走各的。");
        }

        [Test]
        public void 两处授予同一条被动_只算一条()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");
            lab.Equipment("EQP_TEST_CHARM", EquipmentSlot.Talisman, passiveId: "PSV_TEST_FORCE");
            lab.Sutra("SUT_TEST_MIND", passiveId: "PSV_TEST_FORCE");

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_STAFF");
            loadout.Equip("CHR_A", "Talisman", "EQP_TEST_CHARM");
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_MIND");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(1, unit.Statuses.Count, "被动清单是集合语义：三件东西给同一条，身上还是一条。");
            Assert.AreEqual(20, unit.EffectiveAttack, "叠不上去——两处给同一条不改变任何行为，也不翻倍：基础 10 × (1 + 100%)。");
        }

        [Test]
        public void 常驻状态_回合收尾不递减也不到期()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, durationTurns: 1, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];
            var status = unit.FindStatus(PassiveStatus);
            var turnsBefore = status.RemainingTurns;

            var expired = new List<BattleStatusInstance>();
            for (var i = 0; i < 5; i++)
            {
                unit.TickStatuses(expired);
            }

            Assert.IsNotNull(unit.FindStatus(PassiveStatus), "常驻状态轮到第几个回合都还在。");
            Assert.AreEqual(turnsBefore, status.RemainingTurns, "它的剩余回合数不动——常驻不是一个很大的数，是不减的那个数。");
            Assert.AreEqual(0, expired.Count, "没到期过，所以什么都没有从身上掉下来。");
            Assert.AreEqual(20, unit.EffectiveAttack, "五个回合之后加成照旧：基础 10 × (1 + 100%)。");
        }

        [Test]
        public void 常驻的每回合量_照旧结算()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, isDebuff: false, healthDeltaPerTurn: 5, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];
            var expired = new List<BattleStatusInstance>();

            var delta = unit.TickStatuses(expired);

            Assert.AreEqual(5, delta, "常驻只免掉「递减时长」，不免掉「结算每回合量」——回血类被动正是靠这一条成立。");
            Assert.IsNotNull(unit.FindStatus(PassiveStatus), "顺带确认它也没被这一次结算清掉。");
            Assert.AreEqual(0, expired.Count);
        }

        [Test]
        public void 被动只作用携带者本人_队友与敌人都没有()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab, withSecondCharacter: true);
            lab.Status(PassiveStatus, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));

            Assert.IsNotNull(session.PlayerUnits[0].FindStatus(PassiveStatus), "穿它的那个人身上有。");
            Assert.IsNull(session.PlayerUnits[1].FindStatus(PassiveStatus), "没穿的人身上没有：被动不是光环。");
            Assert.AreEqual(10, session.PlayerUnits[1].EffectiveAttack, "队友的攻击一点没动。");
            Assert.IsNull(session.EnemyUnits[0].FindStatus(PassiveStatus), "敌人更不会有。");
        }

        [Test]
        public void 被动查不到_只跳过它自己_战斗照打()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                passiveId: "PSV_NOT_IN_TABLE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(1, session.PlayerUnits.Count, "坏引用不该让人无法开战。");
            Assert.AreEqual(0, unit.Statuses.Count, "查不到的被动不进来。");
            Assert.AreEqual(15, unit.BaseAttack, "这件装备的数值加成照旧生效：坏的是那一栏指向。");
        }

        [Test]
        public void 被动的承载状态查不到_只跳过它自己()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Passive("PSV_TEST_FORCE", "STS_NOT_IN_TABLE");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(0, unit.Statuses.Count, "两跳查表的第二跳失败，就当作这条被动没写。");
            Assert.AreEqual(15, unit.BaseAttack, "装备本身照旧算数。");
        }

        [Test]
        public void 被动指向了状态以外的东西_不施加也不抛异常()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Passive("PSV_TEST_FORCE", "SKL_HIT");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(0, unit.Statuses.Count, "指向技能不是状态：不施加，也不把技能当状态硬塞进去。");
            Assert.AreEqual(10, unit.EffectiveAttack);
        }

        [Test]
        public void 被动是增益_祛毒那条路径摘不走它()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Status(PassiveStatus, isDebuff: false, attackModifier: 1f, permanent: true);
            lab.Passive("PSV_TEST_FORCE", PassiveStatus);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];
            var removed = new List<BattleStatusInstance>();

            var cured = unit.RemoveDebuffs(removed);

            Assert.AreEqual(0, cured, "祛毒只摘负面，被动是增益（今天的口径：没有道具针对它）。");
            Assert.IsNotNull(unit.FindStatus(PassiveStatus), "它照旧在身上。");
            Assert.AreEqual(
                1,
                unit.Statuses.Count,
                "但它确实在单位的清单里——将来任何按清单挑选的移除机制都碰得到它。");
        }

        [Test]
        public void 界面上的常驻状态_不画剩余回合数()
        {
            var permanent = new BattleStatusChip("STS_TEST_FORCE", "test.battle.sts.name", 1, 1, isDebuff: false, isPermanent: true);
            Assert.AreEqual(
                "STS_TEST_FORCEx1(permanent)",
                permanent.ToString(),
                "常驻状态不画到期日：写个数字只会让人以为它要到期。");

            var timed = new BattleStatusChip("STS_TEST_POISON", "test.battle.sts.name", 1, 2, isDebuff: true);
            Assert.AreEqual("STS_TEST_POISONx1(2)", timed.ToString(), "限时状态照旧带回合数。");
        }

        /// <summary>两名基础值刻意取「一眼能算」的角色与一个不会立刻结束战斗的敌人。</summary>
        private static void RegisterCombatants(BattleLab lab, bool withSecondCharacter = false)
        {
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 100, 10, 5, 10, FiveElement.None, breakThreshold: 30, maxSpirit: 40, "SKL_HIT");

            if (withSecondCharacter)
            {
                lab.Character("CHR_B", 100, 10, 5, 10, FiveElement.None, breakThreshold: 30, maxSpirit: 40, "SKL_HIT");
            }

            lab.Enemy("ENM_A", 300, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");
        }

        private static BattleLoadout EquipWeapon(string itemId)
        {
            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", itemId);
            return loadout;
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
