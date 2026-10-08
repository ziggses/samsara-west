using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 防御的行为锁定（批次 4）：占主行动、减伤 50%、持续 2 回合，
    /// 以及它与破防的 ×1.5 怎么互算。
    /// </summary>
    /// <remarks>
    /// 减伤幅度与持续回合<b>不住在代码里</b>：它们跟着状态表走（<c>statuses.csv</c> 的 STS_DEFEND）。
    /// 所以这里两头都锁：一头锁「内核把状态挂对了」（阶段机、事件、叠层规则），
    /// 一头锁「挂上去之后伤害真的少了一半」——前者防代码回退，后者防有人改 CSV 把数值改坏。
    /// </remarks>
    [TestFixture]
    public sealed class BattleDefendTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 防御占掉主行动_并给自己挂上守势()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 20, FiveElement.None, 20, false, "SKL_HIT");

            var bus = BattleLab.Bus();
            BattleStatusAppliedEvent? applied = null;
            using var subscription = bus.Subscribe<BattleStatusAppliedEvent>(
                BattleEventChannel.Channel,
                e => applied = e);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            var actor = session.BeginNextTurn();

            var result = session.TryDefend();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(BattleActionKind.Defend, result.Kind);
            Assert.IsNull(result.SkillId, "防御不是技能表里的招式，结果里不该冒出一个技能 ID。");
            Assert.AreEqual(TurnPhase.MoveOrSwap, session.Phase, "主行动用掉，只剩一次移动／换位。");

            var status = actor.FindStatus(lab.Config.DefendStatusId);
            Assert.IsNotNull(status, "减伤走的就是状态留下的承伤乘区，防御必须留下状态。");
            Assert.AreEqual(1, status.Stacks);
            Assert.AreEqual(2, status.RemainingTurns);
            Assert.AreEqual(0.5f, actor.IncomingDamageMultiplier, 0.0001f);

            Assert.IsTrue(applied.HasValue, "挂状态要发事件，界面靠它上图标。");
            Assert.AreEqual(lab.Config.DefendStatusId, applied.Value.StatusId);
            Assert.AreEqual(StatusChangeKind.Applied, applied.Value.Change);
            Assert.AreEqual(actor.RuntimeId, applied.Value.RuntimeId);

            Assert.AreEqual(
                BattleCommandRejection.WrongPhase,
                session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Rejection,
                "防御之后再打技能就该被拒：一手只有一次主行动。");
        }

        [Test]
        public void 举架之后_同一下打在身上只剩一半()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT", breakDamage: 0);
            lab.AttackSkill("SKL_BITE", power: 40, breakDamage: 0);
            lab.Character("CHR_A", 600, 10, 5, 30, FiveElement.None, 999, 50, "SKL_HIT");

            // 敌人速度与我方持平：行动队列在平局时按布阵顺序（我方在前），
            // 于是出手顺序是干净的一人一手——我方举架之后立刻轮到他挨这一下。
            lab.Enemy("ENM_A", 3000, 100, 0, 30, FiveElement.None, 999, false, "SKL_BITE");

            // 暴击率归零，两次对照才有相同的掷骰结果：伤害只差在防御上。
            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var guarded = TakeOneEnemyHit(lab, defend: true);
            var plain = TakeOneEnemyHit(lab, defend: false);

            Assert.Greater(plain.Damage, 0, "对照组必须真的挨了一下，否则下面的减半是空断言。");
            Assert.Greater(guarded.Damage, 0, "防御是减伤不是免伤：一下都不掉就说明乘区写错了。");
            Assert.AreEqual(
                plain.Damage,
                guarded.Damage * 2,
                1,
                "减半之后再取整，允许 1 点四舍五入的差。");

            var expected = DamageCalculator.Compute(
                lab.Config,
                new DamageInput(
                    40,
                    plain.Attack,
                    plain.Defense,
                    FiveElement.None,
                    FiveElement.None,
                    incomingModifier: 0.5f,
                    defenderMaxHealth: guarded.MaxHealth)).Damage;
            Assert.AreEqual(expected, guarded.Damage, "挨的这一下应当正好等于按承伤 0.5 算出来的值。");
        }

        [Test]
        public void 守势只吃两个自己的回合_第二个回合结束时消失()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT", power: 10, breakDamage: 0);
            lab.Character("CHR_A", 400, 10, 0, 20, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 20, FiveElement.None, 999, false, "SKL_HIT");

            var bus = BattleLab.Bus();
            var expired = new List<string>();
            using var subscription = bus.Subscribe<BattleStatusExpiredEvent>(
                BattleEventChannel.Channel,
                e => expired.Add(e.StatusId));

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            session.BeginNextTurn();
            var unit = session.PlayerUnits[0];
            Assert.IsTrue(session.TryDefend().Success);
            Assert.AreEqual(2, unit.FindStatus(lab.Config.DefendStatusId).RemainingTurns);

            session.EndTurn();
            session.BeginNextTurn();
            Assert.IsNotNull(unit.FindStatus(lab.Config.DefendStatusId), "才过一个自己的回合，守势还在。");
            Assert.AreEqual(1, unit.FindStatus(lab.Config.DefendStatusId).RemainingTurns);

            Assert.AreEqual(
                BattleSide.Player,
                AdvanceToPlayer(session).Side,
                "回到自己的回合时守势仍然生效——这正是「持续两个回合」的意义：中间的敌人都被减伤。");
            Assert.IsNotNull(unit.FindStatus(lab.Config.DefendStatusId));
            Assert.AreEqual(0.5f, unit.IncomingDamageMultiplier, 0.0001f);

            session.EndTurn();
            Assert.IsNull(unit.FindStatus(lab.Config.DefendStatusId), "第二个自己的回合结束时守势到期。");
            Assert.AreEqual(1f, unit.IncomingDamageMultiplier, 0.0001f, "状态走了，承伤乘区回到中性。");
            CollectionAssert.Contains(expired, lab.Config.DefendStatusId, "到期要说一声，界面靠它撤图标。");
        }

        [Test]
        public void 破防与防御相乘_破防中举架仍然更疼但不白举()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT", breakDamage: 0);
            lab.AttackSkill("SKL_CLAW", power: 30, breakDamage: 20);
            lab.Character("CHR_A", 600, 10, 0, 30, FiveElement.None, 20, 50, "SKL_HIT");

            // 三个单位同速：出手顺序是「我方 → 鬼一 → 鬼二 → 我方」，
            // 所以第一下把护体打空、第二下才落在破防状态上，正好凑出要测的那一手。
            lab.Enemy("ENM_1", 3000, 50, 0, 30, FiveElement.None, 999, false, "SKL_CLAW");
            lab.Enemy("ENM_2", 3000, 50, 0, 30, FiveElement.None, 999, false, "SKL_CLAW");

            BattleLab.SetPrivate(lab.Config, "_criticalChance", 0f);

            var bus = BattleLab.Bus();
            var hits = new List<int>();
            using var subscription = bus.Subscribe<BattleDamagedEvent>(
                BattleEventChannel.Channel,
                e =>
                {
                    if (e.TargetSide == BattleSide.Player)
                    {
                        hits.Add(e.Damage);
                    }
                });

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_1", "ENM_2")), bus: bus);
            var unit = session.PlayerUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.TryDefend().Success);
            session.EndTurn();

            Assert.AreEqual(BattleSide.Enemy, session.BeginNextTurn().Side);
            session.EndTurn();
            Assert.AreEqual(BattleSide.Enemy, session.BeginNextTurn().Side);
            session.EndTurn();

            Assert.AreEqual(2, hits.Count, "两个敌人各打一下，应当正好两段伤害。");
            Assert.IsTrue(unit.IsBroken, "第一下把护体打空，第二下才是打在破防状态上的。");

            // 破防的 ×1.5 与防御的 ×0.5 各自是独立乘区，按顺序相乘，净效果 0.75。
            var notBroken = DamageCalculator.Compute(
                lab.Config,
                new DamageInput(
                    30, 50, 0, FiveElement.None, FiveElement.None,
                    incomingModifier: 0.5f,
                    defenderMaxHealth: unit.MaxHealth)).Damage;
            var broken = DamageCalculator.Compute(
                lab.Config,
                new DamageInput(
                    30, 50, 0, FiveElement.None, FiveElement.None,
                    defenderIsBroken: true,
                    incomingModifier: 0.5f,
                    defenderMaxHealth: unit.MaxHealth)).Damage;

            Assert.AreEqual(notBroken, hits[0], "第一下还没破防，只吃防御的减半。");
            Assert.AreEqual(broken, hits[1], "第二下破防 ×1.5 与防御 ×0.5 相乘。");
            Assert.Greater(hits[1], hits[0], "破防中举架仍然比平常更疼：防御不会把破防的惩罚顶掉。");
            Assert.Less(
                hits[1],
                DamageCalculator.Compute(
                    lab.Config,
                    new DamageInput(
                        30, 50, 0, FiveElement.None, FiveElement.None,
                        defenderIsBroken: true,
                        defenderMaxHealth: unit.MaxHealth)).Damage,
                "举架仍然有收益，破防不等于白举。");
        }

        [Test]
        public void 主行动已经用掉之后_防御被拒且不留下状态()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();
            var unit = session.PlayerUnits[0];
            Assert.IsTrue(session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success);

            var result = session.TryDefend();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(BattleActionKind.Defend, result.Kind, "被拒的结果也要说清是哪条指令被拒的。");
            Assert.AreEqual(BattleCommandRejection.WrongPhase, result.Rejection);
            Assert.IsNull(unit.FindStatus(lab.Config.DefendStatusId), "被拒不该顺手把状态挂上。");
            Assert.AreEqual(TurnPhase.MoveOrSwap, session.Phase);
        }

        [Test]
        public void 还没轮到谁或已经收场时_防御给出对应的拒绝原因()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));

            Assert.AreEqual(
                BattleCommandRejection.NoActiveTurn,
                session.TryDefend().Rejection,
                "还没轮到谁的时候，防御错在「没有行动者」，不是别的。");

            session.BeginNextTurn();
            Assert.IsTrue(session.TryDefend().Success);
            Assert.IsTrue(session.ForceRetreat());

            Assert.AreEqual(
                BattleCommandRejection.BattleFinished,
                session.TryDefend().Rejection,
                "战斗已经收场，再点防御只能报「已经结束」。");
        }

        [Test]
        public void 连着两个回合都防御_只刷新时长不叠层()
        {
            using var lab = new BattleLab();
            RegisterDefend(lab);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 600, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 600, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();
            var unit = session.PlayerUnits[0];

            var first = session.TryDefend();
            Assert.AreEqual(StatusChangeKind.Applied, first.Effects[0].StatusChange);
            AdvanceToPlayer(session);
            Assert.AreEqual(1, unit.FindStatus(lab.Config.DefendStatusId).RemainingTurns);

            var second = session.TryDefend();
            var status = unit.FindStatus(lab.Config.DefendStatusId);

            Assert.AreEqual(StatusChangeKind.Refreshed, second.Effects[0].StatusChange);
            Assert.AreEqual(2, status.RemainingTurns, "再举一次只把时长拉回满，不该变成 3。");
            Assert.AreEqual(1, status.Stacks);
            Assert.AreEqual(0.5f, unit.IncomingDamageMultiplier, 0.0001f, "刷新不是叠加：减伤不会变成 25%。");
            Assert.AreEqual(1, unit.Statuses.Count, "状态行上只该有一条守势。");
        }

        [Test]
        public void 配置指的状态没登记时_防御被拒且不吃掉这一手()
        {
            using var lab = new BattleLab();

            // 刻意不登记守势：配置里的 ID 在数据目录里查不到，这是数据错误而不是玩家操作错误。
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 400, 10, 0, 30, FiveElement.None, 999, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 400, 10, 0, 10, FiveElement.None, 999, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            session.BeginNextTurn();

            var result = session.TryDefend();

            Assert.AreEqual(BattleCommandRejection.DefinitionMissing, result.Rejection);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase, "数据错了不该顺手吃掉这一手。");
            Assert.IsTrue(
                session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId).Success,
                "主行动还在，技能照打。");
        }

        /// <summary>
        /// 造一场「我方先手、敌人随后打一下」的对照：<paramref name="defend"/> 为真时我方举架。
        /// 用同一个种子跑两遍，暴击率归零之后两遍的伤害只差在防御上。
        /// </summary>
        private static (int Damage, int Attack, int Defense, int MaxHealth) TakeOneEnemyHit(
            BattleLab lab,
            bool defend)
        {
            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var unit = session.PlayerUnits[0];
            var enemy = session.EnemyUnits[0];

            session.BeginNextTurn();
            if (defend)
            {
                Assert.IsTrue(session.TryDefend().Success);
            }
            else
            {
                Assert.IsTrue(session.UseSkill("SKL_HIT", enemy.RuntimeId).Success);
            }

            session.EndTurn();
            Assert.AreEqual(BattleSide.Enemy, session.BeginNextTurn().Side, "敌人接在我方后面出手。");

            return (unit.MaxHealth - unit.Health, enemy.EffectiveAttack, unit.EffectiveDefense, unit.MaxHealth);
        }

        /// <summary>把守势按数据表里的口径登记进目录：减伤 50%、持续 2 回合、增益、刷新不叠层。</summary>
        private static void RegisterDefend(BattleLab lab) =>
            lab.Status(
                lab.Config.DefendStatusId,
                durationTurns: 2,
                stackRule: StackRule.Refresh,
                maxStacks: 1,
                isDebuff: false,
                incomingDamageModifier: 0.5f);

        /// <summary>结束当前回合、推进到我方下一次出手；推不动就直接失败，而不是让断言测到别的回合。</summary>
        private static BattleUnit AdvanceToPlayer(BattleSession session)
        {
            for (var guard = 0; guard < 12; guard++)
            {
                session.EndTurn();
                var actor = session.BeginNextTurn();
                if (actor != null && actor.Side == BattleSide.Player)
                {
                    return actor;
                }
            }

            Assert.Fail("推进了十几手都没轮到我方，建场数据已经和用例的假设脱节。");
            return null;
        }

        private static BattleSetup Setup(List<BattleUnitBlueprint> party, List<BattleUnitBlueprint> enemies) =>
            new BattleSetup(Encounter, party, enemies);

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
