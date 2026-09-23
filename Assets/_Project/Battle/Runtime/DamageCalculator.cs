using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>一次伤害结算的全部输入。纯数据，便于在测试里构造极端场景。</summary>
    public readonly struct DamageInput
    {
        public DamageInput(
            int power,
            int attack,
            int defense,
            FiveElement attackElement = FiveElement.None,
            FiveElement defenderElement = FiveElement.None,
            bool defenderIsBroken = false,
            float attackModifier = 0f,
            float defenseModifier = 0f,
            float incomingModifier = 1f,
            int hitIndex = 0,
            int defenderMaxHealth = 0)
        {
            Power = power;
            Attack = attack;
            Defense = defense;
            AttackElement = attackElement;
            DefenderElement = defenderElement;
            DefenderIsBroken = defenderIsBroken;
            AttackModifier = attackModifier;
            DefenseModifier = defenseModifier;
            IncomingModifier = incomingModifier;
            HitIndex = hitIndex;
            DefenderMaxHealth = defenderMaxHealth;
        }

        public int Power { get; }

        public int Attack { get; }

        public int Defense { get; }

        public FiveElement AttackElement { get; }

        public FiveElement DefenderElement { get; }

        public bool DefenderIsBroken { get; }

        /// <summary>攻击方攻击加成，0.2 表示 +20%。</summary>
        public float AttackModifier { get; }

        /// <summary>防御方防御加成，0.5 表示 +50% 减伤权重。</summary>
        public float DefenseModifier { get; }

        /// <summary>承伤倍率，用于易伤 / 抗性状态。</summary>
        public float IncomingModifier { get; }

        /// <summary>多段攻击的段序号，从 0 起。</summary>
        public int HitIndex { get; }

        public int DefenderMaxHealth { get; }
    }

    /// <summary>结算结果。把中间项一并带出来，便于战斗日志与调试面板显示「为什么是这个数」。</summary>
    public readonly struct DamageResult
    {
        public DamageResult(int damage, int rawDamage, float elementMultiplier, float brokenMultiplier, float hitDecay)
        {
            Damage = damage;
            RawDamage = rawDamage;
            ElementMultiplier = elementMultiplier;
            BrokenMultiplier = brokenMultiplier;
            HitDecay = hitDecay;
        }

        public int Damage { get; }

        public int RawDamage { get; }

        public float ElementMultiplier { get; }

        public float BrokenMultiplier { get; }

        public float HitDecay { get; }

        public bool IsCritical => ElementMultiplier > 1f;
    }

    /// <summary>
    /// 纯函数伤害计算。不触碰 Unity 对象、不读时间、不使用全局随机，
    /// 因此可以被单元测试穷举，也保证「同种子同结果」。
    /// </summary>
    public static class DamageCalculator
    {
        public static DamageResult Compute(BattleConfig config, in DamageInput input)
        {
            if (config == null)
            {
                throw new System.ArgumentNullException(nameof(config));
            }

            var raw = input.Power
                      + (input.Attack * config.AttackScale)
                      - (input.Defense * config.DefenseScale);

            raw *= 1f + input.AttackModifier;

            // 防御加成以分母形式生效，避免出现负数与除零式爆炸。
            raw /= 1f + Mathf.Max(0f, input.DefenseModifier);

            var elementMultiplier = config.GetElementMultiplier(input.AttackElement, input.DefenderElement);
            raw *= elementMultiplier;

            if (input.DefenderIsBroken)
            {
                raw *= config.BrokenIncomingMultiplier;
                raw += input.DefenderMaxHealth * config.BrokenBonusHealthRatio;
            }

            var hitDecay = input.HitIndex > 0 ? config.GetHitDecay(input.HitIndex) : 1f;
            raw *= hitDecay;

            raw *= Mathf.Max(0f, input.IncomingModifier);

            var rounded = Mathf.RoundToInt(raw);
            var damage = Mathf.Max(config.MinimumDamage, rounded);

            return new DamageResult(
                damage,
                Mathf.RoundToInt(raw),
                elementMultiplier,
                input.DefenderIsBroken ? config.BrokenIncomingMultiplier : 1f,
                hitDecay);
        }

        /// <summary>一段技能的总伤害。多段攻击逐段结算，破防收益更高。</summary>
        public static int ComputeSkillTotal(
            BattleConfig config,
            int power,
            int attack,
            int defense,
            int hitCount,
            FiveElement attackElement,
            FiveElement defenderElement,
            bool defenderIsBroken,
            int defenderMaxHealth,
            float attackModifier = 0f,
            float defenseModifier = 0f)
        {
            var total = 0;
            var hits = Mathf.Max(1, hitCount);
            for (var i = 0; i < hits; i++)
            {
                var result = Compute(
                    config,
                    new DamageInput(
                        power,
                        attack,
                        defense,
                        attackElement,
                        defenderElement,
                        defenderIsBroken,
                        attackModifier,
                        defenseModifier,
                        hitIndex: i,
                        defenderMaxHealth: defenderMaxHealth));
                total += result.Damage;
            }

            return total;
        }

        /// <summary>
        /// 护体值削减。段数越多削减越高，这正是「多段技能更适合破防」的机制来源。
        /// </summary>
        public static int ComputeBreakDamage(int breakDamage, int hitCount, float breakModifier = 1f)
        {
            var hits = Mathf.Max(1, hitCount);
            var perHit = Mathf.Max(0, breakDamage);
            return Mathf.Max(0, Mathf.RoundToInt(perHit * hits * Mathf.Max(0f, breakModifier)));
        }

        /// <summary>按当前速度推进行动值，返回行动条上的下一个行动者序号。</summary>
        public static int FindNextActor(BattleConfig config, System.Collections.Generic.IReadOnlyList<int> actionValues)
        {
            if (actionValues == null || actionValues.Count == 0)
            {
                return -1;
            }

            var best = 0;
            for (var i = 1; i < actionValues.Count; i++)
            {
                if (actionValues[i] < actionValues[best])
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
