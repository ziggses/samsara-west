using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 挂在战斗单位身上的一个状态实例。定义（数值与规则）来自 <see cref="StatusDefinition"/>，
    /// 这里只保存「这一次」的层数与剩余回合。
    /// </summary>
    /// <remarks>
    /// 层数的语义：<b>按层数线性放大该状态的全部修正</b>。
    /// 灼烧 <c>healthDeltaPerTurn = -8</c>、最多 3 层，于是叠满后每回合掉 24 点——
    /// 这正是灼烧值得被清掉的理由。
    /// </remarks>
    public sealed class BattleStatusInstance
    {
        internal BattleStatusInstance(StatusDefinition definition, int stacks, int remainingTurns)
        {
            Definition = definition;
            Stacks = Mathf.Max(1, stacks);
            RemainingTurns = Mathf.Max(1, remainingTurns);
        }

        public StatusDefinition Definition { get; }

        public string StatusId => Definition.Id;

        /// <summary>状态名称的文本键，界面层拿去取词。</summary>
        public string DisplayNameKey => Definition.DisplayNameKey;

        public bool IsDebuff => Definition.IsDebuff;

        public int Stacks { get; internal set; }

        public int RemainingTurns { get; internal set; }

        /// <summary>每回合按层数结算的生命增减。<b>不</b>走伤害公式，见 <c>BattleSession</c> 的回合结算。</summary>
        public int HealthDeltaPerTurn => Definition.HealthDeltaPerTurn * Stacks;

        public float AttackModifier => Definition.AttackModifier * Stacks;

        public float DefenseModifier => Definition.DefenseModifier * Stacks;

        public float SpeedModifier => Definition.SpeedModifier * Stacks;

        /// <summary>
        /// 承伤倍率。多个状态之间<b>相乘</b>：护盾 0.8 与易伤 1.25 同时存在就是 1.0。
        /// 中性值是 1（不是 0），数据表里所有未配置该列的状态都写着 1，这一点由
        /// <c>statuses.csv</c> 的表头注释「承伤修正为倍率（1.25 = 受伤 +25%）」定死。
        /// </summary>
        public float IncomingDamageModifier => Multiply(Definition.IncomingDamageModifier, Stacks);

        /// <summary>护体削减倍率，语义与承伤倍率一致：中性值 1、多状态相乘。</summary>
        public float BreakDamageModifier => Multiply(Definition.BreakDamageModifier, Stacks);

        /// <summary>
        /// 状态的「强度」评分，只用于 <see cref="StackRule.StrongestOnly"/> 的取舍比较。
        /// 把所有修正折成一个可比较的数：绝对值相加，生命增减按 1 点 ≈ 0.01 折算，
        /// 这样「-8 生命」与「攻击 -20%」不会被量纲差淹没。
        /// </summary>
        public float Magnitude => MagnitudeOf(Definition);

        /// <summary>按定义（不含层数）算强度，用于「更强的一次」这类比较。</summary>
        public static float MagnitudeOf(StatusDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            return
                Mathf.Abs(definition.AttackModifier) +
                Mathf.Abs(definition.DefenseModifier) +
                Mathf.Abs(definition.SpeedModifier) +
                Mathf.Abs(1f - definition.IncomingDamageModifier) +
                Mathf.Abs(1f - definition.BreakDamageModifier) +
                (Mathf.Abs(definition.HealthDeltaPerTurn) * 0.01f);
        }

        public override string ToString() => $"{StatusId}×{Stacks}({RemainingTurns})";

        /// <summary>
        /// 倍率型修正按层数放大：1 层 1.25、2 层 1.5，而不是 1.25²。
        /// 取加法是刻意的——乘法叠层会让「多段破防」这类叠加路径爆炸。
        /// </summary>
        private static float Multiply(float modifier, int stacks) => 1f + ((modifier - 1f) * stacks);
    }
}
