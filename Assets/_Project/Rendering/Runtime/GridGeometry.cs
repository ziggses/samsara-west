using SamsaraWest.Data;
using SamsaraWest.Exploration;
using UnityEngine;

namespace SamsaraWest.Rendering
{
    /// <summary>
    /// 网格坐标 ↔ 世界坐标的<b>唯一</b>换算处。
    /// </summary>
    /// <remarks>
    /// <b>为什么单拎出来</b>：底图、交互物、角色、相机四边都要用同一套换算。任何一边各算各的，
    /// 就会出「底图对得上、交互物差半格」这类要查半天的偏差。定死在一处，改也只改这里。
    ///
    /// <b>口径</b>（ADR-032）：
    /// <list type="bullet">
    /// <item>1 格 = <see cref="BaselineTilePixelSize"/> 像素 = 1 世界单位（PPU 固定 <see cref="BaselinePixelsPerUnit"/>）。</item>
    /// <item>格 (0,0) 的<b>左下角</b>落在 <see cref="MapOrigin"/>；格 (x,y) 的<b>中心</b>是 (x+0.5, y+0.5)。</item>
    /// <item>立在地面上的精灵（角色、山门、树）挂在<b>格底边中点</b><see cref="TileBottomCenter"/> 上，不是格中心。</item>
    /// <item>底图与网格 1:1：<c>花果山前山_1x.png</c> 1920×1280 正好 60×40 格，见 <see cref="BackgroundMatchesGrid"/>。</item>
    /// <item><see cref="MapDefinition.OriginOffsetX"/>／<see cref="MapDefinition.OriginOffsetY"/> 按<b>像素</b>解释
    /// （与 <see cref="MapDefinition.TilePixelSize"/> 同单位）。当前 5 张图都是 0；非 0 分支由编辑期测试钉住。</item>
    /// </list>
    /// </remarks>
    public static class GridGeometry
    {
        /// <summary>已交付素材的瓦片像素尺寸。美术只改内容、不改规格（ADR-032），所以这是个基线而不是配置。</summary>
        public const int BaselineTilePixelSize = 32;

        /// <summary>像素与世界单位的比例。与 <see cref="BaselineTilePixelSize"/> 相等，于是「1 格 = 1 单位」。</summary>
        public const float BaselinePixelsPerUnit = 32f;

        /// <summary>一格在世界里的边长。瓦片 32、PPU 32 时正好是 1。</summary>
        public static float TileWorldSize(MapDefinition map)
        {
            var tilePixelSize = map == null ? BaselineTilePixelSize : map.TilePixelSize;
            return TileWorldSize(tilePixelSize);
        }

        /// <summary>一格在世界里的边长。</summary>
        public static float TileWorldSize(int tilePixelSize)
        {
            return tilePixelSize <= 0 ? 1f : tilePixelSize / BaselinePixelsPerUnit;
        }

        /// <summary>格中心的世界坐标。</summary>
        public static Vector3 TileCenter(MapDefinition map, GridPosition cell)
        {
            if (map == null)
            {
                return Vector3.zero;
            }

            return TileCenter(cell, map.TilePixelSize, map.OriginOffsetX, map.OriginOffsetY);
        }

        /// <summary>格中心的世界坐标。</summary>
        public static Vector3 TileCenter(GridPosition cell, int tilePixelSize, float originOffsetX, float originOffsetY)
        {
            var size = TileWorldSize(tilePixelSize);
            return new Vector3(
                (originOffsetX / BaselinePixelsPerUnit) + ((cell.X + 0.5f) * size),
                (originOffsetY / BaselinePixelsPerUnit) + ((cell.Y + 0.5f) * size),
                0f);
        }

        /// <summary>格底边中点的世界坐标。立在地面上的精灵挂这里。</summary>
        public static Vector3 TileBottomCenter(MapDefinition map, GridPosition cell)
        {
            var center = TileCenter(map, cell);
            return new Vector3(center.x, center.y - (TileWorldSize(map) * 0.5f), 0f);
        }

        /// <summary>整张图的世界尺寸（单位即格数）。</summary>
        public static Vector2 MapSize(MapDefinition map)
        {
            if (map == null)
            {
                return Vector2.zero;
            }

            var size = TileWorldSize(map);
            return new Vector2(map.GridWidth * size, map.GridHeight * size);
        }

        /// <summary>网格原点（格 (0,0) 的左下角）的世界坐标。底图也钉在这里。</summary>
        public static Vector3 MapOrigin(MapDefinition map)
        {
            if (map == null)
            {
                return Vector3.zero;
            }

            return new Vector3(
                map.OriginOffsetX / BaselinePixelsPerUnit,
                map.OriginOffsetY / BaselinePixelsPerUnit,
                0f);
        }

        /// <summary>整张图的中心。相机没有跟随目标时，退到这里看全局。</summary>
        public static Vector3 MapCenter(MapDefinition map)
        {
            var origin = MapOrigin(map);
            var size = MapSize(map);
            return new Vector3(origin.x + (size.x * 0.5f), origin.y + (size.y * 0.5f), 0f);
        }

        /// <summary>
        /// 底图尺寸是否与网格 1:1。
        /// </summary>
        /// <remarks>
        /// 不做「自动拉伸到网格」：底图与网格对不上说明素材本身与数据不符，
        /// 悄悄拉伸只会把素材错误藏起来，等到美术换图时才爆。宁可留白也不掩盖。
        /// </remarks>
        public static bool BackgroundMatchesGrid(MapDefinition map, int textureWidth, int textureHeight)
        {
            if (map == null)
            {
                return false;
            }

            return textureWidth == map.GridWidth * map.TilePixelSize
                && textureHeight == map.GridHeight * map.TilePixelSize;
        }
    }
}
