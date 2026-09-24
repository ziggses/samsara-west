using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 一个单位的意图预告：它下一步打算用哪个技能、打谁。
    /// </summary>
    /// <remarks>
    /// 预告必须<b>守信</b>——玩家看见「妖啸·全体」就要真的吃到妖啸。
    /// 因此意图不是界面自己猜的，而是内核算出来并缓存的那一份，敌方行动时执行的就是它。
    /// 唯一的例外是 <see cref="TargetRule.RandomEnemy"/>：随机目标原则上预告不了，
    /// 此时 <see cref="TargetIsRandom"/> 为真、<see cref="TargetRuntimeIds"/> 为空，
    /// 界面如实显示「随机一名」，而不是假装知道打谁。
    /// </remarks>
    public readonly struct BattleIntent
    {
        private static readonly int[] NoTargets = Array.Empty<int>();

        internal BattleIntent(
            BattleUnit actor,
            SkillDefinition skill,
            IReadOnlyList<int> targetRuntimeIds,
            bool targetIsRandom)
        {
            ActorRuntimeId = actor?.RuntimeId ?? -1;
            ActorDefinitionId = actor?.DefinitionId;
            ActorDisplayNameKey = actor?.DisplayNameKey;
            ActorSide = actor?.Side ?? BattleSide.Enemy;
            SkillId = skill?.Id;
            SkillDisplayNameKey = skill?.DisplayNameKey;
            TargetRule = skill?.Target ?? TargetRule.Self;
            TargetRuntimeIds = targetRuntimeIds ?? NoTargets;
            TargetIsRandom = targetIsRandom;
        }

        public int ActorRuntimeId { get; }

        public string ActorDefinitionId { get; }

        public string ActorDisplayNameKey { get; }

        public BattleSide ActorSide { get; }

        public string SkillId { get; }

        public string SkillDisplayNameKey { get; }

        public TargetRule TargetRule { get; }

        public IReadOnlyList<int> TargetRuntimeIds { get; }

        /// <summary>目标随机、无法预告。</summary>
        public bool TargetIsRandom { get; }

        /// <summary>是否有可执行的意图。被控住或没有任何可用技能时为 false。</summary>
        public bool HasIntent => !string.IsNullOrEmpty(SkillId);

        /// <summary>空意图：被控住，或者无技能可用。</summary>
        public static BattleIntent None(BattleUnit actor) => new BattleIntent(actor, null, NoTargets, false);

        public override string ToString()
        {
            if (!HasIntent)
            {
                return $"{ActorDefinitionId ?? "?"} 无意图";
            }

            return $"{ActorDefinitionId} 意图 {SkillId}（{TargetRule}）→ " +
                   (TargetIsRandom ? "随机" : string.Join(",", TargetRuntimeIds));
        }
    }

    /// <summary>
    /// 一份可执行的行动方案：用哪个技能、以谁为主目标。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="BattleIntent"/> 分开是因为两者受众不同：
    /// 意图是给玩家看的（只要 ID 与文本键），方案是给引擎执行的（需要单位引用本体）。
    /// </remarks>
    public readonly struct BattlePlan
    {
        internal BattlePlan(BattleIntent intent, BattleUnit primaryTarget)
        {
            Intent = intent;
            PrimaryTarget = primaryTarget;
        }

        public BattleIntent Intent { get; }

        /// <summary>执行时使用的主目标；全体技能与随机技能可能为空。</summary>
        public BattleUnit PrimaryTarget { get; }

        public bool HasPlan => Intent.HasIntent;

        public static BattlePlan Empty(BattleUnit actor) => new BattlePlan(BattleIntent.None(actor), null);
    }

    /// <summary>
    /// 行动规划器。给定一个单位与当前战场，选出它这一手要放的技能与主目标。
    /// </summary>
    /// <remarks>
    /// 它同时服务两件事：敌方单位的<b>意图预告</b>（以及敌方回合的自动执行），
    /// 以及测试／原型里的<b>自动代打</b>（让整场战斗不接界面也能跑完）。
    /// 因此它不叫「敌方 AI」——它对双方一视同仁，谁是敌人由单位的 <see cref="BattleUnit.Side"/> 决定。
    ///
    /// <b>必须是纯函数</b>——不掷随机、不改状态。因为意图会在每次状态变化后重算
    /// （玩家一动，敌人的最优解就可能变了），若重算顺手消耗随机数，
    /// 「同种子同结果」这条底线会被查询次数带偏。
    ///
    /// 选招口径：先把每个可用技能的<b>确定伤害</b>（暴击掷骰取「必定不暴击」）算一遍，
    /// 取总伤害最高者；伤害都为 0 时取治疗量最高者；仍然为 0 则取技能表里第一个能用的。
    /// 这不追求聪明，只要求<b>可解释</b>——五个人的试玩要判断的是意图能不能被读懂，
    /// 不是敌人会不会最优解。
    /// </remarks>
    public static class BattleActionPlanner
    {
        /// <summary>为一个单位规划当前行动。</summary>
        public static BattlePlan Plan(
            BattleConfig config,
            IDefinitionRegistry registry,
            BattleUnit actor,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> targetScratch)
        {
            if (config == null || actor == null || !actor.IsAlive || units == null)
            {
                return BattlePlan.Empty(actor);
            }

            // 被控住的单位这一步什么都做不了，预告里显示「无意图」才是诚实的。
            if (actor.IsActionPrevented)
            {
                return BattlePlan.Empty(actor);
            }

            SkillDefinition bestSkill = null;
            BattleUnit bestPrimary = null;
            var bestDamage = 0;
            var bestHeal = 0;

            SkillDefinition firstUsable = null;
            BattleUnit firstUsablePrimary = null;

            var skillIds = actor.SkillIds;
            for (var i = 0; i < skillIds.Count; i++)
            {
                var skillId = skillIds[i];
                var skill = ResolveSkill(registry, skillId);
                if (skill == null)
                {
                    continue;
                }

                if (actor.GetCooldown(skillId) > 0)
                {
                    continue;
                }

                if (actor.UsesSpirit && actor.Spirit < skill.SpiritCost)
                {
                    continue;
                }

                var primary = BattleTargeting.ChoosePrimaryForAi(actor, skill, units);
                if (BattleTargeting.NeedsCallerTarget(skill.Target) && primary == null)
                {
                    continue;
                }

                var targets = BattleTargeting.Resolve(actor, skill, primary, units, targetScratch);
                if (targets.Count == 0)
                {
                    continue;
                }

                if (firstUsable == null)
                {
                    firstUsable = skill;
                    firstUsablePrimary = primary;
                }

                var damage = 0;
                var heal = 0;
                for (var t = 0; t < targets.Count; t++)
                {
                    var target = targets[t];
                    if (skill.Power > 0)
                    {
                        damage += DamageCalculator.ComputeSkillTotal(
                            config,
                            skill.Power,
                            actor.EffectiveAttack,
                            target.EffectiveDefense,
                            skill.HitCount,
                            skill.Element,
                            target.Element,
                            target.IsBroken,
                            target.MaxHealth,
                            actor.AttackModifier,
                            target.DefenseModifier);
                    }

                    if (skill.HealPower > 0)
                    {
                        heal += Math.Max(0, Math.Min(skill.HealPower, target.MaxHealth - target.Health));
                    }
                }

                if (bestSkill == null || damage > bestDamage || (damage == bestDamage && heal > bestHeal))
                {
                    bestSkill = skill;
                    bestPrimary = primary;
                    bestDamage = damage;
                    bestHeal = heal;
                }
            }

            if (bestSkill == null)
            {
                return BattlePlan.Empty(actor);
            }

            // 伤害与治疗都为 0 时（纯增益、或者没人受伤的治疗），退回「第一个能用的技能」，
            // 至少不会让一个还有牌可打的单位呆站着。
            if (bestDamage == 0 && bestHeal == 0 && firstUsable != null)
            {
                bestSkill = firstUsable;
                bestPrimary = firstUsablePrimary;
            }

            var finalTargets = BattleTargeting.Resolve(actor, bestSkill, bestPrimary, units, targetScratch);
            var isRandom = bestSkill.Target == TargetRule.RandomEnemy;

            int[] ids;
            if (isRandom)
            {
                ids = Array.Empty<int>();
            }
            else
            {
                ids = new int[finalTargets.Count];
                for (var i = 0; i < finalTargets.Count; i++)
                {
                    ids[i] = finalTargets[i].RuntimeId;
                }
            }

            return new BattlePlan(new BattleIntent(actor, bestSkill, ids, isRandom), bestPrimary);
        }

        /// <summary>只取意图（不关心执行用的单位引用）。</summary>
        public static BattleIntent PlanIntent(
            BattleConfig config,
            IDefinitionRegistry registry,
            BattleUnit actor,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> targetScratch) =>
            Plan(config, registry, actor, units, targetScratch).Intent;

        /// <summary>技能表里的 ID 解析，顺带把「引用了不存在的技能」记进日志。</summary>
        internal static SkillDefinition ResolveSkill(IDefinitionRegistry registry, string skillId)
        {
            if (registry == null || string.IsNullOrEmpty(skillId))
            {
                return null;
            }

            if (registry.TryGet(skillId, out SkillDefinition skill) && skill != null)
            {
                return skill;
            }

            GameLog.Warn(LogChannel.Battle, $"技能 '{skillId}' 在数据表里不存在，已跳过。", skillId);
            return null;
        }
    }
}
