using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>可操作角色。四人队伍的成员之一。</summary>
    [CreateAssetMenu(fileName = "Character", menuName = "SamsaraWest/定义/角色 Character")]
    public sealed class CharacterDefinition : DefinitionBase
    {
        [CsvColumn("maxHealth", required: true)] [SerializeField] private int _maxHealth = 100;
        [CsvColumn("maxSpirit")] [SerializeField] private int _maxSpirit = 50;
        [CsvColumn("attack", required: true)] [SerializeField] private int _attack = 10;
        [CsvColumn("defense")] [SerializeField] private int _defense = 5;
        [CsvColumn("speed")] [SerializeField] private int _speed = 10;
        [CsvColumn("element")] [SerializeField] private FiveElement _element = FiveElement.None;
        [CsvColumn("breakThreshold")] [SerializeField] private int _breakThreshold = 30;
        [CsvColumn("startingSkillIds")] [SerializeField] private string[] _startingSkillIds = System.Array.Empty<string>();
        [CsvColumn("portraitKey")] [SerializeField] private string _portraitKey;
        [CsvColumn("battleSpriteKey")] [SerializeField] private string _battleSpriteKey;
        [CsvColumn("isPlayable")] [SerializeField] private bool _isPlayable = true;

        public override DefinitionKind Kind => DefinitionKind.Character;

        public int MaxHealth => _maxHealth;

        public int MaxSpirit => _maxSpirit;

        public int Attack => _attack;

        public int Defense => _defense;

        public int Speed => _speed;

        public FiveElement Element => _element;

        /// <summary>护体值上限，达到后进入破防状态。</summary>
        public int BreakThreshold => _breakThreshold;

        public string[] StartingSkillIds => _startingSkillIds ?? System.Array.Empty<string>();

        public string PortraitKey => _portraitKey;

        public string BattleSpriteKey => _battleSpriteKey;

        public bool IsPlayable => _isPlayable;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_maxHealth <= 0)
            {
                report.Error("CHR_HEALTH_INVALID", $"最大生命必须为正，当前 {_maxHealth}。", Id, fieldName: "maxHealth");
            }

            if (_speed <= 0)
            {
                report.Error("CHR_SPEED_INVALID", $"速度必须为正，当前 {_speed}。", Id, fieldName: "speed");
            }

            if (_breakThreshold <= 0)
            {
                report.Error("CHR_BREAK_INVALID", $"护体值上限必须为正，当前 {_breakThreshold}。", Id, fieldName: "breakThreshold");
            }

            for (var i = 0; i < StartingSkillIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Skill, StartingSkillIds[i]))
                {
                    report.Error("CHR_SKILL_ID_PATTERN", $"初始技能 ID '{StartingSkillIds[i]}' 不符合技能命名规则。", Id, fieldName: "startingSkillIds");
                }
            }
        }
    }
}
