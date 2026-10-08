using SamsaraWest.Localization;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 存档面板的「状态 → 文本键」对照表。与 <c>ExplorationTextKeys</c> 同一种分工：
    /// 面板不自己拼句子，只把状态转述成键，然后交给本地化服务取词。
    /// </summary>
    /// <remarks>
    /// 「没有存档」与「读档失败」分成两句，是因为它们要玩家做的事完全不同：
    /// 前者是「先存一次」，后者是「去看日志」。
    /// </remarks>
    public static class SaveTextKeys
    {
        public static string Title => LocalizationKeys.UI_SAVE_TITLE;

        public static string Hint => LocalizationKeys.UI_SAVE_VIEW_HINT;

        public static string Unavailable => LocalizationKeys.UI_SAVE_STATUS_UNAVAILABLE;

        /// <summary>该状态对应哪条文案；<see cref="SaveScreenView.SlotStatus.Unknown"/> 返回 null。</summary>
        public static string StatusKey(SaveScreenView.SlotStatus status)
        {
            switch (status)
            {
                case SaveScreenView.SlotStatus.Empty:
                    return LocalizationKeys.UI_SAVE_STATUS_EMPTY;
                case SaveScreenView.SlotStatus.Present:
                    return LocalizationKeys.UI_SAVE_STATUS_PRESENT;
                case SaveScreenView.SlotStatus.Saved:
                    return LocalizationKeys.UI_SAVE_STATUS_SAVED;
                case SaveScreenView.SlotStatus.Loaded:
                    return LocalizationKeys.UI_SAVE_STATUS_LOADED;
                case SaveScreenView.SlotStatus.Failed:
                    return LocalizationKeys.UI_SAVE_STATUS_FAILED;
                default:
                    return null;
            }
        }
    }
}
