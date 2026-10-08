using System.Collections.Generic;
using SamsaraWest.Core;

namespace SamsaraWest.Economy
{
    /// <summary>
    /// 玩家的钱与物：这一局里「有多少钱、每样东西还剩几个」的<b>运行期真源</b>。
    /// </summary>
    /// <remarks>
    /// <para>它为什么必须存在：存档里早就有 <c>Gold</c> 与 <c>InventoryItemIds/Counts</c> 三组字段，
    /// 但一直<b>没人往里写</b>——因为没有任何地方在运行期真的持有这笔钱。掉落表与敌人的赏金都填好了，
    /// 打完一场却只能把战果扔在内存里（ADR-026 的代价栏原话：「需要奖励时由内容侧去读这个键」）。
    /// 这个服务就是那个「内容侧」要读的地方，也是存档搬运要问的地方。</para>
    ///
    /// <para><b>刻意不做的三件事</b>（都写在这里，免得下一轮当作疏漏又补一遍）：</para>
    /// <list type="bullet">
    /// <item><description><b>没有堆叠上限</b>：<c>ItemDefinition.StackLimit</c> 有数据，但「格子」「满仓」
    /// 这些口径一个都没定；此刻硬夹上限，只会把掉落物<b>静默吞掉</b>。宁可先记下超出的数量，
    /// 也不要让玩家看见一件东西凭空少了。</description></item>
    /// <item><description><b>不认装备与经文</b>：<c>EquipmentAssignment</c> 那套是另一条线（在身清单），
    /// 它没有运行期真源，这一层也不替它编。</description></item>
    /// <item><description><b>不认识战斗</b>：内核要的只是「还有几个、扣掉一个」这个最小契约
    /// （<c>IBattleInventory</c>），而本模块只依赖 Core + Data（ADR-001）。
    /// 把两边接起来的适配器长在组合根——与条件源的手法一样，两边都不必认识对方。</description></item>
    /// </list>
    /// </remarks>
    public interface IEconomyService : IService
    {
        /// <summary>身上的钱。只会是 0 或正数。</summary>
        int Gold { get; }

        /// <summary>
        /// 背包内容的一份<b>按 ID 排序</b>的快照。
        /// </summary>
        /// <remarks>
        /// 排序不是审美：存档要的是「同一份状态两次采集得到同样的顺序」，
        /// 而字典的遍历顺序不保证（同 <c>SaveCoordinator</c> 对状态键的处理）。
        /// 数量不为正的条目不存在于快照里——「没有这件东西」与「有 0 个」是同一件事。
        /// </remarks>
        IReadOnlyList<ItemStack> Items { get; }

        /// <summary>还剩几个这件东西；没登记过则为 0。</summary>
        int CountOf(string itemId);

        /// <summary>加钱。数量不为正时什么都不做（扣钱请用 <see cref="TrySpendGold"/>）。</summary>
        void AddGold(int amount);

        /// <summary>扣钱。钱不够时返回 false 且<b>什么也不改</b>。</summary>
        bool TrySpendGold(int amount);

        /// <summary>放进几件东西。数量不为正时什么都不做。</summary>
        void AddItem(string itemId, int count);

        /// <summary>拿走几件东西。存货不足时返回 false 且<b>什么也不改</b>。</summary>
        bool TryRemoveItem(string itemId, int count);

        /// <summary>
        /// 整本换掉（读档用）：钱与背包一起替换，而不是在原状态上叠加。
        /// </summary>
        /// <remarks>
        /// 与账本整本换掉同理（<c>IStoryState.Restore</c>）：读档是「换成另一份状态」。
        /// 数量不为正的条目直接忽略，负数的钱不合法——由存档校验先拦（<c>SaveService.Validate</c>）。
        /// </remarks>
        void Restore(int gold, IEnumerable<ItemStack> items);

        /// <summary>清空（新开局用）。</summary>
        void Clear();
    }
}
