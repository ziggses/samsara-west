using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>Boss 阶段。按生命阈值切换，可替换技能组并附加进入时状态。</summary>
    [CreateAssetMenu(fileName = "BossPhase", menuName = "SamsaraWest/定义/Boss 阶段 BossPhase")]
    public sealed class BossPhaseDefinition : DefinitionBase
    {
        [CsvColumn("phaseIndex", required: true)] [SerializeField] private int _phaseIndex = 1;
        [CsvColumn("encounterId", required: true)] [SerializeField] private string _encounterId;

        [Tooltip("生命百分比阈值，进入该阶段的触发线，取值 (0,1]。")]
        [CsvColumn("healthThreshold", required: true)] [SerializeField] private float _healthThreshold = 1f;

        [CsvColumn("skillIds")] [SerializeField] private string[] _skillIds = System.Array.Empty<string>();
        [CsvColumn("onEnterStatusIds")] [SerializeField] private string[] _onEnterStatusIds = System.Array.Empty<string>();
        [CsvColumn("attackMultiplier")] [SerializeField] private float _attackMultiplier = 1f;
        [CsvColumn("defenseMultiplier")] [SerializeField] private float _defenseMultiplier = 1f;
        [CsvColumn("speedMultiplier")] [SerializeField] private float _speedMultiplier = 1f;
        [CsvColumn("tauntKey")] [SerializeField] private string _tauntKey;
        [CsvColumn("bgmSwitchKey")] [SerializeField] private string _bgmSwitchKey;
        [CsvColumn("cameraCueKey")] [SerializeField] private string _cameraCueKey;

        public override DefinitionKind Kind => DefinitionKind.BossPhase;

        public int PhaseIndex => _phaseIndex;

        public string EncounterId => _encounterId;

        public float HealthThreshold => _healthThreshold;

        public string[] SkillIds => _skillIds ?? System.Array.Empty<string>();

        public string[] OnEnterStatusIds => _onEnterStatusIds ?? System.Array.Empty<string>();

        public float AttackMultiplier => _attackMultiplier;

        public float DefenseMultiplier => _defenseMultiplier;

        public float SpeedMultiplier => _speedMultiplier;

        public string TauntKey => _tauntKey;

        public string BgmSwitchKey => _bgmSwitchKey;

        public string CameraCueKey => _cameraCueKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_phaseIndex < 1)
            {
                report.Error("BSP_INDEX_INVALID", $"阶段序号至少为 1，当前 {_phaseIndex}。", Id, fieldName: "phaseIndex");
            }

            if (_healthThreshold <= 0f || _healthThreshold > 1f)
            {
                report.Error("BSP_THRESHOLD_INVALID", $"生命阈值必须落在 (0,1]，当前 {_healthThreshold}。", Id, fieldName: "healthThreshold");
            }

            if (!IdRules.IsValidId(DefinitionKind.Encounter, _encounterId))
            {
                report.Error("BSP_ENCOUNTER_ID_PATTERN", $"遭遇 ID '{_encounterId}' 不符合命名规则。", Id, fieldName: "encounterId");
            }

            if (_attackMultiplier <= 0f || _defenseMultiplier <= 0f || _speedMultiplier <= 0f)
            {
                report.Error("BSP_MULTIPLIER_INVALID", "倍率必须为正数。", Id, fieldName: "attackMultiplier");
            }

            // 阶段 ID 末位应与 phaseIndex 一致，便于在日志与截图中定位。
            var parts = Id.Split('_');
            if (parts.Length > 0 && parts[parts.Length - 1].Length == 2 && parts[parts.Length - 1][0] == 'P')
            {
                if (int.TryParse(parts[parts.Length - 1].Substring(1), out var suffix) && suffix != _phaseIndex)
                {
                    report.Warn("BSP_INDEX_SUFFIX_MISMATCH", $"ID 末位 P{suffix} 与 phaseIndex={_phaseIndex} 不一致。", Id, fieldName: "phaseIndex");
                }
            }
        }
    }
}
