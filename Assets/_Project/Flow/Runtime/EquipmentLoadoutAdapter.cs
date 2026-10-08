using System;
using System.Collections.Generic;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Equipment;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 把内核要的「这个人带了哪些件」转述给真正的在身清单。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要这么一层薄壳：战斗内核只依赖 Core 与 Data，它<b>不认识</b>装备模块
    /// （<c>IBattleLoadout</c> 的注释写着「正式流程传在身清单」）；而装备模块只依赖 Core 与 Data，
    /// 也<b>不认识</b>战斗——它凭什么要在自己的依赖里挂上战斗？两边都认识的只有组合根，
    /// 于是这个转述者长在这里，与背包（<c>VaultBattleInventory</c>）和条件源
    /// （<c>NarrativeStateSourceAdapter</c>）同一手法：<b>两边都不必认识对方</b>。</para>
    ///
    /// <para>接上它之后，正式流程里的战斗<b>第一次穿上真装备</b>：此前从探索打进去的那场遭遇，
    /// 在身清单一直是 <c>null</c>，也就是全员裸装——装备与经文的加成聚合（ADR-017）、
    /// 技能挂载（ADR-019）、被动登记（ADR-020）只有在诊断层与用例里现搭一份清单时才生效。</para>
    ///
    /// <para>它有意做得<b>薄到只转发一次查询</b>：不缓存、不预取。进场画像是在开战那一刻
    /// （<c>BattleFactory.CreateUnit</c>）问的，而在这之前玩家随时可能换过装；
    /// 缓存会让下一场战斗拖着旧的一身进场，与 ADR-017「换装只影响下一场」正好相反。</para>
    /// </remarks>
    public sealed class EquipmentLoadoutAdapter : IBattleLoadout
    {
        private readonly IEquipmentService _equipment;

        public EquipmentLoadoutAdapter(IEquipmentService equipment)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        }

        public IReadOnlyList<LoadoutEntry> LoadoutOf(string characterId) => _equipment.LoadoutOf(characterId);
    }
}
