using System;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Editor;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 伤害结算是纯函数，因此必须能穷举验证。这里把 v1 数值口径（公式、五行倍率、破防、
    /// 多段衰减、行动值）逐条钉死，改动数值表时这些断言会立刻告诉你影响面。
    /// </summary>
    public sealed class BattleDamageTests
    {
        private BattleConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = BattleConfig.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void CreateDefault_MatchesDocumentedV1Values()
        {
            Assert.AreEqual(1.0f, _config.AttackScale);
            Assert.AreEqual(0.5f, _config.DefenseScale);
            Assert.AreEqual(1, _config.MinimumDamage);
            Assert.AreEqual(1.5f, _config.RestrainMultiplier);
            Assert.AreEqual(0.75f, _config.RestrainedMultiplier);
            Assert.AreEqual(
                1.15f,
                _config.GenerationUpMultiplier,
                "守方生攻方（金生水）算「借势」：相生参与战斗，但倍率低于相克——占便宜的主要手段仍是克制。");
            Assert.AreEqual(
                0.85f,
                _config.GenerationDownMultiplier,
                "攻方生守方（火生土）算「资敌」：五行从此没有「两两相安无事」的配对。");
            Assert.AreEqual(0.1f, _config.CriticalChance, "暴击与五行克制是两件事：克制是稳定倍率，暴击才是随机项。");
            Assert.AreEqual(1.5f, _config.CriticalMultiplier);
            Assert.AreEqual(1.5f, _config.BrokenIncomingMultiplier);
            Assert.AreEqual(2, _config.BrokenDurationTurns);
            Assert.AreEqual(
                0.02f,
                _config.BrokenBonusHealthRatio,
                "破防额外伤害按最大生命的 2% 计：5% 时一回合破防窗口能吃掉 Boss 六成血，阶段会被整段跳过（校算见 Docs/战斗数值-v1.md）。");
            Assert.AreEqual(0.85f, _config.MultiHitDecay);
            Assert.AreEqual(10000, _config.ActionValueBase);
            Assert.AreEqual(1, _config.MinSpeed);
            Assert.AreEqual(999, _config.MaxSpeed);
        }

        [Test]
        public void Compute_UsesDocumentedFormula()
        {
            // power + attack * 1.0 - defense * 0.5 = 10 + 30 - 10
            var result = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20));

            Assert.AreEqual(30, result.Damage);
            Assert.AreEqual(30, result.RawDamage);
            Assert.AreEqual(1f, result.ElementMultiplier);
            Assert.IsFalse(result.IsCritical);
        }

        [Test]
        public void Compute_NeverGoesBelowMinimumDamage()
        {
            var result = DamageCalculator.Compute(_config, new DamageInput(power: 0, attack: 0, defense: 1000));

            Assert.AreEqual(_config.MinimumDamage, result.Damage, "再高的防御也只能把伤害压到下限，不能变成 0 或负数。");
            Assert.Less(result.RawDamage, 0);
        }

        [Test]
        public void Compute_AttackAndDefenseModifiers_AreAppliedAsRatios()
        {
            var boosted = DamageCalculator.Compute(_config, new DamageInput(0, 100, 0, attackModifier: 0.5f));
            var guarded = DamageCalculator.Compute(_config, new DamageInput(0, 100, 0, defenseModifier: 1f));

            Assert.AreEqual(150, boosted.Damage);
            Assert.AreEqual(50, guarded.Damage, "防御加成走分母，避免出现负数伤害。");
        }

        [Test]
        public void Compute_IncomingModifier_ScalesDamage()
        {
            var softened = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20, incomingModifier: 0.5f));

            Assert.AreEqual(15, softened.Damage);
        }

        [Test]
        public void Compute_NegativeIncomingModifier_CLampsToMinimumDamage()
        {
            var result = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20, incomingModifier: -1f));

            Assert.AreEqual(_config.MinimumDamage, result.Damage);
        }

        [Test]
        public void Compute_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DamageCalculator.Compute(null, new DamageInput(1, 1, 1)));
        }

        [TestCase(FiveElement.Metal, FiveElement.Wood, 1.5f)]    // 金克木
        [TestCase(FiveElement.Wood, FiveElement.Earth, 1.5f)]    // 木克土
        [TestCase(FiveElement.Earth, FiveElement.Water, 1.5f)]   // 土克水
        [TestCase(FiveElement.Water, FiveElement.Fire, 1.5f)]    // 水克火
        [TestCase(FiveElement.Fire, FiveElement.Metal, 1.5f)]    // 火克金
        [TestCase(FiveElement.Metal, FiveElement.Earth, 1.15f)]  // 土生金：守方生攻方，借势
        [TestCase(FiveElement.Wood, FiveElement.Water, 1.15f)]   // 水生木：守方生攻方，借势
        [TestCase(FiveElement.Fire, FiveElement.Wood, 1.15f)]    // 木生火：守方生攻方，借势
        [TestCase(FiveElement.Earth, FiveElement.Fire, 1.15f)]   // 火生土：守方生攻方，借势
        [TestCase(FiveElement.Water, FiveElement.Metal, 1.15f)]  // 金生水：守方生攻方，借势
        [TestCase(FiveElement.Metal, FiveElement.Water, 0.85f)]  // 金生水：攻方生守方，资敌（旧口径下这里是 1.0）
        [TestCase(FiveElement.Water, FiveElement.Wood, 0.85f)]   // 水生木：攻方生守方，资敌
        [TestCase(FiveElement.Wood, FiveElement.Fire, 0.85f)]    // 木生火：攻方生守方，资敌
        [TestCase(FiveElement.Fire, FiveElement.Earth, 0.85f)]   // 火生土：攻方生守方，资敌
        [TestCase(FiveElement.Earth, FiveElement.Metal, 0.85f)]  // 土生金：攻方生守方，资敌
        [TestCase(FiveElement.Wood, FiveElement.Metal, 0.75f)]   // 金克木：被克
        [TestCase(FiveElement.Earth, FiveElement.Wood, 0.75f)]   // 木克土：被克
        [TestCase(FiveElement.Water, FiveElement.Earth, 0.75f)]  // 土克水：被克
        [TestCase(FiveElement.Fire, FiveElement.Water, 0.75f)]   // 水克火：被克
        [TestCase(FiveElement.Metal, FiveElement.Fire, 0.75f)]   // 火克金：被克
        [TestCase(FiveElement.Metal, FiveElement.Metal, 1f)]
        [TestCase(FiveElement.Water, FiveElement.Water, 1f)]
        [TestCase(FiveElement.None, FiveElement.Fire, 1f)]
        [TestCase(FiveElement.Fire, FiveElement.None, 1f)]
        public void GetElementMultiplier_FollowsFiveElementCycle(FiveElement attacker, FiveElement defender, float expected)
        {
            Assert.AreEqual(expected, _config.GetElementMultiplier(attacker, defender));
        }

        /// <summary>
        /// 25 种非无属性配对必须被相克环与相生环分完：克制／借势／同属／资敌／被克各 5 对，
        /// 加上 11 对「任一方无属性」正好 36 种。这条断言保证「相生」不是零星补丁——
        /// 一旦有人把某条关系写成落空的中性，这里立刻红。
        /// </summary>
        [Test]
        public void Relate_CoversEveryElementPairExactlyOnce()
        {
            var counts = new int[6];
            foreach (FiveElement attacker in Enum.GetValues(typeof(FiveElement)))
            {
                foreach (FiveElement defender in Enum.GetValues(typeof(FiveElement)))
                {
                    counts[(int)ElementRules.Relate(attacker, defender)]++;
                }
            }

            Assert.AreEqual(11, counts[(int)ElementRelation.None], "6 + 6 − 1 = 11 对含无属性。");
            Assert.AreEqual(5, counts[(int)ElementRelation.Restraining]);
            Assert.AreEqual(5, counts[(int)ElementRelation.GeneratedBy]);
            Assert.AreEqual(5, counts[(int)ElementRelation.Same]);
            Assert.AreEqual(5, counts[(int)ElementRelation.Generates]);
            Assert.AreEqual(5, counts[(int)ElementRelation.Restrained]);

            var total = 0;
            for (var i = 0; i < counts.Length; i++)
            {
                total += counts[i];
            }

            Assert.AreEqual(36, total, "6 种元素的有序配对数，一个都不能漏。");
        }

        [Test]
        public void Compute_RestrainingElement_IsElementAdvantageNotCritical()
        {
            var result = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, FiveElement.Metal, FiveElement.Wood));

            Assert.AreEqual(1.5f, result.ElementMultiplier);
            Assert.AreEqual(ElementRelation.Restraining, result.ElementRelation);
            Assert.IsTrue(result.IsElementAdvantage, "克制是稳定收益，用「五行占优」表达。");
            Assert.IsFalse(result.IsCritical, "克制不再顺带算暴击：暴击率没变，战斗日志也不会把克制说成暴击。");
            Assert.AreEqual(1f, result.CriticalMultiplier);
            Assert.AreEqual(45, result.Damage, "30 * 1.5 = 45。");
        }

        [Test]
        public void Compute_GenerationUp_IsElementAdvantage()
        {
            // 沙僧（水）打石猴投手（金）：金生水，是守方生攻方。
            var result = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, FiveElement.Water, FiveElement.Metal));

            Assert.AreEqual(ElementRelation.GeneratedBy, result.ElementRelation);
            Assert.IsTrue(result.IsElementAdvantage, "借势虽不如克制，但仍是「这一手打对了」。");
            Assert.AreEqual(34, result.Damage, "30 * 1.15 = 34.5，银行家舍入取 34。");
        }

        [Test]
        public void Compute_GenerationDown_IsPenalized()
        {
            // 悟空（火）打土系杂兵：火生土，是攻方生守方。
            var result = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, FiveElement.Fire, FiveElement.Earth));

            Assert.AreEqual(ElementRelation.Generates, result.ElementRelation);
            Assert.IsFalse(result.IsElementAdvantage);
            Assert.AreEqual(26, result.Damage, "30 * 0.85 = 25.5 → 26。");
        }

        [Test]
        public void IsCriticalRoll_IsExclusiveUpperBound()
        {
            Assert.IsTrue(_config.IsCriticalRoll(0f));
            Assert.IsTrue(_config.IsCriticalRoll(0.0999f));
            Assert.IsFalse(_config.IsCriticalRoll(_config.CriticalChance), "掷骰正好等于暴击率不算命中。");
            Assert.IsFalse(_config.IsCriticalRoll(DamageInput.NoCritical), "不传掷骰的调用方拿到的是「必定不暴击」。");
        }

        [Test]
        public void Compute_Critical_MultipliesTheWholeFormulaLast()
        {
            var plain = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20));
            var crit = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20, criticalRoll: 0f));
            var brokenCrit = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, defenderIsBroken: true, defenderMaxHealth: 200, criticalRoll: 0f));

            Assert.AreEqual(30, plain.Damage);
            Assert.IsFalse(plain.IsCritical);
            Assert.AreEqual(45, crit.Damage, "暴击是最后一道乘区：30 * 1.5 = 45。");
            Assert.IsTrue(crit.IsCritical);
            Assert.AreEqual(1.5f, crit.CriticalMultiplier);
            Assert.AreEqual(74, brokenCrit.Damage, "破防先算 30 * 1.5 + 200 * 0.02 = 49，再暴击 49 * 1.5 = 73.5 → 74。");
        }

        [Test]
        public void ComputeSkillTotal_RollsCriticalPerHit()
        {
            // 每段 raw 都是 40：首段 40、次段 40 * 0.85 = 34。
            var firstCrits = DamageCalculator.ComputeSkillTotal(
                _config, 10, 40, 20, 2, FiveElement.None, FiveElement.None, false, 0, criticalRolls: new[] { 0f, 1f });
            var secondCrits = DamageCalculator.ComputeSkillTotal(
                _config, 10, 40, 20, 2, FiveElement.None, FiveElement.None, false, 0, criticalRolls: new[] { 1f, 0f });
            var never = DamageCalculator.ComputeSkillTotal(
                _config, 10, 40, 20, 2, FiveElement.None, FiveElement.None, false, 0, criticalRolls: Array.Empty<float>());

            Assert.AreEqual(94, firstCrits, "60 + 34：多段技能有多次暴击机会，所以掷骰必须逐段给。");
            Assert.AreEqual(91, secondCrits, "40 + 51：同一次暴击落在衰减后的段上，收益更低。");
            Assert.AreEqual(74, never, "掷骰数量不足的段按不暴击算，别让漏传参数变成隐形增伤。");
            Assert.AreNotEqual(firstCrits, secondCrits, "逐段掷骰的意义就在这里：一次判定乘以全部段数会抹掉这个差别。");
        }

        [Test]
        public void Compute_Broken_PaysMultiplierPlusMaxHealthRatio()
        {
            var normal = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20, defenderMaxHealth: 200));
            var broken = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, defenderIsBroken: true, defenderMaxHealth: 200));

            Assert.AreEqual(30, normal.Damage);
            Assert.AreEqual(49, broken.Damage, "30 * 1.5 + 200 * 0.02 = 49。");
            Assert.AreEqual(1.5f, broken.BrokenMultiplier);
            Assert.AreEqual(1f, normal.BrokenMultiplier);
        }

        [Test]
        public void Compute_MultiHitDecay_OnlyFromSecondHit()
        {
            var first = DamageCalculator.Compute(_config, new DamageInput(10, 40, 20, hitIndex: 0));
            var second = DamageCalculator.Compute(_config, new DamageInput(10, 40, 20, hitIndex: 1));
            var third = DamageCalculator.Compute(_config, new DamageInput(10, 40, 20, hitIndex: 2));

            Assert.AreEqual(1f, first.HitDecay);
            Assert.AreEqual(40, first.Damage);
            Assert.AreEqual(0.85f, second.HitDecay, 0.0001f);
            Assert.AreEqual(34, second.Damage);
            Assert.AreEqual(29, third.Damage, "40 * 0.85^2 = 28.9 → 29。");
        }

        [Test]
        public void ComputeSkillTotal_MultiHitIsCheaperThanFlatRepeats()
        {
            var total = DamageCalculator.ComputeSkillTotal(
                _config,
                power: 10,
                attack: 40,
                defense: 20,
                hitCount: 3,
                FiveElement.None,
                FiveElement.None,
                defenderIsBroken: false,
                defenderMaxHealth: 0);

            Assert.AreEqual(103, total, "40 + 34 + 29：段数越多总伤害越低，这是多段换破防的代价。");
            Assert.Less(total, 120);
        }

        [Test]
        public void ComputeSkillTotal_TreatsNonPositiveHitCountAsSingleHit()
        {
            var total = DamageCalculator.ComputeSkillTotal(_config, 10, 40, 20, 0, FiveElement.None, FiveElement.None, false, 0);

            Assert.AreEqual(40, total);
        }

        [Test]
        public void ComputeSkillTotal_BrokenBonusIsPaidPerHitAndThenDecayed()
        {
            // 单段：(10 + 40 - 10) * 1.5 + 200 * 0.02 = 64
            // 三段：64 + round(64 * 0.85) + round(64 * 0.85^2) = 64 + 54 + 46
            var single = DamageCalculator.ComputeSkillTotal(_config, 10, 40, 20, 1, FiveElement.None, FiveElement.None, true, 200);
            var triple = DamageCalculator.ComputeSkillTotal(_config, 10, 40, 20, 3, FiveElement.None, FiveElement.None, true, 200);

            Assert.AreEqual(64, single);
            Assert.AreEqual(164, triple, "破防收益按段数放大，这正是「多段更适合破防」的来源。");
            Assert.Greater(triple, single);
        }

        [TestCase(10, 1, 1f, 10)]
        [TestCase(10, 3, 1f, 30)]
        [TestCase(10, 3, 0.5f, 15)]
        [TestCase(-5, 3, 1f, 0)]
        [TestCase(10, 0, 1f, 10)]
        public void ComputeBreakDamage_ScalesWithHits(int breakDamage, int hitCount, float modifier, int expected)
        {
            Assert.AreEqual(expected, DamageCalculator.ComputeBreakDamage(breakDamage, hitCount, modifier));
        }

        [TestCase(50, 200)]
        [TestCase(100, 100)]
        [TestCase(200, 50)]
        [TestCase(999, 10)]
        public void GetActionValue_FasterActsEarlier(int speed, int expected)
        {
            Assert.AreEqual(expected, _config.GetActionValue(speed));
        }

        [Test]
        public void GetActionValue_ClampsSpeedToConfiguredRange()
        {
            Assert.AreEqual(_config.GetActionValue(_config.MinSpeed), _config.GetActionValue(0), "速度 0 应被夹到下限而不是除零。");
            Assert.AreEqual(_config.GetActionValue(_config.MaxSpeed), _config.GetActionValue(99999));
            Assert.Greater(_config.GetActionValue(_config.MaxSpeed), 0);
        }

        [Test]
        public void FindNextActor_ReturnsLowestActionValue()
        {
            Assert.AreEqual(1, DamageCalculator.FindNextActor(_config, new[] { 50, 10, 30 }));
            Assert.AreEqual(0, DamageCalculator.FindNextActor(_config, new[] { 5, 10, 30 }));
        }

        [Test]
        public void FindNextActor_EmptyInput_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, DamageCalculator.FindNextActor(_config, null));
            Assert.AreEqual(-1, DamageCalculator.FindNextActor(_config, Array.Empty<int>()));
        }

        /// <summary>
        /// 资产与代码默认值必须逐字一致。这条断言是补上一个真实踩过的坑：
        /// 上一轮把破防额外伤害从 5% 降到 2% 时只改了代码，`BattleConfig_Default.asset` 仍留着 5%
        /// ——测试读的是 <c>CreateDefault()</c>、游戏读的是资产，于是「测试全绿」与「运行时用的是旧口径」同时成立。
        /// 改口径时请重新跑 ProjectSetup 生成资产，或明确只改一处并同步文档。
        /// </summary>
        [Test]
        public void DefaultAsset_MatchesCodeDefaults()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BattleConfig>(SamsaraWestPaths.BattleConfigAsset);
            Assert.IsNotNull(
                asset,
                $"{SamsaraWestPaths.BattleConfigAsset} 不存在：首次开工程时 ProjectSetup 应当生成它。");

            var code = BattleConfig.CreateDefault();
            try
            {
                AssertSame(nameof(code.AttackScale), code.AttackScale, asset.AttackScale);
                AssertSame(nameof(code.DefenseScale), code.DefenseScale, asset.DefenseScale);
                AssertSame(nameof(code.MinimumDamage), code.MinimumDamage, asset.MinimumDamage);
                AssertSame(nameof(code.RestrainMultiplier), code.RestrainMultiplier, asset.RestrainMultiplier);
                AssertSame(nameof(code.RestrainedMultiplier), code.RestrainedMultiplier, asset.RestrainedMultiplier);
                AssertSame(nameof(code.GenerationUpMultiplier), code.GenerationUpMultiplier, asset.GenerationUpMultiplier);
                AssertSame(
                    nameof(code.GenerationDownMultiplier),
                    code.GenerationDownMultiplier,
                    asset.GenerationDownMultiplier);
                AssertSame(nameof(code.SameElementMultiplier), code.SameElementMultiplier, asset.SameElementMultiplier);
                AssertSame(nameof(code.NeutralMultiplier), code.NeutralMultiplier, asset.NeutralMultiplier);
                AssertSame(nameof(code.CriticalChance), code.CriticalChance, asset.CriticalChance);
                AssertSame(nameof(code.CriticalMultiplier), code.CriticalMultiplier, asset.CriticalMultiplier);
                AssertSame(nameof(code.BrokenIncomingMultiplier), code.BrokenIncomingMultiplier, asset.BrokenIncomingMultiplier);
                AssertSame(nameof(code.BrokenDurationTurns), code.BrokenDurationTurns, asset.BrokenDurationTurns);
                AssertSame(nameof(code.BrokenBonusHealthRatio), code.BrokenBonusHealthRatio, asset.BrokenBonusHealthRatio);
                AssertSame(nameof(code.MultiHitDecay), code.MultiHitDecay, asset.MultiHitDecay);
                AssertSame(nameof(code.ActionValueBase), code.ActionValueBase, asset.ActionValueBase);
                AssertSame(nameof(code.MinSpeed), code.MinSpeed, asset.MinSpeed);
                AssertSame(nameof(code.MaxSpeed), code.MaxSpeed, asset.MaxSpeed);
                AssertSame(nameof(code.IntentPreviewLead), code.IntentPreviewLead, asset.IntentPreviewLead);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(code);
            }
        }

        private static void AssertSame(string field, float expected, float actual)
        {
            Assert.AreEqual(
                expected,
                actual,
                $"{field} 在资产与代码默认值之间不一致：资产是游戏真正读的那份，改了代码不等于改了资产。");
        }
    }
}
