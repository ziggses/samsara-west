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

    /// <summary>
    /// 装备栏位，8 个可用槽位（不含 <see cref="EquipmentSlot.None"/>）。
    /// 枚举数值**只增不改**：已生成的 SO 资产按整数序列化，改动既有数值会让旧资产串位。
    /// 界面展示顺序与枚举数值无关，一律取 <see cref="EquipmentSlots.DisplayOrder"/>。
    /// </summary>
    public enum EquipmentSlot
    {
        None = 0,
        Weapon = 1,
        Armor = 2,
        Talisman = 3,
        Sutra = 4,
        OffHand = 5,
        Helmet = 6,
        Bracer = 7,
        Legging = 8,
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

    /// <summary>
    /// 攻守双方的一次五行关系。枚举顺序即倍率从高到低，
    /// 让「谁占便宜」在日志与校算里一眼可读（数值见 BattleConfig 与 Docs/战斗数值-v1.md）。
    /// </summary>
    public enum ElementRelation
    {
        /// <summary>任一方为无属性，五行不参与结算。</summary>
        None = 0,

        /// <summary>攻方克制守方：金克木。伤害最高。</summary>
        Restraining = 1,

        /// <summary>守方生攻方：金生水，攻方「借势」。次高。</summary>
        GeneratedBy = 2,

        /// <summary>同属性对轰。</summary>
        Same = 3,

        /// <summary>攻方生守方：火生土，攻方「资敌」。偏低。</summary>
        Generates = 4,

        /// <summary>守方克制攻方。最低。</summary>
        Restrained = 5,
    }

    /// <summary>五行相克与相生关系。金克木、木克土、土克水、水克火、火克金；木生火、火生土、土生金、金生水、水生木。</summary>
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

        /// <summary><paramref name="attacker"/> 是否相生 <paramref name="defender"/>（木生火）。</summary>
        public static bool IsGenerating(FiveElement attacker, FiveElement defender) =>
            Generates(attacker) == defender && defender != FiveElement.None;

        /// <summary>
        /// 攻守双方的五行关系。相克环与相生环把五行的 25 种配对恰好分完：
        /// 克制／借势／同属／资敌／被克各 5 对，外加 5 对「任一方无属性」，
        /// 所以<b>不存在</b>「两两相安无事」的配对——这正是五行值得玩家去读的地方。
        /// </summary>
        public static ElementRelation Relate(FiveElement attacker, FiveElement defender)
        {
            if (attacker == FiveElement.None || defender == FiveElement.None)
            {
                return ElementRelation.None;
            }

            if (attacker == defender)
            {
                return ElementRelation.Same;
            }

            if (IsRestraining(attacker, defender))
            {
                return ElementRelation.Restraining;
            }

            if (IsRestraining(defender, attacker))
            {
                return ElementRelation.Restrained;
            }

            return IsGenerating(defender, attacker) ? ElementRelation.GeneratedBy : ElementRelation.Generates;
        }
    }

    /// <summary>
    /// 装备栏位的展示顺序、本地化键与校验，UI 与数据校验共用同一份定义，禁止各处自行排列表顺序。
    /// 本地化键在这里以字面量给出：<c>Data</c> 不依赖 <c>Localization</c>（ADR-001），
    /// 键名与 <c>Localization/Tables/localization-zh-Hans.csv</c> 的一致性由测试兜底。
    /// </summary>
    public static class EquipmentSlots
    {
        /// <summary>可用栏位总数（不含 None）。</summary>
        public const int Count = 8;

        /// <summary>界面展示顺序：武器·副手·头盔·护甲·护腕·腿部护具·法宝·经文。</summary>
        public static readonly EquipmentSlot[] DisplayOrder =
        {
            EquipmentSlot.Weapon,
            EquipmentSlot.OffHand,
            EquipmentSlot.Helmet,
            EquipmentSlot.Armor,
            EquipmentSlot.Bracer,
            EquipmentSlot.Legging,
            EquipmentSlot.Talisman,
            EquipmentSlot.Sutra,
        };

        /// <summary>该栏位是否可装备；<see cref="EquipmentSlot.None"/> 与越界值一律 false。</summary>
        public static bool IsEquippable(EquipmentSlot slot) => Array.IndexOf(DisplayOrder, slot) >= 0;

        /// <summary>栏位名称的文本键，例如 <c>ui.equip.slot.weapon</c>；未知栏位返回空串。</summary>
        public static string LocalizationKey(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: return "ui.equip.slot.weapon";
                case EquipmentSlot.OffHand: return "ui.equip.slot.offhand";
                case EquipmentSlot.Helmet: return "ui.equip.slot.helmet";
                case EquipmentSlot.Armor: return "ui.equip.slot.armor";
                case EquipmentSlot.Bracer: return "ui.equip.slot.bracer";
                case EquipmentSlot.Legging: return "ui.equip.slot.legging";
                case EquipmentSlot.Talisman: return "ui.equip.slot.talisman";
                case EquipmentSlot.Sutra: return "ui.equip.slot.sutra";
                default: return string.Empty;
            }
        }
    }
}
