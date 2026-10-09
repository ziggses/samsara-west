using System;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 装配与接线：探索服务装得进注册表，遇敌事件真的能开出一场战斗，战斗结束后遭遇被了结，
    /// 走到一扇门真的会换一张图。
    /// </summary>
    /// <remarks>
    /// 这里走的是与引导期同一条装配路径（<see cref="CoreModule.Install"/> →
    /// 注册数据 → <c>BattleModule.Install</c> → <c>ExplorationModule.Install</c> → 接线），
    /// 而不是手工 new 一个会话：这条链最容易坏的地方正是「谁先装、谁缺谁」，
    /// 用真实装配路径才测得到它。
    /// </remarks>
    [TestFixture]
    public sealed class ExplorationWiringTests
    {
        private const string MapId = "CH01_MAP01";
        private const string SecondMapId = "CH01_MAP02";
        private const string EncounterId = "ENC_CH01_001";
        private const string CharacterId = "CHR_TEST_001";
        private const string EnemyId = "ENM_TEST_001";
        private const string SkillId = "SKL_TEST_001";

        [Test]
        public void 装配_数据与随机就位后_能解析出探索服务()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            Assert.IsNotNull(wiring.Exploration);
            Assert.IsFalse(wiring.Exploration.IsExploring);
            Assert.IsNotNull(
                wiring.Exploration.StateSource,
                "没有注入状态源时要退化成「全部读作 0」的替身，而不是空引用。");
        }

        [Test]
        public void 装配_数据不在注册表里_装探索模块会立刻报错()
        {
            var services = new ServiceRegistry();
            CoreModule.Install(services, TestCoreOptions());

            // 地图表是硬依赖：没有它就无处可走。装的时候报错，胜过运行期一个空引用。
            Assert.Throws<ServiceNotRegisteredException>(() => ExplorationModule.Install(services));
        }

        [Test]
        public void 进图_地图ID查不到或不合规_返回空而不是抛异常()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            Assert.IsNull(wiring.Exploration.EnterMap("CH01_MAP99", GridPosition.Origin), "目录里没有这张图。");
            Assert.IsNull(wiring.Exploration.EnterMap("MAP_BAD", GridPosition.Origin), "ID 不合命名规则。");
            Assert.IsNull(wiring.Exploration.EnterMap(null, GridPosition.Origin));
            Assert.IsNull(wiring.Exploration.EnterMap(string.Empty, GridPosition.Origin));
            Assert.IsFalse(wiring.Exploration.IsExploring, "进图失败不该留下一个半开的会话。");
        }

        [Test]
        public void 进图与离图_当前会话跟着变()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(1, 1), MoveDirection.North);

            Assert.IsNotNull(session);
            Assert.IsTrue(wiring.Exploration.IsExploring);
            Assert.AreSame(session, wiring.Exploration.Current);
            Assert.AreEqual(new GridPosition(1, 1), session.Position);

            wiring.Exploration.LeaveMap();

            Assert.IsFalse(wiring.Exploration.IsExploring);
            Assert.IsNull(wiring.Exploration.Current);
        }

        [Test]
        public void 进图_只收这张图名下的交互物()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var mine = lab.Interactable("INT_CH01_MINE", MapId, 1, 1);
            lab.Interactable("INT_CH01_FAR", "CH01_MAP02", 2, 2);
            using var wiring = new Wiring(lab, battleLab);

            var session = wiring.Exploration.EnterMap(MapId, GridPosition.Origin, MoveDirection.North);

            Assert.AreEqual(1, session.Grid.VisibleInteractables.Count, "别的地图的交互物不该被搬进来。");
            Assert.AreSame(mine, session.Grid.VisibleInteractables[0]);
            Assert.AreEqual(0, session.Grid.IgnoredInteractableCount, "过滤在建图之前做完，网格不该再见到别图的交互物。");
        }

        [Test]
        public void 遇敌接线_收到遇敌_开出一场真战斗()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, GridPosition.Origin, 3));

            Assert.IsTrue(wiring.Battle.HasActiveBattle, "接线收到遇敌就该把战斗开起来。");
            Assert.AreEqual(1, wiring.Link.BattlesStarted);
            Assert.AreEqual(EncounterId, wiring.Battle.Current.Setup.EncounterId);
            Assert.AreEqual(1, wiring.Battle.Current.EnemyUnits.Count);
            Assert.AreEqual(1, wiring.Battle.Current.PlayerUnits.Count, "队伍来自定义目录里的可操作角色。");
        }

        [Test]
        public void 遇敌接线_走一步撞上遭遇_战斗结束后玩家能接着走()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            var session = wiring.Exploration.EnterMap(MapId, GridPosition.Origin, MoveDirection.North);

            // 遭遇率填 1：这一步必遇敌，不需要靠种子去碰运气。
            Assert.IsTrue(session.TryMove(MoveDirection.North).Moved);

            Assert.IsTrue(session.IsEncounterPending);
            Assert.IsTrue(wiring.Battle.HasActiveBattle, "遇敌事件应当已经把战斗开起来了。");
            Assert.AreEqual(MoveRejection.EncounterPending, session.TryMove(MoveDirection.North).Rejection);

            wiring.Battle.Current.RunToEnd();

            Assert.IsFalse(session.IsEncounterPending, "战斗结束之后遭遇必须被了结，否则玩家一步都走不动。");
            Assert.IsTrue(session.TryMove(MoveDirection.East).Moved);
        }

        [Test]
        public void 遇敌接线_遭遇ID查不到_不炸也不开战()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, "ENC_CH01_999", GridPosition.Origin, 1));

            Assert.IsFalse(wiring.Battle.HasActiveBattle);
            Assert.AreEqual(0, wiring.Link.BattlesStarted);
            Assert.AreEqual(1, wiring.Link.IgnoredEncounters);
        }

        [Test]
        public void 遇敌接线_已经有战斗在进行_第二次遇敌被忽略()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, GridPosition.Origin, 1));
            var first = wiring.Battle.Current;

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, GridPosition.Origin, 2));

            Assert.AreEqual(1, wiring.Link.BattlesStarted, "不该把正在打的仗顶掉。");
            Assert.AreSame(first, wiring.Battle.Current);
            Assert.AreEqual(1, wiring.Link.IgnoredEncounters);
        }

        [Test]
        public void 遇敌接线_目录里没有可操作角色_不开一场空战斗()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab, withParty: false);

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, GridPosition.Origin, 1));

            Assert.IsFalse(wiring.Battle.HasActiveBattle, "没有队伍就不该开战：空战斗比不开战更难查。");
            Assert.AreEqual(0, wiring.Link.BattlesStarted);
            Assert.AreEqual(1, wiring.Link.IgnoredEncounters);
        }

        [Test]
        public void 遇敌接线_退订之后_遇敌不再开战()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            using var wiring = new Wiring(lab, battleLab);

            wiring.Link.Dispose();
            wiring.Link.Dispose(); // 重复退订是空操作：销毁路径可能重入。

            wiring.Bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, GridPosition.Origin, 1));

            Assert.IsFalse(wiring.Battle.HasActiveBattle);
            Assert.AreEqual(0, wiring.Link.BattlesStarted);
        }

        [Test]
        public void 换图接线_走到一扇门_真的换到目标图并落在门附近()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var door = lab.Interactable("INT_CH01_DOOR", MapId, 2, 2, targetId: SecondMapId);
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            var result = session.TryInteract();

            Assert.IsTrue(result.RequestsMapChange);
            Assert.AreEqual(1, wiring.MapChange.MapChanges);
            Assert.AreEqual(0, wiring.MapChange.IgnoredRequests);
            Assert.IsTrue(wiring.Exploration.IsExploring, "换图不是「离图」：换完必须还在某张图上。");
            Assert.AreNotSame(session, wiring.Exploration.Current, "旧会话要被换掉，否则网格还停在上一张图。");
            Assert.AreEqual(SecondMapId, wiring.Exploration.Current.MapId);
            Assert.AreEqual(
                new GridPosition(1, 1),
                wiring.Exploration.Current.Position,
                "门在 (2,2)，目标图只有 2x2，越界的那一半要夹回来。");
        }

        [Test]
        public void 换图接线_落点被别的交互物占着_就近落到能站的格子()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var door = lab.Interactable("INT_CH01_DOOR", MapId, 2, 2, targetId: SecondMapId);
            lab.Interactable("INT_CH01_BLOCK", SecondMapId, 1, 1);
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            session.TryInteract();

            Assert.AreEqual(SecondMapId, wiring.Exploration.Current.MapId);
            Assert.AreEqual(
                new GridPosition(1, 0),
                wiring.Exploration.Current.Position,
                "(1,1) 站不住，正下方 (1,0) 是可走的，就近落那里。");
            Assert.IsTrue(wiring.Exploration.Current.Grid.IsWalkable(wiring.Exploration.Current.Position));
        }

        [Test]
        public void 换图接线_目标图不在定义目录里_留在原地并记一次忽略()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var door = lab.Interactable("INT_CH01_DOOR", MapId, 2, 2, targetId: "CH01_MAP99");
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            session.TryInteract();

            Assert.AreEqual(0, wiring.MapChange.MapChanges);
            Assert.AreEqual(1, wiring.MapChange.IgnoredRequests);
            Assert.AreSame(
                session,
                wiring.Exploration.Current,
                "目标图查不到就不该先把玩家从当前图里踢出去——那会让他哪张图都不在。");
            Assert.AreEqual(new GridPosition(2, 1), session.Position, "位置也不该被挪动。");
        }

        [Test]
        public void 换图接线_门指向自己所在的图_忽略()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var door = lab.Interactable("INT_CH01_DOOR", MapId, 2, 2, targetId: MapId);
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            session.TryInteract();

            Assert.AreEqual(0, wiring.MapChange.MapChanges, "换到自己脚下没有意义，等于让玩家原地卡一次读图。");
            Assert.AreEqual(1, wiring.MapChange.IgnoredRequests);
            Assert.AreSame(session, wiring.Exploration.Current);
        }

        [Test]
        public void 换图接线_退订之后_请求照发但没人接()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            var door = lab.Interactable("INT_CH01_DOOR", MapId, 2, 2, targetId: SecondMapId);
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            wiring.MapChange.Dispose();
            wiring.MapChange.Dispose(); // 重复退订是空操作：销毁路径可能重入。

            var result = session.TryInteract();

            Assert.IsTrue(result.RequestsMapChange, "请求是探索发的，与谁订阅无关。");
            Assert.AreEqual(0, wiring.MapChange.MapChanges);
            Assert.AreSame(session, wiring.Exploration.Current);
        }

        [Test]
        public void 定点遭遇_交互物的目标是一场遭遇_挂起并把仗开起来()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            lab.Interactable("INT_CH01_KING", MapId, 2, 2, targetId: EncounterId);
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            var result = session.TryInteract();

            Assert.IsTrue(result.RequestsEncounter, "targetId 是合法遭遇 ID，这次交互就是定点遭遇。");
            Assert.AreEqual(EncounterId, result.TargetEncounterId);
            Assert.IsFalse(result.RequestsMapChange, "遭遇 ID 不该被当成地图 ID。");
            Assert.IsTrue(session.IsEncounterPending);
            Assert.AreEqual(
                MoveRejection.EncounterPending,
                session.TryMove(MoveDirection.North).Rejection,
                "挂起口径与随机遭遇一致：仗没打完就一步都走不动。");
            Assert.IsTrue(wiring.Battle.HasActiveBattle, "定点遭遇与随机遭遇走的是同一条接线。");
            Assert.AreEqual(1, wiring.Link.BattlesStarted);
            Assert.AreEqual(EncounterId, wiring.Battle.Current.Setup.EncounterId);

            wiring.Battle.Current.RunToEnd();

            Assert.IsFalse(session.IsEncounterPending, "打完必须了结，否则玩家从此一步都走不动。");
        }

        [Test]
        public void 定点遭遇_目标不是遭遇ID的交互物_不算开战()
        {
            using var lab = new ExplorationLab();
            using var battleLab = new BattleLab();
            lab.Interactable("INT_CH01_RELIC", MapId, 2, 2, targetId: "CH01_N28_ENTER_BY_RELIC");
            using var wiring = new Wiring(lab, battleLab);
            var session = wiring.Exploration.EnterMap(MapId, new GridPosition(2, 1), MoveDirection.North);

            var result = session.TryInteract();

            Assert.IsTrue(result.RequestsMapChange == false && result.RequestsEncounter == false);
            Assert.IsNull(result.TargetEncounterId, "剧情节点名不是遭遇 ID。");
            Assert.IsFalse(session.IsEncounterPending);
            Assert.IsFalse(wiring.Battle.HasActiveBattle, "读一块碑不该顺手把仗开起来。");
        }

        private static CoreOptions TestCoreOptions() => new CoreOptions
        {
            MasterSeed = ExplorationLab.DefaultSeed,
            EnableFileLog = false,
        };

        /// <summary>
        /// 按引导期的顺序把服务装起来：Core → 数据 → 战斗 → 探索 → 接线。
        /// </summary>
        private sealed class Wiring : IDisposable
        {
            internal Wiring(ExplorationLab lab, BattleLab battleLab, bool withParty = true, bool withEncounter = true)
            {
                battleLab.AttackSkill(SkillId);
                if (withParty)
                {
                    battleLab.Character(CharacterId, 200, 60, 0, 20, FiveElement.None, 30, 50, SkillId);
                }

                battleLab.Enemy(EnemyId, 1, 5, 0, 10, FiveElement.None, 20, false, SkillId);
                if (withEncounter)
                {
                    battleLab.Encounter(EncounterId, EnemyId);
                }

                lab.Map(id: MapId, width: 3, height: 3, encounterId: withEncounter ? EncounterId : null, encounterRate: 1f);

                // 目标图刻意比来源图小：换图落点要「夹进边界再就近找」，尺寸一样就测不到夹这一下。
                lab.Map(id: SecondMapId, width: 2, height: 2);

                Registry = lab.Registry(battleLab.Definitions);

                Services = new ServiceRegistry();
                CoreModule.Install(Services, TestCoreOptions());
                Services.Register<IDefinitionRegistry>(Registry);
                BattleModule.Install(Services, battleLab.Config);
                ExplorationModule.Install(Services);

                Bus = Services.Resolve<IEventBus>();
                Link = new ExplorationBattleLink(
                    Services.Resolve<IBattleService>(),
                    Registry,
                    Bus,
                    Services.Resolve<IExplorationService>());

                MapChange = new MapChangeLink(
                    Bus,
                    Registry,
                    Services.Resolve<IExplorationService>());
                }

                internal ServiceRegistry Services { get; }

                internal IDefinitionRegistry Registry { get; }

                internal IEventBus Bus { get; }

                internal ExplorationBattleLink Link { get; }

                internal MapChangeLink MapChange { get; }

                internal IExplorationService Exploration => Services.Resolve<IExplorationService>();

                internal IBattleService Battle => Services.Resolve<IBattleService>();

                public void Dispose()
                {
                MapChange.Dispose();
                Link.Dispose();
                Services.Clear();
                }
        }
    }
}
