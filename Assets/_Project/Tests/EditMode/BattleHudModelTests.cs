using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Localization;
using SamsaraWest.UI;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗界面快照（<see cref="BattleHudModel"/>）的行为锁定。
    /// </summary>
    /// <remarks>
    /// 这一层不碰 uGUI，只把 <see cref="BattleSession"/> 翻译成界面能直接画的字段，
    /// 因此可以在这里逐条钉死「血条画什么、按钮给不给、灰掉写什么理由」，
    /// 而不必先搭一个场景。
    /// </remarks>
    [TestFixture]
    public sealed class BattleHudModelTests
    {
        private const string Encounter = "ENC_TEST_HUD";

        [Test]
        public void 战斗快照_按建队顺序给出两方单位行()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A", "CHR_B"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            Assert.AreEqual(2, hud.PlayerRows.Count);
            Assert.AreEqual(1, hud.EnemyRows.Count);
            Assert.AreEqual(3, hud.Rows.Count, "全部单位行是我方在前、敌方在后。");

            Assert.AreEqual("CHR_A", hud.PlayerRows[0].DefinitionId);
            Assert.AreEqual("CHR_B", hud.PlayerRows[1].DefinitionId);
            Assert.AreEqual("ENM_A", hud.EnemyRows[0].DefinitionId);

            Assert.AreEqual(BattleSide.Player, hud.PlayerRows[0].Side);
            Assert.AreEqual(BattleSide.Enemy, hud.EnemyRows[0].Side);

            // 血与护体都从定义里成比例地落到行上，界面不需要自己再除一遍。
            Assert.AreEqual(1f, hud.PlayerRows[0].HealthRatio, 0.0001f);
            Assert.AreEqual(1f, hud.PlayerRows[0].BreakRatio, 0.0001f, "护体条建队时是满的，之后只会被削。");
            Assert.AreEqual(
                hud.PlayerRows[0].BreakThreshold,
                hud.PlayerRows[0].BreakValue,
                "护体是「剩余值」，不是「累计值」。");
            Assert.IsTrue(hud.PlayerRows[0].IsAlive);
        }

        [Test]
        public void 还没轮到我方时_没有任何指令可下()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            Assert.AreEqual(-1, hud.CurrentActorRuntimeId, "还没 BeginNextTurn，不该有行动者。");
            Assert.AreEqual(TurnPhase.Idle, hud.Phase);
            CollectionAssert.IsEmpty(hud.Commands, "没人待行动时不该给出按钮。");
        }

        [Test]
        public void 轮到我方时_指令里带着技能_防御_逃跑与结束回合()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            var actor = session.BeginNextTurn();
            hud.Refresh();

            Assert.AreEqual(actor.RuntimeId, hud.CurrentActorRuntimeId);
            Assert.AreEqual(TurnPhase.MainAction, hud.Phase);

            var attack = Find(hud, BattleCommandId.Skill, "SKL_HIT");
            Assert.IsNotNull(attack, "自己会的技能都得有按钮。");
            Assert.IsTrue(attack.Enabled);
            Assert.AreEqual(BattleCommandRejection.None, attack.Rejection);
            Assert.AreEqual("test.battle.atk.name", attack.LabelKey, "按钮文案必须取技能定义的文本键，不能是技能 ID。");

            Assert.IsNotNull(Find(hud, BattleCommandId.Flee, null), "逃跑是主行动，得有按钮。");
            Assert.IsNotNull(Find(hud, BattleCommandId.EndTurn, null), "放弃剩余行动也得有按钮。");

            var defend = Find(hud, BattleCommandId.Defend, null);
            Assert.IsNotNull(defend, "防御是主行动，得有按钮。");
            Assert.IsTrue(defend.Enabled, "防御不吃灵力也不进冷却，轮到我方就该是可点的。");
            Assert.AreEqual(
                LocalizationKeys.UI_BATTLE_COMMAND_DEFEND,
                defend.LabelKey,
                "防御按钮的文案键是数据表里的那一条，不是现编的中文。");

            Assert.IsTrue(hud.PlayerRows[0].IsCurrentActor, "轮到的单位必须被标出来。");
        }

        [Test]
        public void 主行动用掉之后_不再给指令按钮()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            session.BeginNextTurn();
            var result = session.UseSkill("SKL_HIT", session.EnemyUnits[0].RuntimeId);
            Assert.IsTrue(result.Success);

            hud.Refresh();

            Assert.AreEqual(TurnPhase.MoveOrSwap, hud.Phase);
            CollectionAssert.IsEmpty(hud.Commands, "只剩移动／换位时不该再给主行动按钮。");
        }

        [Test]
        public void 灵力不足的技能_按钮置灰并写明原因()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.AttackSkill("SKL_BIG", power: 40, breakDamage: 0, spiritCost: 60);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT", "SKL_BIG");
            lab.Enemy("ENM_A", 3000, 5, 0, 10, FiveElement.None, 200, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());
            session.BeginNextTurn();
            hud.Refresh();

            var big = Find(hud, BattleCommandId.Skill, "SKL_BIG");
            Assert.IsNotNull(big);
            Assert.IsFalse(big.Enabled, "灵力不够的按钮必须置灰。");
            Assert.AreEqual(BattleCommandRejection.NotEnoughSpirit, big.Rejection);
            Assert.AreEqual(60, big.SpiritCost);

            Assert.IsTrue(Find(hud, BattleCommandId.Skill, "SKL_HIT").Enabled, "灵力不够只该拖累那一个技能。");
        }

        [Test]
        public void 冷却中的技能_按钮置灰并写明原因()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.AttackSkill("SKL_CD", power: 20, breakDamage: 0, cooldownTurns: 1);
            // 速度只差 1.5 倍：行动值=基准/速度，差得太远会让快的一方连动好几手，
            // 慢的那一方根本插不进来，用例就测不到「第二回合还轮到我方」这一手。
            lab.Character("CHR_A", 600, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT", "SKL_CD");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            var first = session.BeginNextTurn();
            Assert.Greater(first.EffectiveSpeed, session.EnemyUnits[0].EffectiveSpeed, "用例的前提是我方先手。");
            hud.Refresh();
            Assert.IsTrue(Find(hud, BattleCommandId.Skill, "SKL_CD").Enabled, "第一手它还没进冷却。");

            Assert.IsTrue(session.UseSkill("SKL_CD", session.EnemyUnits[0].RuntimeId).Success);
            session.EndTurn();

            session.BeginNextTurn();
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome, "用例的前提是打不完。");

            var again = session.BeginNextTurn();
            Assert.AreEqual(first.RuntimeId, again.RuntimeId, "又轮到同一个我方单位。");

            hud.Refresh();
            var onCooldown = Find(hud, BattleCommandId.Skill, "SKL_CD");
            Assert.IsFalse(onCooldown.Enabled, "冷却没走完的按钮必须置灰。");
            Assert.AreEqual(BattleCommandRejection.SkillOnCooldown, onCooldown.Rejection);
            Assert.IsTrue(Find(hud, BattleCommandId.Skill, "SKL_HIT").Enabled);
        }

        [Test]
        public void 刷新写回同一批行对象_列表身份不变()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            var rows = hud.Rows;
            var firstRow = hud.PlayerRows[0];
            var health = firstRow.Health;

            session.BeginNextTurn();
            session.EndTurn();
            session.BeginNextTurn();
            hud.Refresh();

            Assert.AreSame(rows, hud.Rows, "刷新不该换掉列表，否则界面绑定会失效。");
            Assert.AreSame(firstRow, hud.PlayerRows[0], "刷新该把新数值写回同一个行对象。");
            Assert.LessOrEqual(firstRow.Health, health, "写回的是当前数值，不是建队时的旧值。");
        }

        [Test]
        public void 敌方意图_按单位给且不问到我方头上()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());

            var enemy = session.EnemyUnits[0];
            Assert.AreEqual(1, hud.IntentRows.Count, "只有一个敌人，也就只有一条预告。");
            Assert.AreEqual(enemy.RuntimeId, hud.IntentRows[0].ActorRuntimeId);
            Assert.AreEqual("SKL_HIT", hud.IntentRows[0].SkillId);
            Assert.IsTrue(hud.IntentRows[0].HasIntent);

            Assert.IsNotNull(hud.FindIntent(enemy.RuntimeId));
            Assert.IsNull(hud.FindIntent(session.PlayerUnits[0].RuntimeId), "我方单位不参与敌方预告。");
        }

        [Test]
        public void 逃跑按钮的文案与成功率都取自会话()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());
            session.BeginNextTurn();
            hud.Refresh();

            var flee = Find(hud, BattleCommandId.Flee, null);
            Assert.AreEqual(LocalizationKeys.UI_BATTLE_COMMAND_FLEE, flee.LabelKey);
            Assert.Greater(hud.EscapeChance, 0f);
            Assert.LessOrEqual(hud.EscapeChance, 1f);
        }

        [Test]
        public void 守势以增益色挂到状态行上_并写清还剩几回合()
        {
            using var lab = new BattleLab();
            BuildBasic(lab);
            RegisterDefend(lab);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var hud = new BattleHudModel(session, lab.Registry());
            session.BeginNextTurn();
            Assert.IsTrue(session.TryDefend().Success);
            hud.Refresh();

            var chip = hud.PlayerRows[0].Statuses[0];
            Assert.AreEqual(1, hud.PlayerRows[0].Statuses.Count, "状态行只该多出一枚守势。");
            Assert.AreEqual(lab.Config.DefendStatusId, chip.StatusId);
            Assert.AreEqual("test.battle.sts.name", chip.NameKey);
            Assert.IsFalse(chip.IsDebuff, "守势是增益，配色与图标按增益画。");
            Assert.AreEqual(1, chip.Stacks);
            Assert.AreEqual(2, chip.RemainingTurns, "剩余回合数直接给界面，界面不回头找定义。");
            Assert.IsEmpty(hud.EnemyRows[0].Statuses, "守势只挂在自己身上。");
        }

        private static BattleCommandOption Find(BattleHudModel hud, BattleCommandId id, string skillId)
        {
            for (var i = 0; i < hud.Commands.Count; i++)
            {
                var option = hud.Commands[i];
                if (option.Id != id)
                {
                    continue;
                }

                if (skillId == null || option.SkillId == skillId)
                {
                    return option;
                }
            }

            Assert.Fail($"找不到指令 {id}/{skillId}。");
            return default;
        }

        private static void BuildBasic(BattleLab lab)
        {
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 300, 10, 5, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 10, FiveElement.None, 900, false, "SKL_HIT");
        }

        /// <summary>按数据表里的口径登记守势：减伤 50%、持续 2 回合、增益。</summary>
        private static void RegisterDefend(BattleLab lab) =>
            lab.Status(
                lab.Config.DefendStatusId,
                durationTurns: 2,
                stackRule: StackRule.Refresh,
                maxStacks: 1,
                isDebuff: false,
                incomingDamageModifier: 0.5f);

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

        private static BattleSession NewSession(BattleLab lab, BattleSetup setup) =>
            new BattleSession(lab.Config, lab.Registry(), BattleLab.Stream(), setup);
    }
}
