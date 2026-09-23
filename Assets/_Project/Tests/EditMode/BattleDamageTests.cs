using System;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
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
            Assert.AreEqual(1.5f, _config.BrokenIncomingMultiplier);
            Assert.AreEqual(2, _config.BrokenDurationTurns);
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

        [TestCase(FiveElement.Metal, FiveElement.Wood, 1.5f)]
        [TestCase(FiveElement.Wood, FiveElement.Earth, 1.5f)]
        [TestCase(FiveElement.Earth, FiveElement.Water, 1.5f)]
        [TestCase(FiveElement.Water, FiveElement.Fire, 1.5f)]
        [TestCase(FiveElement.Fire, FiveElement.Metal, 1.5f)]
        [TestCase(FiveElement.Wood, FiveElement.Metal, 0.75f)]
        [TestCase(FiveElement.Fire, FiveElement.Water, 0.75f)]
        [TestCase(FiveElement.Metal, FiveElement.Metal, 1f)]
        [TestCase(FiveElement.Water, FiveElement.Water, 1f)]
        [TestCase(FiveElement.Metal, FiveElement.Water, 1f)]
        [TestCase(FiveElement.None, FiveElement.Fire, 1f)]
        [TestCase(FiveElement.Fire, FiveElement.None, 1f)]
        public void GetElementMultiplier_FollowsFiveElementCycle(FiveElement attacker, FiveElement defender, float expected)
        {
            Assert.AreEqual(expected, _config.GetElementMultiplier(attacker, defender));
        }

        [Test]
        public void Compute_RestrainingElement_IsFlaggedCritical()
        {
            var result = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, FiveElement.Metal, FiveElement.Wood));

            Assert.AreEqual(1.5f, result.ElementMultiplier);
            Assert.IsTrue(result.IsCritical);
            Assert.AreEqual(45, result.Damage, "30 * 1.5 = 45。");
        }

        [Test]
        public void Compute_Broken_PaysMultiplierPlusMaxHealthRatio()
        {
            var normal = DamageCalculator.Compute(_config, new DamageInput(10, 30, 20, defenderMaxHealth: 200));
            var broken = DamageCalculator.Compute(
                _config,
                new DamageInput(10, 30, 20, defenderIsBroken: true, defenderMaxHealth: 200));

            Assert.AreEqual(30, normal.Damage);
            Assert.AreEqual(55, broken.Damage, "30 * 1.5 + 200 * 0.05 = 55。");
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
            // 单段：(10 + 40 - 10) * 1.5 + 200 * 0.05 = 70
            // 三段：70 + round(70 * 0.85) + round(70 * 0.85^2) = 70 + 60 + 51
            var single = DamageCalculator.ComputeSkillTotal(_config, 10, 40, 20, 1, FiveElement.None, FiveElement.None, true, 200);
            var triple = DamageCalculator.ComputeSkillTotal(_config, 10, 40, 20, 3, FiveElement.None, FiveElement.None, true, 200);

            Assert.AreEqual(70, single);
            Assert.AreEqual(181, triple, "破防收益按段数放大，这正是「多段更适合破防」的来源。");
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
    }
}
