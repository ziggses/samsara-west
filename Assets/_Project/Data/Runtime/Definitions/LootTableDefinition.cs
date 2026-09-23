using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 掉落表。并列数组必须等长（itemIds[i] 对应 weights[i]、minCounts[i]、maxCounts[i]），
    /// 长度不一致会在编辑器导入期直接报错。
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "SamsaraWest/定义/掉落表 LootTable")]
    public sealed class LootTableDefinition : DefinitionBase
    {
        [CsvColumn("itemIds", required: true)] [SerializeField] private string[] _itemIds = System.Array.Empty<string>();
        [CsvColumn("weights")] [SerializeField] private int[] _weights = System.Array.Empty<int>();
        [CsvColumn("minCounts")] [SerializeField] private int[] _minCounts = System.Array.Empty<int>();
        [CsvColumn("maxCounts")] [SerializeField] private int[] _maxCounts = System.Array.Empty<int>();

        [Tooltip("必定掉落，不参与权重抽取。")]
        [CsvColumn("guaranteedItemIds")] [SerializeField] private string[] _guaranteedItemIds = System.Array.Empty<string>();

        [CsvColumn("guaranteedItemCounts")] [SerializeField] private int[] _guaranteedItemCounts = System.Array.Empty<int>();

        [Tooltip("按权重抽取的次数。0 表示不抽。")]
        [CsvColumn("dropRolls")] [SerializeField] private int _dropRolls;

        [CsvColumn("goldMin")] [SerializeField] private int _goldMin;
        [CsvColumn("goldMax")] [SerializeField] private int _goldMax;

        public override DefinitionKind Kind => DefinitionKind.LootTable;

        public string[] ItemIds => _itemIds ?? System.Array.Empty<string>();

        public int[] Weights => _weights ?? System.Array.Empty<int>();

        public int[] MinCounts => _minCounts ?? System.Array.Empty<int>();

        public int[] MaxCounts => _maxCounts ?? System.Array.Empty<int>();

        public string[] GuaranteedItemIds => _guaranteedItemIds ?? System.Array.Empty<string>();

        public int[] GuaranteedItemCounts => _guaranteedItemCounts ?? System.Array.Empty<int>();

        public int DropRolls => _dropRolls;

        public int GoldMin => _goldMin;

        public int GoldMax => _goldMax;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            var items = ItemIds;
            if (items.Length == 0 && GuaranteedItemIds.Length == 0 && _goldMax <= 0)
            {
                report.Warn("LUT_EMPTY", "掉落表既无物品也无金钱，可能还没填。", Id, fieldName: "itemIds");
                return;
            }

            for (var i = 0; i < items.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Item, items[i])
                    && !IdRules.IsValidId(DefinitionKind.Equipment, items[i])
                    && !IdRules.IsValidId(DefinitionKind.Sutra, items[i]))
                {
                    report.Error("LUT_ITEM_ID_PATTERN", $"掉落物 ID '{items[i]}' 不符合道具/装备/经文的命名规则。", Id, fieldName: "itemIds");
                }
            }

            for (var i = 0; i < GuaranteedItemIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Item, GuaranteedItemIds[i])
                    && !IdRules.IsValidId(DefinitionKind.Equipment, GuaranteedItemIds[i])
                    && !IdRules.IsValidId(DefinitionKind.Sutra, GuaranteedItemIds[i]))
                {
                    report.Error("LUT_GUARANTEED_ID_PATTERN", $"必掉物 ID '{GuaranteedItemIds[i]}' 命名不合法。", Id, fieldName: "guaranteedItemIds");
                }
            }

            RequireLength(report, Id, "weights", Weights.Length, items.Length);
            RequireLength(report, Id, "minCounts", MinCounts.Length, items.Length);
            RequireLength(report, Id, "maxCounts", MaxCounts.Length, items.Length);
            RequireLength(report, Id, "guaranteedItemCounts", GuaranteedItemCounts.Length, GuaranteedItemIds.Length);

            var totalWeight = 0;
            for (var i = 0; i < Weights.Length; i++)
            {
                if (Weights[i] <= 0)
                {
                    report.Error("LUT_WEIGHT_INVALID", $"weights 第 {i + 1} 项必须为正，当前 {Weights[i]}。", Id, fieldName: "weights");
                }

                totalWeight += Weights[i];
            }

            for (var i = 0; i < MinCounts.Length && i < MaxCounts.Length; i++)
            {
                if (MinCounts[i] < 1)
                {
                    report.Error("LUT_MIN_INVALID", $"minCounts 第 {i + 1} 项必须 >= 1，当前 {MinCounts[i]}。", Id, fieldName: "minCounts");
                }

                if (MaxCounts[i] < MinCounts[i])
                {
                    report.Error("LUT_RANGE_INVALID", $"maxCounts 第 {i + 1} 项（{MaxCounts[i]}）小于 minCounts（{MinCounts[i]}）。", Id, fieldName: "maxCounts");
                }
            }

            if (items.Length > 0 && _dropRolls <= 0)
            {
                report.Warn("LUT_ROLLS_ZERO", "配置了权重掉落但 dropRolls 为 0，这些条目永远不会掉出。", Id, fieldName: "dropRolls");
            }

            if (_dropRolls > 0 && totalWeight <= 0)
            {
                report.Error("LUT_NO_WEIGHT", "dropRolls 大于 0 但没有任何有效权重。", Id, fieldName: "dropRolls");
            }

            if (_goldMax < _goldMin)
            {
                report.Error("LUT_GOLD_RANGE", $"goldMax（{_goldMax}）小于 goldMin（{_goldMin}）。", Id, fieldName: "goldMax");
            }

            if (_goldMin < 0)
            {
                report.Error("LUT_GOLD_NEGATIVE", $"goldMin 不能为负，当前 {_goldMin}。", Id, fieldName: "goldMin");
            }
        }

        private static void RequireLength(ValidationReport report, string id, string field, int actual, int expected)
        {
            if (expected > 0 && actual != expected)
            {
                report.Error(
                    "LUT_ARRAY_LENGTH",
                    $"列 '{field}' 有 {actual} 项，itemIds 有 {expected} 项，并列数组必须等长。",
                    id,
                    fieldName: field);
            }
        }
    }
}
