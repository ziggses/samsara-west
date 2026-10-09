using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 探索运行时测试的建场器。与 <see cref="BattleLab"/> 同一套路：
    /// 地图与交互物都是资产，测试要的是「刚好能钉住某条规则」的尺寸与坐标，
    /// 所以用反射直接写字段。反射只出现在测试程序集里。
    /// </summary>
    /// <remarks>
    /// 网格尺寸一律取小的（默认 4×3）：探索的口径集中在「边界、占格、朝向、遭遇」四处，
    /// 用小图能让断言里的坐标一眼可读，也不会让「越界」用例要写一行走十步。
    /// </remarks>
    internal sealed class ExplorationLab : IDisposable
    {
        internal const ulong DefaultSeed = 20240924UL;

        private const string KeyPrefix = "test.explore.";

        private readonly List<Object> _created = new List<Object>();

        private readonly List<DefinitionBase> _definitions = new List<DefinitionBase>();

        /// <summary>已登记的定义，供 <see cref="Registry"/> 装配。</summary>
        internal IReadOnlyList<DefinitionBase> Definitions => _definitions;

        /// <summary>
        /// 造一张地图。
        /// </summary>
        /// <param name="encounterId">野外遭遇 ID；不填表示这张图不遇敌。</param>
        /// <param name="encounterRate">
        /// 每走一步的遭遇概率。取 0 或 1 时 <c>Chance</c> 会短路、不消耗随机数，
        /// 因此「一定遇敌 / 一定不遇敌」的用例可以做到完全确定。
        /// </param>
        internal MapDefinition Map(
            string id = "CH01_MAP01",
            int width = 4,
            int height = 3,
            string encounterId = null,
            float encounterRate = 0f,
            bool isSafeZone = false,
            bool isTown = false,
            params string[] connections)
            => Map(id, width, height, 32, 0f, 0f, null, encounterId, encounterRate, isSafeZone, isTown, connections);

        /// <summary>
        /// 造一张带瓦片规格与原点偏移的地图。渲染层的坐标换算是按像素解释这两个字段的，
        /// 因此钉住换算的用例需要能把它们摆成非零，而不是只有「32 像素、原点在零」这一种。
        /// </summary>
        internal MapDefinition Map(
            string id,
            int width,
            int height,
            int tilePixelSize,
            float originOffsetX,
            float originOffsetY,
            string backgroundSpriteKey = null,
            string encounterId = null,
            float encounterRate = 0f,
            bool isSafeZone = false,
            bool isTown = false,
            params string[] connections)
        {
            var map = New<MapDefinition>(id, "map");
            Set(map, "_chapterIndex", 1);
            Set(map, "_sceneName", "TestMap");
            Set(map, "_gridWidth", width);
            Set(map, "_gridHeight", height);
            Set(map, "_tilePixelSize", tilePixelSize);
            Set(map, "_originOffsetX", originOffsetX);
            Set(map, "_originOffsetY", originOffsetY);
            Set(map, "_encounterTableId", encounterId);
            Set(map, "_encounterRate", encounterRate);
            Set(map, "_isTown", isTown);
            Set(map, "_isSafeZone", isSafeZone);
            Set(map, "_allowsSaving", true);
            Set(map, "_bgmKey", null);
            Set(map, "_ambientKey", null);
            Set(map, "_backgroundSpriteKey", backgroundSpriteKey);
            Set(map, "_connections", connections ?? Array.Empty<string>());
            return map;
        }

        /// <summary>
        /// 造一条交互物。
        /// </summary>
        /// <param name="requiredStateKey">条件键，形如 <c>flag.ch01.door_open</c>；为空表示无条件。</param>
        /// <param name="hiddenUntilConditionMet">
        /// 条件未满足时是否连看都看不见。为 false 时物件照旧可见、只是不让交互
        /// （对应「门就在那儿，但还锁着」）。
        /// </param>
        internal InteractableDefinition Interactable(
            string id,
            string mapId,
            int x,
            int y,
            string interactionTypeKey = null,
            string targetId = null,
            string requiredStateKey = null,
            CompareOperator comparison = CompareOperator.Equal,
            int requiredValue = 0,
            bool oneShot = false,
            bool hiddenUntilConditionMet = false)
        {
            var interactable = New<InteractableDefinition>(id, "int");
            Set(interactable, "_mapId", mapId);
            Set(interactable, "_gridX", x);
            Set(interactable, "_gridY", y);
            Set(interactable, "_interactionTypeKey", interactionTypeKey ?? KeyPrefix + "interact.chest");
            Set(interactable, "_targetId", targetId);
            Set(interactable, "_requiredStateKey", requiredStateKey);
            Set(interactable, "_requiredOperator", comparison);
            Set(interactable, "_requiredValue", requiredValue);
            Set(interactable, "_oneShot", oneShot);
            Set(interactable, "_isHiddenUntilConditionMet", hiddenUntilConditionMet);
            Set(interactable, "_promptKey", KeyPrefix + "prompt");
            Set(interactable, "_interactSpriteKey", null);
            return interactable;
        }

        /// <summary>一张「有几格开着的」网格，交互物由调用方给。</summary>
        internal ExplorationGrid Grid(
            MapDefinition map,
            IExplorationStateSource state,
            params InteractableDefinition[] interactables) =>
            ExplorationGrid.Build(map, interactables, state);

        /// <summary>开一次探索会话。事件总线与随机流都可以省：不接总线就只算结果不发事件。</summary>
        internal ExplorationSession Session(
            MapDefinition map,
            IExplorationStateSource state,
            IRandomStream stream = null,
            IEventBus bus = null,
            GridPosition start = default,
            MoveDirection facing = MoveDirection.South,
            params InteractableDefinition[] interactables) =>
            new ExplorationSession(map, interactables, state, stream, bus, start, facing);

        /// <summary>把已登记的定义装进数据目录，返回运行期查询入口。</summary>
        /// <param name="extra">
        /// 额外并入的定义（例如接线测试要同时装探索的地图与战斗的遭遇）。
        /// 之所以并成一张目录：接线两边查的都是同一份定义表。
        /// </param>
        internal IDefinitionRegistry Registry(IReadOnlyList<DefinitionBase> extra = null)
        {
            var definitions = new List<DefinitionBase>(_definitions);
            if (extra != null)
            {
                for (var i = 0; i < extra.Count; i++)
                {
                    definitions.Add(extra[i]);
                }
            }

            var catalog = ScriptableObject.CreateInstance<DefinitionCatalog>();
            _created.Add(catalog);
            catalog.SetDefinitions(definitions);
            catalog.Rebuild();
            return new DefinitionRegistry(catalog);
        }

        /// <summary>造一个探索随机流。同名同种子必然给出同一序列。</summary>
        internal static IRandomStream Stream(ulong seed = DefaultSeed) =>
            new PcgRandomStream(RandomStreams.Exploration, seed);

        internal static IEventBus Bus() => new EventBus();

        /// <summary>造一个「写死在测试里的」剧情状态源。</summary>
        internal static IExplorationStateSource State(params (string Key, int Value)[] entries)
        {
            var source = new DictionaryStateSource();
            for (var i = 0; i < entries.Length; i++)
            {
                source.Set(entries[i].Key, entries[i].Value);
            }

            return source;
        }

        public void Dispose()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            _definitions.Clear();
        }

        private T New<T>(string id, string keySuffix) where T : DefinitionBase
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = id;
            _created.Add(definition);
            _definitions.Add(definition);

            Set(definition, "_id", id);
            Set(definition, "_displayNameKey", KeyPrefix + keySuffix + ".name");
            Set(definition, "_descriptionKey", KeyPrefix + keySuffix + ".desc");
            Set(definition, "_tags", Array.Empty<string>());
            Set(definition, "_version", 1);
            return definition;
        }

        private static void Set(object target, string fieldName, object value) =>
            BattleLab.SetPrivate(target, fieldName, value);

        /// <summary>
        /// 测试用的剧情状态源：一本可以随手改的账，未登记的键读作 0
        /// （与 <see cref="MissingExplorationStateSource"/> 的口径一致）。
        /// </summary>
        internal sealed class DictionaryStateSource : IExplorationStateSource
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>被查过的键，按查询顺序。用来钉住「条件键真的被读了」。</summary>
            internal List<string> Queried { get; } = new List<string>();

            internal void Set(string key, int value) => _values[key] = value;

            public int GetValue(string stateKey)
            {
                Queried.Add(stateKey);
                return stateKey != null && _values.TryGetValue(stateKey, out var value) ? value : 0;
            }
        }
    }
}
