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
    /// 选招口径是<b>威胁评估</b>：对每个「可用技能 × 合法主目标」的候选算一个威胁值，取最高的那一个。
    /// 威胁 = 伤害期望 × 伤害权重 + 治疗量 × 治疗权重 + 命中集合里敌对单位的有效速度 × 速度权重，
    /// 三项权重都在 <see cref="BattleConfig"/> 里（速度默认关）。伤害与治疗都是<b>确定性估算</b>
    /// （暴击按「必定不暴击」算，理由与「不掷随机」是同一条）。威胁全为 0 时（纯增益、没人受伤的治疗）
    /// 由破平局规则自然退回「技能表里第一个能用的」，不会让还有牌可打的单位呆站着。
    /// 这不追求聪明，只要求<b>可解释</b>——五个人的试玩要判断的是意图能不能被读懂，
    /// 不是敌人会不会最优解。
    /// </remarks>
    public static class BattleActionPlanner
    {
        /// <summary>为一个单位规划当前行动。</summary>
        /// <param name="primaryScratch">
        /// 枚举候选主目标用的容器，由调用方复用；不传就自己开一个（只在测试里会走到）。
        /// </param>
        public static BattlePlan Plan(
            BattleConfig config,
            IDefinitionRegistry registry,
            BattleUnit actor,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> targetScratch,
            List<BattleUnit> primaryScratch = null)
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

            var primaries = primaryScratch ?? new List<BattleUnit>(FormationSlot.Capacity);

            SkillDefinition bestSkill = null;
            BattleUnit bestPrimary = null;
            var bestThreat = 0f;
            var hasBest = false;

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

                float threat;
                if (BattleTargeting.TriesPrimaryPerTarget(skill.Target))
                {
                    BattleTargeting.CollectCandidatePrimaries(actor, skill, units, primaries);
                    for (var p = 0; p < primaries.Count; p++)
                    {
                        if (!TryMeasureThreat(config, actor, skill, primaries[p], units, targetScratch, out threat))
                        {
                            continue;
                        }

                        if (IsBetterCandidate(threat, primaries[p], bestThreat, bestPrimary, hasBest))
                        {
                            bestSkill = skill;
                            bestPrimary = primaries[p];
                            bestThreat = threat;
                            hasBest = true;
                        }
                    }
                }
                else
                {
                    // 自身技能的主目标就是自己；全体技能没有主目标。两者都只评估一次。
                    var primary = skill.Target == TargetRule.Self ? actor : null;
                    if (TryMeasureThreat(config, actor, skill, primary, units, targetScratch, out threat)
                        && IsBetterCandidate(threat, primary, bestThreat, bestPrimary, hasBest))
                    {
                        bestSkill = skill;
                        bestPrimary = primary;
                        bestThreat = threat;
                        hasBest = true;
                    }
                }
            }

            if (bestSkill == null)
            {
                return BattlePlan.Empty(actor);
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

        /// <summary>
        /// 算一个「技能 × 主目标」候选的威胁值。
        /// </summary>
        /// <remarks>
        /// 公式：伤害期望 × <see cref="BattleConfig.ThreatWeightDamage"/>
        /// + 治疗量 × <see cref="BattleConfig.ThreatWeightHeal"/>
        /// + 命中集合里敌对单位的有效速度之和 × <see cref="BattleConfig.ThreatWeightSpeed"/>。
        /// 速度那一项只算<b>敌对</b>单位：加速自己人不是「威胁」，去打一个跑得快的敌人才是。
        /// 治疗量按「最多回满」计，所以给满血同伴加血算 0 威胁——这是有意的，
        /// 界面上的意图读出来才符合直觉。
        /// 返回 false 表示这一手打不出去（命中集合为空，例如对面已经全灭）。
        /// </remarks>
        private static bool TryMeasureThreat(
            BattleConfig config,
            BattleUnit actor,
            SkillDefinition skill,
            BattleUnit primary,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> targetScratch,
            out float threat)
        {
            threat = 0f;

            var targets = BattleTargeting.Resolve(actor, skill, primary, units, targetScratch);
            if (targets.Count == 0)
            {
                return false;
            }

            var damage = 0;
            var heal = 0;
            var hostileSpeed = 0;
            for (var t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                if (target.Side != actor.Side)
                {
                    hostileSpeed += target.EffectiveSpeed;
                }

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

            threat = (damage * config.ThreatWeightDamage)
                     + (heal * config.ThreatWeightHeal)
                     + (hostileSpeed * config.ThreatWeightSpeed);
            return true;
        }

        /// <summary>
        /// 新候选是否压过当前最优。
        /// </summary>
        /// <remarks>
        /// 判据依次是：<b>威胁值更大</b> → <b>主目标 <c>RuntimeId</c> 更小</b> → <b>技能更靠前</b>。
        /// 后两级由枚举顺序兜住：候选按 <c>RuntimeId</c> 升序（见
        /// <see cref="BattleTargeting.CollectCandidatePrimaries"/>）、技能按 <c>SkillIds</c> 顺序，
        /// 再加上「严格优于才替换」，平局就自然落到先枚举到的那一个。
        /// 无主目标的技能（自身／全体）记 -1，平局时排在具体目标之前。
        /// </remarks>
        private static bool IsBetterCandidate(
            float threat,
            BattleUnit primary,
            float bestThreat,
            BattleUnit bestPrimary,
            bool hasBest)
        {
            if (!hasBest || threat > bestThreat)
            {
                return true;
            }

            return threat >= bestThreat && PrimaryOrder(primary) < PrimaryOrder(bestPrimary);
        }

        private static int PrimaryOrder(BattleUnit unit) => unit == null ? -1 : unit.RuntimeId;

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
