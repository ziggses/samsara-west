using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 道具的行为锁定（批次 5）：占主行动、不吃灵力、一回合一件、扣背包，
    /// 效果复用既有的治疗／状态通路。
    /// </summary>
    /// <remarks>
    /// <para>回多少血、回多少灵<b>不住在代码里</b>：它们跟着 <c>items.csv</c> 的
    /// <c>effectMagnitude</c> 走。所以这里锁的是「内核把 <c>effectKey</c> 解释对了没有」，
    /// 而不是某个具体数字——数字坏了该由数据侧的分档表负责。</para>
    /// <para>唯一刻意钉死的是「一回合只有一件」：它由主行动门保证，因此用例必须
    /// 真的把同一回合的第二件点两次，证明那道门确实拦得住。</para>
    /// </remarks>
    [TestFixture]
    public sealed class BattleItemTests
    {
        private const string Encounter = "ENC_TEST_ITEM";
        private const string Potion = "ITM_TEST_HEAL";
        private const string SpiritPotion = "ITM_TEST_SPIRIT";
        private const string Antidote = "ITM_TEST_CURE";
        private const string Charm = "ITM_TEST_CHARM";

        private const int PotionMagnitude = 40;

        [Test]
        public void 用道具占掉主行动_扣掉背包一个_回血走既有的治疗事件()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_BITE", power: 100, breakDamage: 0);
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude);
            // 行动值是「行动间隔」并且在出手后累加（见 BattleSessionTests 的顺序用例）：
            // 我方速度 10 → 间隔 1000；敌人速度 12 → 间隔 833。833 < 1000 且 1666 > 1000，
            // 所以敌人只先手一次。别把敌人调到 30（间隔 333）：那会让它在 1000 之前连动三次，
            // 「敌人打一下、然后轮到我方」这条前提就不成立了。
            lab.Character("CHR_A", 5000, 10, 0, 10, FiveElement.None, 999, 50);
            lab.Enemy("ENM_A", 5000, 500, 0, 12, FiveElement.None, 999, false, "SKL_BITE");
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var inventory = new BattleInventory();
            inventory.Add(Potion, 2);

            var bus = BattleLab.Bus();
            BattleHealedEvent? healed = null;
            BattleItemUsedEvent? used = null;
            using var healSubscription = bus.Subscribe<BattleHealedEvent>(
                BattleEventChannel.Channel,
                e => healed = e);
            using var itemSubscription = bus.Subscribe<BattleItemUsedEvent>(
                BattleEventChannel.Channel,
                e => used = e);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory), bus: bus);
            var unit = session.PlayerUnits[0];

            Assert.AreEqual(BattleSide.Enemy, session.BeginNextTurn().Side, "敌人更快，先手打一下。");
            var actor = session.BeginNextTurn();
            Assert.AreEqual(BattleSide.Player, actor.Side);
            Assert.Less(unit.Health, unit.MaxHealth, "先挨了一下，下面才有血可回。");

            var spiritBefore = unit.Spirit;
            var healthBefore = unit.Health;
            var expectedHeal = Mathf.Min(PotionMagnitude, unit.MaxHealth - healthBefore);
            Assert.Greater(expectedHeal, 0, "回血量必须是个正数，否则下面全是空断言。");

            var result = session.UseItem(Potion, unit.RuntimeId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(BattleActionKind.UseItem, result.Kind);
            Assert.AreEqual(Potion, result.ItemId, "结果里要能看出用的是哪一件。");
            Assert.IsNull(result.SkillId, "用道具不是技能表里的招式，结果里不该冒出技能 ID。");
            Assert.AreEqual(
                TurnPhase.MoveOrSwap,
                session.Phase,
                "主行动被道具用掉，只剩一次移动／换位。");

            Assert.AreEqual(expectedHeal, unit.Health - healthBefore, "回了多少就写在道具表里。");
            Assert.AreEqual(1, inventory.CountOf(Potion), "用掉一件就少一个。");
            Assert.AreEqual(spiritBefore, unit.Spirit, "道具不吃灵力。");

            Assert.AreEqual(1, result.Effects.Count, "本版道具只作用于我方单体，只有一条落点。");
            Assert.AreEqual(unit.RuntimeId, result.Effects[0].TargetRuntimeId);
            Assert.AreEqual(expectedHeal, result.Effects[0].Healing);
            Assert.AreEqual(unit.Health, result.Effects[0].HealthAfter);

            Assert.IsTrue(healed.HasValue, "回血必须照旧发治疗事件，界面读血条的那条通路靠它。");
            Assert.AreEqual(Potion, healed.Value.SkillId, "道具的身份就填在这个字段里。");
            Assert.AreEqual(unit.RuntimeId, healed.Value.TargetRuntimeId);
            Assert.AreEqual(expectedHeal, healed.Value.Amount);
            Assert.AreEqual(unit.Health, healed.Value.HealthAfter);

            Assert.IsTrue(used.HasValue, "用掉道具要发一条「用掉了什么、还剩几个」的事件。");
            Assert.AreEqual(actor.RuntimeId, used.Value.ActorRuntimeId);
            Assert.AreEqual(Potion, used.Value.ItemId);
            Assert.AreEqual(ItemEffectKeys.HealHealth, used.Value.EffectKey);
            Assert.AreEqual(unit.RuntimeId, used.Value.TargetRuntimeId);
            Assert.AreEqual(expectedHeal, used.Value.Amount);
            Assert.AreEqual(1, used.Value.Remaining);

            Assert.AreEqual(
                BattleCommandRejection.WrongPhase,
                session.UseItem(Potion, unit.RuntimeId).Rejection,
                "一手只有一次主行动：同一回合再点一件道具就该被拒。");
            Assert.AreEqual(1, inventory.CountOf(Potion), "被拒的那一次不该顺手扣掉背包。");
        }

        [Test]
        public void 连着两个回合各能用一件_限制是按回合算的()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_BITE", power: 100, breakDamage: 0);
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude);
            lab.Character("CHR_A", 5000, 10, 0, 30, FiveElement.None, 999, 50);
            lab.Enemy("ENM_A", 5000, 500, 0, 10, FiveElement.None, 999, false, "SKL_BITE");
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var inventory = new BattleInventory();
            inventory.Add(Potion, 3);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            var unit = session.PlayerUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseItem(Potion, unit.RuntimeId).Success, "第一手的道具。");
            Assert.AreEqual(2, inventory.CountOf(Potion));

            AdvanceToPlayer(session);

            Assert.IsTrue(
                session.UseItem(Potion, unit.RuntimeId).Success,
                "下一手是新的主行动，道具应该又能用一件——限制是「一回合一件」而不是「一场一件」。");
            Assert.AreEqual(1, inventory.CountOf(Potion));
        }

        [Test]
        public void 背包没有存货时_被拒且不吃掉这一手()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add(Potion, 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            session.BeginNextTurn();
            var unit = session.PlayerUnits[0];

            Assert.IsTrue(session.UseItem(Potion, unit.RuntimeId).Success);
            Assert.AreEqual(0, inventory.CountOf(Potion), "只剩一个，用掉就是 0。");

            AdvanceToPlayer(session);
            var result = session.UseItem(Potion, unit.RuntimeId);

            Assert.AreEqual(BattleCommandRejection.ItemOutOfStock, result.Rejection);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase, "缺货不该顺手吃掉这一手。");
            Assert.IsTrue(
                session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success,
                "主行动还在，技能照打。");
        }

        [Test]
        public void 没接背包时_道具一律用不了()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            Assert.IsNull(session.Inventory, "这场没接背包。");
            Assert.AreEqual(
                BattleCommandRejection.ItemOutOfStock,
                session.UseItem(Potion, session.PlayerUnits[0].RuntimeId).Rejection,
                "没有背包就等于一件都没有：给「缺货」而不是「有货但用不了」。");
        }

        [Test]
        public void 回灵道具把灵力补回去_并写进效果读数()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_BITE", power: 100, breakDamage: 0);
            lab.AttackSkill("SKL_COST", power: 10, breakDamage: 0, spiritCost: 20);
            lab.Item(SpiritPotion, ItemEffectKeys.HealSpirit, 25);
            lab.Character("CHR_A", 5000, 10, 0, 30, FiveElement.None, 999, 50, "SKL_COST");
            lab.Enemy("ENM_A", 5000, 500, 0, 10, FiveElement.None, 999, false, "SKL_BITE");
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var inventory = new BattleInventory();
            inventory.Add(SpiritPotion, 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            var unit = session.PlayerUnits[0];
            session.BeginNextTurn();

            Assert.IsTrue(
                session.UseSkill("SKL_COST", session.EnemyUnits[0].RuntimeId).Success,
                "先花掉 20 点灵力，才看得出回灵真的回了。");
            Assert.AreEqual(30, unit.Spirit);

            AdvanceToPlayer(session);
            var result = session.UseItem(SpiritPotion, unit.RuntimeId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(50, unit.Spirit, "回灵会被灵力上限夹住：30 + 25 只能到 50。");
            Assert.AreEqual(20, result.Effects[0].SpiritRestored, "读数记的是<b>实际</b>补进去的量。");
            Assert.AreEqual(0, result.Effects[0].Healing, "回灵不该顺手记成回血。");
            Assert.AreEqual(0, inventory.CountOf(SpiritPotion));
        }

        [Test]
        public void 祛负面的道具只摘负面_增益留在身上()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Status("STS_POISON", durationTurns: 3, isDebuff: true, healthDeltaPerTurn: -5);
            lab.Status("STS_GUARD", durationTurns: 3, isDebuff: false, incomingDamageModifier: 0.5f);
            lab.Item(Antidote, ItemEffectKeys.CureStatus, 1);
            lab.Character("CHR_A", 5000, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 5000, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add(Antidote, 1);

            var bus = BattleLab.Bus();
            var expired = new List<string>();
            using var subscription = bus.Subscribe<BattleStatusExpiredEvent>(
                BattleEventChannel.Channel,
                e => expired.Add(e.StatusId));

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory), bus: bus);
            var unit = session.PlayerUnits[0];
            session.BeginNextTurn();

            // 用夹具直接挂状态：这条用例只关心「摘什么、留什么」，与谁挂上去无关。
            unit.ApplyStatus(StatusDefinitionOf(lab, "STS_POISON"));
            unit.ApplyStatus(StatusDefinitionOf(lab, "STS_GUARD"));
            Assert.AreEqual(2, unit.Statuses.Count);

            var result = session.UseItem(Antidote, unit.RuntimeId);

            Assert.IsTrue(result.Success);
            Assert.IsNull(unit.FindStatus("STS_POISON"), "负面状态被摘掉。");
            Assert.IsNotNull(unit.FindStatus("STS_GUARD"), "增益不该被顺手洗掉。");
            Assert.AreEqual(0.5f, unit.IncomingDamageMultiplier, 0.0001f, "保住的那个增益仍然在生效。");
            Assert.AreEqual(1, result.Effects[0].CuredDebuffs);
            Assert.AreEqual(new[] { "STS_POISON" }, expired.ToArray(), "摘状态也要发既有的事件，界面靠它撤图标。");
        }

        [Test]
        public void 对敌方用道具_报目标阵营不符()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add(Potion, 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            session.BeginNextTurn();

            var result = session.UseItem(Potion, session.EnemyUnits[0].RuntimeId);

            Assert.AreEqual(BattleCommandRejection.TargetSideMismatch, result.Rejection);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase);
            Assert.AreEqual(1, inventory.CountOf(Potion), "被拒的指令不该扣背包。");
        }

        [Test]
        public void 没标记战斗可用的道具_用不了()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item(Potion, ItemEffectKeys.HealHealth, PotionMagnitude, usableInBattle: false);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add(Potion, 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            session.BeginNextTurn();

            Assert.AreEqual(
                BattleCommandRejection.ItemNotUsable,
                session.UseItem(Potion, session.PlayerUnits[0].RuntimeId).Rejection);
            Assert.AreEqual(1, inventory.CountOf(Potion));
        }

        [Test]
        public void 效果键内核不认识时_被拒且不吃掉这一手()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item("ITM_TEST_WEIRD", "item.effect.buff.attack", 5);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add("ITM_TEST_WEIRD", 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            session.BeginNextTurn();

            var result = session.UseItem("ITM_TEST_WEIRD", session.PlayerUnits[0].RuntimeId);

            Assert.AreEqual(BattleCommandRejection.ItemEffectUnknown, result.Rejection);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase, "数据错了不该顺手吃掉这一手。");
            Assert.AreEqual(1, inventory.CountOf("ITM_TEST_WEIRD"), "没结算就一分存货都不该扣。");
            Assert.IsTrue(
                session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success,
                "主行动还在，技能照打。");
        }

        [Test]
        public void 非消耗类道具_用掉之后背包不动()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT");
            lab.Item(Charm, ItemEffectKeys.HealHealth, PotionMagnitude, isConsumedOnUse: false);
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add(Charm, 1);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A"), inventory));
            session.BeginNextTurn();

            Assert.IsTrue(session.UseItem(Charm, session.PlayerUnits[0].RuntimeId).Success);
            Assert.AreEqual(1, inventory.CountOf(Charm), "isConsumedOnUse 为假的道具用掉不算消耗。");
        }

        /// <summary>从建场器里取一条已登记的状态定义，供直接挂状态用。</summary>
        private static StatusDefinition StatusDefinitionOf(BattleLab lab, string statusId)
        {
            for (var i = 0; i < lab.Definitions.Count; i++)
            {
                if (lab.Definitions[i] is StatusDefinition status &&
                    string.Equals(status.Id, statusId, System.StringComparison.Ordinal))
                {
                    return status;
                }
            }

            Assert.Fail($"建场器里没有登记状态 '{statusId}'。");
            return null;
        }

        /// <summary>
        /// 结束当前回合、推进到我方下一次出手；推不动就直接失败。
        /// </summary>
        /// <remarks>
        /// 先进 <see cref="BattleSession.EndTurn"/>：调用方可能停在
        /// <see cref="TurnPhase.MoveOrSwap"/>（刚用掉主行动），这时直接
        /// <see cref="BattleSession.BeginNextTurn"/> 只会把同一个我方单位再还回来。
        /// 敌人出手是自动的，所以循环体里只需要「不是我方就结束这一手」。
        /// </remarks>
        private static BattleUnit AdvanceToPlayer(BattleSession session)
        {
            session.EndTurn();
            for (var guard = 0; guard < 12; guard++)
            {
                var actor = session.BeginNextTurn();
                if (actor != null && actor.Side == BattleSide.Player)
                {
                    return actor;
                }

                session.EndTurn();
            }

            Assert.Fail("推进了十几手都没轮到我方，建场数据已经和用例的假设脱节。");
            return null;
        }

        private static BattleSetup Setup(
            List<BattleUnitBlueprint> party,
            List<BattleUnitBlueprint> enemies,
            IBattleInventory inventory = null) =>
            new BattleSetup(Encounter, party, enemies, inventory: inventory);

        private static List<BattleUnitBlueprint> Line(params string[] definitionIds)
        {
            var line = new List<BattleUnitBlueprint>(definitionIds.Length);
            for (var i = 0; i < definitionIds.Length; i++)
            {
                line.Add(new BattleUnitBlueprint(definitionIds[i], BattleFormation.SlotForIndex(i)));
            }

            return line;
        }

        private static BattleSession NewSession(
            BattleLab lab,
            BattleSetup setup,
            ulong seed = BattleLab.DefaultSeed,
            IEventBus bus = null) =>
            new BattleSession(lab.Config, lab.Registry(), BattleLab.Stream(seed), setup, bus);
    }
}
