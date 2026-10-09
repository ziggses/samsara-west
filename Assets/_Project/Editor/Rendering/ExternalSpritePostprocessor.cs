using SamsaraWest.Rendering;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor.Rendering
{
    /// <summary>
    /// 把 <c>Assets/_External</c> 下的贴图按像素画的参数导入，不需要人手工点。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须是代码而不是手工改 .meta</b>：<c>Assets/_External</c> 是指向
    /// <c>E:\tx2\素材</c> 的目录联接，且<b>不在版本库里</b>——它的 <c>.meta</c> 提交不了。
    /// 手工改的导入参数换个工作区就没了，只有走 <see cref="AssetPostprocessor"/> 才可复现。
    ///
    /// <b>参数依据</b>：美术侧的视觉锚点是 32×32 素材（见 <c>架构决策.md</c> 的界面基准稿一节），
    /// 所以 PPU 取 32、1 格 = 32 像素；像素画必须 Point 采样、关 mipmap、不压缩，
    /// 否则放大后边缘发糊、出现色带。
    /// </remarks>
    public sealed class ExternalSpritePostprocessor : AssetPostprocessor
    {
        /// <summary>外部素材根的工程内路径。</summary>
        public const string ExternalRoot = "Assets/_External";

        /// <summary>
        /// 这些子目录不导入。
        /// </summary>
        /// <remarks>
        /// <c>preview</c> 是给人看核对图的放大渲染（尺寸不成比例，不是游戏素材）；
        /// <c>_work</c>／<c>_src</c>／<c>_base_1x</c> 是生成过程中的中间产物。
        /// 导进来只会白占内存、拖慢导入，还会让人分不清哪张才是要用的那张。
        /// </remarks>
        private static readonly string[] ExcludedFolders =
        {
            "/preview/",
            "/_work/",
            "/_src/",
            "/_base_1x/",
        };

        /// <summary>已跳过的贴图数。无头导入后用来看「跳得对不对」。</summary>
        public static int SkippedCount { get; private set; }

        /// <summary>已经按像素画参数处理过的贴图数。</summary>
        public static int AppliedCount { get; private set; }

        /// <summary>发布前把外部素材重新导入一遍时，用来重置计数。</summary>
        public static void ResetCounters()
        {
            SkippedCount = 0;
            AppliedCount = 0;
        }

        /// <summary>这个路径归不归本管线管。</summary>
        public static bool IsExternalAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith(ExternalRoot + "/", System.StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var folder in ExcludedFolders)
            {
                if (normalized.Contains(folder))
                {
                    return false;
                }
            }

            return true;
        }

        private void OnPreprocessTexture()
        {
            if (!IsExternalAsset(assetPath))
            {
                if (assetPath != null && assetPath.Replace('\\', '/').StartsWith(ExternalRoot + "/", System.StringComparison.Ordinal))
                {
                    SkippedCount++;
                }

                return;
            }

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = GridGeometry.BaselinePixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 8192;

            // 图集里的元素靠 JSON 里的矩形切，不靠 Unity 的多精灵模式；
            // 这样素材换内容时只重跑构建器，不必重建任何 Sprite 资产。
            importer.spriteBorder = Vector4.zero;

            AppliedCount++;
        }
    }
}
