using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Localization;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 取词服务必须做到「缺键可见」：宁可显示占位符，也不能静默回落到中文原文，
    /// 否则硬编码中文会以看起来正常的方式进入发布版（FND-07）。
    /// </summary>
    public sealed class LocalizationServiceTests
    {
        private LocalizationTable _table;

        [TearDown]
        public void TearDown()
        {
            if (_table != null)
            {
                Object.DestroyImmediate(_table);
                _table = null;
            }
        }

        private LocalizationService CreateService(params LocalizationTable.Entry[] entries)
        {
            _table = ScriptableObject.CreateInstance<LocalizationTable>();
            _table.SetEntries("zh-Hans", entries);
            return new LocalizationService(_table);
        }

        [Test]
        public void Get_ReturnsTextForKnownKey()
        {
            var service = CreateService(new LocalizationTable.Entry("ui.battle.attack", "攻击"));

            Assert.AreEqual("攻击", service.Get("ui.battle.attack"));
            Assert.IsTrue(service.HasKey("ui.battle.attack"));
        }

        [Test]
        public void Get_MissingKey_ReturnsPlaceholderAndRecordsIt()
        {
            var service = CreateService(new LocalizationTable.Entry("ui.battle.attack", "攻击"));

            var text = service.Get("ui.battle.defend");

            Assert.AreEqual("[[ui.battle.defend]]", text);
            Assert.IsFalse(service.HasKey("ui.battle.defend"));
            CollectionAssert.Contains((ICollection<string>)service.MissingKeys, "ui.battle.defend");
        }

        [Test]
        public void MissingKeys_RecordedOncePerKey()
        {
            var service = CreateService();

            service.Get("a.b");
            service.Get("a.b");
            service.Get("a.c");

            Assert.AreEqual(2, service.MissingKeys.Count);
        }

        [Test]
        public void Get_EmptyKey_ReturnsEmptyWithoutWarning()
        {
            var service = CreateService();

            Assert.AreEqual(string.Empty, service.Get(null));
            Assert.AreEqual(string.Empty, service.Get(string.Empty));
            Assert.AreEqual(0, service.MissingKeys.Count);
        }

        [Test]
        public void KeyCount_IgnoresBlankKeys()
        {
            var service = CreateService(
                new LocalizationTable.Entry("a.b", "甲"),
                new LocalizationTable.Entry("   ", "无键"),
                new LocalizationTable.Entry("a.c", "乙"));

            Assert.AreEqual(2, service.KeyCount);
        }

        [Test]
        public void DuplicateKey_KeepsFirstAndDoesNotThrow()
        {
            var service = CreateService(
                new LocalizationTable.Entry("a.b", "第一条"),
                new LocalizationTable.Entry("a.b", "第二条"));

            Assert.AreEqual(1, service.KeyCount);
            Assert.AreEqual("第一条", service.Get("a.b"), "重复键保留首条，保证导入结果可预期。");
        }

        [Test]
        public void Format_ReplacesPlaceholders()
        {
            var service = CreateService(new LocalizationTable.Entry("ui.battle.damage", "造成 {0} 点伤害（{1} 段）"));

            Assert.AreEqual("造成 45 点伤害（3 段）", service.Format("ui.battle.damage", 45, 3));
        }

        [Test]
        public void Format_WithoutArgs_ReturnsTemplate()
        {
            var service = CreateService(new LocalizationTable.Entry("ui.battle.damage", "造成 {0} 点伤害"));

            Assert.AreEqual("造成 {0} 点伤害", service.Format("ui.battle.damage"));
        }

        [Test]
        public void Format_ArgCountMismatch_ReturnsTemplateInsteadOfThrowing()
        {
            var service = CreateService(new LocalizationTable.Entry("ui.battle.damage", "造成 {0} 点伤害（{1} 段）"));

            Assert.DoesNotThrow(() => service.Format("ui.battle.damage", 45));
            Assert.AreEqual("造成 {0} 点伤害（{1} 段）", service.Format("ui.battle.damage", 45));
        }

        [Test]
        public void Format_MissingKey_ReturnsPlaceholder()
        {
            var service = CreateService();

            Assert.AreEqual("[[ui.battle.damage]]", service.Format("ui.battle.damage", 1));
        }

        [Test]
        public void Language_FallsBackToSimplifiedChinese()
        {
            var service = CreateService();

            Assert.AreEqual("zh-Hans", service.Language, "骨架期只做中文单语。");
        }

        [Test]
        public void NullTable_IsUsableButEmpty()
        {
            var service = new LocalizationService(null);

            Assert.AreEqual(0, service.KeyCount);
            Assert.IsFalse(service.HasKey("a.b"));
            Assert.AreEqual("[[a.b]]", service.Get("a.b"));
        }

        [Test]
        public void EnumerateKeys_ListsDefinedKeys()
        {
            var service = CreateService(
                new LocalizationTable.Entry("a.b", "甲"),
                new LocalizationTable.Entry("a.c", "乙"));

            var keys = new List<string>(service.EnumerateKeys());

            CollectionAssert.AreEquivalent(new[] { "a.b", "a.c" }, keys);
        }

        [Test]
        public void DescribeMissingKeys_IsReadable()
        {
            var service = CreateService();

            Assert.AreEqual("无缺失键。", service.DescribeMissingKeys());

            service.Get("a.b");
            StringAssert.Contains("a.b", service.DescribeMissingKeys());
        }
    }
}
