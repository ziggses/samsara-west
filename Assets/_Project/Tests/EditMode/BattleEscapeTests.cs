using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 逃跑的行为锁定：成功率怎么算、掷骰的边界、失败只白费一手、成功则整场收场。
    /// </summary>
    /// <remarks>
    /// 逃跑是唯一一条「战斗不靠打死人收场」的路径，所以两头都锁：
    /// 一头是纯函数（战力、概率、掷骰比较，不依赖随机流）；
    /// 一头是它与回合阶段机的接缝（失败之后还能不能动、战斗算不算结束）。
    /// </remarks>
    [TestFixture]
    public sealed class BattleEscapeTests
    {
        private const string Encounter = "ENC_TEST_001";

        [Test]
        public void 战力按四项加权求和_权重改了就跟着变()
        {
            using var lab = new BattleLab();

            // 默认权重 攻 1 / 防 1 / 速 1 / 血 0.1：10 + 4 + 6 + 100×0.1 = 30。
            Assert.AreEqual(30f, lab.Config.GetCombatPower(10, 4, 6, 100), 0.0001f);

            BattleLab.SetPrivate(lab.Config, "_powerWeightHealth", 0f);
            Assert.AreEqual(20f, lab.Config.GetCombatPower(10, 4, 6, 100), 0.0001f, "血量权重归零之后只该剩攻防速。");
        }

        [Test]
        public void 逃跑概率随战力比线性变化并被夹在配置的上下限之间()
        {
            using var lab = new BattleLab();
            var config = lab.Config;

            Assert.AreEqual(0.6f, config.GetEscapeChance(100f, 100f), 0.0001f, "势均力敌应当落在上下限中间。");
            Assert.AreEqual(0.75f, config.GetEscapeChance(300f, 100f), 0.0001f, "我方三倍于敌。");
            Assert.AreEqual(0.45f, config.GetEscapeChance(100f, 300f), 0.0001f, "敌方三倍于我：线性给值，不是对称取反。");
            Assert.AreEqual(0.9f, config.GetEscapeChance(100000f, 1f), 0.0001f, "再强也只能顶到上限。");
            Assert.AreEqual(0.3f, config.GetEscapeChance(1f, 100000f), 0.0001f, "再弱也只能压到下限。");
            Assert.AreEqual(0.6f, config.GetEscapeChance(0f, 0f), 0.0001f, "两边都是 0 时取中值：不许除零，也不许变成必跑掉。");
        }

        [Test]
        public void 掷骰取小于_概率贴到下限时结论仍然确定()
        {
            using var lab = new BattleLab();

            // 100 : 100 → 60%。取「小于」意味着正好掷出 0.6 算失败：边界只允许有一种解释。
            Assert.IsTrue(lab.Config.IsEscapeRoll(0.5999f, 100f, 100f));
            Assert.IsFalse(lab.Config.IsEscapeRoll(0.6f, 100f, 100f));

            PinChance(lab, 0f);
            Assert.IsFalse(lab.Config.IsEscapeRoll(0f, 100f, 100f), "概率为 0 时连掷出 0 都不算成功。");
        }

        [Test]
        public void 逃跑成功_整队脱离战斗并以逃跑收场()
        {
            using var lab = new BattleLab();
            PinChance(lab, 1f);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 50, 5, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var bus = BattleLab.Bus();
            BattleEscapeResolvedEvent? escape = null;
            using var subscription = bus.Subscribe<BattleEscapeResolvedEvent>(
                BattleEventChannel.Channel,
                e => escape = e);

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")), bus: bus);
            var actor = session.BeginNextTurn();

            var result = session.TryEscape();

            Assert.IsTrue(result.Success, "指令本身必须是被接受的，跑没跑掉看 Escaped。");
            Assert.AreEqual(BattleActionKind.Escape, result.Kind);
            Assert.IsTrue(result.Escaped);
            Assert.AreEqual(1f, result.EscapeChance, 0.0001f);

            Assert.IsTrue(session.IsFinished, "逃跑成功就是这场战斗的结局，不需要再打死谁。");
            Assert.AreEqual(BattleOutcome.PlayerEscaped, session.Outcome);
            Assert.AreEqual(TurnPhase.Finished, session.Phase);

            Assert.IsTrue(escape.HasValue, "逃跑必须发事件，界面靠它放动画与提示。");
            Assert.AreEqual(actor.RuntimeId, escape.Value.RuntimeId);
            Assert.IsTrue(escape.Value.Escaped);
            Assert.Greater(escape.Value.PlayerPower, escape.Value.EnemyPower, "这条用例里我方战力本来就更高。");
        }

        [Test]
        public void 逃跑失败_只白费一手且敌人照常行动()
        {
            using var lab = new BattleLab();
            PinChance(lab, 0f);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 200, 10, 0, 20, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));
            var player = session.PlayerUnits[0];
            var enemy = session.EnemyUnits[0];
            session.BeginNextTurn();

            var result = session.TryEscape();

            Assert.IsTrue(result.Success, "指令是被接受的，只是没跑掉。");
            Assert.IsFalse(result.Escaped);
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome, "没跑掉不等于打输了。");
            Assert.AreEqual(TurnPhase.MoveOrSwap, session.Phase, "逃跑算主行动，用掉之后只该剩移动／换位。");
            Assert.AreEqual(
                BattleCommandRejection.WrongPhase,
                session.UseSkill("SKL_HIT", enemy.RuntimeId).Rejection,
                "逃跑失败之后这一手就没了，不能接着打技能。");

            session.EndTurn();
            var actor = session.BeginNextTurn();

            Assert.AreEqual(BattleSide.Enemy, actor.Side, "同速时我方先手，敌方接在后面。");
            Assert.Less(player.Health, player.MaxHealth, "逃跑失败不该替玩家省掉敌人这一轮。");
            Assert.AreEqual(BattleOutcome.Ongoing, session.Outcome);
        }

        [Test]
        public void 战力只算站着的单位_打掉一个敌人之后更容易跑掉()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 40);
            lab.Character("CHR_A", 400, 40, 0, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_BIG", 300, 20, 5, 5, FiveElement.None, 20, false, "SKL_HIT");
            lab.Enemy("ENM_SMALL", 1, 1, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_BIG", "ENM_SMALL")));
            var small = session.EnemyUnits[1];
            Assert.AreEqual(
                "ENM_SMALL",
                small.DefinitionId,
                "建队顺序变了就该有人来改这条用例，而不是让它悄悄测别的东西。");

            var before = session.EscapeChance;
            session.BeginNextTurn();
            Assert.IsTrue(session.UseSkill("SKL_HIT", small.RuntimeId).Success);

            Assert.AreEqual(1, session.AliveCount(BattleSide.Enemy));
            Assert.Greater(session.EscapeChance, before, "少一个敌人就该更好跑：战力只算还站着的单位。");
        }

        [Test]
        public void 战斗已收场或还没轮到谁时_逃跑给出对应的拒绝原因()
        {
            using var lab = new BattleLab();
            PinChance(lab, 1f);
            lab.AttackSkill("SKL_HIT");
            lab.Character("CHR_A", 200, 10, 0, 10, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 50, 5, 0, 5, FiveElement.None, 20, false, "SKL_HIT");

            var session = NewSession(lab, Setup(Line("CHR_A"), Line("ENM_A")));

            Assert.AreEqual(
                BattleCommandRejection.NoActiveTurn,
                session.TryEscape().Rejection,
                "还没轮到谁的时候，逃跑错在「没有行动者」，不是别的。");

            session.BeginNextTurn();
            Assert.IsTrue(session.TryEscape().Escaped);

            Assert.AreEqual(
                BattleCommandRejection.BattleFinished,
                session.TryEscape().Rejection,
                "战斗已经收场，再点逃跑只能报「已经结束」。");
        }

        /// <summary>
        /// 把成功率上下限钉死，让断言与掷骰的具体取值无关：
        /// 概率 1 时 [0,1) 内的任何掷骰都成功，概率 0 时任何掷骰都失败。
        /// </summary>
        private static void PinChance(BattleLab lab, float chance)
        {
            BattleLab.SetPrivate(lab.Config, "_escapeMinChance", chance);
            BattleLab.SetPrivate(lab.Config, "_escapeMaxChance", chance);
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
