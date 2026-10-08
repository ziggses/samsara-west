namespace SamsaraWest.Data
{
    /// <summary>
    /// 道具的<b>效果语义键</b>。
    /// </summary>
    /// <remarks>
    /// 它们是 <c>items.csv</c> 里 <c>effectKey</c> 列的合法取值，由战斗内核的效果解释器消费。
    /// 键名形如本地化键（允许点号分段），但<b>不是</b>本地化键——查不到任何文案，
    /// 给玩家看的是道具自己的 <c>displayNameKey</c>／<c>descriptionKey</c>。
    ///
    /// 为什么单独立一个类而不是在解释器里写字符串：这些键同时被<b>数据</b>（CSV 作者）
    /// 与<b>内核</b>（效果解释器）引用，抄在两边迟早会分家，然后出现「数据写对了、内核不认」。
    /// </remarks>
    public static class ItemEffectKeys
    {
        /// <summary>回血：给目标恢复 <c>effectMagnitude</c> 点生命（最多回满）。</summary>
        public const string HealHealth = "item.effect.heal.health";

        /// <summary>回灵：给目标恢复 <c>effectMagnitude</c> 点灵力（最多回满）。</summary>
        public const string HealSpirit = "item.effect.heal.spirit";

        /// <summary>祛负面：解除目标身上全部负面状态（<c>effectMagnitude</c> 本版不参与计算）。</summary>
        public const string CureStatus = "item.effect.cure.status";

        /// <summary>本版内核能解释的效果键是否包括这一个。</summary>
        public static bool IsKnown(string effectKey)
        {
            switch (effectKey)
            {
                case HealHealth:
                case HealSpirit:
                case CureStatus:
                    return true;
                default:
                    return false;
            }
        }
    }
}
