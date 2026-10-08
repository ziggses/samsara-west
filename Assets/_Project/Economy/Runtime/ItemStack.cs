namespace SamsaraWest.Economy
{
    /// <summary>一样东西与它的数量。「掉落了几件」与「背包里还剩几件」共用这一种写法。</summary>
    /// <remarks>
    /// 用不可变的小结构而不是字典项：掉落表可能<b>重复抽到同一样东西</b>
    /// （两次权重抽都落在 <c>ITM_HERB</c> 上），合并是结算时的显式动作，
    /// 不该靠「往字典里塞」来顺手完成——那样一写，掉了几次就再也数不出来了。
    /// </remarks>
    public readonly struct ItemStack
    {
        public ItemStack(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        /// <summary>道具／装备／经文的定义 ID。</summary>
        public string ItemId { get; }

        /// <summary>件数，恒为正。零件的「掉落」不该被造出来。</summary>
        public int Count { get; }

        public override string ToString() => $"{ItemId}×{Count}";
    }
}
