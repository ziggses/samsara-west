using System;

namespace SamsaraWest.Data
{
    /// <summary>五行。相克顺序由 <see cref="ElementRules"/> 统一定义，禁止在业务代码里散落 switch。</summary>
    public enum FiveElement
    {
        None = 0,
        Metal = 1,
        Wood = 2,
        Water = 3,
        Fire = 4,
        Earth = 5,
    }

    /// <summary>伤害性质。</summary>
    public enum DamageNature
    {
        Physical = 0,
        Mystic = 1,
        True = 2,
    }

    /// <summary>技能作用目标的选取方式（3×2 阵型下的位置语义）。</summary>
    public enum TargetRule
    {
        Self = 0,
        SingleEnemy = 1,
        SingleAlly = 2,
        AllEnemies = 3,
        AllAllies = 4,
        Column = 5,
        Row = 6,
        RandomEnemy = 7,
    }

    /// <summary>状态叠层规则。</summary>
    public enum StackRule
    {
        /// <summary>不可叠加，重复施加时刷新持续时间。</summary>
        Refresh = 0,

        /// <summary>可叠加层数，按层数放大效果。</summary>
        Stackable = 1,

        /// <summary>只保留最强的一次。</summary>
        StrongestOnly = 2,
    }

    /// <summary>稀有度 / 品阶，与已交付素材 spritesheet.json 的 tier 字段对齐。</summary>
    public enum RarityTier
    {
        Common = 1,
        Refined = 2,
        Rare = 3,
        Legendary = 4,
        Mythic = 5,
    }

    public enum EquipmentSlot
    {
        None = 0,
        Weapon = 1,
        Armor = 2,
        Talisman = 3,
        Sutra = 4,
    }

    /// <summary>道具大类。决定背包分页与是否可消耗。</summary>
    public enum ItemCategory
    {
        None = 0,
        Consumable = 1,
        Material = 2,
        Key = 3,
        Currency = 4,
        QuestItem = 5,
        Recipe = 6,
    }

    /// <summary>条件比较运算符，供任务、结局、掉落表共用。</summary>
    public enum CompareOperator
    {
        Equal = 0,
        NotEqual = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5,
    }

    /// <summary>五行相克关系。金克木、木克土、土克水、水克火、火克金。</summary>
    public static class ElementRules
    {
        private static readonly FiveElement[] Overcomes =
        {
            FiveElement.None,   // None
            FiveElement.Wood,   // Metal 克 Wood
            FiveElement.Earth,  // Wood  克 Earth
            FiveElement.Fire,   // Water 克 Fire
            FiveElement.Metal,  // Fire  克 Metal
            FiveElement.Water,  // Earth 克 Water
        };

        /// <summary>返回 <paramref name="element"/> 所克制的五行。</summary>
        public static FiveElement Overcome(FiveElement element) =>
            (int)element >= 0 && (int)element < Overcomes.Length ? Overcomes[(int)element] : FiveElement.None;

        /// <summary>返回克制 <paramref name="element"/> 的五行。</summary>
        public static FiveElement OvercomeBy(FiveElement element)
        {
            for (var i = 1; i < Overcomes.Length; i++)
            {
                if (Overcomes[i] == element)
                {
                    return (FiveElement)i;
                }
            }

            return FiveElement.None;
        }

        public static bool IsRestraining(FiveElement attacker, FiveElement defender) =>
            Overcome(attacker) == defender && defender != FiveElement.None;

        /// <summary>五行是否相生（用于治疗与增益加成）。木生火、火生土、土生金、金生水、水生木。</summary>
        public static FiveElement Generates(FiveElement element)
        {
            switch (element)
            {
                case FiveElement.Wood: return FiveElement.Fire;
                case FiveElement.Fire: return FiveElement.Earth;
                case FiveElement.Earth: return FiveElement.Metal;
                case FiveElement.Metal: return FiveElement.Water;
                case FiveElement.Water: return FiveElement.Wood;
                default: return FiveElement.None;
            }
        }
    }
}
