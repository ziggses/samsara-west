using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.UI;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 技能挂载接进战斗：装备与经文能把额外的技能带进这场战斗。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 聚合层的算术、顺序与坏引用错误码在 <c>CharacterStatsTests</c> 里锁过，这里只证明<b>战斗真的用上了那份清单</b>：
    /// 挂载来的技能排在自带之后、点得出来也放得出去、消耗与冷却与自带技能一模一样；
    /// 坏引用只跳过那一手，战斗照打；换装不改进行中的战斗。
    /// </para>
    /// <para>
    /// 「被动技能」里<b>被动</b>那一层语义（不占行动、常驻生效）目前没有承载物——本版把
    /// <c>passiveSkillId</c> 落成「多一手可用技能」（ADR-019）。所以这里特意钉住<b>同权</b>：
    /// 谁哪天给挂载来的技能开了特权（免灵力、不占主行动、不跳冷却），下面这几条会先红。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BattleSkillMountTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 装备带来的技能_进了单位的技能清单并排在自带之后()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5, passiveSkillId: "SKL_MOUNTED");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            CollectionAssert.AreEqual(
                new[] { "SKL_HIT", "SKL_MOUNTED" },
                unit.SkillIds,
                "自带技能在前、挂载的在后：顺序就是优先级。");
            Assert.IsTrue(unit.HasSkill("SKL_MOUNTED"), "挂上来的那一手就是自己会的，判定入口与自带技能同一个。");
            Assert.AreEqual(15, unit.BaseAttack, "顺带确认这件装备的数值加成也照旧生效。");
            Assert.IsFalse(session.EnemyUnits[0].HasSkill("SKL_MOUNTED"), "敌人不穿装备，挂载与它无关。");
        }

        [Test]
        public void 经文带来的技能_同样进清单()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30);
            lab.Sutra("SUT_TEST_MIND", spiritRegenPerTurn: 3, passiveSkillId: "SKL_MOUNTED");

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_MIND");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];

            CollectionAssert.AreEqual(new[] { "SKL_HIT", "SKL_MOUNTED" }, unit.SkillIds);
            Assert.AreEqual(3, unit.SpiritRegenPerTurn, "每回合效果与挂载的技能各走各的，互不干扰。");
        }

        [Test]
        public void 两处授予同一手_只算一手()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_MOUNTED");
            lab.Equipment("EQP_TEST_CHARM", EquipmentSlot.Talisman, passiveSkillId: "SKL_MOUNTED");
            lab.Sutra("SUT_TEST_MIND", passiveSkillId: "SKL_MOUNTED");

            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_STAFF");
            loadout.Equip("CHR_A", "Talisman", "EQP_TEST_CHARM");
            loadout.Equip("CHR_A", "Sutra", "SUT_TEST_MIND");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));

            CollectionAssert.AreEqual(
                new[] { "SKL_HIT", "SKL_MOUNTED" },
                session.PlayerUnits[0].SkillIds,
                "三件都给了同一手：清单里只留一份，而且只留第一个位置。");
        }

        [Test]
        public void 挂载自己已经会的那一手_不会多出一份()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));

            CollectionAssert.AreEqual(
                new[] { "SKL_HIT" },
                session.PlayerUnits[0].SkillIds,
                "自带的那一手不会被挂载复制成两份。");
        }

        [Test]
        public void 挂载的技能_点得出来也放得出去()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30, spiritCost: 5);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_MOUNTED");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var hud = new BattleHudModel(session, lab.Registry());
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            hud.Refresh();

            // 取不到就判失败：界面读的是单位的技能清单，挂上来的那一手必须自动就有按钮。
            var button = Command(hud, BattleCommandId.Skill, "SKL_MOUNTED");
            Assert.IsTrue(button.Enabled);
            Assert.AreEqual(5, button.SpiritCost, "按钮上的消耗取自技能定义，不因为是从装备来的就免掉。");

            var before = enemy.Health;
            var result = session.UseSkill("SKL_MOUNTED", enemy.RuntimeId);

            Assert.IsTrue(result.Success, "按钮点下去要能真的放出来。");
            Assert.Less(enemy.Health, before, "打出去就得掉血——不是「清单里有、放出来是空的」。");
        }

        [Test]
        public void 挂载的技能与自带技能同权_照样吃灵力与进冷却()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab, skillSpiritCost: 5, skillCooldown: 2);
            lab.AttackSkill("SKL_MOUNTED", power: 30, spiritCost: 5, cooldownTurns: 2);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_MOUNTED");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            session.BeginNextTurn();
            Assert.AreEqual(40, unit.Spirit, "开场灵力按上限开满。");

            var result = session.UseSkill("SKL_MOUNTED", session.EnemyUnits[0].RuntimeId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(35, unit.Spirit, "灵力照扣：挂载不带来减免。");
            Assert.AreEqual(2, unit.GetCooldown("SKL_MOUNTED"), "冷却照进：挂载不跳过冷却。");
            Assert.IsFalse(unit.IsSkillReady("SKL_MOUNTED"), "放完就在冷却里，与自带技能走在同一条路上。");
        }

        [Test]
        public void 灵力不够时_挂载的技能与自带技能同因被拒()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30, spiritCost: 999);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_MOUNTED");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var hud = new BattleHudModel(session, lab.Registry());
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            hud.Refresh();

            var button = Command(hud, BattleCommandId.Skill, "SKL_MOUNTED");
            Assert.AreEqual(
                BattleCommandRejection.NotEnoughSpirit,
                button.Rejection,
                "灵力不够时按钮同因置灰：挂载不绕过负担判定。");
            Assert.IsFalse(button.Enabled);

            var result = session.UseSkill("SKL_MOUNTED", enemy.RuntimeId);

            Assert.AreEqual(
                BattleCommandRejection.NotEnoughSpirit,
                result.Rejection,
                "内核的拒绝理由与按钮上的理由同源，没有第二条判据。");
            Assert.AreEqual(300, enemy.Health, "被拒的那一手不该打出任何伤害。");
        }

        [Test]
        public void 挂载的技能查不到_只跳过那一手_战斗照打()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                passiveSkillId: "SKL_NOT_IN_TABLE");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), EquipWeapon("EQP_TEST_STAFF")));
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(1, session.PlayerUnits.Count, "坏引用不该让人无法开战。");
            Assert.AreEqual(15, unit.BaseAttack, "这件装备的数值加成照旧生效：坏的是那一手指向。");
            CollectionAssert.AreEqual(new[] { "SKL_HIT" }, unit.SkillIds, "查不到的那一手不进来。");
        }

        [Test]
        public void 换装不改场上单位的技能清单_进场快照()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);
            lab.AttackSkill("SKL_MOUNTED", power: 30);
            lab.AttackSkill("SKL_OTHER", power: 30);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_MOUNTED");
            lab.Equipment("EQP_TEST_IRON", EquipmentSlot.Weapon, passiveSkillId: "SKL_OTHER");

            var loadout = EquipWeapon("EQP_TEST_STAFF");
            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout));
            var unit = session.PlayerUnits[0];
            Assert.IsTrue(unit.HasSkill("SKL_MOUNTED"));

            loadout.Equip("CHR_A", "Weapon", "EQP_TEST_IRON");

            Assert.IsTrue(unit.HasSkill("SKL_MOUNTED"), "换装只影响下一场：技能清单也是进场快照。");
            Assert.IsFalse(unit.HasSkill("SKL_OTHER"), "换上的那件带来的技能不会追进进行中的战斗。");
        }

        [Test]
        public void 没穿东西时_技能清单就是角色自带的()
        {
            using var lab = new BattleLab();
            RegisterCombatants(lab);

            var bare = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), loadout: null));
            var withEmpty = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), new BattleLoadout()));

            CollectionAssert.AreEqual(new[] { "SKL_HIT" }, bare.PlayerUnits[0].SkillIds, "裸装：不多不少。");
            CollectionAssert.AreEqual(
                bare.PlayerUnits[0].SkillIds,
                withEmpty.PlayerUnits[0].SkillIds,
                "传一个空的在身清单与不传，技能清单必须一样。");
        }

        /// <summary>一名基础值刻意取「一眼能算」的角色与一个不会立刻结束战斗的敌人。</summary>
        private static void RegisterCombatants(BattleLab lab, int skillSpiritCost = 0, int skillCooldown = 0)
        {
            lab.AttackSkill("SKL_HIT", spiritCost: skillSpiritCost, cooldownTurns: skillCooldown);
            lab.Character("CHR_A", 100, 10, 5, 10, FiveElement.None, breakThreshold: 30, maxSpirit: 40, "SKL_HIT");
            lab.Enemy("ENM_A", 300, 10, 0, 10, FiveElement.None, 20, false, "SKL_HIT");
        }

        private static BattleLoadout EquipWeapon(string itemId)
        {
            var loadout = new BattleLoadout();
            loadout.Equip("CHR_A", "Weapon", itemId);
            return loadout;
        }

        /// <summary>取一条指令；取不到就直接判失败——指令是值类型，没有「返回 null 让调用方自己判」这一说。</summary>
        private static BattleCommandOption Command(BattleHudModel hud, BattleCommandId id, string skillId)
        {
            for (var i = 0; i < hud.Commands.Count; i++)
            {
                var option = hud.Commands[i];
                if (option.Id == id && option.SkillId == skillId)
                {
                    return option;
                }
            }

            Assert.Fail($"指令列表里没有 {id}:{skillId}，实际是 [{string.Join(", ", hud.Commands)}]。");
            return default;
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
