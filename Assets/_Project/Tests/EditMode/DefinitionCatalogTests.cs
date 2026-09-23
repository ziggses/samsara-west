using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 「重复数据 ID 会报错」是本次交付的验收项之一，因此这里不只测 happy path，
    /// 还专门构造重复/空/不合规 ID 三种坏数据，确认它们真的会被判为错误。
    /// </summary>
    public sealed class DefinitionCatalogTests
    {
        private readonly List<Object> _created = new List<Object>();

        private DefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = Create<DefinitionCatalog>();
        }

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
            _catalog = null;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        private ItemDefinition CreateItem(string id, string displayNameKey = null, string effectKey = "heal.health")
        {
            var item = Create<ItemDefinition>();
            item.SetIdentity(
                id,
                displayNameKey ?? $"item.{id?.ToLowerInvariant()}",
                null,
                System.Array.Empty<string>(),
                1);

            // 道具标记为可用就必须有效果键，否则校验会（正确地）报 ITM_NO_EFFECT。
            // 效果键只由 CSV 导入写入，测试里没有公开入口，只能按导入器的写法直接落到字段上。
            SetPrivateField(item, "_effectKey", effectKey);
            item.name = id ?? "ItemDefinition_未命名";
            return item;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"字段 {fieldName} 不存在：定义类改名后这条测试必须同步更新。");
            field.SetValue(target, value);
        }

        [Test]
        public void Rebuild_DuplicateId_IsReportedAsErrorAndKeepsFirstEntry()
        {
            var first = CreateItem("ITM_HEAL_PILL");
            var duplicate = CreateItem("ITM_HEAL_PILL");
            _catalog.SetDefinitions(new DefinitionBase[] { first, duplicate });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.AreEqual(1, _catalog.Duplicates.Count);
            Assert.AreEqual("DEF_ID_DUPLICATE", _catalog.Duplicates[0].Code);
            Assert.AreEqual(1, report.ErrorCount, report.ToString());
            Assert.AreEqual(1, _catalog.IndexedCount, "重复 ID 只保留第一条，第二条不得覆盖。");
            Assert.AreSame(first, _catalog.Get<ItemDefinition>("ITM_HEAL_PILL"));
        }

        [Test]
        public void Rebuild_EmptyId_IsReportedAndSkipped()
        {
            var blank = CreateItem(null, displayNameKey: "item.blank");
            blank.name = "ItemDefinition_未命名";
            _catalog.SetDefinitions(new DefinitionBase[] { blank });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.IsTrue(HasIssue(report, "CAT_EMPTY_ID"));
            Assert.AreEqual(0, _catalog.IndexedCount);
        }

        [Test]
        public void Rebuild_MalformedId_IsReported()
        {
            var bad = CreateItem("ITM-heal-pill");
            _catalog.SetDefinitions(new DefinitionBase[] { bad });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.IsTrue(HasIssue(report, "DEF_ID_PATTERN"));
        }

        [Test]
        public void Rebuild_ChineseInLocalizationKey_IsReported()
        {
            var bad = CreateItem("ITM_HEAL_PILL", displayNameKey: "回气丹");
            _catalog.SetDefinitions(new DefinitionBase[] { bad });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.IsTrue(HasIssue(report, "DEF_LOC_KEY_HAS_CHINESE"), "定义里存中文原文必须报错（FND-07）。");
        }

        [Test]
        public void Rebuild_CleanData_HasNoErrors()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL") });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.AreEqual(0, report.ErrorCount, report.ToString());
            Assert.AreEqual(0, _catalog.Duplicates.Count);
        }

        [Test]
        public void Rebuild_MissingIcon_IsOnlyInformational()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL") });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.IsTrue(HasIssue(report, "DEF_ICON_MISSING"));
            Assert.IsFalse(HasIssueWithSeverity(report, "DEF_ICON_MISSING", ValidationSeverity.Error),
                "图标由导入器按表汇总，单条不应升级为错误。");
        }

        [Test]
        public void Rebuild_NullEntry_IsReportedAsWarning()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL"), null });

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            Assert.IsTrue(HasIssue(report, "CAT_NULL_ENTRY"));
            Assert.AreEqual(0, report.ErrorCount);
        }

        [Test]
        public void TryGet_TypeMismatch_ReturnsFalse()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL") });
            _catalog.Rebuild();

            Assert.IsTrue(_catalog.TryGet<ItemDefinition>("ITM_HEAL_PILL", out _));
            Assert.IsFalse(_catalog.TryGet<EquipmentDefinition>("ITM_HEAL_PILL", out _));
        }

        [Test]
        public void Get_UnknownId_Throws()
        {
            _catalog.Rebuild();
            Assert.Throws<KeyNotFoundException>(() => _catalog.Get<ItemDefinition>("ITM_NOPE"));
        }

        [Test]
        public void TryGet_WithoutExplicitRebuild_BuildsIndexLazily()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL") });
            Assert.IsFalse(_catalog.IsIndexBuilt);

            Assert.IsTrue(_catalog.TryGet<ItemDefinition>("ITM_HEAL_PILL", out var found));

            Assert.IsTrue(_catalog.IsIndexBuilt);
            Assert.AreEqual("ITM_HEAL_PILL", found.Id);
        }

        [Test]
        public void SetDefinitions_InvalidatesIndex()
        {
            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_HEAL_PILL") });
            _catalog.Rebuild();
            Assert.IsTrue(_catalog.IsIndexBuilt);

            _catalog.SetDefinitions(new DefinitionBase[] { CreateItem("ITM_OTHER") });

            Assert.IsFalse(_catalog.IsIndexBuilt, "换过内容后必须重建索引，否则会查到已删除的定义。");
        }

        [Test]
        public void OfKind_FiltersByConcreteType()
        {
            _catalog.SetDefinitions(new DefinitionBase[]
            {
                CreateItem("ITM_HEAL_PILL"),
                CreateItem("ITM_OTHER"),
            });
            _catalog.Rebuild();

            var count = 0;
            foreach (var _ in _catalog.OfKind<ItemDefinition>())
            {
                count++;
            }

            Assert.AreEqual(2, count);
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

        private static bool HasIssueWithSeverity(ValidationReport report, string code, ValidationSeverity severity)
        {
            var issues = report.Issues;
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Code == code && issues[i].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
