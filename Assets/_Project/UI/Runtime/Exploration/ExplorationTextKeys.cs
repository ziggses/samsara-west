using SamsaraWest.Exploration;
using SamsaraWest.Localization;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 探索面板的「结果 → 文本键」对照表。与 <c>BattleTextKeys</c> 同一种分工：
    /// 界面不判断对错，只把内核给的枚举转述成键，然后交给本地化服务取词。
    /// </summary>
    /// <remarks>
    /// <para><b>哪些原因有文案，与战斗层刻意不同</b>：战斗的置灰理由只转述「玩家改操作能绕开的」，
    /// 因为其余几种是界面本该拦住的（点了根本没转发）。探索这边没有按钮可置灰——玩家按的是方向键，
    /// 拒绝只表现为「人没动」。所以移动的三种拒绝<b>全部</b>给文案：按了没反应必须有一行字解释，
    /// 否则玩家会先怀疑按键丢了，而不是先怀疑墙。图边与占格在格子图上虽然看得见，
    /// 但「看得见」与「知道刚才那下被拒了」不是一回事。</para>
    /// <para><b>唯一不给文案的是交互的「藏起来了」</b>（<see cref="InteractionRejection.Hidden"/>）：
    /// 那句话等于替数据宣布「这里有个机关，只是现在看不见」，把
    /// <c>isHiddenUntilConditionMet</c> 想藏的意图当场泄掉。看不见就说不知道，
    /// 玩家那时得到的反馈与「那里本来就没东西」相同——这正是隐藏该有的样子。</para>
    /// </remarks>
    public static class ExplorationTextKeys
    {
        public static string Title => LocalizationKeys.UI_EXPLORE_TITLE;

        public static string Hint => LocalizationKeys.UI_EXPLORE_VIEW_HINT;

        public static string NoMap => LocalizationKeys.UI_EXPLORE_NO_MAP;

        public static string Status => LocalizationKeys.UI_EXPLORE_STATUS;

        public static string GridLegend => LocalizationKeys.UI_EXPLORE_GRID_LEGEND;

        public static string EncounterPending => LocalizationKeys.UI_EXPLORE_ENCOUNTER_PENDING;

        public static string FacingTarget => LocalizationKeys.UI_EXPLORE_FACING_TARGET;

        /// <summary>交互物没登记 <c>promptKey</c> 时的降级一行（只报名字）。</summary>
        public static string FacingTargetPlain => LocalizationKeys.UI_EXPLORE_FACING_TARGET_PLAIN;

        public static string FacingEmpty => LocalizationKeys.UI_EXPLORE_FACING_EMPTY;

        /// <summary>
        /// 移动被拒的理由对应的键；走成了（<see cref="MoveRejection.None"/>）返回 null，
        /// 由调用方决定「不画这一行」。
        /// </summary>
        public static string MoveRejectionKey(MoveRejection rejection)
        {
            switch (rejection)
            {
                case MoveRejection.OutOfBounds:
                    return LocalizationKeys.UI_EXPLORE_MOVE_OUT_OF_BOUNDS;
                case MoveRejection.Blocked:
                    return LocalizationKeys.UI_EXPLORE_MOVE_BLOCKED;
                case MoveRejection.EncounterPending:
                    return EncounterPending;
                default:
                    return null;
            }
        }

        /// <summary>交互被拒的理由对应的键；<see cref="InteractionRejection.None"/> 与隐藏都返回 null。</summary>
        public static string InteractionRejectionKey(InteractionRejection rejection)
        {
            switch (rejection)
            {
                case InteractionRejection.NoTarget:
                    return LocalizationKeys.UI_EXPLORE_INTERACT_NO_TARGET;
                case InteractionRejection.RequirementNotMet:
                    return LocalizationKeys.UI_EXPLORE_INTERACT_REQUIREMENT_NOT_MET;
                case InteractionRejection.AlreadyUsed:
                    return LocalizationKeys.UI_EXPLORE_INTERACT_ALREADY_USED;
                case InteractionRejection.EncounterPending:
                    return EncounterPending;
                default:
                    return null;
            }
        }

        public static string Facing(MoveDirection direction)
        {
            switch (direction)
            {
                case MoveDirection.North:
                    return LocalizationKeys.UI_EXPLORE_FACING_NORTH;
                case MoveDirection.East:
                    return LocalizationKeys.UI_EXPLORE_FACING_EAST;
                case MoveDirection.West:
                    return LocalizationKeys.UI_EXPLORE_FACING_WEST;
                default:
                    return LocalizationKeys.UI_EXPLORE_FACING_SOUTH;
            }
        }

        /// <summary>对白块的标题（「对白」）。</summary>
        public static string DialogueTitle => LocalizationKeys.UI_DIALOGUE_TITLE;

        /// <summary>对白块的状态行：节点名 · 第 n/N 行。</summary>
        public static string DialogueStatus => LocalizationKeys.UI_DIALOGUE_STATUS;

        /// <summary>对白块底部的按键提示。对白开着时顶替探索那一条。</summary>
        public static string DialogueHint => LocalizationKeys.UI_DIALOGUE_HINT;

        /// <summary>对白行没登记说话人时的兜底（旁白）。</summary>
        public static string DialogueNarrator => LocalizationKeys.UI_DIALOGUE_NARRATOR;

        /// <summary>
        /// 缺文本时的提示。本层不允许给兜底正文，但「这里该有一行字却没对上」必须说出来——
        /// 否则一次数据错位在玩家眼里就是「对话面板上一片空白」。
        /// </summary>
        public static string DialogueMissing => LocalizationKeys.UI_DIALOGUE_MISSING;

        public static string InteractTriggered => LocalizationKeys.UI_EXPLORE_INTERACT_TRIGGERED;

        public static string InteractMapChange => LocalizationKeys.UI_EXPLORE_INTERACT_MAP_CHANGE;
    }
}
