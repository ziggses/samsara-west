using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>经文。占用 <see cref="EquipmentSlot.Sutra"/> 栏位，提供心法与被动加成。</summary>
    [CreateAssetMenu(fileName = "Sutra", menuName = "SamsaraWest/定义/经文 Sutra")]
    public sealed class SutraDefinition : DefinitionBase
    {
        [CsvColumn("tier", required: true)] [SerializeField] private RarityTier _tier = RarityTier.Common;

        [Tooltip("心法类型键，例如 sutra.mind.stillness、sutra.mind.rage。")]
        [CsvColumn("mantraTypeKey")] [SerializeField] private string _mantraTypeKey;

        [CsvColumn("attackBonus")] [SerializeField] private int _attackBonus;
        [CsvColumn("defenseBonus")] [SerializeField] private int _defenseBonus;
        [CsvColumn("healthBonus")] [SerializeField] private int _healthBonus;
        [CsvColumn("spiritBonus")] [SerializeField] private int _spiritBonus;
        [CsvColumn("breakThresholdBonus")] [SerializeField] private int _breakThresholdBonus;
        [CsvColumn("spiritRegenPerTurn")] [SerializeField] private int _spiritRegenPerTurn;

        [CsvColumn("element")] [SerializeField] private FiveElement _element = FiveElement.None;
        [CsvColumn("passiveSkillId")] [SerializeField] private string _passiveSkillId;
        [CsvColumn("requiredLevel")] [SerializeField] private int _requiredLevel = 1;
        [CsvColumn("price")] [SerializeField] private int _price;

        [Tooltip("代价类心法：装备后每回合流失的生命，0 表示无代价。")]
        [CsvColumn("healthCostPerTurn")] [SerializeField] private int _healthCostPerTurn;

        [CsvColumn("spriteKey")] [SerializeField] private string _spriteKey;

        public override DefinitionKind Kind => DefinitionKind.Sutra;

        public RarityTier Tier => _tier;

        public string MantraTypeKey => _mantraTypeKey;

        public int AttackBonus => _attackBonus;

        public int DefenseBonus => _defenseBonus;

        public int HealthBonus => _healthBonus;

        public int SpiritBonus => _spiritBonus;

        public int BreakThresholdBonus => _breakThresholdBonus;

        public int SpiritRegenPerTurn => _spiritRegenPerTurn;

        public FiveElement Element => _element;

        public string PassiveSkillId => _passiveSkillId;

        public int RequiredLevel => _requiredLevel;

        public int Price => _price;

        public int HealthCostPerTurn => _healthCostPerTurn;

        public string SpriteKey => _spriteKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (string.IsNullOrWhiteSpace(_mantraTypeKey))
            {
                report.Error("SUT_MANTRA_EMPTY", "经文必须指定心法类型键。", Id, fieldName: "mantraTypeKey");
            }
            else if (!IdRules.IsValidLocalizationKey(_mantraTypeKey))
            {
                report.Error("SUT_MANTRA_FORMAT", $"心法类型键 '{_mantraTypeKey}' 不是合法键名。", Id, fieldName: "mantraTypeKey");
            }

            if (_requiredLevel < 1)
            {
                report.Error("SUT_LEVEL_INVALID", $"需求等级至少为 1，当前 {_requiredLevel}。", Id, fieldName: "requiredLevel");
            }

            if (_healthCostPerTurn < 0)
            {
                report.Error("SUT_COST_INVALID", $"每回合生命代价不能为负，当前 {_healthCostPerTurn}。", Id, fieldName: "healthCostPerTurn");
            }

            if (!string.IsNullOrWhiteSpace(_passiveSkillId) && !IdRules.IsValidId(DefinitionKind.Skill, _passiveSkillId))
            {
                report.Error("SUT_SKILL_ID_PATTERN", $"被动技能 ID '{_passiveSkillId}' 不符合技能命名规则。", Id, fieldName: "passiveSkillId");
            }

            if (_healthCostPerTurn > 0 && _healthBonus <= 0)
            {
                report.Warn("SUT_COST_NO_BENEFIT", "带有生命代价但没有任何生命加成，确认这是有意的苦修设计。", Id, fieldName: "healthBonus");
            }
        }
    }
}
