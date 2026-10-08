using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 探索会话：走一步、交互一次、掷一次遭遇，以及这三件事各自的拒绝理由。
    /// </summary>
    /// <remarks>
    /// 拒绝理由与事件分开断言：拒绝是返回值（界面要立刻反馈），事件是「发生了什么」
    /// （别的模块要跟着反应）。把两者混在一起会写出「拒绝也发事件」这种自相矛盾的设计。
    ///
    /// 遭遇一律用 0 或 1 的概率：<c>Chance</c> 在两端会短路、不消耗随机数，
    /// 于是「一定遇敌」「一定不遇敌」的用例可以做到完全确定；需要真的掷骰时再固定种子。
    /// </remarks>
    [TestFixture]
    public sealed class ExplorationSessionTests
    {
        private const string MapId = "CH01_MAP01";
        private const string EncounterId = "ENC_CH01_001";
        private const string ChestId = "INT_CH01_CHEST";
        private const string FlagKey = "flag.ch01.prologue_done";

        [Test]
        public void 会话_进图_发出一条地图进入事件()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f);
            var bus = ExplorationLab.Bus();
            var entered = Capture<MapEnteredEvent>(bus);

            lab.Session(map, ExplorationLab.State(), bus: bus, start: new GridPosition(1, 1));

            Assert.AreEqual(1, entered.Count);
            Assert.AreEqual(MapId, entered[0].MapId);
            Assert.AreEqual(new GridPosition(1, 1), entered[0].Position);
            Assert.AreEqual(4, entered[0].GridWidth);
            Assert.AreEqual(3, entered[0].GridHeight);
            Assert.AreEqual(EncounterId, entered[0].EncounterId, "界面与遭遇校验都要知道这张图的遭遇 ID。");
        }

        [Test]
        public void 会话_走一步_位置与朝向都更新并发出移动事件()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var bus = ExplorationLab.Bus();
            var moved = Capture<PlayerMovedEvent>(bus);
            var session = lab.Session(map, ExplorationLab.State(), bus: bus, start: new GridPosition(1, 1));

            var result = session.TryMove(MoveDirection.North);

            Assert.IsTrue(result.Moved);
            Assert.AreEqual(MoveRejection.None, result.Rejection);
            Assert.AreEqual(new GridPosition(1, 1), result.From);
            Assert.AreEqual(new GridPosition(1, 2), result.To);
            Assert.AreEqual(new GridPosition(1, 2), session.Position);
            Assert.AreEqual(MoveDirection.North, session.Facing);
            Assert.AreEqual(1, session.StepCount);
            Assert.AreEqual(1, moved.Count);
            Assert.AreEqual(new GridPosition(1, 2), moved[0].To);
            Assert.AreEqual(1, moved[0].StepCount);
        }

        [Test]
        public void 会话_往图外走_被拒绝且位置与步数都不变()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var bus = ExplorationLab.Bus();
            var moved = Capture<PlayerMovedEvent>(bus);
            var session = lab.Session(map, ExplorationLab.State(), bus: bus, start: GridPosition.Origin);

            var result = session.TryMove(MoveDirection.West);

            Assert.IsFalse(result.Moved);
            Assert.AreEqual(MoveRejection.OutOfBounds, result.Rejection);
            Assert.AreEqual(GridPosition.Origin, session.Position);
            Assert.AreEqual(GridPosition.Origin, result.To, "被拒绝时 To 必须等于 From，否则界面会画出一次假的位移。");
            Assert.AreEqual(0, session.StepCount);
            Assert.AreEqual(0, moved.Count);
        }

        [Test]
        public void 会话_撞上交互物_被拒绝且朝向不跟着变()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(ChestId, map.Id, 1, 1);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                start: new GridPosition(1, 0),
                facing: MoveDirection.East,
                interactables: chest);

            var result = session.TryMove(MoveDirection.North);

            Assert.AreEqual(MoveRejection.Blocked, result.Rejection);
            Assert.AreEqual(new GridPosition(1, 0), session.Position);
            Assert.AreEqual(MoveDirection.East, session.Facing, "撞墙不转身：朝向只随走成的移动改。");
        }

        [Test]
        public void 会话_交互_面朝的那一格上有东西就触发()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(
                ChestId,
                map.Id,
                1,
                1,
                targetId: "LUT_CH01_001");
            var bus = ExplorationLab.Bus();
            var triggered = Capture<InteractionTriggeredEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                bus: bus,
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: chest);

            var result = session.TryInteract();

            Assert.IsTrue(result.Triggered);
            Assert.AreEqual(ChestId, result.InteractableId);
            Assert.AreEqual("test.explore.interact.chest", result.InteractionTypeKey);
            Assert.AreEqual("LUT_CH01_001", result.TargetId);
            Assert.AreEqual(new GridPosition(1, 1), result.Position, "交互发生在他面朝的那一格，不是他站的那一格。");
            Assert.IsFalse(result.RequestsMapChange);

            Assert.AreEqual(1, triggered.Count);
            Assert.AreEqual(ChestId, triggered[0].InteractableId);
        }

        [Test]
        public void 会话_交互_前方没东西_拒绝原因是没目标()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(ChestId, map.Id, 3, 2);
            var bus = ExplorationLab.Bus();
            var triggered = Capture<InteractionTriggeredEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                bus: bus,
                start: GridPosition.Origin,
                facing: MoveDirection.North,
                interactables: chest);

            var result = session.TryInteract();

            Assert.AreEqual(InteractionRejection.NoTarget, result.Rejection);
            Assert.AreEqual(new GridPosition(0, 1), result.Position);
            Assert.AreEqual(0, triggered.Count);
        }

        [Test]
        public void 会话_交互_条件没满足_拒绝原因是条件不满足()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var door = lab.Interactable(
                "INT_CH01_DOOR",
                map.Id,
                1,
                1,
                targetId: "CH01_MAP02",
                requiredStateKey: FlagKey,
                requiredValue: 1);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: door);

            var result = session.TryInteract();

            Assert.AreEqual(InteractionRejection.RequirementNotMet, result.Rejection);
        }

        [Test]
        public void 会话_交互_条件没满足且要求隐藏_拒绝原因是隐藏()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(
                ChestId,
                map.Id,
                1,
                1,
                requiredStateKey: FlagKey,
                requiredValue: 1,
                hiddenUntilConditionMet: true);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: chest);

            // 与上一条的区别是「为什么没反应」：一个是打不开，一个是根本看不见。
            // 界面要靠这两个不同的拒绝理由给出不同的提示。
            Assert.AreEqual(InteractionRejection.Hidden, session.TryInteract().Rejection);
        }

        [Test]
        public void 会话_交互_条件满足后就放行()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(
                ChestId,
                map.Id,
                1,
                1,
                requiredStateKey: FlagKey,
                requiredValue: 1,
                hiddenUntilConditionMet: true,
                oneShot: true);
            var session = lab.Session(
                map,
                ExplorationLab.State((FlagKey, 1)),
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: chest);

            Assert.IsTrue(session.TryInteract().Triggered);
        }

        [Test]
        public void 会话_一次性交互物_第二次被拒绝且第一次已经记账()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(ChestId, map.Id, 1, 1, oneShot: true);
            var bus = ExplorationLab.Bus();
            var triggered = Capture<InteractionTriggeredEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                bus: bus,
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: chest);

            Assert.IsTrue(session.TryInteract().Triggered);
            Assert.IsTrue(session.HasUsed(ChestId), "触发之前就该记账：订阅方不该自己防重入。");
            Assert.AreEqual(InteractionRejection.AlreadyUsed, session.TryInteract().Rejection);
            Assert.AreEqual(1, triggered.Count, "用过的箱子不该再发一次事件——那会让奖励发两遍。");
        }

        [Test]
        public void 会话_交互目标是一张地图_额外发出换图请求()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(id: MapId);
            var door = lab.Interactable(
                "INT_CH01_DOOR",
                map.Id,
                1,
                1,
                interactionTypeKey: "test.explore.interact.door",
                targetId: "CH01_MAP02");
            var bus = ExplorationLab.Bus();
            var requested = Capture<MapChangeRequestedEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                bus: bus,
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: door);

            var result = session.TryInteract();

            Assert.IsTrue(result.RequestsMapChange, "判据是数据：targetId 是合法地图 ID 就算换图。");
            Assert.AreEqual("CH01_MAP02", result.TargetMapId);
            Assert.AreEqual(1, requested.Count);
            Assert.AreEqual(MapId, requested[0].FromMapId);
            Assert.AreEqual("CH01_MAP02", requested[0].ToMapId);
            Assert.AreEqual(door.Id, requested[0].InteractableId);
            Assert.AreEqual(
                new GridPosition(1, 1),
                requested[0].Position,
                "门在哪一格必须跟着事件走：订阅方要拿它算落点，光知道目标图是反推不出来的。");
        }

        [Test]
        public void 会话_交互目标是别的定义_不算换图()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(ChestId, map.Id, 1, 1, targetId: "LUT_CH01_001");
            var bus = ExplorationLab.Bus();
            var requested = Capture<MapChangeRequestedEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                bus: bus,
                start: new GridPosition(1, 0),
                facing: MoveDirection.North,
                interactables: chest);

            var result = session.TryInteract();

            Assert.IsTrue(result.Triggered);
            Assert.IsFalse(result.RequestsMapChange);
            Assert.IsNull(result.TargetMapId);
            Assert.AreEqual(0, requested.Count);
        }

        [Test]
        public void 会话_遭遇概率为一时_走一步必遇敌()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f);
            var bus = ExplorationLab.Bus();
            var encounters = Capture<EncounterTriggeredEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                bus,
                new GridPosition(1, 1));

            var result = session.TryMove(MoveDirection.North);

            Assert.IsTrue(result.Moved, "这一步是先走成的——遇敌判定在移动结算之后。");
            Assert.AreEqual(EncounterId, session.PendingEncounterId);
            Assert.IsTrue(session.IsEncounterPending);
            Assert.AreEqual(1, session.EncounterRollCount);
            Assert.AreEqual(1, encounters.Count);
            Assert.AreEqual(EncounterId, encounters[0].EncounterId);
            Assert.AreEqual(new GridPosition(1, 2), encounters[0].Position);
            Assert.AreEqual(1, encounters[0].StepCount, "遇敌时带的是「走到第几步撞上」。");
        }

        [Test]
        public void 会话_遭遇挂着的时候_走也走不动交互也不让()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                null,
                new GridPosition(1, 1),
                MoveDirection.North);

            Assert.IsTrue(session.TryMove(MoveDirection.North).Moved, "先撞上遭遇。");
            var blocked = session.TryMove(MoveDirection.West);

            Assert.AreEqual(MoveRejection.EncounterPending, blocked.Rejection);
            Assert.AreEqual(new GridPosition(1, 2), session.Position, "战斗没打完之前位置不该动。");
            Assert.AreEqual(
                InteractionRejection.EncounterPending,
                session.TryInteract().Rejection);

            // 玩家站着不动，但这一步还是走成了：StepCount 记的是「走成了几步」。
            Assert.AreEqual(1, session.StepCount);
        }

        [Test]
        public void 会话_了结遭遇之后_可以继续走动()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                null,
                new GridPosition(1, 1));

            session.TryMove(MoveDirection.North);
            session.ResolveEncounter();

            Assert.IsFalse(session.IsEncounterPending);
            Assert.IsNull(session.PendingEncounterId);
            Assert.IsTrue(session.TryMove(MoveDirection.West).Moved, "遭遇了结之后玩家必须能继续走。");

            // 没有挂着遭遇时再调一次是空操作：收尾流程可能重入，不该因此报错。
            session.ResolveEncounter();
            Assert.IsFalse(session.IsEncounterPending);
        }

        [Test]
        public void 会话_安全区里_即便填了遭遇率也不遇敌()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f, isSafeZone: true);
            var bus = ExplorationLab.Bus();
            var encounters = Capture<EncounterTriggeredEvent>(bus);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                bus,
                new GridPosition(1, 1));

            Assert.IsTrue(session.TryMove(MoveDirection.North).Moved);

            // 数据校验只警告「城镇不该有遭遇率」，运行期必须真的不发生。
            Assert.IsFalse(session.IsEncounterPending);
            Assert.AreEqual(0, session.EncounterRollCount, "安全区连骰子都不该掷。");
            Assert.AreEqual(0, encounters.Count);
        }

        [Test]
        public void 会话_没有遭遇表或概率为零_都不掷骰()
        {
            using var lab = new ExplorationLab();

            // 有表无率：表配了但这一步永远不会触发。
            var balanced = lab.Map(id: "CH01_MAP02", encounterId: EncounterId, encounterRate: 0f);
            var balancedSession = lab.Session(
                balanced,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                null,
                new GridPosition(1, 1));
            Assert.IsTrue(balancedSession.TryMove(MoveDirection.North).Moved);
            Assert.AreEqual(0, balancedSession.EncounterRollCount);

            // 有率无表：数据是坏的（Validate 会报 MAP_RATE_NO_TABLE），运行期只能不掷。
            var broken = lab.Map(id: "CH01_MAP03", encounterRate: 1f);
            var brokenSession = lab.Session(
                broken,
                ExplorationLab.State(),
                ExplorationLab.Stream(),
                null,
                new GridPosition(1, 1));
            Assert.IsTrue(brokenSession.TryMove(MoveDirection.North).Moved);
            Assert.AreEqual(0, brokenSession.EncounterRollCount);
            Assert.IsFalse(brokenSession.IsEncounterPending);
        }

        [Test]
        public void 会话_每走一步掷一次骰_且走的是探索命名流()
        {
            using var lab = new ExplorationLab();

            // 图要够高：走五步得真的走满五步，撞到图顶就变成在测越界了。
            var map = lab.Map(encounterId: EncounterId, encounterRate: 0.5f, height: 8);
            var stream = ExplorationLab.Stream();
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                stream,
                null,
                new GridPosition(1, 1));

            for (var i = 0; i < 5; i++)
            {
                session.TryMove(MoveDirection.North);
            }

            Assert.AreEqual(5, session.EncounterRollCount);
            Assert.AreEqual(5UL, stream.Snapshot().DrawCount, "开了遭遇率的地图，每走成一步恰好消耗一次随机数。");
        }

        [Test]
        public void 会话_同样的种子_遭遇落在同一步()
        {
            using var lab = new ExplorationLab();

            // 图要有足够长的路：只在两三步里比，等于没比。
            var map = lab.Map(encounterId: EncounterId, encounterRate: 0.35f, height: 24);

            var first = Walk(lab, map, ExplorationLab.Stream());
            var second = Walk(lab, map, ExplorationLab.Stream());

            // 探索的随机必须可复现：同种子同走法给同结果，否则遇敌节奏没法校算也没法复现问题。
            Assert.Greater(first.StepCount, 5, "这条路够长，不该几步就走到头。");
            Assert.AreEqual(first.EncounterRollCount, first.StepCount, "走成一步就掷一次。");
            Assert.AreEqual(first.StepCount, second.StepCount);
            Assert.AreEqual(first.PendingEncounterId, second.PendingEncounterId);
        }

        [Test]
        public void 会话_没有随机流时_不掷遭遇也不崩()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(encounterId: EncounterId, encounterRate: 1f);
            var session = lab.Session(
                map,
                ExplorationLab.State(),
                null,
                null,
                new GridPosition(1, 1));

            Assert.IsTrue(session.TryMove(MoveDirection.North).Moved);
            Assert.AreEqual(0, session.EncounterRollCount);
            Assert.IsFalse(session.IsEncounterPending);
        }

        [Test]
        public void 会话_刷新可见性_新出现的交互物开始挡路()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();
            var chest = lab.Interactable(
                ChestId,
                map.Id,
                1,
                1,
                requiredStateKey: FlagKey,
                requiredValue: 1,
                hiddenUntilConditionMet: true);
            var state = (ExplorationLab.DictionaryStateSource)ExplorationLab.State();
            var session = lab.Session(
                map,
                state,
                null,
                null,
                new GridPosition(1, 0),
                MoveDirection.North,
                chest);

            Assert.IsTrue(session.Grid.IsWalkable(new GridPosition(1, 1)), "条件没满足时那一格是通的。");

            // 剧情把键改了：界面与探索在同一条事件里各自刷新。
            state.Set(FlagKey, 1);
            session.RefreshVisibility();

            Assert.IsFalse(session.Grid.IsWalkable(new GridPosition(1, 1)), "开箱/开门的条件满足后，它开始占格。");
            Assert.AreEqual(1, session.Grid.VisibleInteractables.Count);
            Assert.IsTrue(session.TryInteract().Triggered, "刷新之后它才真的能交互。");
        }

        [Test]
        public void 会话_落点越界_夹到原点而不是抛异常()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map();

            // 存档里的位置可能来自旧版本的地图（图变小了），这时候炸掉会让玩家连读档界面都进不去。
            var session = lab.Session(map, ExplorationLab.State(), start: new GridPosition(99, 99));

            Assert.AreEqual(GridPosition.Origin, session.Position);
        }

        private static List<T> Capture<T>(IEventBus bus) where T : IGameEvent
        {
            var captured = new List<T>();
            bus.Subscribe<T>(ExplorationEventChannel.Channel, captured.Add);
            return captured;
        }

        /// <summary>一路往北走，直到撞上遭遇或走到图顶，返回停下来时的会话。</summary>
        private static ExplorationSession Walk(ExplorationLab lab, MapDefinition map, IRandomStream stream)
        {
            var session = lab.Session(map, ExplorationLab.State(), stream, null, new GridPosition(0, 0));

            for (var i = 0; i < map.GridHeight - 1; i++)
            {
                if (session.IsEncounterPending)
                {
                    break;
                }

                session.TryMove(MoveDirection.North);
            }

            return session;
        }
    }
}
