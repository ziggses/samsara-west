using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 属性聚合（角色基础值 + 在身装备 + 经文）的口径用例。
    /// </summary>
    /// <remarks>
    /// 钉三件事：一是<b>加算</b>与<b>夹下限</b>这两个口径本身；二是每一件被跳过的装备都必须留下错误码——
    /// 「坏数据不毁战斗」不等于「坏数据静默生效」；三是装备与经文带来的技能怎么并进清单
    /// （自带在前、挂载在后、重复只留一手，坏引用同样留码）。断言只认错误码，不认日志文案，改文案不会误伤用例。
    /// </remarks>
    [TestFixture]
    public sealed class CharacterStatsTests
    {
        [Test]
        public void 没穿东西时_等于角色基础值()
        {
            using var lab = new BattleLab();
            var character = lab.Character(
                "CHR_TEST_A", 100, 10, 5, 8, FiveElement.None, breakThreshold: 30, maxSpirit: 40);

            var snapshot = CharacterStatsResolver.Resolve(character, null, lab.Registry(), new ValidationReport());

            Assert.AreEqual(100, snapshot.MaxHealth);
            Assert.AreEqual(40, snapshot.MaxSpirit);
            Assert.AreEqual(10, snapshot.Attack);
            Assert.AreEqual(5, snapshot.Defense);
            Assert.AreEqual(8, snapshot.Speed);
            Assert.AreEqual(30, snapshot.BreakThreshold);
        }

        [Test]
        public void 装备与经文的加成一律加在基础值上_不乘不缩放()
        {
            using var lab = new BattleLab();
            var character = lab.Character(
                "CHR_TEST_A", 100, 10, 5, 8, FiveElement.None, breakThreshold: 30, maxSpirit: 40);
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                defenseBonus: 2,
                speedBonus: 3,
                healthBonus: 20,
                spiritBonus: 10);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor, defenseBonus: 7);
            lab.Sutra(
                "SUT_TEST_STILLNESS",
                attackBonus: 6,
                defenseBonus: 1,
                healthBonus: 20,
                spiritBonus: 5,
                breakThresholdBonus: 4,
                spiritRegenPerTurn: 3);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_STAFF"),
                    new LoadoutEntry("Armor", "EQP_TEST_ROBE"),
                    new LoadoutEntry("Sutra", "SUT_TEST_STILLNESS"),
                },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean, "三件都配得上，不该产生任何结论。");
            Assert.AreEqual(140, snapshot.MaxHealth, "100 + 20（武器）+ 20（经文）。");
            Assert.AreEqual(55, snapshot.MaxSpirit, "40 + 10（武器）+ 5（经文）。");
            Assert.AreEqual(21, snapshot.Attack, "10 + 5 + 6，全部加算。");
            Assert.AreEqual(15, snapshot.Defense, "5 + 2 + 7 + 1。");
            Assert.AreEqual(11, snapshot.Speed, "8 + 3。");
            Assert.AreEqual(34, snapshot.BreakThreshold, "30 + 4：护体上限目前只有经文能加。");
        }

        [Test]
        public void 栏位对不上的那一件被跳过_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Armor", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            Assert.AreEqual(10, snapshot.Attack, "栏位对不上就不该生效，哪怕数值算得出来。");
            AssertHasCode(report, "LOADOUT_SLOT_MISMATCH");
        }

        [Test]
        public void 经文挂在非经文栏位_同样被跳过()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Sutra("SUT_TEST_RAGE", attackBonus: 6, breakThresholdBonus: 4);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "SUT_TEST_RAGE") },
                lab.Registry(),
                report);

            Assert.AreEqual(10, snapshot.Attack);
            Assert.AreEqual(30, snapshot.BreakThreshold);
            AssertHasCode(report, "LOADOUT_SLOT_MISMATCH");
        }

        [Test]
        public void 限定角色的装备戴到别人身上_被跳过_本人照常生效()
        {
            using var lab = new BattleLab();
            var owner = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            var other = lab.Character("CHR_TEST_B", 100, 10, 5, 8);
            lab.Equipment(
                "EQP_TEST_RUYI",
                EquipmentSlot.Weapon,
                attackBonus: 8,
                allowedCharacterIds: "CHR_TEST_A");

            var entry = new[] { new LoadoutEntry("Weapon", "EQP_TEST_RUYI") };

            var wrongReport = new ValidationReport();
            var wrongSnapshot = CharacterStatsResolver.Resolve(other, entry, lab.Registry(), wrongReport);
            Assert.AreEqual(10, wrongSnapshot.Attack, "限定角色不符就不该穿得上。");
            AssertHasCode(wrongReport, "LOADOUT_CHARACTER_NOT_ALLOWED");

            var ownerReport = new ValidationReport();
            var ownerSnapshot = CharacterStatsResolver.Resolve(owner, entry, lab.Registry(), ownerReport);
            Assert.IsTrue(ownerReport.IsClean, "本人装备自己的东西不该有任何结论。");
            Assert.AreEqual(18, ownerSnapshot.Attack, "10 + 8。");
        }

        [Test]
        public void 定义缺失的那一件被跳过_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_NOT_IN_TABLE") },
                lab.Registry(),
                report);

            Assert.AreEqual(10, snapshot.Attack);
            AssertHasCode(report, "LOADOUT_ITEM_MISSING");
        }

        [Test]
        public void 非装备种类的_ID_挂在装备栏_被跳过()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Enemy("ENM_TEST_A", 200, 10, 0, 10);

            var report = new ValidationReport();
            CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "ENM_TEST_A") },
                lab.Registry(),
                report);

            AssertHasCode(report, "LOADOUT_KIND_UNSUPPORTED");
        }

        [Test]
        public void 栏位名只认与枚举同名的写法_其余一律不认()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            var report = new ValidationReport();

            // 三种都必须拦下：小写、数字串（Enum.TryParse 会认）、逗号组合
            // （"Weapon, Armor" 会凑出 Talisman，正是要防的「猜一个栏位出来」）。
            CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("weapon", "EQP_TEST_STAFF"),
                    new LoadoutEntry("1", "EQP_TEST_STAFF"),
                    new LoadoutEntry("Weapon, Armor", "EQP_TEST_STAFF"),
                },
                lab.Registry(),
                report);

            Assert.AreEqual(3, report.ErrorCount, "三种写法都不该被认出来。");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                Assert.AreEqual("LOADOUT_SLOT_UNKNOWN", report.Issues[i].Code, "认不出栏位一律归到同一个错误码。");
            }
        }

        [Test]
        public void 同一栏位两件_只算先出现的那件并给出警告()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_IRON", EquipmentSlot.Weapon, attackBonus: 5);
            lab.Equipment("EQP_TEST_RUYI", EquipmentSlot.Weapon, attackBonus: 100);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_IRON"),
                    new LoadoutEntry("Weapon", "EQP_TEST_RUYI"),
                },
                lab.Registry(),
                report);

            Assert.AreEqual(15, snapshot.Attack, "10 + 5：后一件整件不算，不是把两件加起来。");
            Assert.AreEqual(0, report.ErrorCount, "重复栏位是警告而不是错误：战斗要打得下去。");
            AssertHasCode(report, "LOADOUT_SLOT_DUPLICATE");
        }

        [Test]
        public void 负加成把数值压到下限_不会算出零血零速()
        {
            using var lab = new BattleLab();
            var character = lab.Character(
                "CHR_TEST_A", 100, 10, 5, 8, FiveElement.None, breakThreshold: 30, maxSpirit: 40);
            lab.Equipment("EQP_TEST_HEAVY", EquipmentSlot.Weapon, speedBonus: -999);
            lab.Sutra(
                "SUT_TEST_ASCETIC",
                attackBonus: -999,
                healthBonus: -999,
                spiritBonus: -999,
                breakThresholdBonus: -999,
                healthCostPerTurn: 4);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_HEAVY"),
                    new LoadoutEntry("Sutra", "SUT_TEST_ASCETIC"),
                },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean);
            Assert.AreEqual(1, snapshot.MaxHealth, "生命下限 1，与 BattleUnit 构造时的兜底一致。");
            Assert.AreEqual(0, snapshot.MaxSpirit, "灵力下限 0。");
            Assert.AreEqual(0, snapshot.Attack, "攻击下限 0。");
            Assert.AreEqual(5, snapshot.Defense, "没被扣到的项保持基础值。");
            Assert.AreEqual(1, snapshot.Speed, "速度下限 1，否则单位永远排在最后。");
            Assert.AreEqual(1, snapshot.BreakThreshold, "护体下限 1。");
        }

        [Test]
        public void 空格位的行既不报错也不生效()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);

            var report = new ValidationReport();

            // 存档里可能留着一行但已取消装备：这是正常状态，不该每次都报一条结论。
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", null), new LoadoutEntry("Sutra", string.Empty) },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean);
            Assert.AreEqual(10, snapshot.Attack);
        }

        [Test]
        public void 需求等级目前不参与校验_角色尚无等级来源()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment(
                "EQP_TEST_LATE",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                requiredLevel: 99,
                allowedCharacterIds: "CHR_TEST_A");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_LATE") },
                lab.Registry(),
                report);

            // 这条用例钉的是<b>缺口</b>而不是规则：角色目前没有等级字段，等级校验无处可施，
            // 于是高需求等级的装备照样穿得上。等级系统落地那天这条会先变红，提醒把校验补上。
            Assert.IsTrue(report.IsClean, "聚合层不替等级系统下判断，也不会每次都刷一条警告。");
            Assert.AreEqual(15, snapshot.Attack, "10 + 5：等级暂不拦人。");
        }

        [Test]
        public void 经文的每回合效果也进快照()
        {
            using var lab = new BattleLab();
            var character = lab.Character(
                "CHR_TEST_A", 100, 10, 5, 8, FiveElement.None, breakThreshold: 30, maxSpirit: 40);
            lab.Sutra("SUT_TEST_STILLNESS", spiritBonus: 5, spiritRegenPerTurn: 3);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Sutra", "SUT_TEST_STILLNESS") },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean);
            Assert.AreEqual(45, snapshot.MaxSpirit, "灵力上限照旧加算。");
            Assert.AreEqual(3, snapshot.SpiritRegenPerTurn, "每回合回灵原样带出去，等战斗侧按自己的时机结算。");
            Assert.AreEqual(0, snapshot.HealthCostPerTurn, "这本经文不要代价。");
        }

        [Test]
        public void 苦修的生命代价进快照_与上限的夹下限各管各的()
        {
            using var lab = new BattleLab();
            var character = lab.Character(
                "CHR_TEST_A", 100, 10, 5, 8, FiveElement.None, breakThreshold: 30, maxSpirit: 40);
            lab.Sutra("SUT_TEST_ASCETIC", healthBonus: -999, healthCostPerTurn: 4);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Sutra", "SUT_TEST_ASCETIC") },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean);
            Assert.AreEqual(1, snapshot.MaxHealth, "生命上限照旧夹在下限 1。");
            Assert.AreEqual(4, snapshot.HealthCostPerTurn, "代价是每回合 4 点：它是反复发生的量，不跟着上限一起夹。");
        }

        [Test]
        public void 没穿经文时_每回合效果都是零()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean);
            Assert.AreEqual(0, snapshot.SpiritRegenPerTurn, "装备没有「每回合效果」这一说，只有经文有。");
            Assert.AreEqual(0, snapshot.HealthCostPerTurn);
        }

        [Test]
        public void 装备与经文带来的技能并进清单_自带在前挂载在后()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8, skillIds: "SKL_TEST_HIT");
            lab.AttackSkill("SKL_TEST_MOUNTED");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "SKL_TEST_MOUNTED");
            lab.Sutra("SUT_TEST_MIND", passiveSkillId: "SKL_TEST_MOUNTED");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_STAFF"),
                    new LoadoutEntry("Sutra", "SUT_TEST_MIND"),
                },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean, "两处授予同一手是集合语义上的重复，不是坏数据，不该留结论。");
            CollectionAssert.AreEqual(
                new[] { "SKL_TEST_HIT", "SKL_TEST_MOUNTED" },
                snapshot.SkillIds,
                "自带技能在前、挂载的在后；重复的只留第一次出现。");
        }

        [Test]
        public void 挂载的技能查不到_跳过那一手_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8, skillIds: "SKL_TEST_HIT");
            lab.AttackSkill("SKL_TEST_HIT");
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                passiveSkillId: "SKL_NOT_IN_TABLE");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            Assert.AreEqual(15, snapshot.Attack, "挂载引用坏掉不影响这件装备的数值加成：坏的是那一手指向，不是整件装备。");
            CollectionAssert.AreEqual(
                new[] { "SKL_TEST_HIT" },
                snapshot.SkillIds,
                "查不到的那一手不进来，自带技能照旧。");
            AssertHasCode(report, "LOADOUT_SKILL_MISSING");
        }

        [Test]
        public void 挂载指向的不是技能_跳过那一手_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Enemy("ENM_TEST_A", 200, 10, 0, 10);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveSkillId: "ENM_TEST_A");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            CollectionAssert.IsEmpty(snapshot.SkillIds, "指向敌人的 ID 不是技能，进不了清单。");
            AssertHasCode(report, "LOADOUT_SKILL_KIND_UNSUPPORTED");
        }

        [Test]
        public void 装备与经文带来的被动并进清单_按在身顺序()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Status("STS_TEST_FORCE", isDebuff: false, permanent: true);
            lab.Passive("PSV_TEST_FORCE", "STS_TEST_FORCE");
            lab.Passive("PSV_TEST_BREATH", "STS_TEST_FORCE");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");
            lab.Sutra("SUT_TEST_MIND", passiveId: "PSV_TEST_BREATH");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_STAFF"),
                    new LoadoutEntry("Sutra", "SUT_TEST_MIND"),
                },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean, "两条各来自一处、互不重复，不该留结论。");
            CollectionAssert.AreEqual(
                new[] { "PSV_TEST_FORCE", "PSV_TEST_BREATH" },
                snapshot.PassiveIds,
                "按在身清单顺序排：先武器后经文。顺序在这里没有玩法含义，只为日志与排查读起来稳定。");
            CollectionAssert.IsEmpty(snapshot.SkillIds, "被动与技能各走各的清单：带了被动不等于多了一手技能。");
        }

        [Test]
        public void 两处授予同一条被动_只算一条()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Status("STS_TEST_FORCE", isDebuff: false, permanent: true);
            lab.Passive("PSV_TEST_FORCE", "STS_TEST_FORCE");
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "PSV_TEST_FORCE");
            lab.Sutra("SUT_TEST_MIND", passiveId: "PSV_TEST_FORCE");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[]
                {
                    new LoadoutEntry("Weapon", "EQP_TEST_STAFF"),
                    new LoadoutEntry("Sutra", "SUT_TEST_MIND"),
                },
                lab.Registry(),
                report);

            Assert.IsTrue(report.IsClean, "重复不是坏数据：它不改变任何行为，因此不报。");
            CollectionAssert.AreEqual(
                new[] { "PSV_TEST_FORCE" },
                snapshot.PassiveIds,
                "被动清单是集合语义：会就是会，两处给同一条只算一条。");
        }

        [Test]
        public void 没穿东西时_被动清单是空的()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);

            var report = new ValidationReport();
            var bare = CharacterStatsResolver.Resolve(character, null, lab.Registry(), report);
            var withEmpty = CharacterStatsResolver.Resolve(
                character,
                System.Array.Empty<LoadoutEntry>(),
                lab.Registry(),
                report);

            CollectionAssert.IsEmpty(bare.PassiveIds, "角色没有「自带被动」这一列：裸装就是一条都没有。");
            CollectionAssert.IsEmpty(withEmpty.PassiveIds, "传空清单与不传，被动清单必须一样。");
        }

        [Test]
        public void 挂载的被动查不到_跳过那一条_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment(
                "EQP_TEST_STAFF",
                EquipmentSlot.Weapon,
                attackBonus: 5,
                passiveId: "PSV_NOT_IN_TABLE");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            Assert.AreEqual(15, snapshot.Attack, "被动引用坏掉不影响这件装备的数值加成：坏的是那一栏指向，不是整件装备。");
            CollectionAssert.IsEmpty(snapshot.PassiveIds, "查不到的被动不进来。");
            AssertHasCode(report, "LOADOUT_PASSIVE_MISSING");
        }

        [Test]
        public void 挂载指向的不是被动_跳过那一条_并留下错误码()
        {
            using var lab = new BattleLab();
            var character = lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Status("STS_TEST_FORCE", isDebuff: false, permanent: true);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, passiveId: "STS_TEST_FORCE");

            var report = new ValidationReport();
            var snapshot = CharacterStatsResolver.Resolve(
                character,
                new[] { new LoadoutEntry("Weapon", "EQP_TEST_STAFF") },
                lab.Registry(),
                report);

            CollectionAssert.IsEmpty(snapshot.PassiveIds, "被动栏只接受被动：指向状态的 ID 进不了清单。");
            AssertHasCode(report, "LOADOUT_PASSIVE_KIND_UNSUPPORTED");
        }

        private static void AssertHasCode(ValidationReport report, string code)
        {
            var codes = new List<string>(report.Issues.Count);
            for (var i = 0; i < report.Issues.Count; i++)
            {
                codes.Add(report.Issues[i].Code);
            }

            CollectionAssert.Contains(codes, code, $"期望报告里有 {code}，实际是 [{string.Join(", ", codes)}]。");
        }
    }
}
