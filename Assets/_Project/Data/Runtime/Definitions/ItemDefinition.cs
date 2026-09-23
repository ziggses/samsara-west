using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>消耗品与材料类道具。装备、经文另有专用定义类。</summary>
    [CreateAssetMenu(fileName = "Item", menuName = "SamsaraWest/定义/道具 Item")]
    public sealed class ItemDefinition : DefinitionBase
    {
        [CsvColumn("category", required: true)] [SerializeField] private ItemCategory _category = ItemCategory.Consumable;
        [CsvColumn("tier")] [SerializeField] private RarityTier _tier = RarityTier.Common;

        [Tooltip("单格堆叠上限。1 表示不可堆叠。")]
        [CsvColumn("stackLimit")] [SerializeField] private int _stackLimit = 99;

        [CsvColumn("price")] [SerializeField] private int _price;
        [CsvColumn("usableInBattle")] [SerializeField] private bool _usableInBattle = true;
        [CsvColumn("usableInField")] [SerializeField] private bool _usableInField = true;
        [CsvColumn("isConsumedOnUse")] [SerializeField] private bool _isConsumedOnUse = true;

        [Tooltip("效果类型键，例如 heal.health、buff.attack、cure.status。")]
        [CsvColumn("effectKey")] [SerializeField] private string _effectKey;

        [CsvColumn("effectMagnitude")] [SerializeField] private int _effectMagnitude;
        [CsvColumn("effectDurationTurns")] [SerializeField] private int _effectDurationTurns;

        [Tooltip("作用的技能 ID，用于「立刻施展某技能」这类道具。")]
        [CsvColumn("effectSkillId")] [SerializeField] private string _effectSkillId;

        [CsvColumn("spriteKey")] [SerializeField] private string _spriteKey;

        public override DefinitionKind Kind => DefinitionKind.Item;

        public ItemCategory Category => _category;

        public RarityTier Tier => _tier;

        public int StackLimit => _stackLimit;

        public int Price => _price;

        public bool UsableInBattle => _usableInBattle;

        public bool UsableInField => _usableInField;

        public bool IsConsumedOnUse => _isConsumedOnUse;

        public string EffectKey => _effectKey;

        public int EffectMagnitude => _effectMagnitude;

        public int EffectDurationTurns => _effectDurationTurns;

        public string EffectSkillId => _effectSkillId;

        public string SpriteKey => _spriteKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_category == ItemCategory.None)
            {
                report.Error("ITM_CATEGORY_INVALID", "道具必须指定分类。", Id, fieldName: "category");
            }

            if (_stackLimit < 1)
            {
                report.Error("ITM_STACK_INVALID", $"堆叠上限至少为 1，当前 {_stackLimit}。", Id, fieldName: "stackLimit");
            }

            if (_price < 0)
            {
                report.Error("ITM_PRICE_INVALID", $"价格不能为负，当前 {_price}。", Id, fieldName: "price");
            }

            var usable = _usableInBattle || _usableInField;
            if (usable && string.IsNullOrWhiteSpace(_effectKey) && string.IsNullOrWhiteSpace(_effectSkillId))
            {
                report.Error(
                    "ITM_NO_EFFECT",
                    "道具标记为可用，但没有 effectKey 也没有 effectSkillId，使用后不会有任何效果。",
                    Id,
                    fieldName: "effectKey");
            }

            if (!string.IsNullOrWhiteSpace(_effectKey) && !IdRules.IsValidLocalizationKey(_effectKey))
            {
                report.Error("ITM_EFFECT_KEY_FORMAT", $"效果键 '{_effectKey}' 不是合法键名（允许点号分段）。", Id, fieldName: "effectKey");
            }

            if (!string.IsNullOrWhiteSpace(_effectSkillId) && !IdRules.IsValidId(DefinitionKind.Skill, _effectSkillId))
            {
                report.Error("ITM_SKILL_ID_PATTERN", $"技能 ID '{_effectSkillId}' 不符合技能命名规则。", Id, fieldName: "effectSkillId");
            }
        }
    }
}
