using System.Collections.Generic;
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
    /// 它直接跑在 CSV 导入出来的资产上，所以策划改表会立刻体现为回合数变化。四条设计目标：
    /// 常规遭遇 4–6 回合、隐藏遭遇 3 回合内、Boss 10–15 回合、单个回合不得吃掉 Boss 两成半以上的血。
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
        /// 隐藏遭遇（OPT_CH01_001）的节奏目标：3 回合内解决。
        /// 它没有 EXP、属「愿者上钩」的内容，短而险比拖长更合理（2026-10-08 拍板，
        /// 取代此前「暂按常规 4–6 对待」的临时口径）。
        /// </summary>
        private const int HiddenMaxRounds = 3;

        /// <summary>
        /// Boss 每个阶段至少要占的回合数。P2（急速）与 P4（最后一难）此前各只有 1 个回合，
        /// 玩家还没看清机制就滑过去了——这条断言就是那个问题的守卫（2026-10-08）。
        /// </summary>
        private const int MinBossPhaseRounds = 2;

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

        private DefinitionCatalog _catalog;
        private BattleConfig _config;
        private PacingReport _report;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // 强制全量导入，保证断言针对的是策划表当前的内容，而不是上一次留下的生成物。
            var summary = CsvImporter.ImportAll(force: true);
            Assert.AreEqual(0, summary.Report.ErrorCount, "导入本身有错，节奏校算的前提不成立。");

            _catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            Assert.IsNotNull(_catalog, "导入后必须存在定义目录资产。");
            _catalog.Rebuild();

            _config = BattleConfig.CreateDefault();
            _report = EncounterPacingEstimator.EstimateChapter(_config, _catalog, ChapterOne);

            TestContext.WriteLine($"第一章节奏校算（共导入 {summary.RowCount} 行，孤儿资产 {summary.OrphanAssets} 个）：");
            foreach (var pacing in _report.Encounters)
            {
                TestContext.WriteLine("  " + pacing);

                // Boss 额外打一条回合血线：阶段停留、破防窗口落在哪几回合，全靠它才看得见。
                if (pacing.IsBoss)
                {
                    TestContext.WriteLine($"      {pacing.EncounterId} 逐回合剩余总血：{Join(pacing.RoundEndTotalHealth)}");
                }
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
            var hiddenCount = 0;
            var bossCount = 0;
            foreach (var pacing in _report.Encounters)
            {
                if (pacing.IsBoss)
                {
                    bossCount++;
                }
                else if (pacing.IsHidden)
                {
                    hiddenCount++;
                }
                else
                {
                    normalCount++;
                }
            }

            Assert.GreaterOrEqual(normalCount, 5, "第一章常规遭遇至少 5 场（ENC_CH01_001–005），校算不应漏掉。");
            Assert.GreaterOrEqual(hiddenCount, 1, "第一章应当有隐藏遭遇（OPT_CH01_001），校算不应漏掉。");
            Assert.AreEqual(1, bossCount, "第一章应当只有一场 Boss 遭遇。");
        }

        [Test]
        public void ChapterOne_NormalEncounters_LastFourToSixRounds()
        {
            // 隐藏遭遇不在这条里：它有自己的目标（见 ChapterOne_HiddenEncounter_IsOverWithinThreeRounds），
            // 而 Boss 的 10–15 回合另有一条。这里只管 5 场常规遭遇。
            foreach (var pacing in _report.Encounters)
            {
                if (pacing.IsBoss || pacing.IsHidden)
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
        public void ChapterOne_HiddenEncounter_IsOverWithinThreeRounds()
        {
            var seen = 0;
            foreach (var pacing in _report.Encounters)
            {
                if (!pacing.IsHidden)
                {
                    continue;
                }

                seen++;
                Assert.LessOrEqual(
                    pacing.Rounds,
                    HiddenMaxRounds,
                    $"隐藏遭遇应在 {HiddenMaxRounds} 回合内解决，拖长了就不再是「愿者上钩」：{pacing}");
            }

            Assert.GreaterOrEqual(seen, 1, "第一章没有可校算的隐藏遭遇，这条断言等于没跑。");
        }

        /// <summary>
        /// 5 场常规遭遇要「由易到难」：按 ID 顺序，总血量与回合数都不得回退。
        /// 这是 2026-10-08 拍板「6 场遭遇由易到难」的落地——此前 5 场全部压在上界 6 回合，
        /// 首章没有难度曲线，玩家感觉不到自己在变强。
        /// </summary>
        [Test]
        public void ChapterOne_NormalEncounters_GetHarderInStoryOrder()
        {
            var normal = new List<EncounterPacing>();
            foreach (var pacing in _report.Encounters)
            {
                if (!pacing.IsBoss && !pacing.IsHidden)
                {
                    normal.Add(pacing);
                }
            }

            // ID 是 ENC_CH01_001…005，定长补零，所以序数比较就是章节序号比较。
            normal.Sort((left, right) => string.CompareOrdinal(left.EncounterId, right.EncounterId));
            Assert.GreaterOrEqual(normal.Count, 5, "第一章常规遭遇至少 5 场。");

            for (var i = 1; i < normal.Count; i++)
            {
                var previous = normal[i - 1];
                var current = normal[i];

                Assert.GreaterOrEqual(
                    current.TotalHealth,
                    previous.TotalHealth,
                    $"难度曲线回退：{current.EncounterId} 的总血量 {current.TotalHealth} 低于 {previous.EncounterId} 的 {previous.TotalHealth}。");

                Assert.GreaterOrEqual(
                    current.Rounds,
                    previous.Rounds,
                    $"难度曲线回退：{current.EncounterId} 的 {current.Rounds} 回合少于 {previous.EncounterId} 的 {previous.Rounds} 回合。");
            }

            var first = normal[0];
            var last = normal[normal.Count - 1];
            Assert.Greater(
                last.Rounds,
                first.Rounds,
                $"首章最后一场常规遭遇（{last.EncounterId}，{last.Rounds} 回合）必须比第一场（{first.EncounterId}，{first.Rounds} 回合）明显更长，" +
                "否则「由易到难」只是纸面说法。");
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
        /// Boss 的每一段都要「停留得住」——每个阶段至少占 2 个回合。
        /// 这条断言的来历：P2（急速）与 P4（最后一难）原先各只停留 1 个回合，血量被破防窗口推着走，
        /// 刚好卡在阶段阈值边上，玩家来不及看见机制。2026-10-08 把 Boss 血量抬到 1500 让窗口与阈值错开，
        /// 这里负责把「错开了多少」钉死。
        /// </summary>
        [Test]
        public void BossEncounter_EveryPhaseGetsAtLeastTwoRounds()
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
            var pacingValue = boss.Value;

            var thresholds = LoadBossPhaseThresholds(pacingValue.EncounterId);
            Assert.GreaterOrEqual(
                thresholds.Count,
                4,
                $"{pacingValue.EncounterId} 的阶段少于 4 个，阶段停留无从谈起。");

            var counts = CountPhaseRounds(pacingValue.RoundEndTotalHealth, pacingValue.TotalHealth, thresholds);
            for (var i = 0; i < counts.Length; i++)
            {
                Assert.GreaterOrEqual(
                    counts[i],
                    MinBossPhaseRounds,
                    $"Boss 第 {i + 1} 阶段只占了 {counts[i]} 个回合（血线：{Join(pacingValue.RoundEndTotalHealth)}）。" +
                    $"少于 {MinBossPhaseRounds} 个回合意味着玩家看不到这一段机制。");
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

        /// <summary>从 bossphases 表里取出该遭遇各阶段的阈值，按满血在前排序（1.0 / 0.7 / 0.4 / 0.15）。</summary>
        private List<float> LoadBossPhaseThresholds(string encounterId)
        {
            var thresholds = new List<float>();

            Assert.IsTrue(_catalog.TryGet(encounterId, out var definition), $"定义目录里找不到 {encounterId}。");
            var encounter = definition as EncounterDefinition;
            Assert.IsNotNull(encounter, $"{encounterId} 不是遭遇定义。");

            var phaseIds = encounter.BossPhaseIds;
            for (var i = 0; i < phaseIds.Length; i++)
            {
                Assert.IsTrue(_catalog.TryGet(phaseIds[i], out var phase), $"找不到 Boss 阶段 {phaseIds[i]}。");
                var bossPhase = phase as BossPhaseDefinition;
                Assert.IsNotNull(bossPhase, $"{phaseIds[i]} 不是 Boss 阶段定义。");
                thresholds.Add(bossPhase.HealthThreshold);
            }

            thresholds.Sort((left, right) => right.CompareTo(left));
            return thresholds;
        }

        /// <summary>
        /// 数一数每个阶段各占几个回合：按「回合结束时的血线落在哪一段」统计。
        /// 这样定义完全依赖 <see cref="EncounterPacing.RoundEndTotalHealth"/>，
        /// 不必猜「切换发生在回合中间还是结尾」——那种细节在数据里看不见。
        /// </summary>
        private static int[] CountPhaseRounds(
            IReadOnlyList<int> roundEndHealth,
            int maxHealth,
            IReadOnlyList<float> thresholds)
        {
            var counts = new int[thresholds.Count];

            for (var i = 0; i < roundEndHealth.Count; i++)
            {
                var health = roundEndHealth[i];

                // thresholds 满血在前：逐段往下走，走到血线还没跌破的那一段为止。
                var phase = 0;
                for (var candidate = 1; candidate < thresholds.Count; candidate++)
                {
                    if (health > thresholds[candidate] * maxHealth)
                    {
                        break;
                    }

                    phase = candidate;
                }

                counts[phase]++;
            }

            return counts;
        }

        private static string Join(IReadOnlyList<int> values)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(values[i]);
            }

            return builder.ToString();
        }
    }
}
