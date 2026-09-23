using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>任务。条件与奖励都用并列数组描述，长度不一致在导入期报错。</summary>
    [CreateAssetMenu(fileName = "Quest", menuName = "SamsaraWest/定义/任务 Quest")]
    public sealed class QuestDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;
        [CsvColumn("questTypeKey")] [SerializeField] private string _questTypeKey;

        [CsvColumn("isMainQuest")] [SerializeField] private bool _isMainQuest;
        [CsvColumn("prerequisiteQuestIds")] [SerializeField] private string[] _prerequisiteQuestIds = System.Array.Empty<string>();

        [Tooltip("达成目标所需的状态键 / 运算符 / 期望值，三个数组必须等长。")]
        [CsvColumn("objectiveStateKeys")] [SerializeField] private string[] _objectiveStateKeys = System.Array.Empty<string>();

        [CsvColumn("objectiveOperators")] [SerializeField] private string[] _objectiveOperators = System.Array.Empty<string>();
        [CsvColumn("objectiveValues")] [SerializeField] private int[] _objectiveValues = System.Array.Empty<int>();

        [CsvColumn("rewardItemIds")] [SerializeField] private string[] _rewardItemIds = System.Array.Empty<string>();
        [CsvColumn("rewardItemCounts")] [SerializeField] private int[] _rewardItemCounts = System.Array.Empty<int>();
        [CsvColumn("rewardGold")] [SerializeField] private int _rewardGold;

        [CsvColumn("karmaChannel")] [SerializeField] private string _karmaChannel;
        [CsvColumn("karmaDelta")] [SerializeField] private int _karmaDelta;

        [CsvColumn("giverCharacterId")] [SerializeField] private string _giverCharacterId;
        [CsvColumn("isOptional")] [SerializeField] private bool _isOptional;
        [CsvColumn("autoComplete")] [SerializeField] private bool _autoComplete = true;

        public override DefinitionKind Kind => DefinitionKind.Quest;

        public int ChapterIndex => _chapterIndex;

        public string QuestTypeKey => _questTypeKey;

        public bool IsMainQuest => _isMainQuest;

        public string[] PrerequisiteQuestIds => _prerequisiteQuestIds ?? System.Array.Empty<string>();

        public string[] ObjectiveStateKeys => _objectiveStateKeys ?? System.Array.Empty<string>();

        public string[] ObjectiveOperators => _objectiveOperators ?? System.Array.Empty<string>();

        public int[] ObjectiveValues => _objectiveValues ?? System.Array.Empty<int>();

        public string[] RewardItemIds => _rewardItemIds ?? System.Array.Empty<string>();

        public int[] RewardItemCounts => _rewardItemCounts ?? System.Array.Empty<int>();

        public int RewardGold => _rewardGold;

        public string KarmaChannel => _karmaChannel;

        public int KarmaDelta => _karmaDelta;

        public string GiverCharacterId => _giverCharacterId;

        public bool IsOptional => _isOptional;

        public bool AutoComplete => _autoComplete;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_chapterIndex < 1 || _chapterIndex > 8)
            {
                report.Error("QST_CHAPTER_INVALID", $"章节序号必须在 1–8，当前 {_chapterIndex}。", Id, fieldName: "chapterIndex");
            }

            var keys = ObjectiveStateKeys;
            var operators = ObjectiveOperators;
            var values = ObjectiveValues;

            if (keys.Length == 0)
            {
                report.Error("QST_NO_OBJECTIVE", "任务至少要有一个完成条件。", Id, fieldName: "objectiveStateKeys");
            }

            RequireLength(report, "objectiveOperators", operators.Length, keys.Length);
            RequireLength(report, "objectiveValues", values.Length, keys.Length);
            RequireLength(report, "rewardItemCounts", RewardItemCounts.Length, RewardItemIds.Length);

            for (var i = 0; i < keys.Length; i++)
            {
                if (!IdRules.IsValidStateKey(keys[i]))
                {
                    report.Error("QST_OBJECTIVE_KEY_FORMAT", $"完成条件键 '{keys[i]}' 不符合剧情状态键格式。", Id, fieldName: "objectiveStateKeys");
                }
            }

            for (var i = 0; i < operators.Length; i++)
            {
                if (!System.Enum.TryParse<CompareOperator>(operators[i], ignoreCase: true, out _))
                {
                    report.Error(
                        "QST_OPERATOR_INVALID",
                        $"objectiveOperators 第 {i + 1} 项 '{operators[i]}' 不是合法运算符（{string.Join("/", System.Enum.GetNames(typeof(CompareOperator)))}）。",
                        Id,
                        fieldName: "objectiveOperators");
                }
            }

            for (var i = 0; i < PrerequisiteQuestIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Quest, PrerequisiteQuestIds[i]))
                {
                    report.Error("QST_PREREQ_ID_PATTERN", $"前置任务 ID '{PrerequisiteQuestIds[i]}' 命名不合法。", Id, fieldName: "prerequisiteQuestIds");
                }

                if (PrerequisiteQuestIds[i] == Id)
                {
                    report.Error("QST_SELF_REFERENCE", "任务不能把自己设为前置任务。", Id, fieldName: "prerequisiteQuestIds");
                }
            }

            for (var i = 0; i < RewardItemIds.Length; i++)
            {
                var reward = RewardItemIds[i];
                if (!IdRules.IsValidId(DefinitionKind.Item, reward)
                    && !IdRules.IsValidId(DefinitionKind.Equipment, reward)
                    && !IdRules.IsValidId(DefinitionKind.Sutra, reward))
                {
                    report.Error("QST_REWARD_ID_PATTERN", $"奖励 ID '{reward}' 命名不合法。", Id, fieldName: "rewardItemIds");
                }
            }

            for (var i = 0; i < RewardItemCounts.Length; i++)
            {
                if (RewardItemCounts[i] < 1)
                {
                    report.Error("QST_REWARD_COUNT", $"rewardItemCounts 第 {i + 1} 项必须 >= 1，当前 {RewardItemCounts[i]}。", Id, fieldName: "rewardItemCounts");
                }
            }

            if (_rewardGold < 0)
            {
                report.Error("QST_GOLD_INVALID", $"金钱奖励不能为负，当前 {_rewardGold}。", Id, fieldName: "rewardGold");
            }

            if (_isMainQuest && _isOptional)
            {
                report.Error("QST_FLAG_CONFLICT", "主线任务不能同时是可选任务。", Id, fieldName: "isOptional");
            }

            if (!string.IsNullOrWhiteSpace(_karmaChannel) && !_karmaChannel.StartsWith("karma.", System.StringComparison.Ordinal))
            {
                report.Error("QST_KARMA_FORMAT", $"心念频道 '{_karmaChannel}' 必须以 karma. 开头。", Id, fieldName: "karmaChannel");
            }

            if (_karmaDelta != 0 && string.IsNullOrWhiteSpace(_karmaChannel))
            {
                report.Error("QST_KARMA_NO_CHANNEL", "设置了心念增减但没有指定心念频道。", Id, fieldName: "karmaChannel");
            }

            if (!string.IsNullOrWhiteSpace(_giverCharacterId) && !IdRules.IsValidId(DefinitionKind.Character, _giverCharacterId))
            {
                report.Error("QST_GIVER_ID_PATTERN", $"发布者 ID '{_giverCharacterId}' 不符合角色命名规则。", Id, fieldName: "giverCharacterId");
            }
        }

        private void RequireLength(ValidationReport report, string field, int actual, int expected)
        {
            if (expected > 0 && actual != expected)
            {
                report.Error("QST_ARRAY_LENGTH", $"列 '{field}' 有 {actual} 项，应与 {expected} 项等长。", Id, fieldName: field);
            }
        }
    }
}
