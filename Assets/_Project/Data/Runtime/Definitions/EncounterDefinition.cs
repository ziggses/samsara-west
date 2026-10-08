using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>遭遇编排。ID 沿用剧本约定：普通 ENC_CH01_001、精英 EENC_、Boss BENC_。</summary>
    [CreateAssetMenu(fileName = "Encounter", menuName = "SamsaraWest/定义/遭遇 Encounter")]
    public sealed class EncounterDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;
        [CsvColumn("enemyIds", required: true)] [SerializeField] private string[] _enemyIds = System.Array.Empty<string>();

        /// <summary>策划备注用的阵型标签。见下方 remarks。</summary>
        /// <remarks>
        /// 这一列<b>不参与任何计算</b>：运行时一律按 <see cref="EnemyIds"/> 的顺序落位
        /// （前三个进前排、从左到右，见 <c>BattleFormation.SlotForIndex</c>），留空与填值的结果完全一样。
        /// 它的用途是让读表的人一眼看出编成形状。
        ///
        /// 口径与 <c>encounters.csv</c> 的表头一致。字段注释原先写的是坐标
        /// 「例如 1,1;2,1;3,2（列,行）」——与「只是标签」的说法互相矛盾，会让人以为落位真的读它，
        /// 2026-10-08 拍板统一为标签，并补上格式与数量的两条校验（见 <see cref="Validate"/>）。
        /// </remarks>
        [Tooltip("策划备注用的阵型标签，写作「前排x后排」（如 2x1）。运行时按 enemyIds 顺序落位，本列不参与计算。")]
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

            ValidateFormationLabel(report);

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

        /// <summary>
        /// 校验 formation 这条备注：格式必须是「前排x后排」，数量要与编成对得上。
        /// </summary>
        /// <remarks>
        /// 这两条<b>不影响落位</b>——落位永远按 enemyIds 的顺序。之所以要校验，是因为这一列曾经被写成
        /// 坐标（1,1;2,1;3,2），那种写法会让人以为它驱动站位；格式错误直接报错，就是不让它退回去。
        /// 数量不符只报警告：标签是给读表的人看的，写错了不至于让导入失败，但必须被看见。
        /// </remarks>
        private void ValidateFormationLabel(ValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(_formation))
            {
                return;
            }

            if (!TryParseFormationLabel(_formation, out var front, out var back))
            {
                report.Error(
                    "ENC_FORMATION_LABEL_FORMAT",
                    $"formation 只是备注标签，写作「前排x后排」（如 2x1），当前 '{_formation}'。落位不读这一列，写成坐标也没有用。",
                    Id,
                    fieldName: "formation");
                return;
            }

            var declared = front + back;
            if (declared != EnemyIds.Length)
            {
                report.Warn(
                    "ENC_FORMATION_LABEL_STALE",
                    $"formation 标签写的是 {front} 前排 + {back} 后排 = {declared} 个，实际编成 {EnemyIds.Length} 个敌人。" +
                    "标签不参与落位，但这条备注已经和编成对不上了，请顺手改对（或留空）。",
                    Id,
                    fieldName: "formation");
            }
        }

        /// <summary>解析「前排x后排」形式的备注标签。分隔符固定为小写 x。</summary>
        private static bool TryParseFormationLabel(string value, out int front, out int back)
        {
            front = 0;
            back = 0;

            var separator = value.IndexOf('x');
            if (separator <= 0 || separator == value.Length - 1)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (i == separator)
                {
                    continue;
                }

                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return int.TryParse(value.Substring(0, separator), out front)
                && int.TryParse(value.Substring(separator + 1), out back);
        }
    }
}
