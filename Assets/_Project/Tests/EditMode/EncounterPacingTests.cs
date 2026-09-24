using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Editor;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗节奏校算的断言（口径与推导见 Docs/战斗数值-v1.md）。
    /// 它直接跑在 CSV 导入出来的资产上，所以策划改表会立刻体现为回合数变化。三条设计目标：
    /// 常规遭遇 4–6 回合、Boss 10–15 回合、单个回合不得吃掉 Boss 两成半以上的血。
    /// 这些是设计目标、不是实现细节——数据一旦偏离，这里必须先红。
    /// </summary>
    public sealed class EncounterPacingTests
    {
        private const int ChapterOne = 1;
        private const int NormalMinRounds = 4;
        private const int NormalMaxRounds = 6;
        private const int BossMinRounds = 10;
        private const int BossMaxRounds = 15;

        /// <summary>
        /// 单个回合能打掉的最大生命上限。取 25% 是因为 Boss 阶段阈值 100%/70%/40%/15%
        /// 之间最窄的一段就是 25 个百分点——超过它，阶段必然被跨过。
        /// </summary>
        private const float MaxBossRoundHealthShare = 0.25f;

        /// <summary>
        /// 破防伤害允许占总量的一半。v1 的 55–68% 意味着「普攻只是攒破防的铺垫」，
        /// 破防成为战斗主旋律之后，护体条就成了真正的血条、普攻的打击感也被抽空。
        /// </summary>
        private const float MaxBrokenDamageShare = 0.5f;

        private BattleConfig _config;
        private PacingReport _report;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // 强制全量导入，保证断言针对的是策划表当前的内容，而不是上一次留下的生成物。
            var summary = CsvImporter.ImportAll(force: true);
            Assert.AreEqual(0, summary.Report.ErrorCount, "导入本身有错，节奏校算的前提不成立。");

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            Assert.IsNotNull(catalog, "导入后必须存在定义目录资产。");
            catalog.Rebuild();

            _config = BattleConfig.CreateDefault();
            _report = EncounterPacingEstimator.EstimateChapter(_config, catalog, ChapterOne);

            TestContext.WriteLine($"第一章节奏校算（共导入 {summary.RowCount} 行，孤儿资产 {summary.OrphanAssets} 个）：");
            foreach (var pacing in _report.Encounters)
            {
                TestContext.WriteLine("  " + pacing);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void ChapterOne_EveryEncounterCouldBeEstimated()
        {
            Assert.IsEmpty(_report.Problems, string.Join("；", _report.Problems));
            Assert.IsNotEmpty(_report.Encounters, "第一章应当有可以校算的遭遇。");

            var normalCount = 0;
            var bossCount = 0;
            foreach (var pacing in _report.Encounters)
            {
                if (pacing.IsBoss)
                {
                    bossCount++;
                }
                else
                {
                    normalCount++;
                }
            }

            Assert.GreaterOrEqual(normalCount, 5, "第一章常规遭遇至少 5 场（ENC_CH01_001–005），校算不应漏掉。");
            Assert.AreEqual(1, bossCount, "第一章应当只有一场 Boss 遭遇。");
        }

        [Test]
        public void ChapterOne_NormalEncounters_LastFourToSixRounds()
        {
            // 隐藏遭遇（OPT_CH01_001）目前暂按常规口径要求 4–6 回合：它是「无 EXP 的隐藏内容」，
            // 该不该比常规更短（奖励速战）或更长（考验准备）还没拍板，暂不单独放宽、免得悄悄漂走。
            foreach (var pacing in _report.Encounters)
            {
                if (pacing.IsBoss)
                {
                    continue;
                }

                Assert.That(
                    pacing.Rounds,
                    Is.InRange(NormalMinRounds, NormalMaxRounds),
                    $"常规遭遇应打 {NormalMinRounds}–{NormalMaxRounds} 回合：{pacing}");
            }
        }

        [Test]
        public void ChapterOne_BossEncounter_LastsTenToFifteenRounds()
        {
            EncounterPacing? boss = null;
            foreach (var pacing in _report.Encounters)
            {
                if (pacing.IsBoss)
                {
                    boss = pacing;
                    break;
                }
            }

            Assert.IsTrue(boss.HasValue, "第一章应存在 Boss 遭遇。");
            Assert.That(
                boss.Value.Rounds,
                Is.InRange(BossMinRounds, BossMaxRounds),
                $"Boss 战应打 {BossMinRounds}–{BossMaxRounds} 回合：{boss.Value}");
        }

        [Test]
        public void BossEncounter_NoSingleRoundSwallowsAWholePhase()
        {
            // Boss 四阶段阈值是 100% / 70% / 40% / 15%，最窄的一段只有 25 个百分点。
            // 一旦某个回合能打掉超过 25% 的最大生命，就必然跨过阶段边界、阶段机制沦为摆设——
            // 这正是破防额外伤害取「最大生命的 5%」时的症状：实测单回合峰值 28%。
            foreach (var pacing in _report.Encounters)
            {
                if (!pacing.IsBoss)
                {
                    continue;
                }

                Assert.LessOrEqual(
                    pacing.PeakRoundHealthShare,
                    MaxBossRoundHealthShare,
                    $"Boss 存在单回合打掉超过 {MaxBossRoundHealthShare:P0} 最大生命的情况，阶段会被整段跳过：{pacing}");
            }
        }

        /// <summary>
        /// 破防必须是「攒出来的窗口」，不能是战斗主旋律。这条断言是抬护体（38–52 → 90–120）的目的本身：
        /// 改数值的人如果把护体又降低、或把破防收益提高，这里会先红，而不是等到手感被玩出来才发现。
        /// </summary>
        [Test]
        public void BrokenDamage_NeverBecomesTheWholeFight()
        {
            foreach (var pacing in _report.Encounters)
            {
                Assert.LessOrEqual(
                    pacing.BrokenDamageShare,
                    MaxBrokenDamageShare,
                    $"破防伤害占比超过 {MaxBrokenDamageShare:P0}，破防又变成常态了：{pacing}");
            }
        }
    }
}
