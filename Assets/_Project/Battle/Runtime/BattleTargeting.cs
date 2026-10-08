using System.Collections.Generic;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 目标规则的解释器。技能表里的 <see cref="TargetRule"/> 只声明「打谁」，
    /// 把它翻译成一组具体单位是这里唯一的职责。
    /// </summary>
    /// <remarks>
    /// 这里是纯函数、<b>不掷随机</b>。随机目标（<see cref="TargetRule.RandomEnemy"/>）由调用方
    /// 先从「战斗」随机流里抽出一个目标再传进来，原因有两条：
    /// 一是敌方意图要在每次状态变化后重算，若重算就消耗随机数，随机序列会被查询次数带偏；
    /// 二是纯函数才能被穷举测试。
    ///
    /// 命中集合一律按 <see cref="BattleUnit.FormationIndex"/> 升序返回。
    /// 顺序即「多段技能逐段掷骰」的顺序，因此它必须确定，否则同种子也重放不出同结果。
    /// </remarks>
    public static class BattleTargeting
    {
        /// <summary>该规则是否必须由调用方指定一个目标（或一个中心点）。</summary>
        public static bool NeedsCallerTarget(TargetRule rule) =>
            rule == TargetRule.SingleEnemy ||
            rule == TargetRule.SingleAlly ||
            rule == TargetRule.Column ||
            rule == TargetRule.Row;

        /// <summary>
        /// 主目标是否必须站在敌对阵营。
        /// </summary>
        /// <remarks>
        /// 列／排规则本身不含敌我信息——「打那一列」既可以打敌人那一列，也可以给自己人那一列加盾，
        /// 所以它读技能表里的显式字段 <see cref="SkillDefinition.HostileOnly"/>。
        /// 其余规则由 <see cref="TargetRule"/> 直接决定阵营，这个字段对它们无效
        /// （误配时由 <c>SKL_HOSTILE_REDUNDANT</c> 报出来，而不是静默生效）。
        /// </remarks>
        public static bool RequiresHostilePrimary(SkillDefinition skill)
        {
            if (skill == null)
            {
                return false;
            }

            switch (skill.Target)
            {
                case TargetRule.SingleEnemy:
                case TargetRule.RandomEnemy:
                    return true;

                case TargetRule.SingleAlly:
                    return false;

                case TargetRule.Column:
                case TargetRule.Row:
                    return skill.HostileOnly;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 求命中集合。
        /// </summary>
        /// <param name="actor">施法者，决定「敌我」的参照。</param>
        /// <param name="skill">技能定义。</param>
        /// <param name="primary">
        /// 主目标。单体与列／排规则必须有；<see cref="TargetRule.RandomEnemy"/> 时是已经抽好的那一个。
        /// </param>
        /// <param name="units">场上全部单位（含已倒下者，内部会过滤）。</param>
        /// <param name="sink">结果容器，由调用方复用以免每次行动都分配。</param>
        /// <returns>命中集合，可能为空（例如对面已经全灭）。</returns>
        public static IReadOnlyList<BattleUnit> Resolve(
            BattleUnit actor,
            SkillDefinition skill,
            BattleUnit primary,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> sink)
        {
            sink.Clear();
            if (actor == null || skill == null || units == null)
            {
                return sink;
            }

            switch (skill.Target)
            {
                case TargetRule.Self:
                    if (actor.IsAlive)
                    {
                        sink.Add(actor);
                    }

                    break;

                case TargetRule.AllEnemies:
                    CollectSide(actor, units, hostile: true, sink);
                    break;

                case TargetRule.AllAllies:
                    CollectSide(actor, units, hostile: false, sink);
                    break;

                case TargetRule.SingleEnemy:
                case TargetRule.RandomEnemy:
                    if (primary != null && primary.IsAlive && primary.Side != actor.Side)
                    {
                        sink.Add(primary);
                    }

                    break;

                case TargetRule.SingleAlly:
                    if (primary != null && primary.IsAlive && primary.Side == actor.Side)
                    {
                        sink.Add(primary);
                    }

                    break;

                case TargetRule.Column:
                    CollectLine(actor, primary, units, byColumn: true, sink);
                    break;

                case TargetRule.Row:
                    CollectLine(actor, primary, units, byColumn: false, sink);
                    break;
            }

            return sink;
        }

        /// <summary>
        /// 该规则的主目标是否需要在规划时<b>逐一试算</b>。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="NeedsCallerTarget"/> 的差别只在随机目标上：随机技能的落点由执行时抽签决定，
        /// 但规划阶段仍要把每个可能的落点试算一遍才知道这一手值多少威胁
        /// （<see cref="Resolve"/> 拿不到主目标就返回空集，那样任何随机技能都会被算成「打不出去」）。
        /// 自身与全体目标不需要试算，用 <c>null</c> 主目标评估一次即可。
        /// </remarks>
        public static bool TriesPrimaryPerTarget(TargetRule rule) =>
            NeedsCallerTarget(rule) || rule == TargetRule.RandomEnemy;

        /// <summary>
        /// 列出这个技能所有合法的主目标候选，按 <see cref="BattleUnit.RuntimeId"/> 升序。
        /// </summary>
        /// <remarks>
        /// 顺序就是威胁评估的破平局顺序（口径：同值时取 <c>RuntimeId</c> 最小），
        /// 所以调用方只要「严格优于才替换」，平局自然落到先出现的那一个。
        /// </remarks>
        /// <param name="actor">施法者。</param>
        /// <param name="skill">技能定义。</param>
        /// <param name="units">场上全部单位（含已倒下者，内部会过滤）。</param>
        /// <param name="sink">结果容器，由调用方复用；进入时会被清空。</param>
        public static void CollectCandidatePrimaries(
            BattleUnit actor,
            SkillDefinition skill,
            IReadOnlyList<BattleUnit> units,
            List<BattleUnit> sink)
        {
            sink.Clear();
            if (actor == null || skill == null || units == null)
            {
                return;
            }

            var wantHostile = RequiresHostilePrimary(skill);
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                if ((unit.Side != actor.Side) != wantHostile)
                {
                    continue;
                }

                sink.Add(unit);
            }

            sink.Sort(static (left, right) => left.RuntimeId.CompareTo(right.RuntimeId));
        }

        private static void CollectSide(BattleUnit actor, IReadOnlyList<BattleUnit> units, bool hostile, List<BattleUnit> sink)
        {
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                if ((unit.Side != actor.Side) == hostile)
                {
                    sink.Add(unit);
                }
            }

            SortByFormation(sink);
        }

        private static void CollectLine(
            BattleUnit actor,
            BattleUnit primary,
            IReadOnlyList<BattleUnit> units,
            bool byColumn,
            List<BattleUnit> sink)
        {
            if (primary == null || !primary.IsAlive)
            {
                return;
            }

            // 命中范围落在「主目标所在的那一侧」：打敌人就是打敌人那一列，
            // 这样治疗／增益类的列技能将来也能复用同一条规则，不必再加一个枚举值。
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive || unit.Side != primary.Side)
                {
                    continue;
                }

                var sameLine = byColumn
                    ? unit.Slot.Column == primary.Slot.Column
                    : unit.Slot.Row == primary.Slot.Row;

                if (sameLine)
                {
                    sink.Add(unit);
                }
            }

            SortByFormation(sink);
        }

        private static void SortByFormation(List<BattleUnit> units)
        {
            if (units.Count < 2)
            {
                return;
            }

            units.Sort(static (left, right) =>
            {
                var byFormation = left.FormationIndex.CompareTo(right.FormationIndex);
                return byFormation != 0 ? byFormation : left.RuntimeId.CompareTo(right.RuntimeId);
            });
        }
    }
}
