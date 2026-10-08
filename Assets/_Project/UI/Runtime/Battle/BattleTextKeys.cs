using SamsaraWest.Battle;
using SamsaraWest.Localization;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 战斗界面要用到的文本键。<b>只有</b>键，没有文案——文案在文本表里，界面不做拼接。
    /// </summary>
    /// <remarks>
    /// <para>内核里禁止出现面向玩家的文案（<see cref="BattleCommandRejection"/> 的注释就是这么说的），
    /// 于是「枚举 → 文本键」这一步必然存在于界面层。把它收成一个纯函数，就可以被逐条锁死，
    /// 而不是散在按钮回调里靠肉眼核对。</para>
    /// <para>返回 <c>null</c> 表示<b>这个状态没有面向玩家的文案</b>，不是「还没翻译」。
    /// 调用方必须把 null 当作「不画这一行」，而不是拿去查表。用例里有一份兜底名单：
    /// 新增枚举值会被它拦下来，逼你决定这个新状态显不显示。</para>
    /// </remarks>
    public static class BattleTextKeys
    {
        /// <summary>
        /// 战斗结局的标题键。
        /// </summary>
        /// <returns>
        /// <see cref="BattleOutcome.Ongoing"/> 返回 null——仗还没打完，没有结局可报。
        /// </returns>
        public static string Outcome(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerVictory:
                    return LocalizationKeys.UI_BATTLE_RESULT_VICTORY;
                case BattleOutcome.PlayerDefeat:
                    return LocalizationKeys.UI_BATTLE_RESULT_DEFEAT;
                case BattleOutcome.PlayerEscaped:
                    return LocalizationKeys.UI_BATTLE_RESULT_ESCAPED;
                case BattleOutcome.ForcedRetreat:
                    return LocalizationKeys.UI_BATTLE_RESULT_FORCED_RETREAT;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 指令被拒时按钮旁边的短注。
        /// </summary>
        /// <returns>
        /// 只有「玩家看得懂、也确实能靠改操作绕开」的两种原因给文案；
        /// 其余（相位不对、技能不会、目标非法……）都是界面本该拦住的，返回 null。
        /// </returns>
        public static string Rejection(BattleCommandRejection rejection)
        {
            switch (rejection)
            {
                case BattleCommandRejection.SkillOnCooldown:
                    return LocalizationKeys.UI_BATTLE_REJECTION_SKILL_ON_COOLDOWN;
                case BattleCommandRejection.NotEnoughSpirit:
                    return LocalizationKeys.UI_BATTLE_REJECTION_NOT_ENOUGH_SPIRIT;
                default:
                    return null;
            }
        }

        /// <summary>逃跑按钮的文案键。</summary>
        public static string Flee => LocalizationKeys.UI_BATTLE_COMMAND_FLEE;

        /// <summary>防御按钮的文案键。</summary>
        public static string Defend => LocalizationKeys.UI_BATTLE_COMMAND_DEFEND;

        /// <summary>结束回合按钮的文案键。</summary>
        public static string EndTurn => LocalizationKeys.UI_BATTLE_COMMAND_ENDTURN;

        /// <summary>战斗界面标题键。</summary>
        public static string Title => LocalizationKeys.UI_BATTLE_TITLE;

        /// <summary>回合数文本键，参数为回合号。</summary>
        public static string Round => LocalizationKeys.UI_BATTLE_ROUND;

        /// <summary>选目标提示键。</summary>
        public static string TargetPrompt => LocalizationKeys.UI_BATTLE_TARGET_PROMPT;

        /// <summary>当前行动者标记键。</summary>
        public static string CurrentActor => LocalizationKeys.UI_BATTLE_ACTOR_CURRENT;

        /// <summary>气血文本键，参数为（当前，上限）。</summary>
        public static string Health => LocalizationKeys.UI_BATTLE_HP;

        /// <summary>护体文本键，参数为（剩余，上限）。</summary>
        public static string Break => LocalizationKeys.UI_BATTLE_BREAK;

        /// <summary>逃跑成功率文本键，参数为百分比字符串。</summary>
        public static string EscapeChance => LocalizationKeys.UI_BATTLE_ESCAPE;
    }
}
