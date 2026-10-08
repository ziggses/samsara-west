using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Economy;
using SamsaraWest.Flow;
using SamsaraWest.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 「打赢一场 → 钱与物真的进了钱袋与背包 → 并且能跟着存档走出去」的端到端证据。
    /// </summary>
    /// <remarks>
    /// <para>EditMode 的 <c>LootBattleLinkTests</c> 管这条线的规矩（只有胜利结算、败逃与剧情撤退都不发、
    /// 重复打赢重复拿、坏数据只跳那一条）。这一组管另一半：在引导器装出来的<b>真服务、真数据表、
    /// 真事件总线</b>之下，同一批规矩也必须成立——测试只负责开战或广播收场，一行都不替它搬钱。</para>
    ///
    /// <para><b>为什么胜利收场用广播事件给、而不用真实战斗打赢</b>：真实战斗能不能赢取决于数值平衡
    /// （<c>enemies.csv</c> 一改就可能翻盘），拿它当断言会把「接线坏了」和「平衡调了」混成一种失败。
    /// 内核也没有「指定胜利」的入口——为了一个接线用例往内核上开测试专用 API，代价比收益大。
    /// 所以分两条：真打一场只断言<b>账面与结算数一致</b>（怎么收场都对），结算细节用总线上的
    /// 真实 <c>PlayerVictory</c> 事件来验——那正是内核与剧本会发出的同一条事件。</para>
    /// </remarks>
    public sealed class LootWiringTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>maps.csv 里 CH01_MAP01 绑定的那场遭遇；首章野外图唯一的一条。</summary>
        private const string EncounterId = "ENC_CH01_001";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLog.DisableFileSink();
            GameLog.Reset();

            var existing = GameBootstrap.Instance;
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 战利品是写进钱袋与背包的；清掉它，别污染同一场的其它用例。
            // 用 TryResolve 而不是 Resolve：用例中途失败时服务可能没装上，清理不该再抛一次。
            if (GameServices.IsReady)
            {
                if (GameServices.Registry.TryResolve(out IEconomyService economy))
                {
                    economy.Clear();
                }

                if (GameServices.Registry.TryResolve(out IBattleService battle) && battle.HasActiveBattle)
                {
                    battle.EndBattle();
                }
            }

            var bootstrap = GameBootstrap.Instance;
            if (bootstrap != null)
            {
                UnityEngine.Object.Destroy(bootstrap.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        /// <summary>
        /// 真开一场、真打完：不管胜负，账面上的钱与物必须与结算线自己数出来的完全一致。
        /// </summary>
        [UnityTest]
        public IEnumerator RealBattle_VaultMatchesWhatTheLinkSettled()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var battle = registry.Resolve<IBattleService>();
            var definitions = registry.Resolve<IDefinitionRegistry>();
            var economy = registry.Resolve<IEconomyService>();
            var link = GameBootstrap.Instance.LootLink;

            Assert.IsNotNull(economy, "启动场景必须装上经济模块，否则钱与物没有运行期真源。");
            Assert.IsNotNull(link, "战利品结算线要装在引导末尾，否则打赢了没人收钱。");
            Assert.AreEqual(0, economy.Gold, "开局身上不该有钱。");

            var encounter = ResolveEncounter(definitions);
            var party = BattleFactory.DefaultParty(definitions);
            Assert.IsNotEmpty(party, "骨架期的队伍口径是目录里全部可操作角色。");

            // 与组合根同一种接法：战斗拿到的背包就是钱袋与背包的转述者。
            var setup = BattleFactory.FromEncounter(encounter, party, new VaultBattleInventory(economy));
            Assert.IsTrue(setup.Validate(out var error), $"入场清单必须合法：{error}");

            var session = battle.StartBattle(setup);
            Assert.IsNotNull(session);
            session.RunToEnd();

            Assert.AreNotEqual(
                BattleOutcome.Ongoing,
                session.Outcome,
                "这场必须能分出胜负——推不到结局说明战斗内核卡住了，不是这条接线的问题。");

            // 不管输赢：账面的钱必须与结算线自己数出来的钱一致，
            // 不一致就说明还有第二条线在偷偷动钱袋（而那是要极力避免的事）。
            Assert.AreEqual(link.GoldSettled, economy.Gold, "钱袋里的钱必须等于结算线发出去的钱。");
            Assert.AreEqual(link.ItemsSettled, TotalItems(economy), "背包里的件数必须等于结算线发出去的件数。");

            if (session.Outcome == BattleOutcome.PlayerVictory)
            {
                Assert.AreEqual(1, link.Settlements, "赢了就该结算一次。");
                Assert.GreaterOrEqual(
                    economy.Gold,
                    MinimumGold(definitions, encounter),
                    "真打赢一场，按目录该拿的赏金一分都不能少。");
                Assert.Greater(link.ItemsSettled, 0, "这几只敌人的掉落表都配了掉落，赢了不该两手空空。");
            }
            else
            {
                Assert.AreEqual(0, link.Settlements, "没赢就不结算——战败、逃跑与剧情撤退都不发东西。");
                Assert.AreEqual(0, economy.Gold, "没赢就不该有赏金入账。");
                Assert.AreEqual(
                    1,
                    link.SkippedOutcomes,
                    "非胜的收场要被记成「跳过」，而不是「结算了 0 块钱」。");
            }
        }

        /// <summary>
        /// 真实总线上的胜利收场：按真数据表结算出真战利品，并且它们能跟着存档出去、再读回来。
        /// </summary>
        [UnityTest]
        public IEnumerator RealVictory_SettlesRealSpoilsAndTheSaveCarriesThemAway()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var definitions = registry.Resolve<IDefinitionRegistry>();
            var economy = registry.Resolve<IEconomyService>();
            var bus = registry.Resolve<IEventBus>();
            var saves = registry.Resolve<ISaveCoordinator>();
            var link = GameBootstrap.Instance.LootLink;

            Assert.IsNotNull(link, "战利品结算线要装在引导末尾。");
            Assert.IsNotNull(economy);
            Assert.AreEqual(0, economy.Gold, "开局身上不该有钱。");

            // 内核与剧本会发出的同一条事件、同一条总线：组合根装出来的那条线必须当场接住它。
            bus.Publish(
                BattleEventChannel.Channel,
                new BattleEndedEvent(EncounterId, BattleOutcome.PlayerVictory, 3, 11));

            var encounter = ResolveEncounter(definitions);
            var floor = MinimumGold(definitions, encounter);

            Assert.AreEqual(1, link.Settlements, "胜仗要被结算。");
            Assert.GreaterOrEqual(economy.Gold, floor, $"按目录该拿的赏金一分都不能少（至少 {floor}）。");
            Assert.AreEqual(link.GoldSettled, economy.Gold, "钱袋里的钱必须等于结算线发出去的钱。");
            Assert.Greater(link.ItemsSettled, 0, "这三只敌人的掉落表都配了掉落，赢了不该两手空空。");
            Assert.Greater(economy.Items.Count, 0, "掉落的东西要真的进背包。");

            // 掉落只能来自这场遭遇的掉落表：进来了别的东西，说明有人走错了目录。
            var allowed = AllowedLootIds(definitions, encounter);
            foreach (var stack in economy.Items)
            {
                CollectionAssert.Contains(allowed, stack.ItemId, $"'{stack.ItemId}' 不是这场遭遇的掉落物。");
            }

            // 上一轮的存档接线：战利品要能跟着存档走出去。
            var data = saves.Capture();
            Assert.AreEqual(economy.Gold, data.Gold, "钱要进存档。");
            Assert.AreEqual(economy.Items.Count, data.InventoryItemIds.Count, "背包的每一格都要进存档。");
            for (var i = 0; i < economy.Items.Count; i++)
            {
                Assert.AreEqual(economy.Items[i].ItemId, data.InventoryItemIds[i]);
                Assert.AreEqual(economy.Items[i].Count, data.InventoryItemCounts[i]);
            }

            // 清空之后读档回来：只写了「存」没写「读」是最常见的半截接线。
            var gold = economy.Gold;
            var items = new List<ItemStack>(economy.Items);
            economy.Clear();

            Assert.IsTrue(saves.Apply(data), "刚采集的档案必须能灌回去。");
            Assert.AreEqual(gold, economy.Gold, "读档要把钱换回来。");
            Assert.AreEqual(items.Count, economy.Items.Count, "读档要把背包换回来。");
            for (var i = 0; i < items.Count; i++)
            {
                Assert.AreEqual(items[i].ItemId, economy.Items[i].ItemId);
                Assert.AreEqual(items[i].Count, economy.Items[i].Count, $"读档后 '{items[i].ItemId}' 的数量对不上。");
            }
        }

        /// <summary>非胜的收场：一分钱、一件东西都不该动。</summary>
        [UnityTest]
        public IEnumerator Defeat_LeavesTheVaultUntouched()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var economy = registry.Resolve<IEconomyService>();
            var bus = registry.Resolve<IEventBus>();
            var link = GameBootstrap.Instance.LootLink;

            bus.Publish(
                BattleEventChannel.Channel,
                new BattleEndedEvent(EncounterId, BattleOutcome.PlayerDefeat, 4, 12));

            Assert.AreEqual(0, economy.Gold, "战败一分钱都不该有。");
            Assert.AreEqual(0, economy.Items.Count, "战败一件东西都不该有。");
            Assert.AreEqual(0, link.Settlements, "战败不结算。");
            Assert.AreEqual(1, link.SkippedOutcomes, "战败要被记成「跳过」。");
        }

        private static EncounterDefinition ResolveEncounter(IDefinitionRegistry definitions)
        {
            Assert.IsTrue(
                definitions.TryGet(EncounterId, out DefinitionBase found) && found is EncounterDefinition,
                $"定义目录里必须有 {EncounterId}——maps.csv 的 encounterTableId 指着它。");
            return (EncounterDefinition)found;
        }

        /// <summary>按目录算出的赏金下限：敌人身上的赏金 + 掉落表里的钱区间下限。</summary>
        private static int MinimumGold(IDefinitionRegistry definitions, EncounterDefinition encounter)
        {
            var total = 0;
            foreach (var enemyId in encounter.EnemyIds)
            {
                Assert.IsTrue(
                    definitions.TryGet(enemyId, out DefinitionBase found) && found is EnemyDefinition,
                    $"编成里的 '{enemyId}' 必须是敌人定义。");

                var enemy = (EnemyDefinition)found;
                total += Math.Max(0, enemy.GoldReward);

                if (string.IsNullOrEmpty(enemy.LootTableId))
                {
                    continue;
                }

                Assert.IsTrue(
                    definitions.TryGet(enemy.LootTableId, out DefinitionBase tableFound) && tableFound is LootTableDefinition,
                    $"敌人 '{enemyId}' 的掉落表 '{enemy.LootTableId}' 必须在目录里。");

                total += Math.Max(0, ((LootTableDefinition)tableFound).GoldMin);
            }

            return total;
        }

        /// <summary>这场遭遇可能掉出来的所有东西（按权重抽的 + 必掉的）。</summary>
        private static List<string> AllowedLootIds(IDefinitionRegistry definitions, EncounterDefinition encounter)
        {
            var ids = new List<string>();
            foreach (var enemyId in encounter.EnemyIds)
            {
                if (!definitions.TryGet(enemyId, out DefinitionBase found) || !(found is EnemyDefinition enemy))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(enemy.LootTableId))
                {
                    continue;
                }

                if (!definitions.TryGet(enemy.LootTableId, out DefinitionBase tableFound)
                    || !(tableFound is LootTableDefinition table))
                {
                    continue;
                }

                ids.AddRange(table.ItemIds);
                ids.AddRange(table.GuaranteedItemIds);
            }

            return ids;
        }

        /// <summary>背包里的总件数（同一个 ID 分几格算几件）。</summary>
        private static int TotalItems(IEconomyService economy)
        {
            var total = 0;
            var stacks = economy.Items;
            for (var i = 0; i < stacks.Count; i++)
            {
                total += stacks[i].Count;
            }

            return total;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            // 无条件重载：SetUp 刚把场景里的引导器拆掉，只有重新加载才会再装一次服务。
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
