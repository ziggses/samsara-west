namespace SamsaraWest.Battle
{
    /// <summary>
    /// 阵营。数值只增不改：它会进入存档与战斗重放的快照，改数值等于让旧快照串位。
    /// </summary>
    public enum BattleSide
    {
        Player = 0,
        Enemy = 1,
    }

    /// <summary>
    /// 一场战斗的结果。
    /// </summary>
    /// <remarks>
    /// 剧情撤退（由剧本强制结束战斗、不掷骰）已拍板为<b>单独结局、不算失败</b>，
    /// 见 <see cref="ForcedRetreat"/>。
    /// </remarks>
    public enum BattleOutcome
    {
        Ongoing = 0,

        /// <summary>敌方全灭。</summary>
        PlayerVictory = 1,

        /// <summary>我方全灭。双方同时全灭时按「我方负」结算：你方无人站着就是失败。</summary>
        PlayerDefeat = 2,

        /// <summary>
        /// 我方主动脱离战斗（逃跑掷骰成功）。它不是失败：这场战斗只是没打完。
        /// 逃跑之后算不算失败、扣不扣资源属于流程侧口径，内核只报结果。
        /// </summary>
        PlayerEscaped = 3,

        /// <summary>
        /// 剧情强制结束这场战斗（剧本判定这场打不下去，不掷骰、不判胜负）。
        /// </summary>
        /// <remarks>
        /// 它<b>不是失败</b>：流程侧据此回到该战斗之前的剧情节点，不结算奖励、也不写战败。
        /// 内核只负责写下结局并收尾；「回到剧情前」是流程侧的职责。
        /// </remarks>
        ForcedRetreat = 4,
    }

    /// <summary>
    /// 一个单位的行动阶段。任务书第 2 节规定：每个角色每回合拥有
    /// <b>1 次主要行动</b>与<b>1 次移动或换位</b>，两者互相独立——
    /// 换位不花掉主行动，这正是「换位改变前后排与相邻关系」能作为战术手段的前提。
    /// </summary>
    public enum TurnPhase
    {
        /// <summary>尚未推进到任何单位。</summary>
        Idle = 0,

        /// <summary>待主行动。此时既可以打主行动，也可以先移动／换位。</summary>
        MainAction = 1,

        /// <summary>主行动已结算，只剩余一次移动／换位（可以放弃）。</summary>
        MoveOrSwap = 2,

        /// <summary>该单位的回合已经结束，等待推进到下一个行动者。</summary>
        Finished = 3,
    }

    /// <summary>
    /// 指令被拒绝的原因。刻意用枚举而不是中文字符串：
    /// 界面层按枚举查文本键，内核里不允许出现面向玩家的文案。
    /// </summary>
    public enum BattleCommandRejection
    {
        None = 0,

        /// <summary>战斗已经结束。</summary>
        BattleFinished = 1,

        /// <summary>当前没有待行动的单位（还没调用 <c>BeginNextTurn</c>）。</summary>
        NoActiveTurn = 2,

        /// <summary>当前阶段不允许这类指令，例如主行动已经用过了还想再打一次。</summary>
        WrongPhase = 3,

        /// <summary>找不到这个技能。</summary>
        UnknownSkill = 4,

        /// <summary>该单位不会这个技能。</summary>
        SkillNotOwned = 5,

        /// <summary>技能还在冷却。</summary>
        SkillOnCooldown = 6,

        /// <summary>灵力不足。</summary>
        NotEnoughSpirit = 7,

        /// <summary>没有合法目标（例如全体技能但对面已经没人）。</summary>
        NoValidTarget = 8,

        /// <summary>指定的目标已经倒下。</summary>
        TargetDead = 9,

        /// <summary>指定的目标不在技能允许的阵营里。</summary>
        TargetSideMismatch = 10,

        /// <summary>目标就是自己，而技能不允许（例如对自己用单体治疗之外的技能）。</summary>
        TargetIsSelf = 11,

        /// <summary>换位目标不是同阵营的存活同伴。</summary>
        SwapTargetInvalid = 12,

        /// <summary>目标格已经被别人占了。</summary>
        SlotOccupied = 13,

        /// <summary>目标格不在阵型范围内。</summary>
        SlotOutOfRange = 14,

        /// <summary>这一次的移动／换位已经用过了。</summary>
        MoveAlreadyUsed = 15,
    }

    /// <summary>
    /// 一次行动的种类。
    /// </summary>
    /// <remarks>
    /// 任务书里列出的道具、防御、联合技仍然没有口径，因此<b>不</b>为它们预留占位枚举值，
    /// 避免出现一个永远走不通的分支。逃跑已经拍板，见 <see cref="Escape"/>。
    /// </remarks>
    public enum BattleActionKind
    {
        /// <summary>用技能（攻击与法术共用一条数据驱动的路径）。</summary>
        Skill = 0,

        /// <summary>换位或移动到空位。</summary>
        Swap = 1,

        /// <summary>主动结束回合，放弃剩余行动。</summary>
        EndTurn = 2,

        /// <summary>
        /// 逃跑（主行动）。它是<b>整队撤退</b>：任意我方单位的回合都可以发起，
        /// 一次掷骰决定整场走人还是失败；失败只是白费这一手，敌人照常行动。
        /// </summary>
        Escape = 3,
    }

    /// <summary>状态发生变化的方式，用于事件与战斗日志。</summary>
    public enum StatusChangeKind
    {
        /// <summary>新挂上。</summary>
        Applied = 0,

        /// <summary>叠加了一层。</summary>
        Stacked = 1,

        /// <summary>持续时间被刷新。</summary>
        Refreshed = 2,

        /// <summary>被更强的一次顶掉。</summary>
        Replaced = 3,

        /// <summary>持续时间走完，移除。</summary>
        Expired = 4,
    }
}
