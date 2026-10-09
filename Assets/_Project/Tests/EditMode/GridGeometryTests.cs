using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Rendering;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 渲染层的坐标换算。这套换算是底图、交互物、角色、相机<b>共用的唯一口径</b>，
    /// 任何一边各算各的就会出现「底图对得上、物件差半格」——所以这里把它钉死在数值上。
    /// </summary>
    /// <remarks>
    /// 全程只做数学，不碰贴图：素材还没交付也能把「一格多大、挂在格子的哪一点」算清楚。
    /// </remarks>
    public sealed class GridGeometryTests
    {
        private ExplorationLab _lab;

        [SetUp]
        public void SetUp() => _lab = new ExplorationLab();

        [TearDown]
        public void TearDown() => _lab.Dispose();

        [Test]
        public void Baseline_KeepsTilePixelsAndPixelsPerUnitEqual()
        {
            Assert.AreEqual(32, GridGeometry.BaselineTilePixelSize, "已交付素材是 32×32，这条基线不许悄悄漂。");
            Assert.AreEqual(
                (float)GridGeometry.BaselineTilePixelSize,
                GridGeometry.BaselinePixelsPerUnit,
                "两者相等才保证「1 格 = 1 世界单位」，改一个不改另一个会让所有换算整体缩放。");
            Assert.AreEqual(1f, GridGeometry.TileWorldSize(GridGeometry.BaselineTilePixelSize));
        }

        [Test]
        public void TileWorldSize_IsJustPixelsOverBaseline()
        {
            Assert.AreEqual(0.5f, GridGeometry.TileWorldSize(16), "16 像素的格子在山寨单位里是一半。");
            Assert.AreEqual(2f, GridGeometry.TileWorldSize(64));
            Assert.AreEqual(1f, GridGeometry.TileWorldSize(0), "尺寸写坏时退成 1，不要除出无穷大。");
        }

        [Test]
        public void TileCenter_IsHalfACellInFromTheCorner()
        {
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0f), GridGeometry.TileCenter(new GridPosition(0, 0), 32, 0f, 0f));
            Assert.AreEqual(new Vector3(2.5f, 3.5f, 0f), GridGeometry.TileCenter(new GridPosition(2, 3), 32, 0f, 0f));
        }

        [Test]
        public void OriginOffset_IsReadAsPixels()
        {
            // 原点偏移 64 像素 = 2 格（PPU 32）：格 (0,0) 的左下角因此落在世界坐标 (2,2)。
            var map = _lab.Map("CH01_MAP01", 4, 3, 32, 64f, 64f);

            Assert.AreEqual(new Vector3(2f, 2f, 0f), GridGeometry.MapOrigin(map));
            Assert.AreEqual(new Vector3(2.5f, 2.5f, 0f), GridGeometry.TileCenter(map, new GridPosition(0, 0)));
        }

        [Test]
        public void TileBottomCenter_SitsOnTheGroundLineNotTheCenter()
        {
            var map = _lab.Map("CH01_MAP01", 4, 3);

            // 格 (1,1) 的中心是 (1.5,1.5)，底边中点是 (1.5,1)——立在地上的人站的是后者。
            Assert.AreEqual(new Vector3(1.5f, 1f, 0f), GridGeometry.TileBottomCenter(map, new GridPosition(1, 1)));
        }

        [Test]
        public void MapSize_CountsCellsNotPixels()
        {
            var map = _lab.Map("CH01_MAP01", 60, 40);

            Assert.AreEqual(new Vector2(60f, 40f), GridGeometry.MapSize(map), "瓦片 32、PPU 32 时，图的边长就等于格数。");
            Assert.AreEqual(new Vector3(30f, 20f, 0f), GridGeometry.MapCenter(map));
        }

        [Test]
        public void BackgroundMatchesGrid_AcceptsTheDeliveredFrontHillImage()
        {
            var map = _lab.Map("CH01_MAP01", 60, 40);

            // 花果山前山_1x.png 是 1920×1280，正好 60×40 格 @32 像素。
            Assert.IsTrue(GridGeometry.BackgroundMatchesGrid(map, 1920, 1280));
            Assert.IsFalse(GridGeometry.BackgroundMatchesGrid(map, 1920, 1279), "差一个像素也要报出来，不做「差不多就行」。");
            Assert.IsFalse(GridGeometry.BackgroundMatchesGrid(map, 960, 640), "缩过一半的图不算 1:1；宁可留白也不悄悄拉伸。");
        }

        [Test]
        public void NullMap_DegradesToOriginWithoutThrowing()
        {
            Assert.AreEqual(Vector3.zero, GridGeometry.MapOrigin(null));
            Assert.AreEqual(Vector3.zero, GridGeometry.MapCenter(null));
            Assert.AreEqual(Vector2.zero, GridGeometry.MapSize(null));
            Assert.AreEqual(Vector3.zero, GridGeometry.TileCenter(null, new GridPosition(3, 4)));
            Assert.AreEqual(
                new Vector3(0f, -0.5f, 0f),
                GridGeometry.TileBottomCenter(null, new GridPosition(0, 0)),
                "没有地图时退化到原点、再落半格；关键是别抛异常。");
            Assert.IsFalse(GridGeometry.BackgroundMatchesGrid(null, 1920, 1280));
        }
    }
}
