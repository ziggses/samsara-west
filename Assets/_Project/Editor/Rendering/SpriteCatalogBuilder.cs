using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SamsaraWest.Data;
using SamsaraWest.Rendering;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Editor.Rendering
{
    /// <summary>
    /// 重建 <see cref="SpriteCatalog"/>：把「素材键 → 图上一块矩形」整理成一份资产。
    /// </summary>
    /// <remarks>
    /// <b>两个来源，各管一类</b>：
    /// <list type="number">
    /// <item><b>图集 JSON</b>——帧自带 <c>spriteKey</c> 的（如 <c>花果山前山_元素图集.json</c>），
    /// 自动绑定 <c>sprite.int.*</c>。美术改图集，这里跟着变，不用人插手。</item>
    /// <item><b><c>sprite-sources.csv</c></b>——整张图就是一个精灵的（底图、立绘），
    /// 键与文件的对应关系只有人能定，所以列在表里。</item>
    /// </list>
    ///
    /// <b>为什么产物要进版本库</b>：程序产物入 git 才好 diff、好 Review，也才好在没有素材的工作区里
    /// 看出「原本该绑哪些键」。但 <c>Assets/_External</c> 是目录联接、不在库里，
    /// 所以换工作区后要重跑一次本工具，把贴图引用重新接上。
    /// </remarks>
    public static class SpriteCatalogBuilder
    {
        /// <summary>整图绑定表的工程内路径。</summary>
        public const string SourcesAssetPath = "Assets/_Project/Rendering/Data/sprite-sources.csv";

        private const string MenuPath = "Tools/SamsaraWest/重建精灵目录";

        /// <summary>一次构建的结果。</summary>
        public sealed class BuildReport
        {
            /// <summary>写进目录的绑定条数。</summary>
            public int Bound;

            /// <summary>图集里带 spriteKey 的帧数。</summary>
            public int AtlasKeyedFrames;

            /// <summary>图集里没有 spriteKey 的帧数（多半是装饰，不需要键）。</summary>
            public int AtlasUnkeyedFrames;

            /// <summary>表里写了路径、但文件不存在的条目。<b>这是错误</b>，多半是路径写错了。</summary>
            public int BrokenSourceCount;

            /// <summary>表里留空、故意等美术交付的条目。</summary>
            public int PendingSourceCount;

            /// <summary>底图键在定义目录里有、但精灵目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingBackgroundKeys = new List<string>();

            /// <summary>交互物素材键在数据里有、但目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingInteractableKeys = new List<string>();

            /// <summary>装备素材键在数据里有、但目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingEquipmentKeys = new List<string>();

            /// <summary>道具素材键在数据里有、但目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingItemKeys = new List<string>();

            /// <summary>经书素材键在数据里有、但目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingSutraKeys = new List<string>();

            /// <summary>角色立绘／战斗图键在数据里有、但目录里没有的，逐条列出。</summary>
            public readonly List<string> MissingCharacterKeys = new List<string>();

            /// <summary>提示信息（重复键、图集缺贴图等）。</summary>
            public readonly List<string> Notes = new List<string>();

            public bool HasProblems => BrokenSourceCount > 0;

            public string Summary()
            {
                var builder = new StringBuilder();
                builder.Append("精灵目录：绑定 ").Append(Bound).Append(" 条");
                builder.Append("（图集带键帧 ").Append(AtlasKeyedFrames).Append("／无键帧 ").Append(AtlasUnkeyedFrames).Append("）");
                builder.Append("、数据缺口（底图 ").Append(MissingBackgroundKeys.Count);
                builder.Append("／交互物 ").Append(MissingInteractableKeys.Count);
                builder.Append("／装备 ").Append(MissingEquipmentKeys.Count);
                builder.Append("／道具 ").Append(MissingItemKeys.Count);
                builder.Append("／经书 ").Append(MissingSutraKeys.Count);
                builder.Append("／角色 ").Append(MissingCharacterKeys.Count).Append("）");
                builder.Append("、待交付 ").Append(PendingSourceCount);
                builder.Append("、路径写错 ").Append(BrokenSourceCount);
                return builder.ToString();
            }
        }

        [MenuItem(MenuPath)]
        public static void RebuildFromMenu()
        {
            var report = Build(true);
            Debug.Log($"[SamsaraWest] {report.Summary()}");
        }

        /// <summary>只重绑、不重导素材。改过绑定表或图集 JSON 之后跑这个，几秒就好。</summary>
        [MenuItem(MenuPath + "（只重绑，不重导素材）")]
        public static void RebindFromMenu()
        {
            var report = Build(false);
            Debug.Log($"[SamsaraWest] {report.Summary()}");
        }

        /// <summary>重建精灵目录。</summary>
        /// <param name="reimportExternal">
        /// 是否先强制重导入 <c>Assets/_External</c> 下的贴图。改了导入参数、或换工作区后要置 true；
        /// 只改了绑定表则 false，能省下几百张贴图的导入时间。
        /// </param>
        public static BuildReport Build(bool reimportExternal)
        {
            var report = new BuildReport();

            if (reimportExternal)
            {
                ReimportExternalTextures();
            }

            var bindings = new List<SpriteBinding>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            ReadAtlasBindings(bindings, seen, report);
            ReadWholeImageBindings(bindings, seen, report);

            var catalog = EnsureCatalogAsset();
            catalog.SetBindings(bindings.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            report.Bound = bindings.Count;
            ReportDefinitionGaps(catalog, report);

            foreach (var note in report.Notes)
            {
                Debug.LogWarning($"[SamsaraWest] {note}");
            }

            return report;
        }

        /// <summary>取到目录资产；没有就建出来（连同 Resources 目录）。</summary>
        private static SpriteCatalog EnsureCatalogAsset()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(SpriteCatalog.AssetPath);
            if (catalog != null)
            {
                return catalog;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Rendering/Resources"))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Rendering", "Resources");
            }

            catalog = ScriptableObject.CreateInstance<SpriteCatalog>();
            AssetDatabase.CreateAsset(catalog, SpriteCatalog.AssetPath);
            return catalog;
        }

        /// <summary>强制重导入外部素材，让后处理器有机会改导入参数。</summary>
        private static void ReimportExternalTextures()
        {
            ExternalSpritePostprocessor.ResetCounters();

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ExternalSpritePostprocessor.ExternalRoot });
            try
            {
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!ExternalSpritePostprocessor.IsExternalAsset(path))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("重建精灵目录", path, guids.Length == 0 ? 0f : i / (float)guids.Length);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>扫外部素材里的图集 JSON，把带 spriteKey 的帧绑上。</summary>
        private static void ReadAtlasBindings(List<SpriteBinding> bindings, HashSet<string> seen, BuildReport report)
        {
            foreach (var jsonPath in EnumerateExternalJson(report))
            {
                if (!AtlasJsonReader.TryRead(jsonPath, out var atlas, out var error))
                {
                    if (error != null)
                    {
                        report.Notes.Add($"图集 {jsonPath} 读不了：{error}");
                    }

                    continue;
                }

                var keyedFrames = 0;
                foreach (var frame in atlas.Frames)
                {
                    if (string.IsNullOrEmpty(frame.SpriteKey))
                    {
                        report.AtlasUnkeyedFrames++;
                        continue;
                    }

                    keyedFrames++;
                }

                report.AtlasKeyedFrames += keyedFrames;
                if (keyedFrames == 0)
                {
                    // 武器图集、角色图集都是这种：帧有名字但没有 spriteKey。
                    // 要绑它们得先有一张「数据键 → 帧名」的对照表，那是另一件事，先如实报出来。
                    report.Notes.Add($"图集 {jsonPath} 的 {atlas.Frames.Count} 帧都没有 spriteKey，暂不绑定。");
                    continue;
                }

                var texturePath = ResolveAtlasTexturePath(jsonPath, atlas);
                if (texturePath == null)
                {
                    report.Notes.Add($"图集 {jsonPath} 的贴图找不到（字段 atlas／image／body 指向 {atlas.ImageFile ?? "空"}）。");
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null)
                {
                    report.Notes.Add($"图集贴图 {texturePath} 没导入成 Texture2D。");
                    continue;
                }

                foreach (var frame in atlas.Frames)
                {
                    if (string.IsNullOrEmpty(frame.SpriteKey))
                    {
                        continue;
                    }

                    if (!seen.Add(frame.SpriteKey))
                    {
                        report.Notes.Add($"素材键 {frame.SpriteKey} 重复（后出现的来自 {jsonPath}），保留先出现的那条。");
                        continue;
                    }

                    var pivot = AtlasJsonReader.PivotFor(frame.Anchor);
                    bindings.Add(new SpriteBinding(
                        frame.SpriteKey,
                        texture,
                        frame.ToRect(texture.height),
                        pivot,
                        GridGeometry.BaselinePixelsPerUnit));
                }
            }
        }

        /// <summary>读整图绑定表。</summary>
        private static void ReadWholeImageBindings(List<SpriteBinding> bindings, HashSet<string> seen, BuildReport report)
        {
            if (!File.Exists(SourcesAssetPath))
            {
                report.Notes.Add($"整图绑定表 {SourcesAssetPath} 不存在，底图与立绘都不会绑上。");
                return;
            }

            var lines = File.ReadAllLines(SourcesAssetPath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                // notes 里可能有逗号，所以最多切 4 段。
                var columns = line.Split(new[] { ',' }, 4);
                if (columns.Length < 2)
                {
                    report.Notes.Add($"绑定表第 {i + 1} 行字段不够（至少要 spriteKey,assetPath）：{line}");
                    continue;
                }

                var key = columns[0].Trim();
                var assetPath = columns[1].Trim();
                var pivotName = columns.Length >= 3 ? columns[2].Trim() : "bottom-left";

                if (key.Length == 0)
                {
                    report.Notes.Add($"绑定表第 {i + 1} 行没有 spriteKey：{line}");
                    continue;
                }

                if (assetPath.Length == 0)
                {
                    // 故意留空 = 这把键还在等美术。不是错误，但要报出来，免得当成「已经接好了」。
                    report.PendingSourceCount++;
                    continue;
                }

                if (!seen.Add(key))
                {
                    report.Notes.Add($"素材键 {key} 重复（绑定表第 {i + 1} 行），保留先出现的那条。");
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    report.BrokenSourceCount++;
                    report.Notes.Add($"绑定表第 {i + 1} 行的 {assetPath} 找不到或不是贴图。");
                    continue;
                }

                bindings.Add(new SpriteBinding(
                    key,
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    AtlasJsonReader.PivotFor(pivotName),
                    GridGeometry.BaselinePixelsPerUnit));
            }
        }

        /// <summary>图集 JSON 旁边的那张贴图。</summary>
        private static string ResolveAtlasTexturePath(string jsonPath, AtlasJsonReader.Atlas atlas)
        {
            if (string.IsNullOrEmpty(atlas.ImageFile))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(jsonPath);
            if (directory == null)
            {
                return null;
            }

            var candidate = Path.Combine(directory, atlas.ImageFile).Replace('\\', '/');
            return AssetDatabase.LoadAssetAtPath<Texture2D>(candidate) == null ? null : candidate;
        }

        /// <summary>列出外部素材里所有可能的图集 JSON（跳过中间产物目录）。</summary>
        private static IEnumerable<string> EnumerateExternalJson(BuildReport report)
        {
            var root = ExternalSpritePostprocessor.ExternalRoot;

            // 必须用绝对路径枚举：批处理模式下当前目录不保证是工程根，传相对路径时
            // 枚举出来的路径里就没有 "/Assets/_External/" 这一段，再按它去找工程内路径会一个都匹配不上，
            // 而且不报错——图集键全绑不上，日志上却看不出为什么。
            var absoluteRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, root)
                .Replace('\\', '/');

            if (!Directory.Exists(absoluteRoot))
            {
                report.Notes.Add($"外部素材根 {absoluteRoot} 不存在，图集一个都读不到。");
                yield break;
            }

            var scanned = 0;
            foreach (var absolute in Directory.EnumerateFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                scanned++;

                // 只用相对尾巴拼工程内路径，不做子串查找：拼接是确定的，查找依赖分隔符与大小写。
                var tail = absolute.Replace('\\', '/').Substring(absoluteRoot.Length).TrimStart('/');
                var projectPath = root + "/" + tail;
                if (!ExternalSpritePostprocessor.IsExternalAsset(projectPath))
                {
                    continue;
                }

                yield return projectPath;
            }

            if (scanned == 0)
            {
                report.Notes.Add($"外部素材根 {absoluteRoot} 下一个 .json 都没有，图集键一个都绑不上。");
            }
        }

        /// <summary>拿定义目录对一遍：数据侧写了素材键、但精灵目录里没有图的，按类别逐条列出。</summary>
        /// <remarks>
        /// 覆盖六类：底图（maps）、交互物（interactables）、装备（equipment）、道具（items）、
        /// 经书（sutras）、角色（characters）。<b>只管「数据有键、目录没图」这一个方向</b>；
        /// 反方向（图集里有帧、数据侧还没键）由图集 JSON 那段报（<see cref="BuildReport.AtlasUnkeyedFrames"/>）。
        /// 两个方向的数各自可读，合起来才是素材与数据之间的真实缺口。
        /// </remarks>
        private static void ReportDefinitionGaps(SpriteCatalog catalog, BuildReport report)
        {
            var definitions = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            if (definitions == null)
            {
                report.Notes.Add("定义目录资产缺失，无法核对素材缺口。");
                return;
            }

            foreach (var map in definitions.OfKind<MapDefinition>())
            {
                CollectMissing(report.MissingBackgroundKeys, map.Id, "backgroundSpriteKey", map.BackgroundSpriteKey, catalog);
            }

            foreach (var interactable in definitions.OfKind<InteractableDefinition>())
            {
                CollectMissing(report.MissingInteractableKeys, interactable.Id, "interactSpriteKey", interactable.InteractSpriteKey, catalog);
            }

            foreach (var equipment in definitions.OfKind<EquipmentDefinition>())
            {
                CollectMissing(report.MissingEquipmentKeys, equipment.Id, "spriteKey", equipment.SpriteKey, catalog);
            }

            foreach (var item in definitions.OfKind<ItemDefinition>())
            {
                CollectMissing(report.MissingItemKeys, item.Id, "spriteKey", item.SpriteKey, catalog);
            }

            foreach (var sutra in definitions.OfKind<SutraDefinition>())
            {
                CollectMissing(report.MissingSutraKeys, sutra.Id, "spriteKey", sutra.SpriteKey, catalog);
            }

            foreach (var character in definitions.OfKind<CharacterDefinition>())
            {
                // 立绘与战斗图是两把独立的键，各自算一条缺口：只有立绘绑上时，缺口数仍会如实报出战斗图。
                CollectMissing(report.MissingCharacterKeys, character.Id, "portraitKey", character.PortraitKey, catalog);
                CollectMissing(report.MissingCharacterKeys, character.Id, "battleSpriteKey", character.BattleSpriteKey, catalog);
            }

            ReportGapNote(report, "底图", report.MissingBackgroundKeys);
            ReportGapNote(report, "交互物", report.MissingInteractableKeys);
            ReportGapNote(report, "装备", report.MissingEquipmentKeys);
            ReportGapNote(report, "道具", report.MissingItemKeys);
            ReportGapNote(report, "经书", report.MissingSutraKeys);
            ReportGapNote(report, "角色", report.MissingCharacterKeys);
        }

        /// <summary>数据侧写了键、目录里却没图，就记一条。键留空跳过——那一列本来就没填，不是缺口。</summary>
        private static void CollectMissing(List<string> sink, string id, string field, string key, SpriteCatalog catalog)
        {
            if (string.IsNullOrEmpty(key) || catalog.Has(key))
            {
                return;
            }

            sink.Add($"{id}.{field} → {key}");
        }

        /// <summary>把一类的缺口汇总成一条提示，以便摘要之外还能看见究竟是哪几条。空类不出声。</summary>
        private static void ReportGapNote(BuildReport report, string label, List<string> missing)
        {
            if (missing.Count == 0)
            {
                return;
            }

            report.Notes.Add($"{label}缺素材 {missing.Count} 条：{string.Join("、", missing)}");
        }
    }
}
