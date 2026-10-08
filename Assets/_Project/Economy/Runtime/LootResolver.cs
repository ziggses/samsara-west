using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Economy
{
    /// <summary>
    /// 把一场胜利变成 <see cref="BattleSpoils"/>：读敌人的赏金与掉落表，按权重掷出东西。
    /// </summary>
    /// <remarks>
    /// <para>它是<b>纯函数</b>：给定同样的遭遇、同样的数据目录、同样的随机流，必然得到同样的战利品。
    /// 所以它不碰服务注册表、不写日志、不改任何状态——「掷出来什么」与「记到账上」是两件事，
    /// 这里只做前者。后者在组合根的接线里（<c>Flow/LootBattleLink</c>）。</para>
    ///
    /// <para><b>掷骰顺序是有意定死的</b>：按 <c>enemyIds</c> 的出现顺序逐只敌人结算，
    /// 每只敌人先「必掉」再「按权重抽 dropRolls 次」。顺序一旦变了，同一种子掷出的东西就变了——
    /// 而「同一种子必然同一结果」正是这套随机服务存在的理由（《架构决策》ADR-005）。
    /// 因此这里既排序、也不做任何「顺手合并」的优化。</para>
    ///
    /// <para><b>坏数据只跳过那一条</b>：敌人查不到、掉落表查不到，都记进 <c>report</c> 并继续算下一只，
    /// 而不是让一处配置错误把整场结算炸掉（同 <c>BattleFactory</c> 对在身装备的处理）。</para>
    /// </remarks>
    public static class LootResolver
    {
        /// <summary>
        /// 结算一场胜仗。
        /// </summary>
        /// <param name="encounter">打的那场遭遇；编成与顺序都从它来。</param>
        /// <param name="definitions">定义目录，用来查敌人与掉落表。</param>
        /// <param name="stream">掉落流（用 <c>RandomStreams.Loot</c>）；为 null 表示不掷骰，只发固定的赏金。</param>
        /// <param name="report">数据问题的收集处；为 null 时问题只被跳过，不留痕。</param>
        public static BattleSpoils Resolve(
            EncounterDefinition encounter,
            IDefinitionRegistry definitions,
            IRandomStream stream = null,
            ValidationReport report = null)
        {
            if (encounter == null)
            {
                throw new ArgumentNullException(nameof(encounter));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var enemyIds = encounter.EnemyIds;
            if (enemyIds.Length == 0)
            {
                return BattleSpoils.None;
            }

            var gold = 0;
            var experience = 0;
            var items = new List<ItemStack>();

            for (var i = 0; i < enemyIds.Length; i++)
            {
                var enemyId = enemyIds[i];
                if (string.IsNullOrWhiteSpace(enemyId))
                {
                    continue;
                }

                if (!definitions.TryGet(enemyId, out DefinitionBase found) || !(found is EnemyDefinition enemy))
                {
                    report?.Error(
                        "SPOILS_ENEMY_MISSING",
                        $"编成里的 '{enemyId}' 在定义目录里找不到或不是敌人，这一只不参与结算。",
                        encounter.Id,
                        fieldName: "enemyIds");
                    continue;
                }

                // 两套金钱来源都发：敌人身上的赏金是一份，掉落表里的钱区间是另一份（关系见 ADR-027）。
                gold += enemy.GoldReward;
                experience += enemy.ExperienceReward;

                if (string.IsNullOrWhiteSpace(enemy.LootTableId))
                {
                    continue;
                }

                if (!definitions.TryGet(enemy.LootTableId, out DefinitionBase tableFound)
                    || !(tableFound is LootTableDefinition table))
                {
                    report?.Error(
                        "SPOILS_LOOT_TABLE_MISSING",
                        $"敌人 '{enemyId}' 指向的掉落表 '{enemy.LootTableId}' 在定义目录里找不到或不是掉落表。",
                        enemyId,
                        fieldName: "lootTableId");
                    continue;
                }

                gold += RollGold(table, stream);
                RollItems(table, stream, items);
            }

            if (gold == 0 && experience == 0 && items.Count == 0)
            {
                return BattleSpoils.None;
            }

            return new BattleSpoils(gold, experience, items);
        }

        /// <summary>掷掉落表里的钱区间。表里没配钱（上限为 0）或没有流时返回 0。</summary>
        private static int RollGold(LootTableDefinition table, IRandomStream stream)
        {
            var min = table.GoldMin;
            var max = table.GoldMax;

            if (max <= 0)
            {
                return 0;
            }

            if (min < 0)
            {
                min = 0;
            }

            if (max < min)
            {
                max = min;
            }

            if (stream == null || min == max)
            {
                // 没有流时取区间下限：宁可少发，也不去借别的流掷一发（那会污染另一条序列）。
                return min;
            }

            return stream.NextInt(min, max + 1);
        }

        /// <summary>先发必掉，再按权重抽 dropRolls 次。抽到什么由传进来的流决定。</summary>
        private static void RollItems(LootTableDefinition table, IRandomStream stream, List<ItemStack> into)
        {
            var guaranteed = table.GuaranteedItemIds;
            var guaranteedCounts = table.GuaranteedItemCounts;
            for (var i = 0; i < guaranteed.Length; i++)
            {
                var itemId = guaranteed[i];
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                // 两个数组等长由导入期校验（LUT_ARRAY_LENGTH）；这里只做防御，不让越界变成异常。
                var count = i < guaranteedCounts.Length ? guaranteedCounts[i] : 1;
                if (count <= 0)
                {
                    continue;
                }

                into.Add(new ItemStack(itemId, count));
            }

            var rolls = table.DropRolls;
            if (rolls <= 0 || stream == null)
            {
                return;
            }

            var itemIds = table.ItemIds;
            var weights = table.Weights;
            var minCounts = table.MinCounts;
            var maxCounts = table.MaxCounts;

            for (var roll = 0; roll < rolls; roll++)
            {
                var total = 0;
                for (var i = 0; i < weights.Length; i++)
                {
                    if (weights[i] > 0)
                    {
                        total += weights[i];
                    }
                }

                if (total <= 0)
                {
                    // 配了抽取次数却没有一条正权重（导入期会报 LUT_NO_WEIGHT）：没有可抽的东西，停手。
                    return;
                }

                var index = PickWeightedIndex(itemIds, weights, total, stream.NextInt(total));
                if (index < 0)
                {
                    return;
                }

                var count = RollCount(minCounts, maxCounts, index, stream);
                if (count > 0)
                {
                    into.Add(new ItemStack(itemIds[index], count));
                }
            }
        }

        /// <summary>
        /// 按权重落区间。权重与 ID 等长（导入期校验）；越界的项当作 0 权重跳过。
        /// </summary>
        private static int PickWeightedIndex(string[] itemIds, int[] weights, int total, int draw)
        {
            var cursor = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0 || i >= itemIds.Length || string.IsNullOrWhiteSpace(itemIds[i]))
                {
                    continue;
                }

                cursor += weights[i];
                if (draw < cursor)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>掷这一条的数量区间。区间非法时退回下限，下限非法时退回 1。</summary>
        private static int RollCount(int[] minCounts, int[] maxCounts, int index, IRandomStream stream)
        {
            var min = index < minCounts.Length ? minCounts[index] : 1;
            var max = index < maxCounts.Length ? maxCounts[index] : min;

            if (min < 1)
            {
                min = 1;
            }

            if (max < min)
            {
                max = min;
            }

            return min == max ? min : stream.NextInt(min, max + 1);
        }
    }
}
