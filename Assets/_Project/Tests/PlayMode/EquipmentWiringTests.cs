using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Equipment;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 「穿上真装备 → 从探索开出来的那场战斗真的带着它 → 并且能跟着存档走出去」的端到端证据。
    /// </summary>
    /// <remarks>
    /// <para>EditMode 的 <c>EquipmentServiceTests</c> 管这本簿子自己的规矩（写入口只放行配得上的条目、
    /// 快照顺序定死、读档整本换掉）。这一组管另一半：在引导器装出来的<b>真服务、真数据表、
    /// 真事件总线</b>之下，那条「探索遇敌 → 进战斗」的接线要真的把在身清单交进去。</para>
    ///
    /// <para><b>为什么必须走总线，而不是自己造一份入场清单</b>：人手传一遍
    /// <c>EquipmentLoadoutAdapter</c> 只能证明适配器会转述，证明不了<b>组合根把它接上了</b>——
    /// 把 GameBootstrap 里那一行删掉，那种用例照样是绿的。所以这里广播探索模块掷中遭遇时会发的那条事件，
    /// 让 <c>ExplorationBattleLink</c> 自己开战，再回头问战斗里的单位：攻击到底有没有变高。</para>
    /// </remarks>
    public sealed class EquipmentWiringTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>maps.csv 里 CH01_MAP01 绑定的那场遭遇；首章野外图唯一的一条。</summary>
        private const string EncounterId = "ENC_CH01_001";
        private const string MapId = "CH01_MAP01";

        /// <summary>equipment.csv 里唯一一件「不限角色」的武器：Weapon 栏位、+5 攻击、自带被动。</summary>
        private const string IronStaffId = "EQP_STAFF_IRON";

        private const string StillnessSutraId = "SUT_STILLNESS";

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
            // 在身清单与战斗都是过程状态：清掉它，别污染同一场的其它用例。
            // 用 TryResolve 而不是 Resolve：用例中途失败时服务可能没装上，清理不该再抛一次。
            if (GameServices.IsReady)
            {
                if (GameServices.Registry.TryResolve(out IEquipmentService equipment))
                {
                    equipment.Clear();
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
        /// 真穿上、真从探索开战：穿上武器的那位按「本体 + 加成」进场，没穿的那位仍然裸装。
        /// </summary>
        [UnityTest]
        public IEnumerator EncounterBattle_PartyEntersWithWhatTheyWear()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var definitions = registry.Resolve<IDefinitionRegistry>();
            var equipment = registry.Resolve<IEquipmentService>();
            var bus = registry.Resolve<IEventBus>();
            var battle = registry.Resolve<IBattleService>();

            Assert.IsNotNull(equipment, "启动场景必须装上在身清单，否则哪里都没有真的一身可问。");

            var party = BattleFactory.DefaultParty(definitions);
            Assert.GreaterOrEqual(party.Count, 2, "骨架期的队伍口径是目录里全部可操作角色；要有两位才分得出「穿的」与「没穿的」。");

            var armedId = party[0];
            var bareId = party[1];
            var armed = ResolveCharacter(definitions, armedId);
            var bare = ResolveCharacter(definitions, bareId);
            var staff = ResolveEquipment(definitions, IronStaffId);
            Assert.Greater(staff.AttackBonus, 0, "这件武器的攻击加成必须是正数，否则这条用例证明不了什么。");

            Assert.IsTrue(
                equipment.TryEquip(armedId, "Weapon", IronStaffId, out var equipError),
                $"按真目录穿真武器不该失败：{equipError}");

            // 组合根那条线的入口：探索模块掷中遭遇时发的是这条事件。
            bus.Publish(
                ExplorationEventChannel.Channel,
                new EncounterTriggeredEvent(MapId, EncounterId, new GridPosition(0, 0), 1));

            var session = battle.Current;
            Assert.IsNotNull(session, "遇敌事件必须真的开出一场战斗——接线要是没装上，这里就断了。");

            var armedUnit = FindUnit(session.PlayerUnits, armedId);
            Assert.IsNotNull(armedUnit, $"这场战斗里必须有 '{armedId}'。");
            Assert.AreEqual(
                armed.Attack + staff.AttackBonus,
                armedUnit.BaseAttack,
                $"'{armedId}' 进场时的攻击应当是「本体的攻击 + 武器的加成」——这就是穿上真装备的证据。");

            // 对照组：没穿东西的那位必须分毫不加，免得「给全队统一加一笔」也蒙混过关。
            var bareUnit = FindUnit(session.PlayerUnits, bareId);
            Assert.IsNotNull(bareUnit, $"这场战斗里必须有 '{bareId}'。");
            Assert.AreEqual(bare.Attack, bareUnit.BaseAttack, $"'{bareId}' 什么都没穿，攻击不该被改动。");
        }

        /// <summary>
        /// 在身的一身要能跟着存档出去、脱光之后再读回来——只写了「存」没写「读」是最常见的半截接线。
        /// </summary>
        [UnityTest]
        public IEnumerator EquippedItems_SurviveTheSaveRoundTrip()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var definitions = registry.Resolve<IDefinitionRegistry>();
            var equipment = registry.Resolve<IEquipmentService>();
            var saves = registry.Resolve<ISaveCoordinator>();

            var party = BattleFactory.DefaultParty(definitions);
            Assert.IsNotEmpty(party, "骨架期的队伍口径是目录里全部可操作角色。");
            var characterId = party[0];

            Assert.IsTrue(
                equipment.TryEquip(characterId, "Weapon", IronStaffId, out var weaponError),
                $"按真目录穿真武器不该失败：{weaponError}");
            Assert.IsTrue(
                equipment.TryEquip(characterId, "Sutra", StillnessSutraId, out var sutraError),
                $"按真目录穿真经文不该失败：{sutraError}");

            var worn = new List<EquippedItem>(equipment.Snapshot);
            Assert.AreEqual(2, worn.Count, "穿了两件就该有两件在身。");

            var data = saves.Capture();
            Assert.AreEqual(worn.Count, data.Equipment.Count, "在身清单要整本进存档。");
            for (var i = 0; i < worn.Count; i++)
            {
                Assert.AreEqual(worn[i].CharacterId, data.Equipment[i].CharacterId, "成员要进存档。");
                Assert.AreEqual(worn[i].SlotId, data.Equipment[i].SlotId, "栏位要进存档。");
                Assert.AreEqual(worn[i].ItemId, data.Equipment[i].ItemId, "物品要进存档。");
            }

            equipment.Clear();
            Assert.AreEqual(0, equipment.Snapshot.Count, "先脱光，才分得清读档到底有没有把东西换回来。");

            Assert.IsTrue(saves.Apply(data), "刚采集的档案必须能灌回去。");
            CollectionAssert.AreEqual(worn, equipment.Snapshot, "读档要把在身清单整本换回来，顺序也要一样。");
        }

        private static CharacterDefinition ResolveCharacter(IDefinitionRegistry definitions, string characterId)
        {
            Assert.IsTrue(
                definitions.TryGet(characterId, out DefinitionBase found) && found is CharacterDefinition,
                $"'{characterId}' 必须是角色定义。");
            return (CharacterDefinition)found;
        }

        private static EquipmentDefinition ResolveEquipment(IDefinitionRegistry definitions, string itemId)
        {
            Assert.IsTrue(
                definitions.TryGet(itemId, out DefinitionBase found) && found is EquipmentDefinition,
                $"'{itemId}' 必须是装备定义——equipment.csv 里应当有它。");
            return (EquipmentDefinition)found;
        }

        private static BattleUnit FindUnit(IReadOnlyList<BattleUnit> units, string definitionId)
        {
            for (var i = 0; i < units.Count; i++)
            {
                if (string.Equals(units[i].DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    return units[i];
                }
            }

            return null;
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
