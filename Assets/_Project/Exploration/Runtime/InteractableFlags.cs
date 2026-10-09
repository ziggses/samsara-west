namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 一次性交互物的「用过了」在剧情账本里的键。
    /// </summary>
    /// <remarks>
    /// <para>为什么记在账本而不是只记在会话上：会话是「一次进图」的产物，换一张图就丢了，
    /// 于是开过的箱子出镇再回来会复活。账本跨会话、跨地图，还进存档——那才是
    /// 「这件事发生过」该待的地方。</para>
    ///
    /// <para>键名是探索与账本之间的<b>约定</b>，不是依赖：探索只往这个键上问一句，
    /// 它不认识 <c>Narrative</c> 模块；账本也不认识交互物。写这一侧的是
    /// <c>Flow/InteractionFlagLink</c>（它两边都认识），读这一侧是
    /// <see cref="ExplorationSession.HasUsed"/>（它只认识 <see cref="IExplorationStateSource"/>）。</para>
    ///
    /// <para>键形如 <c>flag.interact.int_ch01_015_tomb_entrance</c>：前缀符合 <c>IdRules</c> 的
    /// <c>flag.*</c> 规则，交互物 ID 一律转小写——账本键的规则只认小写，
    /// 而交互物 ID 按数据层的规矩是大写的。</para>
    /// </remarks>
    public static class InteractableFlags
    {
        /// <summary>一次性交互物「用过了」的键前缀。</summary>
        public const string Prefix = "flag.interact.";

        /// <summary>
        /// 这条交互物在账本里的键。ID 为空时返回空串——调用方据此判定「这条不该记账」，
        /// 而不是让一个 <c>flag.interact.</c> 这样的残缺键混进账本。
        /// </summary>
        public static string KeyOf(string interactableId) =>
            string.IsNullOrEmpty(interactableId)
                ? string.Empty
                : Prefix + interactableId.ToLowerInvariant();
    }
}
