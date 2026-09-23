using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>状态效果。持续时间以「回合」为单位。</summary>
    [CreateAssetMenu(fileName = "Status", menuName = "SamsaraWest/定义/状态 Status")]
    public sealed class StatusDefinition : DefinitionBase
    {
        [CsvColumn("durationTurns", required: true)] [SerializeField] private int _durationTurns = 3;
        [CsvColumn("maxStacks")] [SerializeField] private int _maxStacks = 1;
        [CsvColumn("stackRule")] [SerializeField] private StackRule _stackRule = StackRule.Refresh;
        [CsvColumn("healthDeltaPerTurn")] [SerializeField] private int _healthDeltaPerTurn;
        [CsvColumn("attackModifier")] [SerializeField] private float _attackModifier;
        [CsvColumn("defenseModifier")] [SerializeField] private float _defenseModifier;
        [CsvColumn("speedModifier")] [SerializeField] private float _speedModifier;
        [CsvColumn("incomingDamageModifier")] [SerializeField] private float _incomingDamageModifier;
        [CsvColumn("breakDamageModifier")] [SerializeField] private float _breakDamageModifier;
        [CsvColumn("preventsAction")] [SerializeField] private bool _preventsAction;
        [CsvColumn("isDebuff")] [SerializeField] private bool _isDebuff;
        [CsvColumn("vfxKey")] [SerializeField] private string _vfxKey;

        public override DefinitionKind Kind => DefinitionKind.Status;

        public int DurationTurns => _durationTurns;

        public int MaxStacks => _maxStacks;

        public StackRule StackRule => _stackRule;

        public int HealthDeltaPerTurn => _healthDeltaPerTurn;

        public float AttackModifier => _attackModifier;

        public float DefenseModifier => _defenseModifier;

        public float SpeedModifier => _speedModifier;

        public float IncomingDamageModifier => _incomingDamageModifier;

        public float BreakDamageModifier => _breakDamageModifier;

        public bool PreventsAction => _preventsAction;

        public bool IsDebuff => _isDebuff;

        public string VfxKey => _vfxKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_durationTurns < 1)
            {
                report.Error("STS_DURATION_INVALID", $"持续时间至少 1 回合，当前 {_durationTurns}。", Id, fieldName: "durationTurns");
            }

            if (_maxStacks < 1)
            {
                report.Error("STS_STACKS_INVALID", $"最大层数至少为 1，当前 {_maxStacks}。", Id, fieldName: "maxStacks");
            }

            if (_stackRule != StackRule.Stackable && _maxStacks > 1)
            {
                report.Warn("STS_STACK_UNUSED", $"叠层规则为 {_stackRule}，maxStacks={_maxStacks} 不会生效。", Id, fieldName: "maxStacks");
            }
        }
    }
}
