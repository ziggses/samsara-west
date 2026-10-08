using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Equipment
{
    /// <summary>
    /// 玩家的在身清单：这一局里<b>谁身上穿了什么</b>的运行期真源。
    /// </summary>
    /// <remarks>
    /// <para>它为什么必须存在：装备与经文的<b>算法</b>早就通了——加成聚合成进场快照（ADR-017）、
    /// 技能挂载（ADR-019）、被动登记（ADR-020）——存档里的 <c>EquipmentAssignment</c> 字段也早就备好了
    /// （ADR-015），但正式流程里没有任何地方真的持有这份清单：从遭遇开出来的战斗传的一直是 <c>null</c>，
    /// 也就是「全员裸装」。那些能力只有在诊断层与用例里现搭一份清单时才生效。
    /// 这个服务就是「在身清单」第一次有真源的地方，也是存档搬运要问的地方。</para>
    ///
    /// <para><b>刻意不做的四件事</b>（写在这里，免得下一轮当作疏漏又补一遍）：</para>
    /// <list type="bullet">
    /// <item><description><b>不认识背包</b>：穿上不扣背包、脱下不回背包。钱袋与背包（<c>Economy</c>）与这份在身清单是
    /// <b>两本账</b>——ADR-027 的原话就是「<c>EquipmentAssignment</c> 那套是另一条线（在身清单）」。
    /// 要让「背包里的一件东西穿到身上」成为一件事，需要一条同时认识 Economy 与 Equipment 的接线，
    /// 而那条线只能长在组合根；等装备界面出现时再定它的口径，本期不替未来编一个。</description></item>
    /// <item><description><b>不管需求等级</b>：<c>requiredLevel</c> 是有数据的（<c>EQP_TALISMAN_JADE</c> 要 2 级、
    /// <c>EQP_STAFF_RUYI</c> 要 5 级），但等级系统（<c>Progression</c>）还是空壳，没有「现在几级」可问。
    /// 此刻硬拦会把需要等级的装备变成<b>永远穿不上的死数据</b>，所以照旧不校验（ADR-017 的代价栏已登记）。</description></item>
    /// <item><description><b>不管归属</b>：同一个物品 ID 可以同时落在两个人身上。目录里的武器是按 tier 归并过的
    /// 通用件（<c>EQP_STAFF_IRON</c> 谁都能拿），不是唯一物；「一件东西只有一份」需要一条归属线，本期没有。</description></item>
    /// <item><description><b>不认识战斗</b>：战斗要的只是「某成员带了哪些件」这个最小契约（<c>IBattleLoadout</c>），
    /// 而本模块只依赖 Core + Data（ADR-001）。把两边接起来的适配器长在组合根——与背包那条线同一手法。</description></item>
    /// </list>
    /// </remarks>
    public interface IEquipmentService : IService
    {
        /// <summary>
        /// 整本在身清单的快照，按「成员 ID → 栏位展示顺序」排好。
        /// </summary>
        /// <remarks>
        /// 排序不是审美：存档要的是「同一份状态两次采集得到同样的顺序」（同 <c>IEconomyService.Items</c>），
        /// 否则同一身装备会写出两份内容相同、顺序不同的档。
        /// </remarks>
        IReadOnlyList<EquippedItem> Snapshot { get; }

        /// <summary>
        /// 某成员的在身清单，顺序固定为栏位展示顺序；没登记过的成员返回<b>空清单</b>，不返回 null。
        /// </summary>
        /// <remarks>形状与战斗内核的 <c>IBattleLoadout.LoadoutOf</c> 一致，组合根只需转发一次。</remarks>
        IReadOnlyList<LoadoutEntry> LoadoutOf(string characterId);

        /// <summary>某成员某栏位上现在是什么；空栏、或认不出的成员与栏位，一律返回空串。</summary>
        string ItemIn(string characterId, string slotId);

        /// <summary>
        /// 穿上一件装备或经文。校验不过时返回 false 且<b>什么也不改</b>，理由写进 <paramref name="error"/>。
        /// </summary>
        /// <remarks>
        /// 同一栏位已有东西时<b>后来者覆盖</b>，与存档「一份成员 × 栏位只存一条」的约束一致
        /// （<c>SaveService.Validate</c> 也按这条约束拦档）。换下来的那件不会回到任何地方——
        /// 它本来也不在任何地方可回（背包是另一本账）。
        /// </remarks>
        bool TryEquip(string characterId, string slotId, string itemId, out string error);

        /// <summary>
        /// 脱下某栏位。该栏位本来就是空的则返回 false 并说明——那不是错误，是「没有可做的事」。
        /// </summary>
        bool TryUnequip(string characterId, string slotId, out string error);

        /// <summary>
        /// 整本换掉（读档用）：不在 <paramref name="items"/> 里的成员，簿子会被清空。
        /// </summary>
        /// <remarks>
        /// 与账本、钱袋整本换掉同理（<c>IStoryState.Restore</c>、<c>IEconomyService.Restore</c>）：
        /// 读档是「换成另一份状态」，不是「在现在这一身上再穿几件」。
        /// 进不来的条目（栏位名认不出、定义查不到、限定角色不符）只跳过它自己并留一条错误日志。
        /// </remarks>
        void Restore(IEnumerable<EquippedItem> items);

        /// <summary>清空（新开局用）。</summary>
        void Clear();
    }
}
