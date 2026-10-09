using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 对话节点。数据契约加校验，外加 <c>Narrative</c> 侧的<b>节点推进器</b>（ADR-029）：
    /// 推进器只认本表的字段，不接 Ink；Ink 属垂直切片阶段，届时 <see cref="InkKnotName"/> 直接对接剧本节点。
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

        [Tooltip("终局节点：播完本节点就结束会话，不沿 nextNodeIds 续跑。环境调查类节点用它把自己从链上摘下来。")]
        [CsvColumn("isTerminal")] [SerializeField] private bool _isTerminal;

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

        /// <summary>
        /// 播完本节点是否<b>就地收场</b>，而不是沿 <see cref="NextNodeIds"/> 继续跑。
        /// </summary>
        /// <remarks>
        /// 为什么要这一列：环境调查（断鼓、血迹、小猴的桃）是<b>各自独立</b>的一次性旁白——
        /// 调查断鼓之后自动接着播血迹的文本，是数据说不出来的意思。没有这一列时，
        /// 为了让「有文本就必须有后继」的校验闭嘴，只能把它们串成一条假的主线链。
        /// 有了它，环境调查节点可以诚实地承认「到这里就完了」。
        /// </remarks>
        public bool IsTerminal => _isTerminal;

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

            if (_isTerminal && NextNodeIds.Length > 0)
            {
                report.Warn(
                    "DLG_TERMINAL_WITH_NEXT",
                    "标了终局却还登记着后继节点，运行时不会走它们：要么删掉后继，要么取消终局。",
                    Id,
                    fieldName: "nextNodeIds");
            }

            if (!_isTerminal && _lineCount > 0 && NextNodeIds.Length == 0)
            {
                report.Warn("DLG_DEAD_END", "有文本但没有后继节点，若不是结局节点请检查剧本。", Id, fieldName: "nextNodeIds");
            }
        }
    }
}
