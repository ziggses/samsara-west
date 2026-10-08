using System;
using System.Collections.Generic;
using SamsaraWest.Core;

namespace SamsaraWest.Economy
{
    /// <summary>
    /// <see cref="IEconomyService"/> 的实现：一个内存里的钱袋与背包。
    /// </summary>
    /// <remarks>
    /// 它不落盘——落盘是 <c>Save</c> 的事，搬运是组合根的事（ADR-025 的手法）。
    /// 这里只回答「现在有多少」，并在被问倒的时候<b>不改任何状态</b>。
    /// </remarks>
    public sealed class EconomyService : IEconomyService
    {
        private readonly Dictionary<string, int> _items =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int Gold { get; private set; }

        public IReadOnlyList<ItemStack> Items
        {
            get
            {
                var snapshot = new List<ItemStack>(_items.Count);
                foreach (var pair in _items)
                {
                    snapshot.Add(new ItemStack(pair.Key, pair.Value));
                }

                // 顺序定死：同一份状态两次采集必须得到同样的排列（存档要比对、用例要断言）。
                snapshot.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));
                return snapshot;
            }
        }

        public int CountOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            return _items.TryGetValue(itemId, out var count) ? count : 0;
        }

        public void AddGold(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Gold += amount;
        }

        public bool TrySpendGold(int amount)
        {
            // 「花 0 块」不是一件事，负数更不是：两者都不动状态，也都如实说没花成。
            if (amount <= 0 || Gold < amount)
            {
                return false;
            }

            Gold -= amount;
            return true;
        }

        public void AddItem(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0)
            {
                return;
            }

            _items[itemId] = CountOf(itemId) + count;
        }

        public bool TryRemoveItem(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0)
            {
                return false;
            }

            var owned = CountOf(itemId);
            if (owned < count)
            {
                return false;
            }

            if (owned == count)
            {
                _items.Remove(itemId);
            }
            else
            {
                _items[itemId] = owned - count;
            }

            return true;
        }

        public void Restore(int gold, IEnumerable<ItemStack> items)
        {
            _items.Clear();
            Gold = gold > 0 ? gold : 0;

            if (items == null)
            {
                return;
            }

            // 数量不为正的条目直接忽略：存档里不该有它们，真有也当作「没有这件东西」。
            foreach (var stack in items)
            {
                AddItem(stack.ItemId, stack.Count);
            }
        }

        public void Clear()
        {
            _items.Clear();
            Gold = 0;
        }

        public void OnRegistered(IServiceRegistry registry)
        {
            GameLog.Debug(LogChannel.Economy, $"经济服务已就绪，当前 {Gold} 钱、{_items.Count} 种物品。");
        }

        public void OnUnregistered() => Clear();
    }
}
