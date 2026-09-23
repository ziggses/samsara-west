using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>敌人。含 12 种常规敌人的种族、护体值与意图 AI 档位。</summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "SamsaraWest/定义/敌人 Enemy")]
    public sealed class EnemyDefinition : DefinitionBase
    {
        [CsvColumn("maxHealth", required: true)] [SerializeField] private int _maxHealth = 50;
        [CsvColumn("attack")] [SerializeField] private int _attack = 8;
        [CsvColumn("defense")] [SerializeField] private int _defense = 3;
        [CsvColumn("speed")] [SerializeField] private int _speed = 8;
        [CsvColumn("element")] [SerializeField] private FiveElement _element = FiveElement.None;
        [CsvColumn("breakThreshold")] [SerializeField] private int _breakThreshold = 20;
        [CsvColumn("weakToElement")] [SerializeField] private FiveElement _weakToElement = FiveElement.None;
        [CsvColumn("skillIds")] [SerializeField] private string[] _skillIds = System.Array.Empty<string>();
        [CsvColumn("aiProfileKey")] [SerializeField] private string _aiProfileKey;
        [CsvColumn("lootTableId")] [SerializeField] private string _lootTableId;
        [CsvColumn("goldReward")] [SerializeField] private int _goldReward;
        [CsvColumn("experienceReward")] [SerializeField] private int _experienceReward;
        [CsvColumn("isBoss")] [SerializeField] private bool _isBoss;

        public override DefinitionKind Kind => DefinitionKind.Enemy;

        public int MaxHealth => _maxHealth;

        public int Attack => _attack;

        public int Defense => _defense;

        public int Speed => _speed;

        public FiveElement Element => _element;

        public int BreakThreshold => _breakThreshold;

        public FiveElement WeakToElement => _weakToElement;

        public string[] SkillIds => _skillIds ?? System.Array.Empty<string>();

        public string AiProfileKey => _aiProfileKey;

        public string LootTableId => _lootTableId;

        public int GoldReward => _goldReward;

        public int ExperienceReward => _experienceReward;

        public bool IsBoss => _isBoss;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_maxHealth <= 0)
            {
                report.Error("ENM_HEALTH_INVALID", $"最大生命必须为正，当前 {_maxHealth}。", Id, fieldName: "maxHealth");
            }

            if (_breakThreshold <= 0)
            {
                report.Error("ENM_BREAK_INVALID", $"护体值上限必须为正，当前 {_breakThreshold}。", Id, fieldName: "breakThreshold");
            }

            if (SkillIds.Length == 0)
            {
                report.Warn("ENM_NO_SKILL", "敌人没有任何技能，战斗中将无法行动（除非设计中就是纯目标靶）。", Id, fieldName: "skillIds");
            }

            for (var i = 0; i < SkillIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Skill, SkillIds[i]))
                {
                    report.Error("ENM_SKILL_ID_PATTERN", $"技能 ID '{SkillIds[i]}' 不符合技能命名规则。", Id, fieldName: "skillIds");
                }
            }

            if (!string.IsNullOrWhiteSpace(_lootTableId) && !IdRules.IsValidId(DefinitionKind.LootTable, _lootTableId))
            {
                report.Error("ENM_LOOT_ID_PATTERN", $"掉落表 ID '{_lootTableId}' 不符合命名规则。", Id, fieldName: "lootTableId");
            }
        }
    }
}
