using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.UI;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗循环驱动器（<see cref="BattleScreenController"/>）的行为锁定。
    /// </summary>
    /// <remarks>
    /// 这一层把内核的回合推进收拢成「等我方下令 / 等选目标 / 已结束」三态，
    /// 因此整个最小可玩回路可以在 EditMode 里跑到结局，不必先有场景与界面。
    /// </remarks>
    [TestFixture]
    public sealed class BattleScreenControllerTests
    {
        private const string Encounter = "ENC_TEST_SCREEN";

        [Test]
        public void 开工之前_什么指令都不接()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out _);

            Assert.AreEqual(BattlePrompt.NotStarted, screen.Prompt);
            Assert.IsFalse(screen.ChooseSkill("SKL_HIT"), "还没开工就不该接指令。");
            Assert.IsFalse(screen.ChooseTarget(1));
            Assert.IsFalse(screen.ChooseFlee());
            Assert.IsFalse(screen.ChooseEndTurn());
            Assert.IsFalse(screen.ChooseDefend());
            Assert.IsFalse(screen.ChooseMove(BattleFormation.SlotForIndex(1)));
            Assert.IsFalse(screen.ChooseSwap(1));
        }

        [Test]
        public void 防御走通一手_主行动用掉并留下守势()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.IsTrue(screen.ChooseDefend());
            Assert.IsTrue(screen.LastResult.Success);
            Assert.AreEqual(BattleActionKind.Defend, screen.LastResult.Kind);

            Assert.IsNotNull(
                session.PlayerUnits[0].FindStatus(lab.Config.DefendStatusId),
                "防御必须落进内核，界面上不能只是个装饰按钮。");
            Assert.AreEqual(1, screen.Hud.PlayerRows[0].Statuses.Count, "快照里也该多出一枚守势。");
            Assert.IsFalse(screen.Hud.PlayerRows[0].Statuses[0].IsDebuff);

            Assert.AreEqual(
                BattlePrompt.PlayerMoveOrSwap,
                screen.Prompt,
                "防御占主行动，所以之后停在移动窗口，而不是替他结束回合。");
            Assert.AreEqual(TurnPhase.MoveOrSwap, session.Phase);
            Assert.IsFalse(screen.ChooseDefend(), "主行动已经用掉，防御不该再接。");
            Assert.IsFalse(screen.ChooseFlee(), "主行动已经用掉，逃跑也不该再接。");

            Assert.IsTrue(screen.ChooseEndTurn(), "移动窗口里必须交得出去。");
            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt, "敌方走完之后再轮回我方。");
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
        }

        [Test]
        public void 开局推进_停在等我方下令()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
            Assert.AreEqual(TurnPhase.MainAction, session.Phase);
            CollectionAssert.IsNotEmpty(screen.Hud.Commands, "轮到我方时界面必须有按钮可画。");
        }

        [Test]
        public void 单体技_先要求选目标_候选只有敌人()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt);
            Assert.AreEqual("SKL_HIT", screen.PendingSkillId);
            CollectionAssert.AreEqual(
                new[] { session.EnemyUnits[0].RuntimeId },
                screen.TargetCandidateIds,
                "单体攻击的候选只有敌方存活单位。");
        }

        [Test]
        public void 辅助技_候选是自己人()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.HealingSkill("SKL_HEAL", healPower: 20);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HEAL");
            lab.Character("CHR_B", 300, 10, 5, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 10, FiveElement.None, 900, false, "SKL_HIT");

            var screen = BuildScreen(lab, Line("CHR_A", "CHR_B"), Line("ENM_A"), out var session);
            screen.Start();

            Assert.IsTrue(screen.ChooseSkill("SKL_HEAL"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt);
            CollectionAssert.AreEquivalent(
                new[] { session.PlayerUnits[0].RuntimeId, session.PlayerUnits[1].RuntimeId },
                screen.TargetCandidateIds,
                "治疗技的候选是同阵营存活单位，包括自己。");

            // 升序是破平局口径的一部分，界面据此决定默认选中谁。
            for (var i = 1; i < screen.TargetCandidateIds.Count; i++)
            {
                Assert.Less(screen.TargetCandidateIds[i - 1], screen.TargetCandidateIds[i]);
            }
        }

        [Test]
        public void 取消选目标_退回下令()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out _);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");

            Assert.IsTrue(screen.CancelTargeting());
            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.IsNull(screen.PendingSkillId);
            CollectionAssert.IsEmpty(screen.TargetCandidateIds);
        }

        [Test]
        public void 候选之外的目标_不提交给内核()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");

            var before = session.EnemyUnits[0].Health;
            Assert.IsFalse(screen.ChooseTarget(session.PlayerUnits[0].RuntimeId), "自己不是这个技能的合法目标。");
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt, "非法点击不该改变状态。");
            Assert.AreEqual(before, session.EnemyUnits[0].Health);
        }

        [Test]
        public void 用完主行动_停在移动窗口_交棒后敌方走完再轮回我方()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            screen.ChooseSkill("SKL_HIT");
            Assert.IsTrue(screen.ChooseTarget(session.EnemyUnits[0].RuntimeId));

            Assert.AreEqual(
                BattlePrompt.PlayerMoveOrSwap,
                screen.Prompt,
                "主行动结算完还剩一次可选的移动／换位，该交给玩家决定，而不是替他结束回合。");
            Assert.IsFalse(screen.ChooseSkill("SKL_HIT"), "主行动用掉了，技能不该再接。");

            Assert.IsTrue(screen.ChooseEndTurn(), "不想挪就得能交棒，否则玩家卡在空转里。");
            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt, "中间不该停在敌方回合上。");
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
            Assert.Greater(session.ActionCount, 2, "我方一手 + 敌方一手 + 再轮到我方。");
            Assert.Less(screen.Hud.EnemyRows[0].Health, 3000, "敌方掉血了，说明攻击确实打进了内核。");
        }

        [Test]
        public void 主行动之前_可以先挪一步再出招()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();

            var actor = session.PlayerUnits[0];
            var origin = actor.Slot;
            var destination = screen.Hud.MoveCandidates[0];

            Assert.IsTrue(screen.ChooseMove(destination));
            Assert.IsTrue(screen.LastResult.Success);
            Assert.AreEqual(BattleActionKind.Swap, screen.LastResult.Kind, "移动与换位在核心里是同一种动作。");
            Assert.AreEqual(destination, actor.Slot, "落点必须真的落到内核里，界面上不能只是画一下。");
            Assert.AreNotEqual(origin, actor.Slot);
            Assert.AreEqual(
                BattlePrompt.PlayerCommand,
                screen.Prompt,
                "移动不占主行动，挪完还得继续出招。");

            // 走用掉了，这一回合不该再给第二颗移动／换位按钮。
            Assert.IsEmpty(screen.Hud.MoveCandidates);
            Assert.IsTrue(screen.ChooseMove(origin), "这次点击仍然被界面收下——合不合法由内核说了算。");
            Assert.IsFalse(screen.LastResult.Success, "一回合只能走一步，第二手内核会拒。");

            Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
            Assert.IsTrue(screen.ChooseTarget(session.EnemyUnits[0].RuntimeId));
            Assert.AreEqual(BattlePrompt.PlayerMoveOrSwap, screen.Prompt, "打完之后才进移动窗口。");
        }

        [Test]
        public void 换位走通一手_两边站位互换()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Character("CHR_B", 300, 10, 5, 20, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 10, FiveElement.None, 900, false, "SKL_HIT");

            var screen = BuildScreen(lab, Line("CHR_A", "CHR_B"), Line("ENM_A"), out var session);
            screen.Start();

            var actor = session.PlayerUnits[0];
            var ally = session.PlayerUnits[1];
            var actorSlot = actor.Slot;
            var allySlot = ally.Slot;

            Assert.IsTrue(screen.ChooseSwap(ally.RuntimeId));
            Assert.IsTrue(screen.LastResult.Success);
            Assert.AreEqual(allySlot, actor.Slot, "换位就是两边对调。");
            Assert.AreEqual(actorSlot, ally.Slot);
            Assert.AreEqual(
                BattlePrompt.PlayerCommand,
                screen.Prompt,
                "换位不占主行动，换完还得继续出招。");
        }

        [Test]
        public void 结束回合_也会把敌方走完再轮回我方()
        {
            using var lab = new BattleLab();
            BuildDuel(lab);

            var screen = NewScreen(lab, out var session);
            screen.Start();
            Assert.IsTrue(screen.ChooseEndTurn());

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.AreEqual(session.PlayerUnits[0].RuntimeId, screen.Hud.CurrentActorRuntimeId);
        }

        [Test]
        public void 打光敌人_结局是胜利()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 200, breakDamage: 0);
            lab.Character("CHR_A", 300, 300, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 40, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 64)
            {
                if (screen.Prompt == BattlePrompt.PlayerMoveOrSwap)
                {
                    // 每轮都先交棒：移动窗口留着不走会卡住。
                    Assert.IsTrue(screen.ChooseEndTurn());
                    continue;
                }

                Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
                Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
                Assert.IsTrue(screen.ChooseTarget(session.EnemyUnits[0].RuntimeId));
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerVictory, session.Outcome);
            Assert.AreEqual(BattleOutcome.PlayerVictory, screen.Hud.Outcome);
            CollectionAssert.IsEmpty(screen.Hud.Commands, "打完了就不该再有按钮。");
        }

        [Test]
        public void 我方全灭_结局是失败()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 1, breakDamage: 0);
            lab.Character("CHR_A", 20, 1, 0, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 400, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 64)
            {
                Assert.IsTrue(
                    screen.Prompt == BattlePrompt.PlayerCommand
                    || screen.Prompt == BattlePrompt.PlayerMoveOrSwap,
                    "只该停在等我方下令或移动窗口这两个状态上。");
                Assert.IsTrue(screen.ChooseEndTurn());
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerDefeat, session.Outcome);
        }

        [Test]
        public void 逃跑成功_结局是逃脱()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 1, breakDamage: 0);
            lab.Character("CHR_A", 3000, 1, 50, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 100, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var screen = NewScreen(lab, out var session);
            screen.Start();

            Assert.Greater(screen.Hud.EscapeChance, 0.5f, "我方想跑，成功率该偏向高的一端。");

            // 掷骰是确定性的（固定种子），但「第几次才成功」不该被写死，
            // 因此循环到挣脱为止，并给一个远超需要的上限。
            var guard = 0;
            while (screen.Prompt != BattlePrompt.BattleEnded && guard++ < 500)
            {
                if (screen.Prompt == BattlePrompt.PlayerMoveOrSwap)
                {
                    // 逃跑失败只是白费这一手，主行动仍算用掉，于是也会进移动窗口。
                    Assert.IsTrue(screen.ChooseEndTurn());
                    continue;
                }

                Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
                Assert.IsTrue(screen.ChooseFlee());
            }

            Assert.AreEqual(BattlePrompt.BattleEnded, screen.Prompt);
            Assert.AreEqual(BattleOutcome.PlayerEscaped, session.Outcome);
            Assert.IsTrue(screen.LastResult.Escaped);
        }

        [Test]
        public void 选道具_点我方目标_结算后进移动窗口()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Item("ITM_POTION", ItemEffectKeys.HealHealth, 40);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add("ITM_POTION", 2);

            var screen = BuildScreen(lab, Line("CHR_A"), Line("ENM_A"), out var session, inventory);
            screen.Start();
            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);

            Assert.IsTrue(screen.ChooseItem("ITM_POTION"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt, "道具一律先让玩家点人。");
            Assert.AreEqual("ITM_POTION", screen.PendingItemId);
            Assert.IsNull(screen.PendingSkillId, "技能与道具的待定值互斥。");
            Assert.AreEqual(
                new[] { session.PlayerUnits[0].RuntimeId },
                screen.TargetCandidateIds.ToArray(),
                "本版道具只作用于我方单体，候选就是我方还站着的人。");

            var target = session.PlayerUnits[0];
            Assert.IsTrue(screen.ChooseTarget(target.RuntimeId));

            Assert.IsTrue(screen.LastResult.Success);
            Assert.AreEqual(BattleActionKind.UseItem, screen.LastResult.Kind);
            Assert.AreEqual("ITM_POTION", screen.LastResult.ItemId);
            Assert.AreEqual(1, inventory.CountOf("ITM_POTION"), "用掉一件就少一个。");
            Assert.IsNull(screen.PendingItemId, "结算完就该把待定值清掉。");
            Assert.AreEqual(BattlePrompt.PlayerMoveOrSwap, screen.Prompt, "道具同样占掉主行动。");
        }

        [Test]
        public void 选了技能再取消改选道具_结算的是道具()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Item("ITM_POTION", ItemEffectKeys.HealHealth, 40);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add("ITM_POTION", 1);

            var screen = BuildScreen(lab, Line("CHR_A"), Line("ENM_A"), out var session, inventory);
            screen.Start();

            Assert.IsTrue(screen.ChooseSkill("SKL_HIT"));
            Assert.AreEqual(BattlePrompt.PlayerTarget, screen.Prompt);
            Assert.IsFalse(
                screen.ChooseItem("ITM_POTION"),
                "正在给技能点目标时，道具不该插队——先取消再改主意。");
            Assert.AreEqual("SKL_HIT", screen.PendingSkillId, "被拒的输入不许动待定值。");
            Assert.IsNull(screen.PendingItemId);

            Assert.IsTrue(screen.CancelTargeting());
            Assert.IsTrue(screen.ChooseItem("ITM_POTION"));
            Assert.IsNull(screen.PendingSkillId, "技能那一笔待定值必须被清掉，否则会拿它去结算。");
            Assert.AreEqual("ITM_POTION", screen.PendingItemId);

            Assert.IsTrue(screen.ChooseTarget(session.PlayerUnits[0].RuntimeId));

            Assert.AreEqual(BattleActionKind.UseItem, screen.LastResult.Kind);
            Assert.IsNull(screen.LastResult.SkillId);
            Assert.AreEqual(
                session.PlayerUnits[0].MaxHealth,
                session.PlayerUnits[0].Health,
                "结算的是道具（回血），不是那个打向敌人的技能。");
        }

        [Test]
        public void 选了道具再取消_回到下令且不动背包()
        {
            using var lab = new BattleLab();
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Item("ITM_POTION", ItemEffectKeys.HealHealth, 40);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            var inventory = new BattleInventory();
            inventory.Add("ITM_POTION", 1);

            var screen = BuildScreen(lab, Line("CHR_A"), Line("ENM_A"), out var session, inventory);
            screen.Start();

            Assert.IsTrue(screen.ChooseItem("ITM_POTION"));
            Assert.IsTrue(screen.CancelTargeting());

            Assert.AreEqual(BattlePrompt.PlayerCommand, screen.Prompt);
            Assert.IsNull(screen.PendingItemId);
            Assert.IsEmpty(screen.TargetCandidateIds);
            Assert.AreEqual(1, inventory.CountOf("ITM_POTION"), "取消不该扣背包。");
            Assert.AreEqual(TurnPhase.MainAction, session.Phase, "取消也不该吃掉主行动。");
        }

        private static BattleScreenController NewScreen(BattleLab lab, out BattleSession session) =>
            BuildScreen(lab, Line("CHR_A"), Line("ENM_A"), out session);

        private static BattleScreenController BuildScreen(
            BattleLab lab,
            List<BattleUnitBlueprint> party,
            List<BattleUnitBlueprint> enemies,
            out BattleSession session,
            IBattleInventory inventory = null)
        {
            // 会话与界面共用同一个目录实例：各取一个虽然结果相同，但那是两处会各自漂移的来源。
            var registry = lab.Registry();
            session = new BattleSession(
                lab.Config,
                registry,
                BattleLab.Stream(),
                new BattleSetup(Encounter, party, enemies, inventory: inventory));
            return new BattleScreenController(session, registry);
        }

        private static void BuildDuel(BattleLab lab)
        {
            lab.AttackSkill("SKL_HIT", power: 20, breakDamage: 0);
            lab.Character("CHR_A", 300, 10, 5, 30, FiveElement.None, 30, 50, "SKL_HIT");
            lab.Enemy("ENM_A", 3000, 1, 0, 20, FiveElement.None, 900, false, "SKL_HIT");

            // 按数据表里的口径登记守势：防御指令挂的就是它（减伤 50%、持续 2 回合、增益）。
            lab.Status(
                lab.Config.DefendStatusId,
                durationTurns: 2,
                stackRule: StackRule.Refresh,
                maxStacks: 1,
                isDebuff: false,
                incomingDamageModifier: 0.5f);
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
    }
}
