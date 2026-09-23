using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>地图上的可交互物件（机关、宝箱、NPC 触发点、传送点）。</summary>
    [CreateAssetMenu(fileName = "Interactable", menuName = "SamsaraWest/定义/交互物 Interactable")]
    public sealed class InteractableDefinition : DefinitionBase
    {
        [CsvColumn("mapId", required: true)] [SerializeField] private string _mapId;
        [CsvColumn("gridX", required: true)] [SerializeField] private int _gridX;
        [CsvColumn("gridY", required: true)] [SerializeField] private int _gridY;

        [Tooltip("交互类型键，例如 interact.chest、interact.door、interact.talk。")]
        [CsvColumn("interactionTypeKey", required: true)] [SerializeField] private string _interactionTypeKey;

        [Tooltip("目标 ID：对话节点、任务 ID 或目标地图 ID，取决于交互类型。")]
        [CsvColumn("targetId")] [SerializeField] private string _targetId;

        [CsvColumn("requiredStateKey")] [SerializeField] private string _requiredStateKey;
        [CsvColumn("requiredOperator")] [SerializeField] private CompareOperator _requiredOperator = CompareOperator.Equal;
        [CsvColumn("requiredValue")] [SerializeField] private int _requiredValue;

        [Tooltip("只能触发一次（宝箱、一次性机关）。")]
        [CsvColumn("oneShot")] [SerializeField] private bool _oneShot;

        [CsvColumn("isHiddenUntilConditionMet")] [SerializeField] private bool _isHiddenUntilConditionMet;
        [CsvColumn("promptKey")] [SerializeField] private string _promptKey;
        [CsvColumn("interactSpriteKey")] [SerializeField] private string _interactSpriteKey;

        public override DefinitionKind Kind => DefinitionKind.Interactable;

        public string MapId => _mapId;

        public int GridX => _gridX;

        public int GridY => _gridY;

        public string InteractionTypeKey => _interactionTypeKey;

        public string TargetId => _targetId;

        public string RequiredStateKey => _requiredStateKey;

        public CompareOperator RequiredOperator => _requiredOperator;

        public int RequiredValue => _requiredValue;

        public bool OneShot => _oneShot;

        public bool IsHiddenUntilConditionMet => _isHiddenUntilConditionMet;

        public string PromptKey => _promptKey;

        public string InteractSpriteKey => _interactSpriteKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (!IdRules.IsValidId(DefinitionKind.Map, _mapId))
            {
                report.Error("INT_MAP_ID_PATTERN", $"地图 ID '{_mapId}' 不符合地图命名规则（CH01_MAP01）。", Id, fieldName: "mapId");
            }

            if (_gridX < 0 || _gridY < 0)
            {
                report.Error("INT_GRID_INVALID", $"网格坐标不能为负，当前 ({_gridX},{_gridY})。", Id, fieldName: "gridX");
            }

            if (string.IsNullOrWhiteSpace(_interactionTypeKey))
            {
                report.Error("INT_TYPE_EMPTY", "交互类型键不能为空。", Id, fieldName: "interactionTypeKey");
            }
            else if (!IdRules.IsValidLocalizationKey(_interactionTypeKey))
            {
                report.Error("INT_TYPE_FORMAT", $"交互类型键 '{_interactionTypeKey}' 不是合法键名（如 interact.chest）。", Id, fieldName: "interactionTypeKey");
            }

            if (string.IsNullOrWhiteSpace(_targetId))
            {
                report.Warn("INT_NO_TARGET", "没有目标 ID，交互后不会产生任何效果，若为纯装饰请忽略此项。", Id, fieldName: "targetId");
            }

            if (!string.IsNullOrWhiteSpace(_requiredStateKey) && !IdRules.IsValidStateKey(_requiredStateKey))
            {
                report.Error(
                    "INT_STATE_KEY_FORMAT",
                    $"条件键 '{_requiredStateKey}' 不符合剧情状态键格式（flag./relation./karma./ending.）。",
                    Id,
                    fieldName: "requiredStateKey");
            }

            if (string.IsNullOrWhiteSpace(_requiredStateKey) && _requiredValue != 0)
            {
                report.Warn("INT_VALUE_WITHOUT_KEY", "设置了 requiredValue 但没有条件键，该数值不会生效。", Id, fieldName: "requiredValue");
            }

            if (_isHiddenUntilConditionMet && string.IsNullOrWhiteSpace(_requiredStateKey))
            {
                report.Error(
                    "INT_HIDDEN_UNREACHABLE",
                    "标记为「条件未满足时隐藏」但没有任何条件键，物件将永远不可见。",
                    Id,
                    fieldName: "isHiddenUntilConditionMet");
            }

            if (string.IsNullOrWhiteSpace(_promptKey))
            {
                report.Warn("INT_PROMPT_MISSING", "未指定交互提示键，界面中将没有提示文字。", Id, fieldName: "promptKey");
            }
            else if (!IdRules.IsValidLocalizationKey(_promptKey) || IdRules.ContainsChinese(_promptKey))
            {
                report.Error("INT_PROMPT_FORMAT", $"提示键 '{_promptKey}' 必须是本地化键，不能是中文原文。", Id, fieldName: "promptKey");
            }
        }
    }
}
