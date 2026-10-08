using System;
using System.Collections.Generic;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 战斗内的道具来源。内核只问它两件事：还有几个、扣掉一个。
    /// </summary>
    /// <remarks>
    /// 背包住在存档里，而 Battle 只依赖 Core 与 Data（分层见《架构决策》），
    /// 因此内核不认识存档类型，只认这个最小契约：正式流程用存档实现它，
    /// 诊断层与用例用 <see cref="BattleInventory"/> 现搭一个。
    /// 把接口开得再宽一点（枚举全部道具、查价格……）都会把存档的shape漏进内核。
    /// </remarks>
    public interface IBattleInventory
    {
        /// <summary>背包里还剩几个这件道具；没登记过则为 0。</summary>
        int CountOf(string itemId);

        /// <summary>扣掉一个。存货不足时返回 false，且不改变任何状态。</summary>
        bool TryConsume(string itemId);
    }

    /// <summary>
    /// 一个内存背包：让诊断层与用例能搭一场「有道具可用」的战斗，也是将来存档实现的样板。
    /// </summary>
    /// <remarks>
    /// 它<b>不是</b>正式背包：没有堆叠上限、没有格子、不落盘。这些口径属于存档侧，
    /// 不该由战斗内核替你定——内核只在真的要扣一个的时候问一句「够不够」。
    /// </remarks>
    public sealed class BattleInventory : IBattleInventory
    {
        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public BattleInventory()
        {
        }

        /// <summary>用一批「道具 ID → 数量」现搭一个。数量不为正的条目直接忽略。</summary>
        public BattleInventory(IEnumerable<KeyValuePair<string, int>> initial)
        {
            if (initial == null)
            {
                return;
            }

            foreach (var pair in initial)
            {
                Add(pair.Key, pair.Value);
            }
        }

        /// <summary>登记／调整存货。数量不为正时只把这一条撤掉。</summary>
        public void Add(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            if (count <= 0)
            {
                _counts.Remove(itemId);
                return;
            }

            _counts[itemId] = count;
        }

        public int CountOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            return _counts.TryGetValue(itemId, out var count) ? count : 0;
        }

        public bool TryConsume(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) ||
                !_counts.TryGetValue(itemId, out var count) ||
                count <= 0)
            {
                return false;
            }

            if (count == 1)
            {
                _counts.Remove(itemId);
            }
            else
            {
                _counts[itemId] = count - 1;
            }

            return true;
        }
    }
}
