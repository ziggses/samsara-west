using System;
using SamsaraWest.Battle;
using SamsaraWest.Economy;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 把内核要的「还有几个、扣掉一个」转述给真正的钱袋与背包。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要这么一层薄壳：战斗内核只依赖 Core 与 Data，它<b>不认识</b>经济模块
    /// （<c>IBattleInventory</c> 的注释写着「正式流程用存档实现它」）；而经济模块只依赖 Core 与 Data，
    /// 也<b>不认识</b>战斗——它凭什么要在自己的依赖里挂上战斗？两边都认识的只有组合根，
    /// 于是这个转述者长在这里，与条件源的手法完全一样（<c>NarrativeStateSourceAdapter</c>）：
    /// <b>两边都不必认识对方</b>。</para>
    ///
    /// <para>接上它之后，正式流程里的战斗<b>第一次有了真背包</b>：此前从探索打进去的那场遭遇，
    /// 背包一直是 <c>null</c>，道具指令问谁都要不到东西。掉落的丹药从此真的能在战斗里用掉一个。</para>
    /// </remarks>
    public sealed class VaultBattleInventory : IBattleInventory
    {
        private readonly IEconomyService _economy;

        public VaultBattleInventory(IEconomyService economy)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public int CountOf(string itemId) => _economy.CountOf(itemId);

        public bool TryConsume(string itemId) => _economy.TryRemoveItem(itemId, 1);
    }
}
