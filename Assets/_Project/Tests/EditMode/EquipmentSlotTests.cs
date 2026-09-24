using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Editor;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 8 个装备栏位是「数据表取值 + 界面布局 + 存档字段」三处的公共口径（P2 拍板）。
    /// 任何一处漏配都要在这里变红：顺序错了、越界值被当成可用栏位、栏位没有中文文案，
    /// 到了运行期都只会以「界面上出现 [[ui.equip.slot.xxx]]」的形式暴露。
    /// </summary>
    public sealed class EquipmentSlotTests
    {
        private static readonly EquipmentSlot[] ExpectedOrder =
        {
            EquipmentSlot.Weapon,
            EquipmentSlot.OffHand,
            EquipmentSlot.Helmet,
            EquipmentSlot.Armor,
            EquipmentSlot.Bracer,
            EquipmentSlot.Legging,
            EquipmentSlot.Talisman,
            EquipmentSlot.Sutra,
        };

        [Test]
        public void DisplayOrder_IsEightSlotsInAgreedOrder()
        {
            Assert.AreEqual(8, EquipmentSlots.Count, "栏位数量由 P2 拍板为 8，改动须同步本断言与拍板记录。");
            Assert.AreEqual(EquipmentSlots.Count, EquipmentSlots.DisplayOrder.Length);
            CollectionAssert.AreEqual(
                ExpectedOrder,
                EquipmentSlots.DisplayOrder,
                "界面展示顺序：武器·副手·头盔·护甲·护腕·腿部护具·法宝·经文。");
            CollectionAssert.AllItemsAreUnique(EquipmentSlots.DisplayOrder, "同一个栏位不能在列表里出现两次。");
        }

        [Test]
        public void DisplayOrder_ExcludesNone()
        {
            CollectionAssert.DoesNotContain(EquipmentSlots.DisplayOrder, EquipmentSlot.None);
        }

        [Test]
        public void IsEquippable_AcceptsEveryOrderedSlotAndRejectsOthers()
        {
            foreach (var slot in EquipmentSlots.DisplayOrder)
            {
                Assert.IsTrue(EquipmentSlots.IsEquippable(slot), $"{slot} 应当在可用栏位内。");
            }

            Assert.IsFalse(EquipmentSlots.IsEquippable(EquipmentSlot.None), "None 不是可用栏位。");
            Assert.IsFalse(EquipmentSlots.IsEquippable((EquipmentSlot)99), "越界值不得被当成可用栏位。");
        }

        [Test]
        public void LocalizationKey_IsUniqueAndDefinedForEverySlot()
        {
            var keys = new HashSet<string>();
            foreach (var slot in EquipmentSlots.DisplayOrder)
            {
                var key = EquipmentSlots.LocalizationKey(slot);
                Assert.IsNotEmpty(key, $"{slot} 缺少文本键。");
                Assert.IsTrue(keys.Add(key), $"{slot} 的文本键与其它栏位重复：{key}");
            }

            Assert.IsEmpty(EquipmentSlots.LocalizationKey(EquipmentSlot.None), "None 不应有文本键。");
        }

        [Test]
        public void LocalizationKey_ExistsInChineseTable()
        {
            var path = CsvImporter.ToAbsolutePath($"{SamsaraWestPaths.LocalizationTables}/localization-zh-Hans.csv");
            Assert.IsTrue(File.Exists(path), $"缺少文案表：{path}");

            var defined = new HashSet<string>();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.TrimStart('\uFEFF');
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                var separator = trimmed.IndexOf(',');
                if (separator > 0)
                {
                    defined.Add(trimmed.Substring(0, separator).Trim());
                }
            }

            foreach (var slot in EquipmentSlots.DisplayOrder)
            {
                var key = EquipmentSlots.LocalizationKey(slot);
                Assert.IsTrue(defined.Contains(key), $"文案表缺少栏位文本键 {key}，界面上会显示成 [[{key}]]。");
            }
        }
    }
}
