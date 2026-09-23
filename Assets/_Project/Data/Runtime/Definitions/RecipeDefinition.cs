using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>锻造 / 兑换配方。材料并列数组必须等长。</summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "SamsaraWest/定义/配方 Recipe")]
    public sealed class RecipeDefinition : DefinitionBase
    {
        [CsvColumn("resultItemId", required: true)] [SerializeField] private string _resultItemId;
        [CsvColumn("resultCount")] [SerializeField] private int _resultCount = 1;

        [CsvColumn("materialIds", required: true)] [SerializeField] private string[] _materialIds = System.Array.Empty<string>();
        [CsvColumn("materialCounts")] [SerializeField] private int[] _materialCounts = System.Array.Empty<int>();

        [CsvColumn("goldCost")] [SerializeField] private int _goldCost;
        [CsvColumn("requiredLevel")] [SerializeField] private int _requiredLevel = 1;

        [Tooltip("配方是否一开始就可见。false 表示需要先解锁（掉落或剧情）。")]
        [CsvColumn("discoveredByDefault")] [SerializeField] private bool _discoveredByDefault = true;

        [CsvColumn("categoryKey")] [SerializeField] private string _categoryKey;
        [CsvColumn("unlockedByStateKey")] [SerializeField] private string _unlockedByStateKey;

        public override DefinitionKind Kind => DefinitionKind.Recipe;

        public string ResultItemId => _resultItemId;

        public int ResultCount => _resultCount;

        public string[] MaterialIds => _materialIds ?? System.Array.Empty<string>();

        public int[] MaterialCounts => _materialCounts ?? System.Array.Empty<int>();

        public int GoldCost => _goldCost;

        public int RequiredLevel => _requiredLevel;

        public bool DiscoveredByDefault => _discoveredByDefault;

        public string CategoryKey => _categoryKey;

        public string UnlockedByStateKey => _unlockedByStateKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (!IdRules.IsValidId(DefinitionKind.Item, _resultItemId)
                && !IdRules.IsValidId(DefinitionKind.Equipment, _resultItemId)
                && !IdRules.IsValidId(DefinitionKind.Sutra, _resultItemId))
            {
                report.Error("RCP_RESULT_ID_PATTERN", $"产物 ID '{_resultItemId}' 命名不合法。", Id, fieldName: "resultItemId");
            }

            if (_resultCount < 1)
            {
                report.Error("RCP_RESULT_COUNT", $"产物数量至少为 1，当前 {_resultCount}。", Id, fieldName: "resultCount");
            }

            var materials = MaterialIds;
            if (materials.Length == 0)
            {
                report.Error("RCP_NO_MATERIAL", "配方至少要有一项材料。", Id, fieldName: "materialIds");
            }

            if (MaterialCounts.Length != materials.Length && materials.Length > 0)
            {
                report.Error(
                    "RCP_ARRAY_LENGTH",
                    $"materialCounts 有 {MaterialCounts.Length} 项，materialIds 有 {materials.Length} 项，必须等长。",
                    Id,
                    fieldName: "materialCounts");
            }

            for (var i = 0; i < materials.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Item, materials[i])
                    && !IdRules.IsValidId(DefinitionKind.Equipment, materials[i]))
                {
                    report.Error("RCP_MATERIAL_ID_PATTERN", $"材料 ID '{materials[i]}' 命名不合法。", Id, fieldName: "materialIds");
                }

                if (materials[i] == _resultItemId)
                {
                    report.Error("RCP_SELF_REFERENCE", "配方不能把自身产物当作材料。", Id, fieldName: "materialIds");
                }
            }

            for (var i = 0; i < MaterialCounts.Length; i++)
            {
                if (MaterialCounts[i] < 1)
                {
                    report.Error("RCP_MATERIAL_COUNT", $"materialCounts 第 {i + 1} 项必须 >= 1，当前 {MaterialCounts[i]}。", Id, fieldName: "materialCounts");
                }
            }

            if (_goldCost < 0)
            {
                report.Error("RCP_GOLD_INVALID", $"金钱消耗不能为负，当前 {_goldCost}。", Id, fieldName: "goldCost");
            }

            if (_requiredLevel < 1)
            {
                report.Error("RCP_LEVEL_INVALID", $"需求等级至少为 1，当前 {_requiredLevel}。", Id, fieldName: "requiredLevel");
            }

            if (!_discoveredByDefault && string.IsNullOrWhiteSpace(_unlockedByStateKey))
            {
                report.Error(
                    "RCP_UNREACHABLE",
                    "配方标记为「非默认可见」但没有 unlockedByStateKey，玩家永远无法解锁它。",
                    Id,
                    fieldName: "unlockedByStateKey");
            }

            if (!string.IsNullOrWhiteSpace(_unlockedByStateKey) && !IdRules.IsValidStateKey(_unlockedByStateKey))
            {
                report.Error(
                    "RCP_STATE_KEY_FORMAT",
                    $"解锁条件键 '{_unlockedByStateKey}' 不符合剧情状态键格式（flag./relation./karma./ending.）。",
                    Id,
                    fieldName: "unlockedByStateKey");
            }
        }
    }
}
