using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 映射器的两条铁律：
    /// 1) 列存在但值为空 → 清空（否则表格里删掉的值会留在资产里，成为只在跨表比对时才暴露的幽灵数据）；
    /// 2) 列不存在 → 保持原值（否则缺列会把资产打回默认值）。
    /// </summary>
    public sealed class CsvDefinitionMapperTests
    {
        private const string Header =
            "id,displayNameKey,descriptionKey,tags,version,category,tier,stackLimit,price," +
            "usableInBattle,usableInField,isConsumedOnUse,effectKey,effectMagnitude,effectDurationTurns,effectSkillId,spriteKey";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        /// <summary>按表头顺序拼出一整行，避免手写逗号串时错位。</summary>
        private static string Row(
            string id = "",
            string displayNameKey = "",
            string descriptionKey = "",
            string tags = "",
            string version = "",
            string category = "",
            string tier = "",
            string stackLimit = "",
            string price = "",
            string usableInBattle = "",
            string usableInField = "",
            string isConsumedOnUse = "",
            string effectKey = "",
            string effectMagnitude = "",
            string effectDurationTurns = "",
            string effectSkillId = "",
            string spriteKey = "")
        {
            return string.Join(
                ",",
                id,
                displayNameKey,
                descriptionKey,
                tags,
                version,
                category,
                tier,
                stackLimit,
                price,
                usableInBattle,
                usableInField,
                isConsumedOnUse,
                effectKey,
                effectMagnitude,
                effectDurationTurns,
                effectSkillId,
                spriteKey);
        }

        private ItemDefinition MapFirstRow(string csv, ValidationReport report)
        {
            var table = CsvParser.Parse(csv, "items.csv");
            var definition = (ItemDefinition)CsvDefinitionMapper.Map(typeof(ItemDefinition), table.Rows[0], table, report);
            _created.Add(definition);
            return definition;
        }

        [Test]
        public void Map_ConvertsEverySupportedType()
        {
            var report = new ValidationReport();
            var item = MapFirstRow(
                Header + "\n" + Row(
                    id: "ITM_HEAL_PILL",
                    displayNameKey: "item.heal_pill",
                    descriptionKey: "item.heal_pill.desc",
                    tags: "consumable;healing",
                    version: "2",
                    category: "Consumable",
                    tier: "Refined",
                    stackLimit: "20",
                    price: "50",
                    usableInBattle: "true",
                    usableInField: "true",
                    isConsumedOnUse: "true",
                    effectKey: "heal.health",
                    effectMagnitude: "40",
                    spriteKey: "item.heal_pill") + "\n",
                report);

            Assert.AreEqual("ITM_HEAL_PILL", item.Id);
            Assert.AreEqual("item.heal_pill", item.DisplayNameKey);
            Assert.AreEqual("item.heal_pill.desc", item.DescriptionKey);
            CollectionAssert.AreEqual(new[] { "consumable", "healing" }, item.Tags);
            Assert.AreEqual(2, item.Version);
            Assert.AreEqual(ItemCategory.Consumable, item.Category);
            Assert.AreEqual(RarityTier.Refined, item.Tier);
            Assert.AreEqual(20, item.StackLimit);
            Assert.AreEqual(50, item.Price);
            Assert.IsTrue(item.UsableInBattle);
            Assert.IsTrue(item.UsableInField);
            Assert.IsTrue(item.IsConsumedOnUse);
            Assert.AreEqual("heal.health", item.EffectKey);
            Assert.AreEqual(40, item.EffectMagnitude);
            Assert.AreEqual("item.heal_pill", item.SpriteKey);
            Assert.AreEqual(0, report.ErrorCount, report.ToString());
        }

        [Test]
        public void Map_AssetNameFollowsId()
        {
            var item = MapFirstRow(
                Header + "\n" + Row(id: "ITM_HEAL_PILL", displayNameKey: "item.heal_pill", category: "Consumable", effectKey: "heal.health") + "\n",
                new ValidationReport());

            Assert.AreEqual("ITM_HEAL_PILL", item.name, "资产名必须跟随 ID，否则在 Project 窗口里无从检索。");
        }

        [Test]
        public void Apply_EmptyCell_ClearsPreviousValue()
        {
            var report = new ValidationReport();
            var item = MapFirstRow(
                Header + "\n" + Row(
                    id: "ITM_HEAL_PILL",
                    displayNameKey: "item.heal_pill",
                    descriptionKey: "item.heal_pill.desc",
                    tags: "consumable;healing",
                    version: "2",
                    category: "Consumable",
                    tier: "Refined",
                    stackLimit: "20",
                    price: "50",
                    usableInBattle: "true",
                    usableInField: "true",
                    isConsumedOnUse: "true",
                    effectKey: "heal.health",
                    effectMagnitude: "40",
                    effectSkillId: "SKL_CLOUD_STEP",
                    spriteKey: "item.heal_pill") + "\n",
                report);

            Assert.AreEqual("heal.health", item.EffectKey);
            Assert.AreEqual(2, item.Tags.Length);
            Assert.AreEqual(40, item.EffectMagnitude);

            var second = CsvParser.Parse(
                Header + "\n" + Row(
                    id: "ITM_HEAL_PILL",
                    displayNameKey: "item.heal_pill",
                    version: "2",
                    category: "Consumable",
                    tier: "Refined",
                    stackLimit: "20",
                    price: "50",
                    usableInBattle: "true",
                    usableInField: "true",
                    isConsumedOnUse: "true") + "\n",
                "items.csv");
            CsvDefinitionMapper.Apply(item, second.Rows[0], second, report);

            Assert.IsNull(item.DescriptionKey, "表格里删掉的描述必须真的被清掉。");
            Assert.IsNull(item.EffectKey);
            Assert.IsNull(item.EffectSkillId);
            Assert.IsNull(item.SpriteKey);
            Assert.IsNotNull(item.Tags, "数组清空成空数组而不是 null：各定义类的校验是按空数组写的。");
            Assert.AreEqual(0, item.Tags.Length);
            Assert.AreEqual(0, item.EffectMagnitude, "列存在但值为空时，数值列归零。");
            Assert.AreEqual(0, report.ErrorCount, report.ToString());
        }

        [Test]
        public void Apply_AbsentColumn_KeepsPreviousValue()
        {
            var report = new ValidationReport();
            var item = MapFirstRow(
                Header + "\n" + Row(id: "ITM_HEAL_PILL", displayNameKey: "item.heal_pill", category: "Consumable", effectKey: "heal.health") + "\n",
                report);

            var narrowed = CsvParser.Parse("id,displayNameKey,category\nITM_HEAL_PILL,item.heal_pill,Consumable\n", "items.csv");
            CsvDefinitionMapper.Apply(item, narrowed.Rows[0], narrowed, report);

            Assert.AreEqual("heal.health", item.EffectKey, "缺列代表「表里没有这一列」，应保持原值。");
            Assert.IsTrue(HasIssue(report, "CSV_COLUMN_ABSENT"));
        }

        [Test]
        public void Apply_MissingRequiredColumn_ReportsError()
        {
            var report = new ValidationReport();
            var table = CsvParser.Parse("id,displayNameKey\nITM_HEAL_PILL,item.heal_pill\n", "items.csv");

            var item = (ItemDefinition)CsvDefinitionMapper.Map(typeof(ItemDefinition), table.Rows[0], table, report);
            _created.Add(item);

            Assert.IsTrue(HasIssue(report, "CSV_COLUMN_MISSING"));
            Assert.Greater(report.ErrorCount, 0);
        }

        [Test]
        public void Apply_EmptyRequiredCell_ReportsError()
        {
            var report = new ValidationReport();
            MapFirstRow("id,displayNameKey,category\nITM_HEAL_PILL,,Consumable\n", report);

            Assert.IsTrue(HasIssue(report, "CSV_VALUE_EMPTY"));
        }

        [Test]
        public void Apply_UnparsableValue_ReportsErrorButKeepsConvertingRow()
        {
            var report = new ValidationReport();
            var item = MapFirstRow(
                "id,displayNameKey,category,stackLimit,effectKey\nITM_HEAL_PILL,item.heal_pill,Consumable,很多,heal.health\n",
                report);

            Assert.IsTrue(HasIssue(report, "CSV_VALUE_INVALID"));
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual("heal.health", item.EffectKey, "单列失败不应中断整行导入。");
        }

        [Test]
        public void Apply_UnmappedColumn_ReportsWarning()
        {
            var report = new ValidationReport();
            MapFirstRow("id,displayNameKey,category,note\nITM_HEAL_PILL,item.heal_pill,Consumable,temp\n", report);

            Assert.IsTrue(HasIssue(report, "CSV_COLUMN_UNMAPPED"));
            Assert.AreEqual(0, report.ErrorCount);
        }

        [Test]
        public void Apply_UnknownEnumValue_ReportsError()
        {
            var report = new ValidationReport();
            MapFirstRow("id,displayNameKey,category\nITM_HEAL_PILL,item.heal_pill,NotACategory\n", report);

            Assert.IsTrue(HasIssue(report, "CSV_VALUE_INVALID"));
        }

        [Test]
        public void Tags_AcceptPipeSeparator()
        {
            var item = MapFirstRow(
                "id,displayNameKey,category,tags\nITM_HEAL_PILL,item.heal_pill,Consumable,a|b\n",
                new ValidationReport());

            CollectionAssert.AreEqual(new[] { "a", "b" }, item.Tags);
        }

        [Test]
        public void GetBindings_IncludesInheritedBaseFields()
        {
            var bindings = CsvDefinitionMapper.GetBindings(typeof(ItemDefinition));
            var columns = new HashSet<string>();
            for (var i = 0; i < bindings.Length; i++)
            {
                columns.Add(bindings[i].Column);
            }

            CollectionAssert.Contains(columns, "id");
            CollectionAssert.Contains(columns, "displaynamekey");
            CollectionAssert.Contains(columns, "effectkey");
            Assert.IsTrue(System.Array.Exists(bindings, b => b.Column == "id" && b.Required), "id 必须是必需列。");
        }

        [Test]
        public void Map_NonDefinitionType_Throws()
        {
            var table = CsvParser.Parse("id\nX\n", "items.csv");
            Assert.Throws<System.ArgumentException>(
                () => CsvDefinitionMapper.Map(typeof(string), table.Rows[0], table, new ValidationReport()));
        }

        [Test]
        public void Apply_NullInstance_Throws()
        {
            var table = CsvParser.Parse("id\nITM_A\n", "items.csv");
            Assert.Throws<System.ArgumentNullException>(
                () => CsvDefinitionMapper.Apply(null, table.Rows[0], table, new ValidationReport()));
        }

        private static bool HasIssue(ValidationReport report, string code)
        {
            var issues = report.Issues;
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Code == code)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
