using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>技能。数值全部走数据，战斗逻辑不写死在角色脚本里。</summary>
    [CreateAssetMenu(fileName = "Skill", menuName = "SamsaraWest/定义/技能 Skill")]
    public sealed class SkillDefinition : DefinitionBase
    {
        [CsvColumn("power", required: true)] [SerializeField] private int _power = 10;
        [CsvColumn("element")] [SerializeField] private FiveElement _element = FiveElement.None;
        [CsvColumn("nature")] [SerializeField] private DamageNature _nature = DamageNature.Physical;
        [CsvColumn("target")] [SerializeField] private TargetRule _target = TargetRule.SingleEnemy;

        [Tooltip("主目标是否必须在敌对阵营。只对列／排技能有效：单体与全体规则的阵营由 target 自己决定，"
            + "在那些规则上配它会被校验报成冗余。")]
        [CsvColumn("hostileOnly")] [SerializeField] private bool _hostileOnly;
        [CsvColumn("spiritCost")] [SerializeField] private int _spiritCost;
        [CsvColumn("cooldownTurns")] [SerializeField] private int _cooldownTurns;
        [CsvColumn("hitCount")] [SerializeField] private int _hitCount = 1;

        [Tooltip("命中时对护体值造成的削减量。归零即触发破防。")]
        [CsvColumn("breakDamage")] [SerializeField] private int _breakDamage = 10;

        [CsvColumn("appliedStatusId")] [SerializeField] private string _appliedStatusId;
        [CsvColumn("statusChance")] [SerializeField] private float _statusChance;
        [CsvColumn("healPower")] [SerializeField] private int _healPower;
        [CsvColumn("vfxKey")] [SerializeField] private string _vfxKey;
        [CsvColumn("animationKey")] [SerializeField] private string _animationKey;
        [CsvColumn("sfxKey")] [SerializeField] private string _sfxKey;

        public override DefinitionKind Kind => DefinitionKind.Skill;

        public int Power => _power;

        public FiveElement Element => _element;

        public DamageNature Nature => _nature;

        public TargetRule Target => _target;

        /// <summary>列／排技能的主目标是否必须敌对；其余目标规则的阵营由 <see cref="Target"/> 自己决定。</summary>
        public bool HostileOnly => _hostileOnly;

        public int SpiritCost => _spiritCost;

        public int CooldownTurns => _cooldownTurns;

        public int HitCount => _hitCount;

        public int BreakDamage => _breakDamage;

        public string AppliedStatusId => _appliedStatusId;

        public float StatusChance => _statusChance;

        public int HealPower => _healPower;

        public string VfxKey => _vfxKey;

        public string AnimationKey => _animationKey;

        public string SfxKey => _sfxKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_power < 0)
            {
                report.Error("SKL_POWER_INVALID", $"威力不能为负，当前 {_power}。", Id, fieldName: "power");
            }

            if (_hitCount < 1)
            {
                report.Error("SKL_HIT_INVALID", $"攻击段数至少为 1，当前 {_hitCount}。", Id, fieldName: "hitCount");
            }

            if (_spiritCost < 0)
            {
                report.Error("SKL_COST_INVALID", $"灵力消耗不能为负，当前 {_spiritCost}。", Id, fieldName: "spiritCost");
            }

            if (_statusChance < 0f || _statusChance > 1f)
            {
                report.Error("SKL_CHANCE_INVALID", $"状态命中率必须在 [0,1]，当前 {_statusChance}。", Id, fieldName: "statusChance");
            }

            if (!string.IsNullOrWhiteSpace(_appliedStatusId) && !IdRules.IsValidId(DefinitionKind.Status, _appliedStatusId))
            {
                report.Error("SKL_STATUS_ID_PATTERN", $"附加状态 ID '{_appliedStatusId}' 不符合状态命名规则。", Id, fieldName: "appliedStatusId");
            }

            if (!string.IsNullOrWhiteSpace(_appliedStatusId) && _statusChance <= 0f)
            {
                report.Warn("SKL_STATUS_UNREACHABLE", "配置了附加状态但命中率为 0，该状态永远不会生效。", Id, fieldName: "statusChance");
            }

            var targetsLine = _target == TargetRule.Column || _target == TargetRule.Row;

            if (_hostileOnly && !targetsLine)
            {
                report.Warn(
                    "SKL_HOSTILE_REDUNDANT",
                    $"{_target} 的阵营由 target 自己决定，hostileOnly 只对 Column／Row 生效，配了也不会影响选目标。",
                    Id,
                    fieldName: "hostileOnly");
            }

            // 这一条挡的是「列／排技能的敌我漏配」：不勾 hostileOnly 时主目标会被认成自己人，
            // 于是纯伤害的列技能会对着我方那一列放。宁可报出来让人确认，也不要静默生效。
            if (!_hostileOnly && targetsLine && _power > 0 && _healPower <= 0)
            {
                report.Warn(
                    "SKL_HOSTILE_SUSPICIOUS",
                    "列／排技能只有伤害、没有治疗，却没勾 hostileOnly：主目标会被认成自己人那一列。"
                    + "确认是故意的，否则补上 hostileOnly=true。",
                    Id,
                    fieldName: "hostileOnly");
            }
        }
    }
}
