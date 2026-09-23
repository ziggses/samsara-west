using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using SamsaraWest.Save;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 存档校验与迁移是「坏档不能污染运行态」的最后一道闸门。
    /// 每个用例用独立临时目录，避免互相污染，也不写到玩家存档目录。
    /// </summary>
    public sealed class SaveServiceTests
    {
        private string _directory;
        private SaveService _service;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "samsara-west-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _service = new SaveService(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_directory != null && Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static SaveData CreateData(int chapter = 1, string mapId = "CH01_MAP01")
        {
            return new SaveData
            {
                Version = SaveService.LatestVersion,
                ChapterIndex = chapter,
                MapId = mapId,
                GridX = 3,
                GridY = 5,
                RandomSeed = 20260923UL,
                Gold = 120,
                PlayTimeSeconds = 3725.5,
                FlagKeys = new List<string> { "flag.ch01.truth_told", "relation.wukong" },
                FlagValues = new List<int> { 1, 30 },
                KarmaCompassion = 4,
                KarmaTruth = 1,
                KarmaFreedom = 2,
                PartyCharacterIds = new List<string> { "CHR_WUKONG", "CHR_BAJIE" },
                CompletedQuestIds = new List<string> { "QST_CH01_MAIN" },
                InventoryItemIds = new List<string> { "ITM_HEAL_PILL" },
                InventoryItemCounts = new List<int> { 3 },
                EquippedItemIds = new List<string> { "EQP_SWORD_001" },
            };
        }

        private string PathOfSlot(int slot) => Path.Combine(_directory, $"slot_{slot:00}.json");

        [Test]
        public void SaveDirectory_DefaultsToPersistentDataPathSaves()
        {
            var service = new SaveService();

            StringAssert.EndsWith(Path.Combine("saves"), service.SaveDirectory);
        }

        [Test]
        public void TrySave_ThenTryLoad_RoundTripsEveryField()
        {
            var original = CreateData();

            Assert.IsTrue(_service.TrySave(1, original), "保存应当成功。");
            Assert.IsTrue(_service.Exists(1));
            Assert.IsTrue(_service.TryLoad(1, out var loaded));

            Assert.AreEqual(original.Version, loaded.Version);
            Assert.AreEqual(original.ChapterIndex, loaded.ChapterIndex);
            Assert.AreEqual(original.MapId, loaded.MapId);
            Assert.AreEqual(original.GridX, loaded.GridX);
            Assert.AreEqual(original.GridY, loaded.GridY);
            Assert.AreEqual(original.RandomSeed, loaded.RandomSeed, "主种子必须存下来，否则读档后战斗无法复现。");
            Assert.AreEqual(original.Gold, loaded.Gold);
            Assert.AreEqual(original.PlayTimeSeconds, loaded.PlayTimeSeconds);
            CollectionAssert.AreEqual(original.FlagKeys, loaded.FlagKeys);
            CollectionAssert.AreEqual(original.FlagValues, loaded.FlagValues);
            Assert.AreEqual(original.KarmaCompassion, loaded.KarmaCompassion);
            Assert.AreEqual(original.KarmaTruth, loaded.KarmaTruth);
            Assert.AreEqual(original.KarmaFreedom, loaded.KarmaFreedom);
            CollectionAssert.AreEqual(original.PartyCharacterIds, loaded.PartyCharacterIds);
            CollectionAssert.AreEqual(original.InventoryItemIds, loaded.InventoryItemIds);
            CollectionAssert.AreEqual(original.InventoryItemCounts, loaded.InventoryItemCounts);
        }

        [Test]
        public void TrySave_StampsLatestVersion()
        {
            var data = CreateData();
            data.Version = 0;

            Assert.IsTrue(_service.TrySave(2, data));
            Assert.IsTrue(_service.TryLoad(2, out var loaded));
            Assert.AreEqual(SaveService.LatestVersion, loaded.Version, "未带版本号的内存存档在写盘时应被盖章为当前版本。");
        }

        [Test]
        public void TrySave_NullData_ReturnsFalse()
        {
            Assert.IsFalse(_service.TrySave(1, null));
            Assert.IsFalse(_service.Exists(1));
        }

        [TestCase(9)]
        [TestCase(-1)]
        public void TrySave_RejectsOutOfRangeChapter(int chapter)
        {
            Assert.IsFalse(_service.TrySave(1, CreateData(chapter)));
            Assert.IsFalse(_service.Exists(1), "校验失败的存档不得落盘。");
        }

        [Test]
        public void TrySave_RejectsMismatchedParallelArrays()
        {
            var data = CreateData();
            data.FlagValues.Add(9);

            Assert.IsFalse(_service.TrySave(1, data));
        }

        [Test]
        public void TrySave_OverwritesExistingSlot()
        {
            Assert.IsTrue(_service.TrySave(1, CreateData(chapter: 1)));
            Assert.IsTrue(_service.TrySave(1, CreateData(chapter: 4)));

            Assert.IsTrue(_service.TryLoad(1, out var loaded));
            Assert.AreEqual(4, loaded.ChapterIndex);
            Assert.AreEqual(new List<int> { 1 }, new List<int>(_service.ListSlots()));
        }

        [Test]
        public void TryLoad_MissingSlot_ReturnsFalse()
        {
            Assert.IsFalse(_service.TryLoad(7, out var data));
            Assert.IsNull(data);
        }

        [Test]
        public void TryLoad_LegacyV1_MigratesToLatest()
        {
            // v1 存档：没有心念三轴、没有并列数组，靠迁移补齐。
            File.WriteAllText(
                PathOfSlot(3),
                "{\"Version\":1,\"ChapterIndex\":2,\"MapId\":\"CH02_MAP01\",\"Gold\":40}",
                new UTF8Encoding(false));

            Assert.IsTrue(_service.TryLoad(3, out var loaded));
            Assert.AreEqual(SaveService.LatestVersion, loaded.Version);
            Assert.AreEqual(2, loaded.ChapterIndex);
            Assert.AreEqual("CH02_MAP01", loaded.MapId);
            Assert.IsNotNull(loaded.FlagKeys);
            Assert.IsNotNull(loaded.InventoryItemCounts);
            Assert.AreEqual(0, loaded.KarmaCompassion);
        }

        [Test]
        public void TryLoad_VersionZero_IsTreatedAsLegacy()
        {
            File.WriteAllText(PathOfSlot(4), "{\"Version\":0,\"ChapterIndex\":1,\"MapId\":\"CH01_MAP01\"}", new UTF8Encoding(false));

            Assert.IsTrue(_service.TryLoad(4, out var loaded));
            Assert.AreEqual(SaveService.LatestVersion, loaded.Version);
        }

        [Test]
        public void TryLoad_FutureVersion_IsRejected()
        {
            File.WriteAllText(PathOfSlot(5), "{\"Version\":99,\"ChapterIndex\":1,\"MapId\":\"CH01_MAP01\"}", new UTF8Encoding(false));

            Assert.IsFalse(_service.TryLoad(5, out _), "高于本程序支持的版本必须拒绝，而不是带病加载。");
        }

        [Test]
        public void TryLoad_FileWithOutOfRangeChapter_IsRejected()
        {
            // 手工改档把章节改到 99：结构能解析，但内容非法，必须拒绝加载。
            File.WriteAllText(PathOfSlot(6), "{\"Version\":2,\"ChapterIndex\":99}", new UTF8Encoding(false));

            Assert.IsFalse(_service.TryLoad(6, out _));
        }

        [Test]
        public void TryLoad_InconsistentData_IsRejected()
        {
            File.WriteAllText(
                PathOfSlot(7),
                "{\"Version\":2,\"ChapterIndex\":1,\"FlagKeys\":[\"flag.ch01.truth_told\"],\"FlagValues\":[]}",
                new UTF8Encoding(false));

            Assert.IsFalse(_service.TryLoad(7, out _), "并列数组不等长说明文件被改坏，必须拒绝。");
        }

        [Test]
        public void Validate_CollectsEveryProblemAtOnce()
        {
            var data = CreateData();
            data.Version = SaveService.LatestVersion;
            data.Gold = -5;
            data.KarmaTruth = -1;
            data.FlagValues.Clear();

            var report = _service.Validate(data);

            Assert.IsFalse(report.IsValid);
            Assert.GreaterOrEqual(report.Problems.Count, 3, "校验应一次报全，而不是修一个冒一个。");
        }

        [Test]
        public void Validate_EmptyFlagKey_IsRejected()
        {
            var data = CreateData();
            data.FlagKeys[0] = "  ";

            Assert.IsFalse(_service.Validate(data).IsValid);
        }

        [Test]
        public void Validate_NullData_IsRejected()
        {
            Assert.IsFalse(_service.Validate(null).IsValid);
        }

        [Test]
        public void Validate_NewSaveData_IsValidApartFromVersion()
        {
            var fresh = new SaveData { Version = SaveService.LatestVersion };

            Assert.IsTrue(_service.Validate(fresh).IsValid, "空存档结构本身应当合法，否则新开局无法第一次保存。");
        }

        [Test]
        public void ListSlots_ReturnsSortedSlotsOnly()
        {
            Assert.IsTrue(_service.TrySave(10, CreateData()));
            Assert.IsTrue(_service.TrySave(2, CreateData()));
            File.WriteAllText(Path.Combine(_directory, "readme.txt"), "x", new UTF8Encoding(false));

            CollectionAssert.AreEqual(new List<int> { 2, 10 }, new List<int>(_service.ListSlots()));
        }

        [Test]
        public void ListSlots_MissingDirectory_ReturnsEmpty()
        {
            var service = new SaveService(Path.Combine(_directory, "not-created"));

            Assert.AreEqual(0, service.ListSlots().Count);
        }

        [Test]
        public void Describe_ReportsMetadataWithoutFullLoad()
        {
            Assert.IsTrue(_service.TrySave(1, CreateData(chapter: 3)));

            var info = _service.Describe(1);

            Assert.IsFalse(info.IsEmpty);
            Assert.AreEqual(1, info.Slot);
            Assert.AreEqual(3, info.ChapterIndex);
            Assert.AreEqual(SaveService.LatestVersion, info.Version);
            Assert.IsNotEmpty(info.SavedAtIso);
        }

        [Test]
        public void Describe_EmptySlot_IsMarkedEmpty()
        {
            var info = _service.Describe(9);

            Assert.IsTrue(info.IsEmpty);
            Assert.IsNull(info.SavedAtIso);
        }

        [Test]
        public void TryDelete_RemovesSlotAndIsIdempotent()
        {
            Assert.IsTrue(_service.TrySave(1, CreateData()));

            Assert.IsTrue(_service.TryDelete(1));
            Assert.IsFalse(_service.Exists(1));
            Assert.IsTrue(_service.TryDelete(1));
        }

        [Test]
        public void TrySave_CreatesDirectoryOnDemand()
        {
            var nested = Path.Combine(_directory, "nested", "saves");
            var service = new SaveService(nested);

            Assert.IsTrue(service.TrySave(1, CreateData()));
            Assert.IsTrue(File.Exists(Path.Combine(nested, "slot_01.json")));
        }
    }
}
