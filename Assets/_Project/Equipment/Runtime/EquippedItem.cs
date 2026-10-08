using System;

namespace SamsaraWest.Equipment
{
    /// <summary>
    /// 一件「在身」的东西：谁的、哪个栏位、哪件定义。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它是在身清单用来「整本进出」的通用货币：往里走是读档（<c>Restore</c>），往外走是存档（<c>Snapshot</c>）。
    /// 成员、栏位与物品都用字符串名，口径与 <see cref="SamsaraWest.Data.LoadoutEntry"/>、
    /// 存档的 <c>EquipmentAssignment</c> 一致（ADR-015）：交来源的一侧不必持有枚举。
    /// </para>
    /// <para>
    /// 为什么不在 <c>LoadoutEntry</c> 上加一个成员字段：成员正是「谁的」那一半，
    /// 而在身清单要能<b>整本</b>换掉——快照必须自己带成员，才能回答「哪些人的簿子要被清空」。
    /// 两边的分工也因此清楚：进场画像只关心「这个人带了什么」（<c>LoadoutEntry</c> 就够），
    /// 存档与读档关心「所有人分别带了什么」（这个结构型）。
    /// </para>
    /// </remarks>
    public readonly struct EquippedItem : IEquatable<EquippedItem>
    {
        public EquippedItem(string characterId, string slotId, string itemId)
        {
            CharacterId = characterId ?? string.Empty;
            SlotId = slotId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
        }

        /// <summary>队伍成员定义 ID，例如 <c>CHR_WUKONG</c>。</summary>
        public string CharacterId { get; }

        /// <summary>栏位名，严格对齐 <c>EquipmentSlot</c> 的成员名，例如 <c>Weapon</c>。</summary>
        public string SlotId { get; }

        /// <summary>装备或经文的定义 ID，例如 <c>EQP_STAFF_IRON</c>、<c>SUT_STILLNESS</c>。</summary>
        public string ItemId { get; }

        public bool Equals(EquippedItem other) =>
            string.Equals(CharacterId, other.CharacterId, StringComparison.Ordinal)
            && string.Equals(SlotId, other.SlotId, StringComparison.Ordinal)
            && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is EquippedItem other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = CharacterId.GetHashCode();
                hash = (hash * 397) ^ SlotId.GetHashCode();
                return (hash * 397) ^ ItemId.GetHashCode();
            }
        }

        public override string ToString() => $"{CharacterId}.{SlotId}={ItemId}";
    }
}
