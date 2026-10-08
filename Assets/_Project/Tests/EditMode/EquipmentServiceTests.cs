using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Equipment;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 在身清单的行为：写入口只放行配得上的条目，快照顺序定死，读档整本换掉。
    /// </summary>
    /// <remarks>
    /// <para>这里刻意<b>不</b>验三件本模块声明不做的事——不认识背包（穿上不扣、脱下不还）、
    /// 不管需求等级、不管归属，其中前两件另有专门用例钉住「照旧不做」：
    /// 等背包与等级系统真接上来那天，那两条会先变红，提醒来改。</para>
    /// <para>也不验战斗侧怎么用这份清单：那是 <c>CharacterStatsResolver</c> 的用例与
    /// PlayMode 接线用例的事。本文件只钉「这本簿子自己守不守规矩」。</para>
    /// </remarks>
    internal sealed class EquipmentServiceTests
    {
        [Test]
        public void NewBook_HasNothing()
        {
            using var lab = new BattleLab();
            var equipment = NewService(lab);

            Assert.AreEqual(0, equipment.Snapshot.Count, "新簿子是空的。");
            Assert.AreEqual(0, equipment.LoadoutOf("CHR_TEST_A").Count, "没登记过的成员返回空清单，不是 null。");
            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_TEST_A", "Weapon"));
        }

        [Test]
        public void TryEquip_PutsItemIntoItsSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);
            var equipment = NewService(lab);

            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out var error), error);
            Assert.AreEqual("EQP_TEST_STAFF", equipment.ItemIn("CHR_TEST_A", "Weapon"));

            var loadout = equipment.LoadoutOf("CHR_TEST_A");
            Assert.AreEqual(1, loadout.Count);
            Assert.AreEqual("Weapon", loadout[0].SlotId);
            Assert.AreEqual("EQP_TEST_STAFF", loadout[0].ItemId);
        }

        [Test]
        public void TryEquip_ReplacesWhateverWasInThatSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);
            lab.Equipment("EQP_TEST_RUYI", EquipmentSlot.Weapon, attackBonus: 14);
            var equipment = NewService(lab);

            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_RUYI", out _));

            var loadout = equipment.LoadoutOf("CHR_TEST_A");
            Assert.AreEqual(1, loadout.Count, "一个栏位只存一条：后来者覆盖，不是两条并存。");
            Assert.AreEqual("EQP_TEST_RUYI", loadout[0].ItemId);
        }

        [Test]
        public void TryEquip_CoversAllEightSlots()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_SHIELD", EquipmentSlot.OffHand);
            lab.Equipment("EQP_TEST_HELM", EquipmentSlot.Helmet);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            lab.Equipment("EQP_TEST_BRACER", EquipmentSlot.Bracer);
            lab.Equipment("EQP_TEST_LEGGING", EquipmentSlot.Legging);
            lab.Equipment("EQP_TEST_TALISMAN", EquipmentSlot.Talisman);
            lab.Sutra("SUT_TEST_STILLNESS", spiritBonus: 5);
            var equipment = NewService(lab);

            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "OffHand", "EQP_TEST_SHIELD", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Helmet", "EQP_TEST_HELM", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Armor", "EQP_TEST_ROBE", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Bracer", "EQP_TEST_BRACER", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Legging", "EQP_TEST_LEGGING", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Talisman", "EQP_TEST_TALISMAN", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Sutra", "SUT_TEST_STILLNESS", out _));

            Assert.AreEqual(8, equipment.LoadoutOf("CHR_TEST_A").Count);
        }

        [Test]
        public void TryEquip_YieldsLoadoutInSlotDisplayOrder()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            lab.Sutra("SUT_TEST_STILLNESS");
            var equipment = NewService(lab);

            // 故意乱序穿上：经文 → 护甲 → 武器。
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Sutra", "SUT_TEST_STILLNESS", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Armor", "EQP_TEST_ROBE", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _));

            CollectionAssert.AreEqual(
                new[] { "Weapon=EQP_TEST_STAFF", "Armor=EQP_TEST_ROBE", "Sutra=SUT_TEST_STILLNESS" },
                Describe(equipment.LoadoutOf("CHR_TEST_A")),
                "顺序取栏位展示顺序，不取穿上的先后——技能挂载的先后也跟着它走。");
        }

        [Test]
        public void TryEquip_RejectsUnknownCharacter()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_NOBODY", "Weapon", "EQP_TEST_STAFF", out var error));
            StringAssert.Contains("角色", error);
            Assert.AreEqual(0, equipment.Snapshot.Count, "被拦下的条目什么也不该留下。");
        }

        [Test]
        public void TryEquip_RejectsNonPlayableCharacter()
        {
            using var lab = new BattleLab();
            var npc = lab.Character("CHR_TEST_NPC", 100, 10, 5, 8);
            BattleLab.SetPrivate(npc, "_isPlayable", false);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_TEST_NPC", "Weapon", "EQP_TEST_STAFF", out var error));
            StringAssert.Contains("可操作", error);
        }

        [Test]
        public void TryEquip_RejectsSlotNamesTheTableWouldNotRecognise()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);

            // 与进场画像同一份口径：小写、数字串（Enum.TryParse 会认）、逗号组合
            // （"Weapon, Armor" 会凑出 Talisman）一律不认，免得「猜一个栏位出来」。
            var traps = new[] { "weapon", "1", "Weapon, Armor", "Weaponry", "none", " ", null };
            for (var i = 0; i < traps.Length; i++)
            {
                Assert.IsFalse(
                    equipment.TryEquip("CHR_TEST_A", traps[i], "EQP_TEST_STAFF", out var error),
                    $"'{traps[i]}' 不该被认成栏位。");
                Assert.IsNotEmpty(error);
            }

            Assert.AreEqual(0, equipment.Snapshot.Count);
        }

        [Test]
        public void TryEquip_RejectsItemFromAnotherSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_ROBE", out var error));
            StringAssert.Contains("栏位", error);
            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_TEST_A", "Weapon"));
        }

        [Test]
        public void TryEquip_RejectsKindsThatAreNotEquipment()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Enemy("ENM_TEST_A", 200, 10, 0, 10);
            lab.Item("ITM_TEST_HERB", "heal", effectMagnitude: 30);
            var equipment = NewService(lab);

            // 敌人不是队伍成员；丹药是道具，不该占栏位。
            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "ENM_TEST_A", out _));
            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "ITM_TEST_HERB", out var error));
            StringAssert.Contains("只收装备与经文", error);
            Assert.AreEqual(0, equipment.Snapshot.Count);
        }

        [Test]
        public void TryEquip_RejectsUnknownItem()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_NOT_IN_TABLE", out var error));
            StringAssert.Contains("查不到", error);
        }

        [Test]
        public void TryEquip_RejectsCharactersTheItemDoesNotAllow()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Character("CHR_TEST_B", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_RUYI", EquipmentSlot.Weapon, allowedCharacterIds: "CHR_TEST_A");
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_TEST_B", "Weapon", "EQP_TEST_RUYI", out var error));
            StringAssert.Contains("限定", error);
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_RUYI", out _), "限定里的那位穿得上。");
        }

        [Test]
        public void TryEquip_PutsSutraOnlyIntoTheSutraSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Sutra("SUT_TEST_RAGE", attackBonus: 6);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "SUT_TEST_RAGE", out var error));
            StringAssert.Contains("经文", error);
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Sutra", "SUT_TEST_RAGE", out _));
        }

        [Test]
        public void TryEquip_IgnoresRequiredLevelWhileThereIsNoLevelSystem()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_LATE", EquipmentSlot.Weapon, attackBonus: 14, requiredLevel: 99);
            var equipment = NewService(lab);

            // 这条钉的是<b>缺口</b>而不是规则：等级系统（Progression）还是空壳，没有「现在几级」可问，
            // 硬拦会把需要等级的装备变成永远穿不上的死数据。等级落地那天这条会先变红。
            Assert.IsTrue(
                equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_LATE", out var error),
                $"需求 99 级暂时不该拦住穿戴：{error}");
        }

        [Test]
        public void TryEquip_AllowsTheSameItemOnTwoCharacters()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Character("CHR_TEST_B", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon, attackBonus: 5);
            var equipment = NewService(lab);

            // 同样钉的是缺口：目录里的武器是按 tier 归并的通用件，「一件东西只有一份」需要一条归属线。
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _));
            Assert.IsTrue(equipment.TryEquip("CHR_TEST_B", "Weapon", "EQP_TEST_STAFF", out _));
            Assert.AreEqual(2, equipment.Snapshot.Count);
        }

        [Test]
        public void TryEquip_RejectsEmptyArguments()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);

            Assert.IsFalse(equipment.TryEquip(null, "Weapon", "EQP_TEST_STAFF", out _));
            Assert.IsFalse(equipment.TryEquip("   ", "Weapon", "EQP_TEST_STAFF", out _));
            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", null, out _));
            Assert.IsFalse(equipment.TryEquip("CHR_TEST_A", "Weapon", "   ", out _));
            Assert.AreEqual(0, equipment.Snapshot.Count);
        }

        [Test]
        public void TryUnequip_TakesTheItemOff()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            Assert.IsTrue(equipment.TryUnequip("CHR_TEST_A", "Weapon", out var error), error);
            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_TEST_A", "Weapon"));
            Assert.AreEqual(0, equipment.Snapshot.Count, "脱空之后这个人不该还留着一本空簿子。");
        }

        [Test]
        public void TryUnequip_RefusesEmptySlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            var equipment = NewService(lab);

            // 不是错误，是「没有可做的事」：想判断就先问 ItemIn。
            Assert.IsFalse(equipment.TryUnequip("CHR_TEST_A", "Weapon", out var error));
            StringAssert.Contains("本来就是空的", error);
        }

        [Test]
        public void TryUnequip_RejectsSlotNamesTheTableWouldNotRecognise()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            Assert.IsFalse(equipment.TryUnequip("CHR_TEST_A", "weapon", out _));
            Assert.IsFalse(equipment.TryUnequip("CHR_TEST_A", "99", out _));
            Assert.AreEqual("EQP_TEST_STAFF", equipment.ItemIn("CHR_TEST_A", "Weapon"), "认不出的栏位不该误伤真栏位。");
        }

        [Test]
        public void Snapshot_SortsByCharacterThenSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Character("CHR_TEST_B", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            lab.Sutra("SUT_TEST_STILLNESS");
            var equipment = NewService(lab);

            equipment.TryEquip("CHR_TEST_B", "Armor", "EQP_TEST_ROBE", out _);
            equipment.TryEquip("CHR_TEST_A", "Sutra", "SUT_TEST_STILLNESS", out _);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            CollectionAssert.AreEqual(
                new[]
                {
                    "CHR_TEST_A.Weapon=EQP_TEST_STAFF",
                    "CHR_TEST_A.Sutra=SUT_TEST_STILLNESS",
                    "CHR_TEST_B.Armor=EQP_TEST_ROBE",
                },
                Describe(equipment.Snapshot),
                "先成员、再栏位：存档要的是同一份状态两次采集得到同样的排列。");
        }

        [Test]
        public void Snapshot_IsIdenticalBetweenTwoCaptures()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);
            equipment.TryEquip("CHR_TEST_A", "Armor", "EQP_TEST_ROBE", out _);

            CollectionAssert.AreEqual(Describe(equipment.Snapshot), Describe(equipment.Snapshot));
        }

        [Test]
        public void Restore_ReplacesTheWholeBook()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Character("CHR_TEST_B", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            equipment.Restore(new[]
            {
                new EquippedItem("CHR_TEST_B", "Armor", "EQP_TEST_ROBE"),
            });

            CollectionAssert.AreEqual(
                new[] { "CHR_TEST_B.Armor=EQP_TEST_ROBE" },
                Describe(equipment.Snapshot),
                "读档是换成另一份状态：不在清单里的成员簿子要被清空，而不是叠加上去。");
        }

        [Test]
        public void Restore_SkipsBadEntriesAndKeepsTheRest()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            lab.Equipment("EQP_TEST_ROBE", EquipmentSlot.Armor);
            lab.Equipment("EQP_TEST_RUYI", EquipmentSlot.Weapon, allowedCharacterIds: "CHR_TEST_A");
            var equipment = NewService(lab);

            equipment.Restore(new[]
            {
                new EquippedItem("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF"),
                new EquippedItem("CHR_TEST_A", "Armor", "EQP_TEST_ROBE"),
                new EquippedItem("CHR_TEST_A", "Armor", "EQP_TEST_STAFF"),      // 栏位对不上
                new EquippedItem("CHR_TEST_A", "weapon", "EQP_TEST_ROBE"),      // 栏位名认不出
                new EquippedItem("CHR_TEST_A", "Sutra", "SUT_NOT_IN_TABLE"),    // 定义查不到
                new EquippedItem("CHR_NOBODY", "Weapon", "EQP_TEST_STAFF"),     // 成员查不到
                new EquippedItem("CHR_TEST_B", "Weapon", "EQP_TEST_RUYI"),      // 限定角色不符
            });

            // 坏条目只跳过它自己：半身装备不该让整份存档读不进来。
            // 顺序取栏位展示顺序（武器在护甲前），不取 Restore 的传入先后。
            CollectionAssert.AreEqual(
                new[] { "CHR_TEST_A.Weapon=EQP_TEST_STAFF", "CHR_TEST_A.Armor=EQP_TEST_ROBE" },
                Describe(equipment.Snapshot));
        }

        [Test]
        public void Restore_Nothing_ClearsEverything()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            equipment.Restore(null);
            Assert.AreEqual(0, equipment.Snapshot.Count);
        }

        [Test]
        public void Clear_TakesEverythingOff()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            equipment.Clear();
            Assert.AreEqual(0, equipment.Snapshot.Count);
        }

        [Test]
        public void ItemIn_ReturnsEmptyForUnknownCharacterOrSlot()
        {
            using var lab = new BattleLab();
            lab.Character("CHR_TEST_A", 100, 10, 5, 8);
            lab.Equipment("EQP_TEST_STAFF", EquipmentSlot.Weapon);
            var equipment = NewService(lab);
            equipment.TryEquip("CHR_TEST_A", "Weapon", "EQP_TEST_STAFF", out _);

            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_NOBODY", "Weapon"));
            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_TEST_A", "Armor"));
            Assert.AreEqual(string.Empty, equipment.ItemIn("CHR_TEST_A", "weapon"));
            Assert.AreEqual(string.Empty, equipment.ItemIn(null, "Weapon"));
        }

        private static EquipmentService NewService(BattleLab lab) => new EquipmentService(lab.Registry());

        /// <summary>把快照或清单摊成一串「成员·栏位=物品」，好让用例一眼看出顺序对不对。</summary>
        private static string[] Describe(IReadOnlyList<EquippedItem> snapshot)
        {
            var parts = new string[snapshot.Count];
            for (var i = 0; i < snapshot.Count; i++)
            {
                parts[i] = $"{snapshot[i].CharacterId}.{snapshot[i].SlotId}={snapshot[i].ItemId}";
            }

            return parts;
        }

        private static string[] Describe(IReadOnlyList<LoadoutEntry> loadout)
        {
            var parts = new string[loadout.Count];
            for (var i = 0; i < loadout.Count; i++)
            {
                parts[i] = $"{loadout[i].SlotId}={loadout[i].ItemId}";
            }

            return parts;
        }
    }
}
