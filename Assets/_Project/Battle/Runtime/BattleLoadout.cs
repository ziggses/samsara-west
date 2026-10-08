using System;
using System.Collections.Generic;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 开战这一刻，每个成员身上带了什么。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IBattleInventory"/> 同一种做法：战斗内核只依赖 <c>Core</c> 与 <c>Data</c>（ADR-001），
    /// 因此内核不认识存档类型，只认这个最小契约。存档的 <c>EquipmentAssignment</c> 与它同形
    /// （成员 → 栏位名 + 定义 ID），正式流程直接搬过来即可，不必让 <c>Battle</c> 反向依赖 <c>Save</c>。
    /// <para>
    /// 数值怎么算不在这里——那是 <see cref="CharacterStatsResolver"/> 的职责。这里只回答「谁带了哪些件」，
    /// 于是「装备从哪来」（存档、掉落、剧情赠送）与「装备怎么算」互不牵扯。
    /// </para>
    /// </remarks>
    public interface IBattleLoadout
    {
        /// <summary>
        /// 某成员的在身清单。没有登记过的成员返回空清单，<b>不返回 null</b>，
        /// 调用方无需为空值分支。
        /// </summary>
        IReadOnlyList<LoadoutEntry> LoadoutOf(string characterId);
    }

    /// <summary>
    /// 内存版在身清单：给诊断层与用例搭一支「穿了装备」的队伍，也是将来存档实现的样板。
    /// </summary>
    /// <remarks>
    /// 同一成员同一栏位重复 <see cref="Equip"/> 时<b>后来者覆盖</b>：这是搭场用的便利口径，
    /// 与存档「一份成员 × 栏位只存一条」的约束一致。想验证重复栏位被忽略的用例，
    /// 直接给聚合器一份手写的清单，别绕这一层。
    /// </remarks>
    public sealed class BattleLoadout : IBattleLoadout
    {
        private static readonly LoadoutEntry[] Empty = Array.Empty<LoadoutEntry>();

        private readonly Dictionary<string, List<LoadoutEntry>> _byCharacter =
            new Dictionary<string, List<LoadoutEntry>>(StringComparer.Ordinal);

        /// <summary>给某成员挂上一件装备或经文。成员 ID 或装备 ID 为空时忽略，不建空条目。</summary>
        public void Equip(string characterId, string slotId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!_byCharacter.TryGetValue(characterId, out var entries))
            {
                entries = new List<LoadoutEntry>(EquipmentSlots.Count);
                _byCharacter[characterId] = entries;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].SlotId, slotId, StringComparison.Ordinal))
                {
                    entries[i] = new LoadoutEntry(slotId, itemId);
                    return;
                }
            }

            entries.Add(new LoadoutEntry(slotId, itemId));
        }

        public IReadOnlyList<LoadoutEntry> LoadoutOf(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId) || !_byCharacter.TryGetValue(characterId, out var entries))
            {
                return Empty;
            }

            return entries;
        }
    }
}
