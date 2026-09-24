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
        /// 列／排规则本身不含敌我信息——「打那一列」既可以打敌人那一列，也可以给自己人那一列加盾。
        /// 骨架期的判据是「<see cref="SkillDefinition.Power"/> 大于 0 即为攻击技」，因此以敌营为中心；
        /// 将来若出现「给同排加护盾」这类技能，就需要给技能表补一个显式的敌我字段，
        /// 而不是继续在这里加特例（已登记在 Docs/战斗内核-v1.md 的遗留清单）。
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
                    return skill.Power > 0;

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
        /// 给 AI 挑一个主目标。规则刻意简单且确定：
        /// 攻击类取<b>当前生命最高</b>的敌对单位（与 Docs/战斗数值-v1.md 里的回合数校算口径一致，
        /// 也就是说校算出来的回合数在这场战斗里真的会发生），治疗类取生命比例最低的同伴。
        /// </summary>
        public static BattleUnit ChoosePrimaryForAi(BattleUnit actor, SkillDefinition skill, IReadOnlyList<BattleUnit> units)
        {
            if (actor == null || skill == null || units == null)
            {
                return null;
            }

            if (skill.Target == TargetRule.Self)
            {
                return actor;
            }

            if (skill.Target == TargetRule.AllEnemies || skill.Target == TargetRule.AllAllies)
            {
                return null;
            }

            var wantHostile = RequiresHostilePrimary(skill);
            BattleUnit best = null;
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                var isHostile = unit.Side != actor.Side;
                if (isHostile != wantHostile)
                {
                    continue;
                }

                if (best == null || IsBetterPrimary(unit, best, preferWounded: !wantHostile))
                {
                    best = unit;
                }
            }

            return best;
        }

        private static bool IsBetterPrimary(BattleUnit candidate, BattleUnit current, bool preferWounded)
        {
            if (preferWounded)
            {
                if (candidate.HealthRatio != current.HealthRatio)
                {
                    return candidate.HealthRatio < current.HealthRatio;
                }
            }
            else if (candidate.Health != current.Health)
            {
                return candidate.Health > current.Health;
            }

            return candidate.FormationIndex < current.FormationIndex;
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
