using System;
using System.Collections.Generic;
using System.Text;

namespace SamsaraWest.Economy
{
    /// <summary>
    /// 一场胜仗的战利品：多少钱、多少经验、几样东西。它是<b>结算的产物</b>，
    /// 不是奖励的规则——规则在掉落表与敌人定义里（数据，不是代码）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么经验也在这个结构里</b>：敌人的 <c>experienceReward</c> 是真填了值的
    /// （首章杂兵 12–38、猴王 300），但还没有等级系统，这份经验<b>无处安放</b>。
    /// 把它算出来并带到这里，是为了让这个缺口<b>看得见</b>——而不是让它在某处被悄悄丢掉、
    /// 等到做成长模块时才发现「经验到底该给多少」已经没人答得上来。消费它的是将来的 Progression。</para>
    /// </remarks>
    public sealed class BattleSpoils
    {
        /// <summary>两手空空的一场（逃跑、战败，或敌人身上什么都没配）。</summary>
        public static readonly BattleSpoils None = new BattleSpoils(0, 0, Array.Empty<ItemStack>());

        public BattleSpoils(int gold, int experience, IReadOnlyList<ItemStack> items)
        {
            Gold = gold;
            Experience = experience;
            Items = items ?? Array.Empty<ItemStack>();
        }

        /// <summary>金钱总额：敌人赏金 + 掉落表掷出的钱（两套来源的关系见 ADR-027）。</summary>
        public int Gold { get; }

        /// <summary>经验总额。当前<b>没有消费者</b>，只有结算日志在念它。</summary>
        public int Experience { get; }

        /// <summary>掉出来的东西，按掉落表与敌人的出现顺序排列；同一样东西<b>可能重复出现</b>。</summary>
        public IReadOnlyList<ItemStack> Items { get; }

        /// <summary>钱、经验、东西全都为零。此时结算是一件无事可做的事。</summary>
        public bool IsEmpty => Gold == 0 && Experience == 0 && Items.Count == 0;

        public override string ToString()
        {
            if (IsEmpty)
            {
                return "（无战利品）";
            }

            var text = new StringBuilder();
            text.Append(Gold).Append(" 钱");
            if (Experience != 0)
            {
                text.Append("、").Append(Experience).Append(" 经验");
            }

            if (Items.Count != 0)
            {
                text.Append("、");
                for (var i = 0; i < Items.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append("＋");
                    }

                    text.Append(Items[i]);
                }
            }

            return text.ToString();
        }
    }
}
