using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 结局条件。优先级数值越大越先判定；全部条件满足才命中该结局。
    /// 心念下限对应剧本中的 <c>karma.compassion / karma.truth / karma.freedom</c>。
    /// </summary>
    [CreateAssetMenu(fileName = "EndingCondition", menuName = "SamsaraWest/定义/结局结局条件 EndingCondition")]
    public sealed class EndingConditionDefinition : DefinitionBase
    {
        [Tooltip("判定优先级，数值越大越先判定。用于让真结局优先于普通结局。")]
        [CsvColumn("priority", required: true)] [SerializeField] private int _priority;

        [CsvColumn("requiredStateKeys")] [SerializeField] private string[] _requiredStateKeys = System.Array.Empty<string>();
        [CsvColumn("requiredOperators")] [SerializeField] private string[] _requiredOperators = System.Array.Empty<string>();
        [CsvColumn("requiredValues")] [SerializeField] private int[] _requiredValues = System.Array.Empty<int>();

        [CsvColumn("minCompassion")] [SerializeField] private int _minCompassion;
        [CsvColumn("minTruth")] [SerializeField] private int _minTruth;
        [CsvColumn("minFreedom")] [SerializeField] private int _minFreedom;

        [CsvColumn("endingTitleKey")] [SerializeField] private string _endingTitleKey;
        [CsvColumn("endingSceneName")] [SerializeField] private string _endingSceneName;
        [CsvColumn("epilogueKey")] [SerializeField] private string _epilogueKey;

        [Tooltip("是否为真结局。真结局通常还要求 ending.blank_page_available。")]
        [CsvColumn("isTrueEnding")] [SerializeField] private bool _isTrueEnding;

        [CsvColumn("unlockedGalleryKey")] [SerializeField] private string _unlockedGalleryKey;

        public override DefinitionKind Kind => DefinitionKind.EndingCondition;

        public int Priority => _priority;

        public string[] RequiredStateKeys => _requiredStateKeys ?? System.Array.Empty<string>();

        public string[] RequiredOperators => _requiredOperators ?? System.Array.Empty<string>();

        public int[] RequiredValues => _requiredValues ?? System.Array.Empty<int>();

        public int MinCompassion => _minCompassion;

        public int MinTruth => _minTruth;

        public int MinFreedom => _minFreedom;

        public string EndingTitleKey => _endingTitleKey;

        public string EndingSceneName => _endingSceneName;

        public string EpilogueKey => _epilogueKey;

        public bool IsTrueEnding => _isTrueEnding;

        public string UnlockedGalleryKey => _unlockedGalleryKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            var keys = RequiredStateKeys;
            var operators = RequiredOperators;
            var values = RequiredValues;

            RequireLength(report, "requiredOperators", operators.Length, keys.Length);
            RequireLength(report, "requiredValues", values.Length, keys.Length);

            if (keys.Length == 0 && _minCompassion <= 0 && _minTruth <= 0 && _minFreedom <= 0)
            {
                if (_priority <= 0)
                {
                    // priority 0 是约定的「兜底结局」槽位：它排在所有结局之后，
                    // 只有别的结局都不命中时才轮到它，所以允许它不带条件。
                    report.Warn(
                        "END_FALLBACK_UNCONDITIONAL",
                        "无条件结局。按约定它必须是优先级最低、且全局唯一的那一个兜底结局，请确认没有第二个。",
                        Id,
                        fieldName: "requiredStateKeys");
                }
                else
                {
                    report.Error(
                        "END_NO_CONDITION",
                        "结局没有任何条件（既无条件键也无心念下限），会与所有结局无条件冲突。",
                        Id,
                        fieldName: "requiredStateKeys");
                }
            }

            for (var i = 0; i < keys.Length; i++)
            {
                if (!IdRules.IsValidStateKey(keys[i]))
                {
                    report.Error("END_STATE_KEY_FORMAT", $"条件键 '{keys[i]}' 不符合剧情状态键格式。", Id, fieldName: "requiredStateKeys");
                }
            }

            for (var i = 0; i < operators.Length; i++)
            {
                if (!System.Enum.TryParse<CompareOperator>(operators[i], ignoreCase: true, out _))
                {
                    report.Error(
                        "END_OPERATOR_INVALID",
                        $"requiredOperators 第 {i + 1} 项 '{operators[i]}' 不是合法运算符（{string.Join("/", System.Enum.GetNames(typeof(CompareOperator)))}）。",
                        Id,
                        fieldName: "requiredOperators");
                }
            }

            if (_minCompassion < 0 || _minTruth < 0 || _minFreedom < 0)
            {
                report.Error("END_KARMA_NEGATIVE", "心念下限不能为负。", Id, fieldName: "minCompassion");
            }

            if (string.IsNullOrWhiteSpace(_endingTitleKey))
            {
                report.Error("END_TITLE_EMPTY", "结局标题键不能为空。", Id, fieldName: "endingTitleKey");
            }
            else if (IdRules.ContainsChinese(_endingTitleKey))
            {
                report.Error("END_TITLE_CHINESE", $"结局标题键 '{_endingTitleKey}' 是中文原文，必须改为本地化键。", Id, fieldName: "endingTitleKey");
            }

            if (string.IsNullOrWhiteSpace(_endingSceneName))
            {
                report.Error("END_SCENE_EMPTY", "结局场景名不能为空。", Id, fieldName: "endingSceneName");
            }

            if (_isTrueEnding)
            {
                var hasBlankPage = false;
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i] == "ending.blank_page_available")
                    {
                        hasBlankPage = true;
                        break;
                    }
                }

                if (!hasBlankPage)
                {
                    report.Warn(
                        "END_TRUE_NO_FLAG",
                        "标记为真结局但没有要求 ending.blank_page_available，请确认这符合剧本设定。",
                        Id,
                        fieldName: "requiredStateKeys");
                }
            }
        }

        private void RequireLength(ValidationReport report, string field, int actual, int expected)
        {
            if (expected > 0 && actual != expected)
            {
                report.Error("END_ARRAY_LENGTH", $"列 '{field}' 有 {actual} 项，应与 {expected} 项等长。", Id, fieldName: field);
            }
        }
    }
}
