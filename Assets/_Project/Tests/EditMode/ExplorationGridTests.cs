using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Exploration;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 可行走格：范围判定、占格、条件可见性、就近落点四件事。
    /// </summary>
    /// <remarks>
    /// 这一层是纯几何 + 纯条件，不涉及玩家与事件，因此可以把边界一次穷举干净：
    /// 越界、被占、条件未满足（要求隐藏／不要求隐藏）、坏数据（越界交互物、同格两条、别图的交互物）、
    /// 以及换图落点（锚点可走／越界／被占／无路可退）。
    /// 走一步会发生什么在 <c>ExplorationSessionTests</c> 里锁。
    /// </remarks>
    [TestFixture]
    public sealed class ExplorationGridTests
    {
        [Test]
        public void 网格_矩形范围内的格子都可走_范围外不可走()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            var grid = lab.Grid(map, ExplorationLab.State());

            Assert.AreEqual(4, grid.Width);
            Assert.AreEqual(3, grid.Height);
            Assert.AreEqual(0, grid.BlockedCellCount, "没有任何交互物时，一个格子都不该被挡。");

            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    Assert.IsTrue(grid.IsWalkable(new GridPosition(x, y)), $"({x},{y}) 应当可走。");
                }
            }

            // 只要越界一格就该判不可走：靠「夹到边界」实现会让人以为墙外还有一格。
            Assert.IsFalse(grid.IsWalkable(new GridPosition(-1, 0)));
            Assert.IsFalse(grid.IsWalkable(new GridPosition(0, -1)));
            Assert.IsFalse(grid.IsWalkable(new GridPosition(4, 0)));
            Assert.IsFalse(grid.IsWalkable(new GridPosition(0, 3)));
            Assert.IsFalse(grid.IsInside(new GridPosition(4, 2)), "右边界的下一格属于图外。");
        }

        [Test]
        public void 网格_可见交互物占住的格子不可走()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);
            var chest = lab.Interactable("INT_CH01_CHEST", map.Id, 2, 1, targetId: "LUT_CH01_001");

            var grid = lab.Grid(map, ExplorationLab.State(), chest);

            Assert.IsFalse(grid.IsWalkable(new GridPosition(2, 1)), "交互物站着的那一格不能走进去。");
            Assert.IsTrue(grid.IsWalkable(new GridPosition(1, 1)), "相邻的格子照旧可走。");
            Assert.AreEqual(1, grid.BlockedCellCount);
            Assert.AreSame(chest, grid.InteractableAt(new GridPosition(2, 1)));
        }

        [Test]
        public void 网格_条件未满足且要求隐藏的交互物_既不挡路也不可见()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            // 开局那口「要等序章结束才出现」的宝箱：条件未满足时连看都看不见。
            var chest = lab.Interactable(
                "INT_CH01_CHEST",
                map.Id,
                2,
                1,
                requiredStateKey: "flag.ch01.prologue_done",
                requiredValue: 1,
                hiddenUntilConditionMet: true);

            var grid = lab.Grid(map, ExplorationLab.State(), chest);

            Assert.AreEqual(0, grid.VisibleInteractables.Count);
            Assert.AreEqual(0, grid.BlockedCellCount, "看不见的东西不该挡路——否则玩家会撞上一堵看不见的墙。");
            Assert.IsTrue(grid.IsWalkable(new GridPosition(2, 1)));
            Assert.IsNull(grid.InteractableAt(new GridPosition(2, 1)));
        }

        [Test]
        public void 网格_条件未满足但没要求隐藏的交互物_看得见也不让走()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            // 看得见、还锁着的那扇门：占格挡路，但交互时会被拒（原因见会话层）。
            var door = lab.Interactable(
                "INT_CH01_DOOR",
                map.Id,
                1,
                1,
                interactionTypeKey: "test.explore.interact.door",
                targetId: "CH01_MAP02",
                requiredStateKey: "flag.ch01.door_open",
                requiredValue: 1);

            var grid = lab.Grid(map, ExplorationLab.State(), door);

            Assert.AreEqual(1, grid.VisibleInteractables.Count, "没要求隐藏就该看得见。");
            Assert.IsFalse(grid.IsWalkable(new GridPosition(1, 1)));
            Assert.IsFalse(
                InteractableConditions.MeetsRequirement(door, ExplorationLab.State()),
                "看得见不代表条件满足。");
        }

        [Test]
        public void 网格_越界的交互物被忽略_不影响可走格()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            // 坐标写飞了：数据错，但探索不该因此崩，也不该把图外当成一堵墙。
            var stray = lab.Interactable("INT_CH01_STRAY", map.Id, 9, 9);

            var grid = lab.Grid(map, ExplorationLab.State(), stray);

            Assert.AreEqual(1, grid.IgnoredInteractableCount);
            Assert.AreEqual(0, grid.VisibleInteractables.Count);
            Assert.AreEqual(0, grid.BlockedCellCount);
            Assert.IsTrue(grid.IsWalkable(new GridPosition(3, 2)), "边界内的格子不受图外坏数据影响。");
        }

        [Test]
        public void 网格_同格两条交互物都保留_交互取第一条()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);
            var first = lab.Interactable("INT_CH01_FIRST", map.Id, 1, 1);
            var second = lab.Interactable("INT_CH01_SECOND", map.Id, 1, 1);

            var grid = lab.Grid(map, ExplorationLab.State(), first, second);

            Assert.AreEqual(2, grid.VisibleInteractables.Count, "两条都留着：丢掉一条等于悄悄吃掉策划的数据。");
            Assert.AreEqual(2, grid.InteractablesAt(new GridPosition(1, 1)).Count);
            Assert.AreSame(first, grid.InteractableAt(new GridPosition(1, 1)), "同格冲突时取数据里的第一条。");
            Assert.AreEqual(1, grid.BlockedCellCount, "只有一格被挡，不重复计数。");
        }

        [Test]
        public void 网格_别的地图的交互物被忽略()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(id: "CH01_MAP01", width: 4, height: 3);
            var other = lab.Interactable("INT_CH01_OTHER", "CH01_MAP02", 1, 1);

            var grid = lab.Grid(map, ExplorationLab.State(), other);

            Assert.AreEqual(1, grid.IgnoredInteractableCount);
            Assert.AreEqual(0, grid.BlockedCellCount);
            Assert.IsTrue(grid.IsWalkable(new GridPosition(1, 1)));
        }

        [Test]
        public void 网格_尺寸为零的地图_建出空网格不抛异常()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 0, height: 0);

            // 尺寸为 0 的数据是坏的（Validate 会报 MAP_GRID_INVALID），但建网格不该抛：
            // 探索的调用方是流程与界面，炸在这里会连带把启动自检一起带走。
            var grid = lab.Grid(map, ExplorationLab.State());

            Assert.AreEqual(0, grid.Width);
            Assert.AreEqual(0, grid.Height);
            Assert.IsFalse(grid.IsInside(GridPosition.Origin));
            Assert.IsFalse(grid.IsWalkable(GridPosition.Origin));
        }

        [Test]
        public void 就近落点_锚点本身可走就原样返回()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            var grid = lab.Grid(map, ExplorationLab.State());

            // 最常见的换图：两张图上同一格都空着，那就别动它。
            Assert.AreEqual(new GridPosition(2, 1), grid.NearestWalkable(new GridPosition(2, 1)));
        }

        [Test]
        public void 就近落点_锚点越界先夹进边界()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);

            var grid = lab.Grid(map, ExplorationLab.State());

            // 来源图比目标图大时必然发生：门在 (18,6)，目标图只有 4x3。
            Assert.AreEqual(new GridPosition(3, 2), grid.NearestWalkable(new GridPosition(18, 6)));
            Assert.AreEqual(GridPosition.Origin, grid.NearestWalkable(new GridPosition(-5, -5)));
        }

        [Test]
        public void 就近落点_锚点被占时按固定的环上顺序就近取()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 4, height: 3);
            var center = lab.Interactable("INT_CH01_CENTER", map.Id, 2, 1);
            var above = lab.Interactable("INT_CH01_ABOVE", map.Id, 2, 2);

            var grid = lab.Grid(map, ExplorationLab.State(), center, above);

            // 先只看中心被占：同环里先查四个正方向，上在前，于是 (2,2) 胜出。
            var onlyCenter = lab.Grid(map, ExplorationLab.State(), center);
            Assert.AreEqual(new GridPosition(2, 2), onlyCenter.NearestWalkable(new GridPosition(2, 1)));

            // 正上方也被占：正方向按 上 → 右 → 下 → 左 走，于是轮到 (3,1)。
            // 这条不是「随便挑一个近的」——顺序固定，换图落点才可复现、可写进存档。
            Assert.AreEqual(new GridPosition(3, 1), grid.NearestWalkable(new GridPosition(2, 1)));
        }

        [Test]
        public void 就近落点_只有一格可走时也能找到它()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 2, height: 1);
            var blocked = lab.Interactable("INT_CH01_BLOCKED", map.Id, 0, 0);

            var grid = lab.Grid(map, ExplorationLab.State(), blocked);

            Assert.AreEqual(new GridPosition(1, 0), grid.NearestWalkable(GridPosition.Origin));
        }

        [Test]
        public void 就近落点_整张图没有可走格_退回夹过的锚点且不抛()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 2, height: 1);
            var first = lab.Interactable("INT_CH01_A", map.Id, 0, 0);
            var second = lab.Interactable("INT_CH01_B", map.Id, 1, 0);

            var grid = lab.Grid(map, ExplorationLab.State(), first, second);

            // 退回原点会让「这张图坏了」看起来像「落点在原点」，所以退的是锚点本身，并记错误日志。
            Assert.AreEqual(GridPosition.Origin, grid.NearestWalkable(GridPosition.Origin));
        }

        [Test]
        public void 就近落点_尺寸为零的地图返回原点不抛异常()
        {
            using var lab = new ExplorationLab();
            var map = lab.Map(width: 0, height: 0);

            var grid = lab.Grid(map, ExplorationLab.State());

            Assert.AreEqual(GridPosition.Origin, grid.NearestWalkable(new GridPosition(3, 3)));
        }

        [Test]
        public void 条件_没有条件键时恒为满足_且不查状态源()
        {
            using var lab = new ExplorationLab();
            var interactable = lab.Interactable("INT_CH01_PLAIN", "CH01_MAP01", 1, 1);
            var state = (ExplorationLab.DictionaryStateSource)ExplorationLab.State();

            Assert.IsTrue(InteractableConditions.MeetsRequirement(interactable, state));
            Assert.IsTrue(InteractableConditions.IsVisible(interactable, state));
            Assert.AreEqual(0, state.Queried.Count, "没有条件键就不该去问状态源。");
        }

        [TestCase(CompareOperator.Equal, 3, true)]
        [TestCase(CompareOperator.Equal, 4, false)]
        [TestCase(CompareOperator.NotEqual, 4, true)]
        [TestCase(CompareOperator.NotEqual, 3, false)]
        [TestCase(CompareOperator.Greater, 2, true)]
        [TestCase(CompareOperator.Greater, 3, false)]
        [TestCase(CompareOperator.GreaterOrEqual, 3, true)]
        [TestCase(CompareOperator.GreaterOrEqual, 4, false)]
        [TestCase(CompareOperator.Less, 4, true)]
        [TestCase(CompareOperator.Less, 3, false)]
        [TestCase(CompareOperator.LessOrEqual, 3, true)]
        [TestCase(CompareOperator.LessOrEqual, 2, false)]
        public void 条件_六种运算符各自的口径(CompareOperator comparison, int target, bool expected)
        {
            // 键在表里读作 3，逐条钉住 requiredOperator 的含义：换运算符就是换语义，别让两处解释不一样。
            Assert.AreEqual(expected, InteractableConditions.Compare(3, target, comparison));
        }

        [Test]
        public void 条件_未知运算符判不满足()
        {
            // 表里填了个没定义的运算符（或枚举将来加了新值而这个 switch 忘了跟上）：
            // 一律判「不满足」，绝不判「满足」——放行一个条件不明的物件比重重拒绝一次危险得多。
            Assert.IsFalse(InteractableConditions.Compare(3, 3, (CompareOperator)99));
        }
    }
}
