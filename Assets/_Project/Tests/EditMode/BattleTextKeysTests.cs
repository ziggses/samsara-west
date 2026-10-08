using System;
using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Editor;
using SamsaraWest.Localization;
using SamsaraWest.UI;
using UnityEditor;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗界面的文本键与文本表的交叉校验。
    /// </summary>
    /// <remarks>
    /// 这里锁两件事：一是界面要用的键<b>确实在表里登记了</b>（否则界面上是一片空白而不是编译失败），
    /// 二是每个状态<b>显不显示文案</b>是个被明确写下来的决定，而不是漏了。
    /// </remarks>
    [TestFixture]
    public sealed class BattleTextKeysTests
    {
        private LocalizationService _text;

        [OneTimeSetUp]
        public void ImportTexts()
        {
            // 文本表要先是最新的，交叉校验才有意义（与 DataPipelineTests 同一套做法）。
            LocalizationImporter.ImportAll();

            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(SamsaraWestPaths.LocalizationTableAsset);
            Assert.IsNotNull(table, "文本表资产必须存在，否则界面的键无从校验。");
            _text = new LocalizationService(table);
        }

        [Test]
        public void 每一种结局都有标题_除了进行中()
        {
            foreach (BattleOutcome outcome in Enum.GetValues(typeof(BattleOutcome)))
            {
                var key = BattleTextKeys.Outcome(outcome);

                if (outcome == BattleOutcome.Ongoing)
                {
                    Assert.IsNull(key, "仗还没打完，不该有结局文案。");
                    continue;
                }

                Assert.IsNotNull(key, $"{outcome} 是玩家会看到的结局，必须有标题键。");
                Assert.IsTrue(_text.HasKey(key), $"{key} 没在文本表里登记。");
                Assert.IsNotEmpty(_text.Get(key), $"{key} 在表里是空的。");
            }
        }

        [Test]
        public void 战斗界面用到的每一个键_都在文本表里登记()
        {
            var keys = new List<string>
            {
                BattleTextKeys.Title,
                BattleTextKeys.Round,
                BattleTextKeys.CurrentActor,
                BattleTextKeys.TargetPrompt,
                BattleTextKeys.Health,
                BattleTextKeys.Break,
                BattleTextKeys.Flee,
                BattleTextKeys.Defend,
                BattleTextKeys.EndTurn,
                BattleTextKeys.EscapeChance,
            };

            foreach (BattleOutcome outcome in Enum.GetValues(typeof(BattleOutcome)))
            {
                var key = BattleTextKeys.Outcome(outcome);
                if (key != null)
                {
                    keys.Add(key);
                }
            }

            foreach (BattleCommandRejection rejection in Enum.GetValues(typeof(BattleCommandRejection)))
            {
                var key = BattleTextKeys.Rejection(rejection);
                if (key != null)
                {
                    keys.Add(key);
                }
            }

            foreach (var key in keys)
            {
                Assert.IsTrue(_text.HasKey(key), $"{key} 没在文本表里登记。");
                Assert.IsNotEmpty(_text.Get(key), $"{key} 在表里是空的。");
            }
        }

        [Test]
        public void 带参数的键_参数位都能填上()
        {
            StringAssert.Contains("3", _text.Format(BattleTextKeys.Round, 3));
            StringAssert.Contains("120", _text.Format(BattleTextKeys.Health, 120, 300));
            StringAssert.Contains("15", _text.Format(BattleTextKeys.Break, 15, 30));
            StringAssert.Contains("85%", _text.Format(BattleTextKeys.EscapeChance, "85%"));
        }

        [Test]
        public void 冷却与灵力不足_给出玩家能看懂的原因()
        {
            Assert.AreEqual(
                LocalizationKeys.UI_BATTLE_REJECTION_SKILL_ON_COOLDOWN,
                BattleTextKeys.Rejection(BattleCommandRejection.SkillOnCooldown));
            Assert.AreEqual(
                LocalizationKeys.UI_BATTLE_REJECTION_NOT_ENOUGH_SPIRIT,
                BattleTextKeys.Rejection(BattleCommandRejection.NotEnoughSpirit));
        }

        [Test]
        public void 不显示文案的拒绝原因_必须被显式列出来()
        {
            // 这是一条审计锁：内核新增一个拒绝原因时，这条用例会变红，
            // 逼你决定「它是否要让玩家看到」，而不是默默变成空白按钮。
            var silent = new List<BattleCommandRejection>();
            foreach (BattleCommandRejection rejection in Enum.GetValues(typeof(BattleCommandRejection)))
            {
                if (BattleTextKeys.Rejection(rejection) == null)
                {
                    silent.Add(rejection);
                }
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    BattleCommandRejection.None,
                    BattleCommandRejection.BattleFinished,
                    BattleCommandRejection.NoActiveTurn,
                    BattleCommandRejection.WrongPhase,
                    BattleCommandRejection.UnknownSkill,
                    BattleCommandRejection.SkillNotOwned,
                    BattleCommandRejection.NoValidTarget,
                    BattleCommandRejection.TargetDead,
                    BattleCommandRejection.TargetSideMismatch,
                    BattleCommandRejection.TargetIsSelf,
                    BattleCommandRejection.SwapTargetInvalid,
                    BattleCommandRejection.SlotOccupied,
                    BattleCommandRejection.SlotOutOfRange,
                    BattleCommandRejection.MoveAlreadyUsed,

                    // 数据错误：配置指的定义没登记，属于「该去修数据」而不是「该去劝玩家」。
                    BattleCommandRejection.DefinitionMissing,
                },
                silent,
                "拒绝原因的显隐名单变了：要么给新原因补文本键，要么把它写进这份名单。");
        }
    }
}
