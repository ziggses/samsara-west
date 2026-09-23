using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 对话节点。骨架期只做数据契约与校验，不接 Ink 运行时；
    /// Ink 属垂直切片阶段，届时 <see cref="InkKnotName"/> 直接对接剧本节点。
    /// </summary>
    [CreateAssetMenu(fileName = "Dialogue", menuName = "SamsaraWest/定义/对话 Dialogue")]
    public sealed class DialogueDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;

        [Tooltip("Ink 中的 knot 名，例如 CH01_N01_ENTRY。与剧本节点 ID 一致。")]
        [CsvColumn("inkKnotName", required: true)] [SerializeField] private string _inkKnotName;

        [CsvColumn("speakerCharacterId")] [SerializeField] private string _speakerCharacterId;

        [Tooltip("文本行数，用于校对剧本与资产是否同步。")]
        [CsvColumn("lineCount")] [SerializeField] private int _lineCount;

        [CsvColumn("nextNodeIds")] [SerializeField] private string[] _nextNodeIds = System.Array.Empty<string>();

        [Tooltip("进入本节点所需状态键。为空表示无条件。")]
        [CsvColumn("requiredStateKey")] [SerializeField] private string _requiredStateKey;

        [CsvColumn("requiredOperator")] [SerializeField] private CompareOperator _requiredOperator = CompareOperator.Equal;
        [CsvColumn("requiredValue")] [SerializeField] private int _requiredValue;

        [Tooltip("本节点播放后置位的状态键。")]
        [CsvColumn("setsStateKeys")] [SerializeField] private string[] _setsStateKeys = System.Array.Empty<string>();

        [CsvColumn("karmaChannel")] [SerializeField] private string _karmaChannel;
        [CsvColumn("karmaDelta")] [SerializeField] private int _karmaDelta;

        [CsvColumn("bgmKey")] [SerializeField] private string _bgmKey;
        [CsvColumn("portraitKey")] [SerializeField] private string _portraitKey;

        public override DefinitionKind Kind => DefinitionKind.Dialogue;

        public int ChapterIndex => _chapterIndex;

        public string InkKnotName => _inkKnotName;

        public string SpeakerCharacterId => _speakerCharacterId;

        public int LineCount => _lineCount;

        public string[] NextNodeIds => _nextNodeIds ?? System.Array.Empty<string>();

        public string RequiredStateKey => _requiredStateKey;

        public CompareOperator RequiredOperator => _requiredOperator;

        public int RequiredValue => _requiredValue;

        public string[] SetsStateKeys => _setsStateKeys ?? System.Array.Empty<string>();

        public string KarmaChannel => _karmaChannel;

        public int KarmaDelta => _karmaDelta;

        public string BgmKey => _bgmKey;

        public string PortraitKey => _portraitKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_chapterIndex < 1 || _chapterIndex > 8)
            {
                report.Error("DLG_CHAPTER_INVALID", $"章节序号必须在 1–8，当前 {_chapterIndex}。", Id, fieldName: "chapterIndex");
            }

            if (!IdRules.IsValidNarrativeNodeId(_inkKnotName))
            {
                report.Error(
                    "DLG_KNOT_FORMAT",
                    $"Ink 节点名 '{_inkKnotName}' 不符合剧本节点格式（CH01_N01_ENTRY）。",
                    Id,
                    fieldName: "inkKnotName");
            }

            if (!string.IsNullOrWhiteSpace(_speakerCharacterId) && !IdRules.IsValidId(DefinitionKind.Character, _speakerCharacterId))
            {
                report.Error("DLG_SPEAKER_ID_PATTERN", $"说话人 ID '{_speakerCharacterId}' 不符合角色命名规则。", Id, fieldName: "speakerCharacterId");
            }

            for (var i = 0; i < NextNodeIds.Length; i++)
            {
                if (!IdRules.IsValidNarrativeNodeId(NextNodeIds[i]))
                {
                    report.Error("DLG_NEXT_NODE_FORMAT", $"后继节点 '{NextNodeIds[i]}' 不符合节点格式。", Id, fieldName: "nextNodeIds");
                }
            }

            if (!string.IsNullOrWhiteSpace(_requiredStateKey) && !IdRules.IsValidStateKey(_requiredStateKey))
            {
                report.Error(
                    "DLG_STATE_KEY_FORMAT",
                    $"进入条件键 '{_requiredStateKey}' 不符合剧情状态键格式（flag./relation./karma./ending.）。",
                    Id,
                    fieldName: "requiredStateKey");
            }

            for (var i = 0; i < SetsStateKeys.Length; i++)
            {
                if (!IdRules.IsValidStateKey(SetsStateKeys[i]))
                {
                    report.Error("DLG_SET_KEY_FORMAT", $"置位键 '{SetsStateKeys[i]}' 不符合剧情状态键格式。", Id, fieldName: "setsStateKeys");
                }
            }

            if (!string.IsNullOrWhiteSpace(_karmaChannel) && !_karmaChannel.StartsWith("karma.", System.StringComparison.Ordinal))
            {
                report.Error("DLG_KARMA_FORMAT", $"心念频道 '{_karmaChannel}' 必须以 karma. 开头。", Id, fieldName: "karmaChannel");
            }

            if (_karmaDelta != 0 && string.IsNullOrWhiteSpace(_karmaChannel))
            {
                report.Error("DLG_KARMA_NO_CHANNEL", "设置了心念增减但没有指定心念频道。", Id, fieldName: "karmaChannel");
            }

            if (_lineCount < 0)
            {
                report.Error("DLG_LINECOUNT_INVALID", $"文本行数不能为负，当前 {_lineCount}。", Id, fieldName: "lineCount");
            }

            if (_lineCount > 0 && NextNodeIds.Length == 0)
            {
                report.Warn("DLG_DEAD_END", "有文本但没有后继节点，若不是结局节点请检查剧本。", Id, fieldName: "nextNodeIds");
            }
        }
    }
}
