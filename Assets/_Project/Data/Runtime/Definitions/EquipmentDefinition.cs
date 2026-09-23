using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 装备。已交付的 90 件武器经 tier 归并进武器栏，图标来自外部素材的 spritesheet.json。
    /// </summary>
    [CreateAssetMenu(fileName = "Equipment", menuName = "SamsaraWest/定义/装备 Equipment")]
    public sealed class EquipmentDefinition : DefinitionBase
    {
        [CsvColumn("slot", required: true)] [SerializeField] private EquipmentSlot _slot = EquipmentSlot.Weapon;
        [CsvColumn("tier", required: true)] [SerializeField] private RarityTier _tier = RarityTier.Common;

        [CsvColumn("attackBonus")] [SerializeField] private int _attackBonus;
        [CsvColumn("defenseBonus")] [SerializeField] private int _defenseBonus;
        [CsvColumn("speedBonus")] [SerializeField] private int _speedBonus;
        [CsvColumn("healthBonus")] [SerializeField] private int _healthBonus;
        [CsvColumn("spiritBonus")] [SerializeField] private int _spiritBonus;

        [CsvColumn("element")] [SerializeField] private FiveElement _element = FiveElement.None;
        [CsvColumn("resistElement")] [SerializeField] private FiveElement _resistElement = FiveElement.None;
        [CsvColumn("breakDamageBonus")] [SerializeField] private int _breakDamageBonus;

        [Tooltip("装备后被动生效的技能 ID，可为空。")]
        [CsvColumn("passiveSkillId")] [SerializeField] private string _passiveSkillId;

        [CsvColumn("requiredLevel")] [SerializeField] private int _requiredLevel = 1;
        [CsvColumn("price")] [SerializeField] private int _price;
        [CsvColumn("forgeRecipeId")] [SerializeField] private string _forgeRecipeId;
        [CsvColumn("spriteKey")] [SerializeField] private string _spriteKey;

        [CsvColumn("allowedCharacterIds")] [SerializeField] private string[] _allowedCharacterIds = System.Array.Empty<string>();

        public override DefinitionKind Kind => DefinitionKind.Equipment;

        public EquipmentSlot Slot => _slot;

        public RarityTier Tier => _tier;

        public int AttackBonus => _attackBonus;

        public int DefenseBonus => _defenseBonus;

        public int SpeedBonus => _speedBonus;

        public int HealthBonus => _healthBonus;

        public int SpiritBonus => _spiritBonus;

        public FiveElement Element => _element;

        public FiveElement ResistElement => _resistElement;

        public int BreakDamageBonus => _breakDamageBonus;

        public string PassiveSkillId => _passiveSkillId;

        public int RequiredLevel => _requiredLevel;

        public int Price => _price;

        public string ForgeRecipeId => _forgeRecipeId;

        public string SpriteKey => _spriteKey;

        /// <summary>限定装备者。为空表示全队可装备。</summary>
        public string[] AllowedCharacterIds => _allowedCharacterIds ?? System.Array.Empty<string>();

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_slot == EquipmentSlot.None)
            {
                report.Error("EQP_SLOT_INVALID", "装备必须指定栏位。", Id, fieldName: "slot");
            }

            if (_requiredLevel < 1)
            {
                report.Error("EQP_LEVEL_INVALID", $"需求等级至少为 1，当前 {_requiredLevel}。", Id, fieldName: "requiredLevel");
            }

            if (_price < 0)
            {
                report.Error("EQP_PRICE_INVALID", $"价格不能为负，当前 {_price}。", Id, fieldName: "price");
            }

            if (_attackBonus == 0 && _defenseBonus == 0 && _speedBonus == 0
                && _healthBonus == 0 && _spiritBonus == 0 && string.IsNullOrWhiteSpace(_passiveSkillId))
            {
                report.Warn("EQP_NO_EFFECT", "装备没有任何数值加成也没有被动技能，可能只是占位数据。", Id, fieldName: "attackBonus");
            }

            if (!string.IsNullOrWhiteSpace(_passiveSkillId) && !IdRules.IsValidId(DefinitionKind.Skill, _passiveSkillId))
            {
                report.Error("EQP_SKILL_ID_PATTERN", $"被动技能 ID '{_passiveSkillId}' 不符合技能命名规则。", Id, fieldName: "passiveSkillId");
            }

            if (!string.IsNullOrWhiteSpace(_forgeRecipeId) && !IdRules.IsValidId(DefinitionKind.Recipe, _forgeRecipeId))
            {
                report.Error("EQP_RECIPE_ID_PATTERN", $"配方 ID '{_forgeRecipeId}' 不符合配方命名规则。", Id, fieldName: "forgeRecipeId");
            }

            for (var i = 0; i < AllowedCharacterIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Character, AllowedCharacterIds[i]))
                {
                    report.Error("EQP_CHR_ID_PATTERN", $"限定角色 ID '{AllowedCharacterIds[i]}' 不符合角色命名规则。", Id, fieldName: "allowedCharacterIds");
                }
            }

            // 素材的 spritesheet.json 用 tier 数值对齐图标档位，此处保持同一口径。
            if (string.IsNullOrWhiteSpace(_spriteKey))
            {
                report.Warn("EQP_SPRITE_KEY_MISSING", "未指定 spritesheet 图标键，背包中将显示占位图。", Id, fieldName: "spriteKey");
            }
        }
    }
}
