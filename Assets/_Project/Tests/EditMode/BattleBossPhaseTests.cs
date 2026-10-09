using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// Boss 阶段机的行为锁定（口径见 ADR-030）：开局进第一档、跌破阈值换档、一次跨两档要走两遍，
    /// 以及阶段表坏掉时怎么退化。
    /// </summary>
    /// <remarks>
    /// <para>血量一律用「Boss 自己身上的持续伤害」来推动：那条路每回合掉一个固定整数，
    /// 比走伤害公式（含防御、暴击、取整）更容易把 Boss 停在刚好越过阈值的那个点上。
    /// 顺带也就把「被持续伤害跌破阈值同样换档」这一路覆盖掉了。</para>
    /// <para>阈值刻意避开 <c>0.4</c> 这类二进制不好表示的线，取 <c>0.7</c>／<c>0.45</c>／<c>0.2</c>，
    /// 免得用例挂在浮点相等的边缘上。</para>
    /// </remarks>
    [TestFixture]
    public sealed class BattleBossPhaseTests
    {
        private const string Encounter = "ENC_TEST_BOSS";

        private const string OtherEncounter = "ENC_TEST_OTHER";

        /// <summary>推动血量的持续伤害：每回合固定掉 35 点，配 100 点血正好把 Boss 停在 65%。</summary>
        private const int TickDamage = 35;

        [Test]
        public void 开局即进第一档_技能组由阶段表接管()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.AttackSkill("SKL_KING_OPEN");
            lab.AttackSkill("SKL_KING_CLOSE");
            lab.BossPhase(
                "BSP_TEST_P1", Encounter, phaseIndex: 1, healthThreshold: 1f,
                skillIds: new[] { "SKL_KING_OPEN" });

            lab.Character("CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE");

            // 敌人表里挂着两招，阶段表只留一招：谁说了算正是这条用例要钉的。
            lab.Enemy(
                "ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true,
                "SKL_KING_OPEN", "SKL_KING_CLOSE");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var session = NewSession(
                lab, Setup(new[] { "BSP_TEST_P1" }, Line("CHR_A"), Line("ENM_BOSS")), bus: bus);
            var unit = session.EnemyUnits[0];

            Assert.AreEqual(1, phases.Count, "开场这一档也要发事件：界面靠它播入场演出。");
            Assert.AreEqual(1, phases[0].PhaseIndex);
            Assert.AreEqual("BSP_TEST_P1", phases[0].PhaseDefinitionId);
            Assert.AreEqual(Encounter, phases[0].EncounterId);
            Assert.AreEqual(unit.RuntimeId, phases[0].RuntimeId);
            Assert.AreEqual(1f, phases[0].HealthRatio, 0.0001f);

            CollectionAssert.AreEqual(
                new[] { "SKL_KING_OPEN" },
                unit.SkillIds,
                "阶段表写的是「这一档会哪几手」的全集；敌人表那两招只在没有阶段表时才算数。");
        }

        [Test]
        public void 跌破七成_进第二档并挂上入场状态()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.StatusSkill("SKL_TICK", "STS_TICK");
            lab.Status("STS_TICK", durationTurns: 20, isDebuff: false, healthDeltaPerTurn: -TickDamage);
            lab.Status("STS_HASTE", durationTurns: 20, isDebuff: false, speedModifier: 0.5f);

            lab.BossPhase(
                "BSP_TEST_P1", Encounter, phaseIndex: 1, healthThreshold: 1f,
                skillIds: new[] { "SKL_KING_OPEN" });
            lab.BossPhase(
                "BSP_TEST_P2", Encounter, phaseIndex: 2, healthThreshold: 0.7f,
                skillIds: new[] { "SKL_KING_RAGE" }, onEnterStatusIds: new[] { "STS_HASTE" },
                speedMultiplier: 1.5f,
                tauntKey: "dlg.bsp.t2", bgmSwitchKey: "music.bsp.t2", cameraCueKey: "cam.bsp.t2");

            lab.AttackSkill("SKL_KING_OPEN");
            lab.AttackSkill("SKL_KING_RAGE");
            lab.Character(
                "CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE", "SKL_TICK");
            lab.Enemy(
                "ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_KING_OPEN");

            var bus = BattleLab.Bus();
            var order = new List<string>();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var sub1 = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel,
                e =>
                {
                    phases.Add(e);
                    order.Add("phase");
                });
            using var sub2 = bus.Subscribe<BattleStatusAppliedEvent>(
                BattleEventChannel.Channel,
                e =>
                {
                    if (e.StatusId == "STS_HASTE")
                    {
                        order.Add("status");
                    }
                });

            var session = NewSession(
                lab, Setup(new[] { "BSP_TEST_P1", "BSP_TEST_P2" }, Line("CHR_A"), Line("ENM_BOSS")),
                bus: bus);
            var unit = session.EnemyUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_TICK", unit.RuntimeId).Success);
            session.EndTurn();

            AdvanceOneBossTurn(session);

            Assert.AreEqual(100 - TickDamage, unit.Health, "持续伤害每回合固定掉一个整数，血量精确可控。");
            Assert.AreEqual(0.65f, unit.HealthRatio, 0.0005f);

            Assert.AreEqual(2, phases.Count, "开场一档、跌破七成一档。");
            var second = phases[1];
            Assert.AreEqual(2, second.PhaseIndex);
            Assert.AreEqual("BSP_TEST_P2", second.PhaseDefinitionId);
            Assert.AreEqual(0.65f, second.HealthRatio, 0.0005f);
            Assert.AreEqual("dlg.bsp.t2", second.TauntKey);
            Assert.AreEqual("music.bsp.t2", second.BgmSwitchKey);
            Assert.AreEqual("cam.bsp.t2", second.CameraCueKey);

            CollectionAssert.AreEqual(new[] { "SKL_KING_RAGE" }, unit.SkillIds);
            Assert.IsNotNull(unit.FindStatus("STS_HASTE"), "入场状态是这一档的一部分。");
            Assert.AreEqual(1.5f, unit.BossSpeedMultiplier, 0.0001f);

            CollectionAssert.AreEqual(
                new[] { "phase", "status", "phase" },
                order,
                "换档事件是「这一档已落实完毕」的信号：先挂状态、后发事件，订阅方接到它时重绘一次就够。");
        }

        [Test]
        public void 一次跨两档_中间那一档的入场状态与演出都不跳过()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.StatusSkill("SKL_TICK", "STS_TICK");
            lab.Status("STS_TICK", durationTurns: 20, isDebuff: false, healthDeltaPerTurn: -60);
            lab.Status("STS_MARK_B", durationTurns: 20, isDebuff: false);
            lab.Status("STS_MARK_C", durationTurns: 20, isDebuff: false);

            lab.AttackSkill("SKL_A");
            lab.AttackSkill("SKL_B");
            lab.AttackSkill("SKL_C");
            lab.AttackSkill("SKL_D");

            lab.BossPhase("BSP_TEST_P1", Encounter, 1, 1f, skillIds: new[] { "SKL_A" });
            lab.BossPhase(
                "BSP_TEST_P2", Encounter, 2, 0.7f, skillIds: new[] { "SKL_B" },
                onEnterStatusIds: new[] { "STS_MARK_B" }, tauntKey: "t2");
            lab.BossPhase(
                "BSP_TEST_P3", Encounter, 3, 0.45f, skillIds: new[] { "SKL_C" },
                onEnterStatusIds: new[] { "STS_MARK_C" }, tauntKey: "t3");
            lab.BossPhase("BSP_TEST_P4", Encounter, 4, 0.2f, skillIds: new[] { "SKL_D" }, tauntKey: "t4");

            lab.Character(
                "CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE", "SKL_TICK");
            lab.Enemy("ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_A");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var session = NewSession(
                lab,
                Setup(
                    new[] { "BSP_TEST_P1", "BSP_TEST_P2", "BSP_TEST_P3", "BSP_TEST_P4" },
                    Line("CHR_A"),
                    Line("ENM_BOSS")),
                bus: bus);
            var unit = session.EnemyUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_TICK", unit.RuntimeId).Success);
            session.EndTurn();

            // 一下掉 60：从满血直接落到 40%，七成线与四成半线在这一次判定里一起越过。
            AdvanceOneBossTurn(session);

            Assert.AreEqual(40, unit.Health);
            Assert.AreEqual(3, phases.Count, "开场一档，加上这一次跨过的两档。");
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                System.Linq.Enumerable.Select(phases, p => p.PhaseIndex),
                "跨档是逐档推进，不是一步跳到最后一档。");
            Assert.AreEqual("t2", phases[1].TauntKey);
            Assert.AreEqual("t3", phases[2].TauntKey);

            Assert.IsNotNull(unit.FindStatus("STS_MARK_B"), "中间那一档的入场状态也要落地。");
            Assert.IsNotNull(unit.FindStatus("STS_MARK_C"));
            CollectionAssert.AreEqual(
                new[] { "SKL_C" },
                unit.SkillIds,
                "停在 40%：两成那档还没到，技能组不该是最后一档的。");
        }

        [Test]
        public void 同一档只进一次_血在阈值下方反复跳动也不重播()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.StatusSkill("SKL_TICK", "STS_TICK");
            lab.Status("STS_TICK", durationTurns: 40, isDebuff: false, healthDeltaPerTurn: -10);

            lab.AttackSkill("SKL_A");
            lab.AttackSkill("SKL_B");
            lab.BossPhase("BSP_TEST_P1", Encounter, 1, 1f, skillIds: new[] { "SKL_A" });
            lab.BossPhase("BSP_TEST_P2", Encounter, 2, 0.72f, skillIds: new[] { "SKL_B" }, tauntKey: "t2");
            lab.BossPhase("BSP_TEST_P3", Encounter, 3, 0.45f, skillIds: new[] { "SKL_B" }, tauntKey: "t3");

            lab.Character(
                "CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE", "SKL_TICK");
            lab.Enemy("ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_A");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var session = NewSession(
                lab,
                Setup(new[] { "BSP_TEST_P1", "BSP_TEST_P2", "BSP_TEST_P3" }, Line("CHR_A"), Line("ENM_BOSS")),
                bus: bus);
            var unit = session.EnemyUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_TICK", unit.RuntimeId).Success);
            session.EndTurn();

            // 四手各掉 10：90 → 80 → 70（跌破 72%）→ 60。只有第三手该发事件。
            for (var i = 0; i < 4; i++)
            {
                AdvanceOneBossTurn(session);
            }

            Assert.AreEqual(60, unit.Health);
            Assert.AreEqual(2, phases.Count, "档位是「打到哪一步」，不是「现在什么状态」：停在下面不再重复播报。");
            Assert.AreEqual(2, phases[1].PhaseIndex);
            CollectionAssert.AreEqual(new[] { "SKL_B" }, unit.SkillIds, "还在第二档，技能组不该往后跑。");
        }

        [Test]
        public void 入场状态查不到_跳过它但照样换档()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.StatusSkill("SKL_TICK", "STS_TICK");
            lab.Status("STS_TICK", durationTurns: 20, isDebuff: false, healthDeltaPerTurn: -TickDamage);

            lab.AttackSkill("SKL_A");
            lab.AttackSkill("SKL_B");
            lab.BossPhase("BSP_TEST_P1", Encounter, 1, 1f, skillIds: new[] { "SKL_A" });

            // 刻意不登记 STS_MISSING：这是数据错误，不是玩家操作错误。
            lab.BossPhase(
                "BSP_TEST_P2", Encounter, 2, 0.7f, skillIds: new[] { "SKL_B" },
                onEnterStatusIds: new[] { "STS_MISSING" });

            lab.Character(
                "CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE", "SKL_TICK");
            lab.Enemy("ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_A");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var session = NewSession(
                lab, Setup(new[] { "BSP_TEST_P1", "BSP_TEST_P2" }, Line("CHR_A"), Line("ENM_BOSS")),
                bus: bus);
            var unit = session.EnemyUnits[0];

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_TICK", unit.RuntimeId).Success);
            session.EndTurn();
            AdvanceOneBossTurn(session);

            Assert.AreEqual(2, phases.Count, "状态拿不到不该拦住换档：档位本身是好的。");
            Assert.AreEqual(2, phases[1].PhaseIndex);
            CollectionAssert.AreEqual(new[] { "SKL_B" }, unit.SkillIds);
            Assert.IsNull(
                unit.FindStatus("STS_MISSING"),
                "查不到的那条状态就是没挂上，不该凭空多一条。");
            Assert.IsNotNull(unit.FindStatus("STS_TICK"), "推动血量的那条还在，没被谁顺手清掉。");
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome, "一处数据错误不该让这场打不下去。");
        }

        [Test]
        public void 没有阶段表时_内核不介入()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.AttackSkill("SKL_OWN_A");
            lab.AttackSkill("SKL_OWN_B");
            lab.Character("CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE");
            lab.Enemy(
                "ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_OWN_A", "SKL_OWN_B");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var session = new BattleSession(
                lab.Config,
                lab.Registry(),
                BattleLab.Stream(),
                new BattleSetup(Encounter, Line("CHR_A"), Line("ENM_BOSS"), isBoss: true),
                bus);

            var unit = session.EnemyUnits[0];
            Assert.AreEqual(0, phases.Count, "没配阶段表就没有档位可言，一条事件都不该发。");
            CollectionAssert.AreEqual(
                new[] { "SKL_OWN_A", "SKL_OWN_B" },
                unit.SkillIds,
                "没有阶段表时，敌人表里的技能组原样进战斗。");
            Assert.AreEqual(1f, unit.BossAttackMultiplier, 0.0001f);
            Assert.AreEqual(1f, unit.BossSpeedMultiplier, 0.0001f);
        }

        [Test]
        public void 阶段查不到或属于别的遭遇_跳过且不换档()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.AttackSkill("SKL_OWN");
            lab.AttackSkill("SKL_FOREIGN");

            // 这一条哪都没错，只是属于另一场遭遇：串表了，不该被这一场吞下。
            lab.BossPhase("BSP_OTHER_P1", OtherEncounter, 1, 1f, skillIds: new[] { "SKL_FOREIGN" });

            lab.Character("CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE");
            lab.Enemy("ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_OWN");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            // 一个查不到的 ID，一个属于别的遭遇：两条都该被跳过。
            var session = NewSession(
                lab,
                Setup(new[] { "BSP_NOT_REGISTERED", "BSP_OTHER_P1" }, Line("CHR_A"), Line("ENM_BOSS")),
                bus: bus);

            var unit = session.EnemyUnits[0];
            Assert.AreEqual(0, phases.Count);
            CollectionAssert.AreEqual(
                new[] { "SKL_OWN" },
                unit.SkillIds,
                "两条都被跳过，技能组保持敌人表里的样子——别的遭遇的阶段表不会漏进来。");
        }

        [Test]
        public void 阶段倍率与状态修正各占一个乘区()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.StatusSkill("SKL_TICK", "STS_TICK");
            lab.Status("STS_TICK", durationTurns: 20, isDebuff: false, healthDeltaPerTurn: -TickDamage);
            lab.Status("STS_FURY", durationTurns: 20, isDebuff: false, attackModifier: 0.5f);

            lab.AttackSkill("SKL_B");
            lab.BossPhase("BSP_TEST_P1", Encounter, 1, 1f, skillIds: new[] { "SKL_B" });
            lab.BossPhase(
                "BSP_TEST_P2", Encounter, 2, 0.7f, skillIds: new[] { "SKL_B" },
                onEnterStatusIds: new[] { "STS_FURY" }, attackMultiplier: 2f);

            lab.Character(
                "CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE", "SKL_TICK");
            lab.Enemy("ENM_BOSS", 100, 100, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_B");

            var session = NewSession(
                lab, Setup(new[] { "BSP_TEST_P1", "BSP_TEST_P2" }, Line("CHR_A"), Line("ENM_BOSS")));
            var unit = session.EnemyUnits[0];

            Assert.AreEqual(100, unit.EffectiveAttack, "第一档倍率是 1，攻击就是基础值。");

            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_TICK", unit.RuntimeId).Success);
            session.EndTurn();
            AdvanceOneBossTurn(session);

            Assert.AreEqual(
                300,
                unit.EffectiveAttack,
                "状态 +50% 加在基数上（1.5 倍），阶段 ×2 是另一个乘区：100 × 1.5 × 2。两者相乘而不是相加。");
            Assert.AreEqual(2f, unit.BossAttackMultiplier, 0.0001f);
        }

        [Test]
        public void 遭遇定义里的阶段表_一路传进战斗()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_PROBE");
            lab.AttackSkill("SKL_KING_OPEN");
            lab.BossPhase("BSP_TEST_P1", Encounter, 1, 1f, skillIds: new[] { "SKL_KING_OPEN" });
            lab.Character("CHR_A", 900, 10, 0, 30, FiveElement.None, 999, 50, "SKL_PROBE");
            lab.Enemy("ENM_BOSS", 100, 10, 0, 5, FiveElement.None, 999, isBoss: true, "SKL_KING_OPEN");

            var encounter = lab.Encounter(Encounter, "ENM_BOSS");
            BattleLab.SetBoss(encounter, "BSP_TEST_P1");

            var bus = BattleLab.Bus();
            var phases = new List<BattleBossPhaseChangedEvent>();
            using var subscription = bus.Subscribe<BattleBossPhaseChangedEvent>(
                BattleEventChannel.Channel, e => phases.Add(e));

            var setup = BattleFactory.FromEncounter(encounter, new[] { "CHR_A" });
            Assert.AreEqual(
                1,
                setup.BossPhaseIds.Count,
                "遭遇上的阶段清单要进得了入场清单，否则阶段机永远不会开工。");

            var session = new BattleSession(
                lab.Config, lab.Registry(), BattleLab.Stream(), setup, bus);
            Assert.AreEqual(1, phases.Count, "清单进得来，开场就该立刻进第一档。");
            Assert.AreEqual(1, phases[0].PhaseIndex);
            Assert.AreEqual(Encounter, phases[0].EncounterId);
        }

        /// <summary>带阶段表的入场清单。清单是「怎么打」与「打什么」的分界，阶段表同样从这里进。</summary>
        private static BattleSetup Setup(
            string[] bossPhaseIds,
            List<BattleUnitBlueprint> party,
            List<BattleUnitBlueprint> enemies) =>
            new BattleSetup(Encounter, party, enemies, isBoss: true, bossPhaseIds: bossPhaseIds);

        /// <summary>把当前回合走完并推进到「下一个属于敌方的回合」，走完它再返回；推不动就直接失败。</summary>
        private static void AdvanceOneBossTurn(BattleSession session)
        {
            for (var guard = 0; guard < 12; guard++)
            {
                var actor = session.BeginNextTurn();
                if (actor != null && actor.Side == BattleSide.Enemy)
                {
                    session.EndTurn();
                    return;
                }

                session.EndTurn();
            }

            Assert.Fail("推进了十几手都没轮到敌方，建场数据已经和用例的假设脱节。");
        }

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
