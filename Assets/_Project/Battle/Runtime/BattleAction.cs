using System.Collections.Generic;
using System.Text;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 一次行动对<b>某一个</b>目标造成的全部结果。
    /// </summary>
    /// <remarks>
    /// 存在的理由是「为什么是这个数」：一场 5 回合试玩要判断意图、破防、换位是否可理解，
    /// 就得能回答「这一下打掉 37、护体从 30 掉到 12、还差多少破防」。
    /// 因此这里把中间量都留下来，而不是只报一个总伤害。
    /// </remarks>
    public sealed class BattleUnitEffect
    {
        internal BattleUnitEffect(BattleUnit target, string skillId)
        {
            TargetRuntimeId = target.RuntimeId;
            TargetDefinitionId = target.DefinitionId;
            TargetDisplayNameKey = target.DisplayNameKey;
            TargetSide = target.Side;
            SkillId = skillId;
            HealthBefore = target.Health;
            HealthAfter = target.Health;
            BreakValueAfter = target.BreakValue;
        }

        public int TargetRuntimeId { get; }

        public string TargetDefinitionId { get; }

        public string TargetDisplayNameKey { get; }

        public BattleSide TargetSide { get; }

        public string SkillId { get; }

        public int HealthBefore { get; }

        public int HealthAfter { get; internal set; }

        /// <summary>本次行动对该目标的伤害合计（含所有段）。</summary>
        public int Damage { get; internal set; }

        /// <summary>实际命中的段数。目标中途倒下时可能少于技能配置的段数。</summary>
        public int LandedHits { get; internal set; }

        public int CriticalHits { get; internal set; }

        /// <summary>本次行动对该目标的治疗合计。</summary>
        public int Healing { get; internal set; }

        /// <summary>被削减的护体值。</summary>
        public int BreakDamage { get; internal set; }

        /// <summary>结算后的护体值。</summary>
        public int BreakValueAfter { get; internal set; }

        /// <summary>这一次是否打空了护体值、令目标进入破防。</summary>
        public bool EnteredBroken { get; internal set; }

        /// <summary>本次行动是否只是打在已经破防的目标身上（此时护体值不会被继续削减）。</summary>
        public bool TargetWasBroken { get; internal set; }

        /// <summary>这一次施加的状态 ID；没施加则为空。</summary>
        public string AppliedStatusId { get; internal set; }

        public StatusChangeKind? StatusChange { get; internal set; }

        /// <summary>状态是被挂上了，还是掷骰没中。区分这两者，界面才能显示「抵抗」而不是静默无事发生。</summary>
        public bool StatusRollFailed { get; internal set; }

        public bool Died { get; internal set; }

        /// <summary>本次结算涉及的五行关系（多段技能取第一段的判定）。</summary>
        public ElementRelation ElementRelation { get; internal set; }

        internal bool IsEmpty =>
            Damage == 0 && Healing == 0 && BreakDamage == 0 && AppliedStatusId == null && !EnteredBroken;

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(TargetDefinitionId).Append('#').Append(TargetRuntimeId);
            if (Damage > 0)
            {
                builder.Append(" 伤害 ").Append(Damage);
                if (CriticalHits > 0)
                {
                    builder.Append("（暴击 ").Append(CriticalHits).Append(" 段）");
                }
            }

            if (Healing > 0)
            {
                builder.Append(" 治疗 ").Append(Healing);
            }

            if (BreakDamage > 0)
            {
                builder.Append(" 削护体 ").Append(BreakDamage);
            }

            if (EnteredBroken)
            {
                builder.Append(" 破防！");
            }

            if (AppliedStatusId != null)
            {
                builder.Append(" 状态 ").Append(AppliedStatusId);
            }
            else if (StatusRollFailed)
            {
                builder.Append(" 状态未中");
            }

            if (Died)
            {
                builder.Append(" 倒下");
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 一次指令的结果。失败时带 <see cref="BattleCommandRejection"/>，
    /// 界面按枚举查文本键，内核里不出现面向玩家的中文。
    /// </summary>
    public sealed class BattleActionResult
    {
        private readonly List<BattleUnitEffect> _effects = new List<BattleUnitEffect>(4);

        private BattleActionResult(
            bool success,
            BattleCommandRejection rejection,
            BattleUnit actor,
            BattleActionKind kind,
            string skillId)
        {
            Success = success;
            Rejection = rejection;
            Actor = actor;
            Kind = kind;
            SkillId = skillId;
        }

        public bool Success { get; }

        public BattleCommandRejection Rejection { get; }

        /// <summary>发起行动的单位。</summary>
        public BattleUnit Actor { get; }

        public BattleActionKind Kind { get; }

        /// <summary>技能 ID；换位与结束回合为空。</summary>
        public string SkillId { get; }

        public IReadOnlyList<BattleUnitEffect> Effects => _effects;

        public int TotalDamage
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _effects.Count; i++)
                {
                    total += _effects[i].Damage;
                }

                return total;
            }
        }

        public int TotalHealing
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _effects.Count; i++)
                {
                    total += _effects[i].Healing;
                }

                return total;
            }
        }

        /// <summary>本次行动读条到破防的目标数。</summary>
        public int BrokenCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _effects.Count; i++)
                {
                    if (_effects[i].EnteredBroken)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int DeathCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _effects.Count; i++)
                {
                    if (_effects[i].Died)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public static BattleActionResult Rejected(
            BattleCommandRejection rejection,
            BattleUnit actor = null,
            BattleActionKind kind = BattleActionKind.Skill,
            string skillId = null) =>
            new BattleActionResult(false, rejection, actor, kind, skillId);

        public static BattleActionResult Succeeded(BattleUnit actor, BattleActionKind kind, string skillId) =>
            new BattleActionResult(true, BattleCommandRejection.None, actor, kind, skillId);

        internal BattleUnitEffect AddEffect(BattleUnit target, string skillId)
        {
            var effect = new BattleUnitEffect(target, skillId);
            _effects.Add(effect);
            return effect;
        }

        /// <summary>战斗日志用的单行描述。刻意不在这里拼接给玩家看的文案，只写诊断信息。</summary>
        public override string ToString()
        {
            if (!Success)
            {
                return $"[拒绝 {Rejection}] {Actor?.DefinitionId}";
            }

            var builder = new StringBuilder();
            builder.Append(Actor?.DefinitionId ?? "?").Append(" 执行 ")
                .Append(SkillId ?? Kind.ToString());

            for (var i = 0; i < _effects.Count; i++)
            {
                builder.Append(" | ").Append(_effects[i]);
            }

            return builder.ToString();
        }
    }
}
