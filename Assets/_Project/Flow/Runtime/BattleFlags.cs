using SamsaraWest.Battle;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 战斗结局在剧情账本里的键约定。
    /// </summary>
    /// <remarks>
    /// <para>形如 <c>flag.battle.enc_ch01_001.won</c>：前缀 + <b>小写</b>的遭遇 ID + 结局词。
    /// 一个结局写一个键、值为 1，与 <c>flag.interact.&lt;交互物&gt;</c> 同一种口径——
    /// 账本记的是「这件事发生过」，不是「它现在的状态码」。内容侧的写法与
    /// <c>interactables.csv</c> 里既有的条件列完全一样（<c>requiredStateKey</c> + <c>requiredOperator</c>
    /// + <c>requiredValue</c>，例：<c>INT_CH01_003_CHEST</c> 要求 <c>flag.ch01.prologue_done = 1</c>）。</para>
    ///
    /// <para><b>四种结局各有一个词，尤其是剧情撤退</b>：已拍板口径是「不算失败、回到剧情前的节点」，
    /// 所以它写 <see cref="Retreated"/> 而<b>绝不</b>写 <see cref="Lost"/>。把撤退记成战败，
    /// 会让剧本里「输过一次」「打赢过」这类条件判错，而且错得很安静。</para>
    ///
    /// <para>为什么键约定放在组合根：今天需要在代码里写这些键的只有 <see cref="BattleStoryLink"/>
    /// （写侧），读侧是内容表里的条件（数据，不是代码）。等对话或任务运行时也要在代码里读它们时，
    /// 再把这个约定和第二个读者一起上提——与 <c>IExplorationStateSource</c> 同一条纪律，
    /// 不为假想的读者提前造一个归属层。</para>
    /// </remarks>
    public static class BattleFlags
    {
        /// <summary>战斗结局键的前缀。</summary>
        public const string Prefix = "flag.battle.";

        /// <summary>打赢了。</summary>
        public const string Won = "won";

        /// <summary>打输了。</summary>
        public const string Lost = "lost";

        /// <summary>逃跑成功（掷骰跑掉的那种）。</summary>
        public const string Fled = "fled";

        /// <summary>剧情强制撤退。<b>它不是失败。</b></summary>
        public const string Retreated = "retreated";

        /// <summary>
        /// 这一场、这个结局对应的账本键；没有可记的键时返回空串。
        /// </summary>
        /// <remarks>
        /// 空串有两种来路：遭遇 ID 为空（手工构造的战斗，没法归因），
        /// 或者结局不是一个收场（<see cref="BattleOutcome.Ongoing"/> 及未知值）。
        /// 两种都不该往账本里塞残缺的键——调用方据此判「不记账」。
        /// </remarks>
        public static string OutcomeKey(string encounterId, BattleOutcome outcome)
        {
            var word = OutcomeWord(outcome);
            if (word.Length == 0 || string.IsNullOrEmpty(encounterId))
            {
                return string.Empty;
            }

            return Prefix + encounterId.ToLowerInvariant() + "." + word;
        }

        /// <summary>结局对应的词；不是收场（或是不认识的取值）时返回空串。</summary>
        public static string OutcomeWord(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerVictory:
                    return Won;
                case BattleOutcome.PlayerDefeat:
                    return Lost;
                case BattleOutcome.PlayerEscaped:
                    return Fled;
                case BattleOutcome.ForcedRetreat:
                    return Retreated;
                default:
                    return string.Empty;
            }
        }
    }
}
