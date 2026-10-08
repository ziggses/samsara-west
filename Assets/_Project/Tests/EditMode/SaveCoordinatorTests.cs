using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;
using SamsaraWest.Save;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 存档搬运：把一局收成一份档，再把一份档灌回来。
    /// </summary>
    /// <remarks>
    /// 装配走的是真模块（Core / Data / Narrative / Save / Exploration），不手搓替身——
    /// 这一段的价值恰恰在「几个模块的服务能不能一起被搬动」，替身会把要验的东西替掉。
    /// </remarks>
    public sealed class SaveCoordinatorTests
    {
        private const string MapId = "CH01_MAP01";
        private const string FlagKey = "flag.ch01.prologue_done";

        private string _directory;
        private ExplorationLab _lab;
        private ServiceRegistry _services;
        private IStoryState _state;
        private IExplorationService _exploration;
        private ISaveService _saves;
        private SaveCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "samsara-west-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            _lab = new ExplorationLab();
            _lab.Map(MapId, 6, 5);

            _services = new ServiceRegistry();
            CoreModule.Install(_services, new CoreOptions
            {
                MasterSeed = ExplorationLab.DefaultSeed,
                EnableFileLog = false,
            });
            _services.Register<IDefinitionRegistry>(_lab.Registry());
            NarrativeModule.Install(_services);
            SaveModule.Install(_services, _directory);
            ExplorationModule.Install(
                _services,
                new NarrativeStateSourceAdapter(_services.Resolve<IStoryState>()));

            _state = _services.Resolve<IStoryState>();
            _exploration = _services.Resolve<IExplorationService>();
            _saves = _services.Resolve<ISaveService>();
            _coordinator = new SaveCoordinator(
                _saves,
                _state,
                _exploration,
                _services.Resolve<IDefinitionRegistry>(),
                _services.Resolve<IRandomService>());
        }

        [TearDown]
        public void TearDown()
        {
            _lab.Dispose();
            _services.Clear();

            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
                // 临时目录删不掉不该让用例变红：它不是这段逻辑的产物。
            }
        }

        [Test]
        public void Capture_TakesPositionLedgerKarmaAndSeed()
        {
            _exploration.EnterMap(MapId, new GridPosition(1, 2), MoveDirection.North);
            _state.SetValue(FlagKey, 1);
            _state.AdjustKarma(KarmaAxis.Compassion, 3);

            var data = _coordinator.Capture();

            Assert.AreEqual(MapId, data.MapId);
            Assert.AreEqual(1, data.GridX);
            Assert.AreEqual(2, data.GridY);
            Assert.AreEqual(3, data.KarmaCompassion, "心念是「这一局的状态」，必须跟着走。");
            Assert.AreEqual(ExplorationLab.DefaultSeed, data.RandomSeed, "母种子跟着走，读档后的战斗才复现得了。");

            CollectionAssert.Contains(data.FlagKeys, FlagKey, "状态账整本进档。");
            Assert.AreEqual(1, data.FlagValues[data.FlagKeys.IndexOf(FlagKey)]);
        }

        [Test]
        public void Capture_WithoutAMap_LeavesTheMapEmpty()
        {
            var data = _coordinator.Capture();

            Assert.IsTrue(string.IsNullOrEmpty(data.MapId), "不在图上时地图留空，而不是留一个会读成「在第一张图左上角」的坐标。");
            Assert.AreEqual(0, data.GridX);
            Assert.AreEqual(0, data.GridY);
        }

        [Test]
        public void Capture_SortsKeys_SoTwoCapturesMatch()
        {
            _state.SetValue("flag.ch01.c", 3);
            _state.SetValue("flag.ch01.a", 1);
            _state.SetValue("flag.ch01.b", 2);

            var first = _coordinator.Capture();
            var second = _coordinator.Capture();

            CollectionAssert.AreEqual(
                new List<string> { "flag.ch01.a", "flag.ch01.b", "flag.ch01.c" },
                first.FlagKeys,
                "键按序排：同一份状态两次采集必须得到同样的键序，否则「存档是否一致」这件事没法比较。");
            CollectionAssert.AreEqual(first.FlagKeys, second.FlagKeys);
        }

        [Test]
        public void Apply_ReplacesTheWholeLedger()
        {
            _state.SetValue("flag.ch01.keep", 1);
            var data = _coordinator.Capture();

            _state.SetValue("flag.ch01.stale", 1);

            Assert.IsTrue(_coordinator.Apply(data));

            Assert.AreEqual(1, _state.GetValue("flag.ch01.keep"));
            Assert.AreEqual(0, _state.GetValue("flag.ch01.stale"), "读档是换成另一份状态，不是在当前状态上叠加。");
        }

        [Test]
        public void Apply_ReturnsToTheSavedMapAndTile()
        {
            _exploration.EnterMap(MapId, new GridPosition(2, 3), MoveDirection.North);
            var data = _coordinator.Capture();

            _exploration.LeaveMap();

            Assert.IsTrue(_coordinator.Apply(data));
            Assert.IsTrue(_exploration.IsExploring);
            Assert.AreEqual(MapId, _exploration.Current.MapId);
            Assert.AreEqual(new GridPosition(2, 3), _exploration.Current.Position, "落点用 EnterMap：存档坐标是玩家自己走出来的，不替他挪。");
        }

        [Test]
        public void Apply_WithNoMapInTheSave_LeavesTheMap()
        {
            _exploration.EnterMap(MapId, new GridPosition(1, 1), MoveDirection.North);
            var data = _coordinator.Capture();
            data.MapId = null;

            Assert.IsTrue(_coordinator.Apply(data));
            Assert.IsFalse(_exploration.IsExploring, "存档本来就是「还没进图」，那就离图，而不是替玩家留在旧图上。");
        }

        [Test]
        public void Apply_RejectsUnknownMap_WithoutTouchingAnything()
        {
            _state.SetValue("flag.ch01.before", 1);
            var data = _coordinator.Capture();
            data.MapId = "CH01_MAP99";

            Assert.IsFalse(_coordinator.Apply(data));
            Assert.AreEqual(1, _state.GetValue("flag.ch01.before"), "整体拒绝：不能留下「账本换了、人还站在旧图上」这种半截状态。");
        }

        [Test]
        public void Apply_RejectsNull()
        {
            Assert.IsFalse(_coordinator.Apply(null));
        }

        [Test]
        public void Apply_RejectsInvalidSave_WithoutTouchingAnything()
        {
            _state.SetValue("flag.ch01.before", 1);
            var data = _coordinator.Capture();
            data.FlagValues.Add(1);

            Assert.IsFalse(_coordinator.Apply(data), "并列数组不等长的存档过不了校验。");
            Assert.AreEqual(1, _state.GetValue("flag.ch01.before"));
        }

        [Test]
        public void SaveAndLoad_RoundTripsThroughASlot()
        {
            _exploration.EnterMap(MapId, new GridPosition(3, 1), MoveDirection.East);
            _state.SetValue(FlagKey, 1);

            Assert.IsTrue(_coordinator.SaveToSlot(1));
            Assert.AreEqual(1, _coordinator.LastSlot);

            _exploration.LeaveMap();
            _state.Restore(null, 0, 0, 0);

            Assert.IsTrue(_coordinator.LoadFromSlot(1), "读回来");
            Assert.IsTrue(_state.Values.ContainsKey(FlagKey), "账本跟着存档一起回来。");
            Assert.AreEqual(1, _state.GetValue(FlagKey));
            Assert.AreEqual(new GridPosition(3, 1), _exploration.Current.Position);
        }

        [Test]
        public void LoadFromSlot_WithoutASave_ReturnsFalse()
        {
            Assert.IsFalse(_coordinator.LoadFromSlot(7));
            Assert.AreEqual(-1, _coordinator.LastSlot);
        }

        [Test]
        public void Capture_KeepsNegativeKarma_Honest()
        {
            _state.AdjustKarma(KarmaAxis.Compassion, -4);

            var data = _coordinator.Capture();

            Assert.AreEqual(-4, data.KarmaCompassion, "采集如实：搬运这一层不替玩家把心念改成 0。");

            // 数据层当前对负心念的取舍是「拒绝保存」。若哪天放宽成允许，这条断言会红，
            // 那正是去改 ADR-025 成本栏的时刻——它是已知摩擦，不是这段逻辑的意外。
            Assert.IsFalse(_saves.Validate(data).IsValid, "负心念现在存不了档：摩擦摆在明处，不在这里悄悄夹取。");
        }
    }
}
