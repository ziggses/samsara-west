namespace SamsaraWest.Narrative
{
    /// <summary>「开始一段对白」被拒的理由。取值进日志与测试断言，所以显式编号、不从 1 开始乱跳。</summary>
    public enum DialogueStartRejection
    {
        /// <summary>没有拒绝，对白已经开始。</summary>
        None = 0,

        /// <summary>对话表里没有这个 ID，或者那个 ID 不是对话（例如一扇门的 targetId 是一张地图）。</summary>
        UnknownDialogue = 1,

        /// <summary>已经有一段对白开着。同屏不叠两段对白——叠了界面也不知道该画哪一段。</summary>
        AlreadyActive = 2,

        /// <summary>节点的进入条件没满足（<c>requiredStateKey</c>）。</summary>
        RequirementNotMet = 3,
    }

    /// <summary>
    /// 一次「开始对白」的结算结果。与 <c>InteractionResult</c> 同一种写法：
    /// 结果自带理由，调用方不必回头问服务「刚才为什么没成」。
    /// </summary>
    public readonly struct DialogueStartResult
    {
        public DialogueStartResult(bool started, DialogueStartRejection rejection, string dialogueId, string knotName)
        {
            Started = started;
            Rejection = rejection;
            DialogueId = dialogueId;
            KnotName = knotName;
        }

        public bool Started { get; }

        public DialogueStartRejection Rejection { get; }

        /// <summary>请求的对话 ID（原样回传，失败时用来定位是哪一条数据）。</summary>
        public string DialogueId { get; }

        /// <summary>真正开起来的剧本节点名；没开起来时为 null。</summary>
        public string KnotName { get; }

        public static DialogueStartResult Rejected(string dialogueId, DialogueStartRejection rejection) =>
            new DialogueStartResult(false, rejection, dialogueId, null);
    }

    /// <summary>
    /// 一次「推进」的结算结果。
    /// </summary>
    /// <remarks>
    /// 为什么要 <see cref="ChangedNode"/>：一段对白可能横跨多个节点（迎客猴讲完自动接上问伤口），
    /// 界面要知道这一步是「同一节点里的下一行」还是「翻到下一节点了」——后者才需要重画标题与行数。
    /// </remarks>
    public readonly struct DialogueAdvanceResult
    {
        public DialogueAdvanceResult(bool advanced, bool changedNode, bool ended, string knotName, int linesPlayed)
        {
            Advanced = advanced;
            ChangedNode = changedNode;
            Ended = ended;
            KnotName = knotName;
            LinesPlayed = linesPlayed;
        }

        /// <summary>这一步有没有推进成功。没有对白开着、或对白已经结束，都是 false。</summary>
        public bool Advanced { get; }

        /// <summary>这一步是否翻到了下一个节点（而不是同一节点里的下一行）。</summary>
        public bool ChangedNode { get; }

        /// <summary>这一步把整段对白读完了。</summary>
        public bool Ended { get; }

        /// <summary>结算后所在的节点名；已结束时是最后读到的那个节点。</summary>
        public string KnotName { get; }

        /// <summary>本次会话到此为止一共播了多少行。</summary>
        public int LinesPlayed { get; }

        public static DialogueAdvanceResult Inactive() =>
            new DialogueAdvanceResult(false, false, false, null, 0);
    }
}
