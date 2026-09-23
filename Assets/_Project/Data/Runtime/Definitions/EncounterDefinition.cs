using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>遭遇编排。ID 沿用剧本约定：普通 ENC_CH01_001、精英 EENC_、Boss BENC_。</summary>
    [CreateAssetMenu(fileName = "Encounter", menuName = "SamsaraWest/定义/遭遇 Encounter")]
    public sealed class EncounterDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;
        [CsvColumn("enemyIds", required: true)] [SerializeField] private string[] _enemyIds = System.Array.Empty<string>();

        [Tooltip("3×2 阵型的占位，例如 1,1;2,1;3,2（列,行）。留空表示自动排布。")]
        [CsvColumn("formation")] [SerializeField] private string _formation;

        [CsvColumn("isElite")] [SerializeField] private bool _isElite;
        [CsvColumn("isBoss")] [SerializeField] private bool _isBoss;
        [CsvColumn("bossPhaseIds")] [SerializeField] private string[] _bossPhaseIds = System.Array.Empty<string>();
        [CsvColumn("backgroundKey")] [SerializeField] private string _backgroundKey;
        [CsvColumn("bgmKey")] [SerializeField] private string _bgmKey;

        [Tooltip("允许逃跑。Boss 战与剧情战通常为 false。")]
        [CsvColumn("allowFlee")] [SerializeField] private bool _allowFlee = true;

        [Tooltip("战斗失败是否直接导致游戏结束（剧情战）或回到最近存档点。")]
        [CsvColumn("defeatGameOver")] [SerializeField] private bool _defeatGameOver;

        public override DefinitionKind Kind => DefinitionKind.Encounter;

        public int ChapterIndex => _chapterIndex;

        public string[] EnemyIds => _enemyIds ?? System.Array.Empty<string>();

        public string Formation => _formation;

        public bool IsElite => _isElite;

        public bool IsBoss => _isBoss;

        public string[] BossPhaseIds => _bossPhaseIds ?? System.Array.Empty<string>();

        public string BackgroundKey => _backgroundKey;

        public string BgmKey => _bgmKey;

        public bool AllowFlee => _allowFlee;

        public bool DefeatGameOver => _defeatGameOver;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_chapterIndex < 1 || _chapterIndex > 8)
            {
                report.Error("ENC_CHAPTER_INVALID", $"章节序号必须在 1–8，当前 {_chapterIndex}。", Id, fieldName: "chapterIndex");
            }

            if (EnemyIds.Length == 0)
            {
                report.Error("ENC_NO_ENEMY", "遭遇至少需要一个敌人。", Id, fieldName: "enemyIds");
            }

            for (var i = 0; i < EnemyIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Enemy, EnemyIds[i]))
                {
                    report.Error("ENC_ENEMY_ID_PATTERN", $"敌人 ID '{EnemyIds[i]}' 不符合敌人命名规则。", Id, fieldName: "enemyIds");
                }
            }

            if (_isBoss && BossPhaseIds.Length == 0)
            {
                report.Warn("ENC_BOSS_NO_PHASE", "标记为 Boss 但没有配置阶段，Boss 将只有单一形态。", Id, fieldName: "bossPhaseIds");
            }

            if (_isBoss && _allowFlee)
            {
                report.Warn("ENC_BOSS_FLEEABLE", "Boss 战允许逃跑，通常不是预期设计。", Id, fieldName: "allowFlee");
            }

            // ID 前缀与标志位必须一致，避免出现 BENC_ 却不是 Boss 的脏数据。
            var isBossPrefix = Id.StartsWith("BENC_", System.StringComparison.Ordinal);
            var isElitePrefix = Id.StartsWith("EENC_", System.StringComparison.Ordinal);
            if (isBossPrefix && !_isBoss)
            {
                report.Error("ENC_BOSS_FLAG_MISMATCH", "ID 前缀为 BENC_ 但未勾选 isBoss。", Id, fieldName: "isBoss");
            }

            if (isElitePrefix && !_isElite)
            {
                report.Error("ENC_ELITE_FLAG_MISMATCH", "ID 前缀为 EENC_ 但未勾选 isElite。", Id, fieldName: "isElite");
            }
        }
    }
}
