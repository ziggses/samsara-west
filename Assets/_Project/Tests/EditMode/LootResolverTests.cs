using System.Linq;
using NUnit.Framework;
using SamsaraWest.Data;
using SamsaraWest.Economy;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 掉落结算：一定发什么、最多发多少、同一种子给不给同一份。
    /// </summary>
    /// <remarks>
    /// 用例只断言<b>能被反证</b>的性质——「必掉的东西一定在」「抽取次数决定条数」「同种子同结果」——
    /// 而不是某一次掷骰的精确结果（那等于把随机数发生器的实现抄进用例，
    /// 换个种子就全红，却什么都没验到）。需要精确值时，就把区间写成上下限相等。
    /// </remarks>
    internal sealed class LootResolverTests
    {
        [Test]
        public void EnemyWithNoLootTable_OnlyPaysItsOwnRewards()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_BANDIT", 30, 8, 3, 6);
            BattleLab.SetLoot(enemy, null, goldReward: 8, experienceReward: 12);
            var encounter = lab.Encounter("ENC_ROAD", "ENM_BANDIT");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(8, spoils.Gold);
            Assert.AreEqual(12, spoils.Experience);
            Assert.AreEqual(0, spoils.Items.Count);
        }

        [Test]
        public void EncounterWithNoEnemies_YieldsNothing()
        {
            using var lab = new BattleLab();

            var spoils = LootResolver.Resolve(lab.Encounter("ENC_EMPTY"), lab.Registry(), BattleLab.LootStream());

            Assert.IsTrue(spoils.IsEmpty);
            Assert.AreSame(BattleSpoils.None, spoils);
        }

        [Test]
        public void GuaranteedItems_AlwaysDropWithTheirCounts()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                guaranteedItemIds: new[] { "ITM_BONE", "ITM_HERB" },
                guaranteedItemCounts: new[] { 2, 1 });
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(2, spoils.Items.Count);
            Assert.AreEqual("ITM_BONE", spoils.Items[0].ItemId);
            Assert.AreEqual(2, spoils.Items[0].Count);
            Assert.AreEqual("ITM_HERB", spoils.Items[1].ItemId);
            Assert.AreEqual(1, spoils.Items[1].Count);
        }

        [Test]
        public void GuaranteedItems_DropEvenWithoutAStream()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                guaranteedItemIds: new[] { "ITM_BONE" },
                guaranteedItemCounts: new[] { 3 },
                goldMin: 5,
                goldMax: 9);
            BattleLab.SetLoot(enemy, "LUT_WOLF", goldReward: 4);
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), stream: null);

            Assert.AreEqual(1, spoils.Items.Count, "必掉的东西不需要掷骰。");
            Assert.AreEqual(3, spoils.Items[0].Count);
            Assert.AreEqual(4 + 5, spoils.Gold, "没有流时金钱取区间下限，而不是借别的流掷一发。");
        }

        [Test]
        public void DropRolls_DecideHowManyTimesTheTableIsDrawn()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB" },
                weights: new[] { 1 },
                minCounts: new[] { 1 },
                maxCounts: new[] { 1 },
                dropRolls: 3);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(3, spoils.Items.Count, "抽三次就该留下三条记录。");
            Assert.IsTrue(spoils.Items.All(stack => stack.ItemId == "ITM_HERB"));
        }

        [Test]
        public void DuplicateDraws_AreKeptAsSeparateStacksInsteadOfBeingMerged()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB" },
                weights: new[] { 1 },
                minCounts: new[] { 2 },
                maxCounts: new[] { 2 },
                dropRolls: 2);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(2, spoils.Items.Count, "抽到两次是两条记录，合并是结算时的事，不是掷骰时的事。");
            Assert.AreEqual(2, spoils.Items[0].Count);
            Assert.AreEqual(2, spoils.Items[1].Count);
        }

        [Test]
        public void ZeroWeightEntries_AreNeverDrawn()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_NEVER", "ITM_ALWAYS" },
                weights: new[] { 0, 1 },
                minCounts: new[] { 1, 1 },
                maxCounts: new[] { 1, 1 },
                dropRolls: 12);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(12, spoils.Items.Count);
            Assert.IsFalse(
                spoils.Items.Any(stack => stack.ItemId == "ITM_NEVER"),
                "权重为 0 的条目永远不该被抽中。");
        }

        [Test]
        public void DrawCounts_StayWithinTheirRange()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB" },
                weights: new[] { 1 },
                minCounts: new[] { 2 },
                maxCounts: new[] { 5 },
                dropRolls: 24);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            foreach (var stack in spoils.Items)
            {
                Assert.GreaterOrEqual(stack.Count, 2);
                Assert.LessOrEqual(stack.Count, 5);
            }
        }

        [Test]
        public void TableGold_StaysWithinItsRange()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable("LUT_WOLF", goldMin: 3, goldMax: 8);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");
            var registry = lab.Registry();

            // 掷很多次，每次都该落在区间里（单次断言只能证明「碰巧」）。
            for (ulong seed = 1; seed <= 64; seed++)
            {
                var spoils = LootResolver.Resolve(encounter, registry, BattleLab.LootStream(seed));

                Assert.GreaterOrEqual(spoils.Gold, 3, $"种子 {seed} 掉出了区间下限。");
                Assert.LessOrEqual(spoils.Gold, 8, $"种子 {seed} 掉出了区间上限。");
            }
        }

        [Test]
        public void BothGoldSourcesArePaid()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_BANDIT", 30, 8, 3, 6);
            lab.LootTable("LUT_BANDIT", goldMin: 7, goldMax: 7);
            BattleLab.SetLoot(enemy, "LUT_BANDIT", goldReward: 8);
            var encounter = lab.Encounter("ENC_ROAD", "ENM_BANDIT");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(
                15,
                spoils.Gold,
                "敌人赏金与掉落表的钱都要发：两列都有真人填的值，也都没写明彼此覆盖（ADR-027）。");
        }

        [Test]
        public void EveryEnemyInTheEncounterContributes()
        {
            using var lab = new BattleLab();
            var first = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            var second = lab.Enemy("ENM_BANDIT", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                guaranteedItemIds: new[] { "ITM_BONE" },
                guaranteedItemCounts: new[] { 1 });
            lab.LootTable(
                "LUT_BANDIT",
                guaranteedItemIds: new[] { "ITM_HERB" },
                guaranteedItemCounts: new[] { 1 });
            BattleLab.SetLoot(first, "LUT_WOLF", goldReward: 6, experienceReward: 12);
            BattleLab.SetLoot(second, "LUT_BANDIT", goldReward: 8, experienceReward: 15);
            var encounter = lab.Encounter("ENC_ROAD", "ENM_WOLF", "ENM_BANDIT");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(14, spoils.Gold);
            Assert.AreEqual(27, spoils.Experience);
            Assert.AreEqual(2, spoils.Items.Count);
            Assert.AreEqual("ITM_BONE", spoils.Items[0].ItemId, "顺序跟着编成走，不跟着字典走。");
            Assert.AreEqual("ITM_HERB", spoils.Items[1].ItemId);
        }

        [Test]
        public void SameSeed_ProducesTheSameSpoils()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB", "ITM_BONE", "ITM_PEACH" },
                weights: new[] { 5, 3, 2 },
                minCounts: new[] { 1, 1, 1 },
                maxCounts: new[] { 3, 2, 1 },
                dropRolls: 6,
                goldMin: 4,
                goldMax: 12);
            BattleLab.SetLoot(enemy, "LUT_WOLF", goldReward: 8, experienceReward: 12);
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");
            var registry = lab.Registry();

            var first = LootResolver.Resolve(encounter, registry, BattleLab.LootStream());
            var second = LootResolver.Resolve(encounter, registry, BattleLab.LootStream());

            Assert.AreEqual(first.Gold, second.Gold);
            Assert.AreEqual(first.Experience, second.Experience);
            Assert.AreEqual(first.Items.Count, second.Items.Count);
            for (var i = 0; i < first.Items.Count; i++)
            {
                Assert.AreEqual(first.Items[i].ItemId, second.Items[i].ItemId);
                Assert.AreEqual(first.Items[i].Count, second.Items[i].Count);
            }
        }

        [Test]
        public void DifferentSeeds_AreFreeToDisagree()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB", "ITM_BONE", "ITM_PEACH" },
                weights: new[] { 1, 1, 1 },
                minCounts: new[] { 1, 1, 1 },
                maxCounts: new[] { 6, 6, 6 },
                dropRolls: 20);
            BattleLab.SetLoot(enemy, "LUT_WOLF");
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");
            var registry = lab.Registry();

            var first = LootResolver.Resolve(encounter, registry, BattleLab.LootStream(1));
            var second = LootResolver.Resolve(encounter, registry, BattleLab.LootStream(2));

            Assert.IsFalse(
                first.Items.Count == second.Items.Count
                && first.Items.Zip(second.Items, (a, b) => a.ItemId == b.ItemId && a.Count == b.Count).All(same => same),
                "换种子却给出逐条相同的战利品，说明掷骰根本没用到流。");
        }

        [Test]
        public void UnknownEnemy_IsReportedAndSkipped()
        {
            using var lab = new BattleLab();
            var known = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            BattleLab.SetLoot(known, null, goldReward: 6);
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF", "ENM_GHOST");
            var report = new ValidationReport();

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream(), report);

            Assert.AreEqual(6, spoils.Gold, "坏掉的那一只跳过，认得的那一只照算。");
            Assert.AreEqual(1, report.ErrorCount);
        }

        [Test]
        public void DanglingLootTable_IsReportedButStillPaysTheBounty()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            BattleLab.SetLoot(enemy, "LUT_GONE", goldReward: 6, experienceReward: 12);
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");
            var report = new ValidationReport();

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream(), report);

            Assert.AreEqual(6, spoils.Gold);
            Assert.AreEqual(12, spoils.Experience);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(0, spoils.Items.Count);
        }

        [Test]
        public void TableWithPositiveRollsButNoPositiveWeight_StopsInsteadOfLooping()
        {
            using var lab = new BattleLab();
            var enemy = lab.Enemy("ENM_WOLF", 30, 8, 3, 6);
            lab.LootTable(
                "LUT_WOLF",
                itemIds: new[] { "ITM_HERB" },
                weights: new[] { 0 },
                minCounts: new[] { 1 },
                maxCounts: new[] { 1 },
                dropRolls: 5);
            BattleLab.SetLoot(enemy, "LUT_WOLF", goldReward: 1);
            var encounter = lab.Encounter("ENC_WOOD", "ENM_WOLF");

            var spoils = LootResolver.Resolve(encounter, lab.Registry(), BattleLab.LootStream());

            Assert.AreEqual(0, spoils.Items.Count);
            Assert.AreEqual(1, spoils.Gold);
        }

        [Test]
        public void NullEncounter_IsAProgrammingError()
        {
            using var lab = new BattleLab();
            var registry = lab.Registry();

            Assert.Throws<System.ArgumentNullException>(
                () => LootResolver.Resolve(null, registry, BattleLab.LootStream()));
        }
    }
}
