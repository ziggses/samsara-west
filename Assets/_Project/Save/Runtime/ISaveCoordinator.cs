using SamsaraWest.Core;

namespace SamsaraWest.Save
{
    /// <summary>
    /// 「运行期状态 ↔ 存档对象」的搬运契约：把正在跑的这一局收成一份 <see cref="SaveData"/>，
    /// 或者把一份存档灌回运行期。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么接口在这里、实现在组合根</b>：搬运要认识探索（人在哪张图哪一格）、
    /// 剧情（那本状态账）与随机（主种子），而这三样 <c>Save</c> 一个都不认识——
    /// 它只依赖 <c>Core</c>（ADR-001，<c>ProjectSkeletonTests</c> 锁住）。于是接口留在
    /// 「存档语义」这一侧，实现落在唯一两边都认识的地方（<c>Flow/SaveCoordinator</c>）。
    /// 这是依赖倒置：下层给出契约，上层给出实现，而不是让 <c>Save</c> 去反向认识玩法。</para>
    ///
    /// <para><b>为什么界面要能拿到它</b>：诊断层的「存一下 / 读一下」总得有个按键。
    /// 界面不许依赖 <c>Flow</c>（<c>ProjectSkeletonTests</c> 锁住，理由见那条用例的注释），
    /// 而按键又必须真的存到一局的状态、读回一局的状态——那就只剩「界面认这个接口」
    /// 一条路。界面因此知道「世界上有存档这件事」，但不知道它是由谁、按什么顺序装起来的。</para>
    ///
    /// <para><see cref="Capture"/> 与 <see cref="Apply"/> 是分开的，因为「采集」与「落盘」
    /// 是两件事：用例只用前者就能核对搬运口径，不必碰磁盘。</para>
    /// </remarks>
    public interface ISaveCoordinator : IService
    {
        /// <summary>
        /// 采集当前运行状态。不落盘。
        /// </summary>
        /// <remarks>
        /// 只填它认识的那几组字段（位置、主种子、状态账、心念三轴、金钱与背包），其余一律留空——
        /// 队伍、装备与进度还没有运行期真源，这里替它们编不出东西来。
        /// 金钱与背包自第二十一轮起有条件搬（经济模块落地了）；组合根没接经济服务时，
        /// 这两组仍然留空，与从前一样。
        /// </remarks>
        SaveData Capture();

        /// <summary>
        /// 把一份存档灌回运行状态。
        /// </summary>
        /// <returns>
        /// 存档为空、校验不过、或它指向的地图不在定义目录里时返回 false，
        /// 并且<b>什么都不改</b>——半灌进去的存档比拒绝读它难查得多。
        /// </returns>
        bool Apply(SaveData data);

        /// <summary>采集并写入槽位。</summary>
        bool SaveToSlot(int slot);

        /// <summary>读槽位并灌回运行状态。</summary>
        bool LoadFromSlot(int slot);
    }
}
