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
            int defenderMaxHealth = 0,
            float criticalRoll = NoCritical)
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
            CriticalRoll = criticalRoll;
        }

        /// <summary>
        /// 「必定不暴击」的掷骰值。取 1 是因为判定用「小于」：暴击率封顶 1 时它也仍然不暴击，
        /// 于是不传掷骰的旧调用方与不掷骰的校算工具都拿到确定的结果。
        /// </summary>
        public const float NoCritical = 1f;

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

        /// <summary>
        /// 本次结算的暴击掷骰值（0–1）。由战斗流程从「战斗」随机流取出后传进来，
        /// 而不是让计算器自己去掷——它必须保持纯函数，否则测试无法穷举、录像也无法重放。
        /// </summary>
        public float CriticalRoll { get; }
    }

    /// <summary>结算结果。把中间项一并带出来，便于战斗日志与调试面板显示「为什么是这个数」。</summary>
    public readonly struct DamageResult
    {
        public DamageResult(
            int damage,
            int rawDamage,
            float elementMultiplier,
            float brokenMultiplier,
            float hitDecay,
            float criticalMultiplier = 1f,
            ElementRelation elementRelation = ElementRelation.None)
        {
            Damage = damage;
            RawDamage = rawDamage;
            ElementMultiplier = elementMultiplier;
            BrokenMultiplier = brokenMultiplier;
            HitDecay = hitDecay;
            CriticalMultiplier = criticalMultiplier;
            ElementRelation = elementRelation;
        }

        public int Damage { get; }

        public int RawDamage { get; }

        public float ElementMultiplier { get; }

        public float BrokenMultiplier { get; }

        public float HitDecay { get; }

        /// <summary>暴击倍率，未暴击为 1。</summary>
        public float CriticalMultiplier { get; }

        /// <summary>本次攻击的五行关系，供战斗日志显示「借势／资敌」而不是一个光秃秃的倍率。</summary>
        public ElementRelation ElementRelation { get; }

        /// <summary>是否暴击。它<b>只</b>由暴击掷骰决定，与五行克制无关。</summary>
        public bool IsCritical => CriticalMultiplier > 1f;

        /// <summary>是否吃到五行便宜（克制或借势），即五行倍率压过同属性基准。</summary>
        public bool IsElementAdvantage => ElementRelation == ElementRelation.Restraining ||
                                          ElementRelation == ElementRelation.GeneratedBy;
    }

    /// <summary>
    /// 纯函数伤害计算。不触碰 Unity 对象、不读时间、不使用全局随机，
    /// 连暴击掷骰也由调用方从「战斗」随机流传入，因此可以被单元测试穷举，也保证「同种子同结果」。
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

            var elementRelation = ElementRules.Relate(input.AttackElement, input.DefenderElement);
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

            // 暴击是最后一道独立乘区：它不是五行倍率的一部分，也不会被别的加成漏掉。
            var isCritical = config.IsCriticalRoll(input.CriticalRoll);
            var criticalMultiplier = isCritical ? config.CriticalMultiplier : 1f;
            raw *= criticalMultiplier;

            var rounded = Mathf.RoundToInt(raw);
            var damage = Mathf.Max(config.MinimumDamage, rounded);

            return new DamageResult(
                damage,
                Mathf.RoundToInt(raw),
                elementMultiplier,
                input.DefenderIsBroken ? config.BrokenIncomingMultiplier : 1f,
                hitDecay,
                criticalMultiplier,
                elementRelation);
        }

        /// <summary>
        /// 一段技能的总伤害。多段攻击逐段结算，破防收益更高。
        /// <paramref name="criticalRolls"/> 是逐段的暴击掷骰值（第 i 段用第 i 个），
        /// 缺省或长度不足的段按不暴击算——多段技能因此有多次暴击机会，而不是一次判定乘以全部段数。
        /// </summary>
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
            float defenseModifier = 0f,
            System.Collections.Generic.IReadOnlyList<float> criticalRolls = null)
        {
            var total = 0;
            var hits = Mathf.Max(1, hitCount);
            for (var i = 0; i < hits; i++)
            {
                var roll = criticalRolls != null && i < criticalRolls.Count
                    ? criticalRolls[i]
                    : DamageInput.NoCritical;

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
                        defenderMaxHealth: defenderMaxHealth,
                        criticalRoll: roll));
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
