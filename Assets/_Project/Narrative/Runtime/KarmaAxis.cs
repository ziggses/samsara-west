using System;

namespace SamsaraWest.Narrative
{
    /// <summary>
    /// 心念三轴。取值顺序即存档里三个字段的顺序（compassion / truth / freedom），
    /// 也是 <c>endingconditions.csv</c> 的 <c>minCompassion</c> / <c>minTruth</c> / <c>minFreedom</c> 三列的顺序。
    /// </summary>
    public enum KarmaAxis
    {
        Compassion = 0,
        Truth = 1,
        Freedom = 2,
    }

    /// <summary>
    /// 心念轴与剧情状态键（<c>karma.compassion</c> 等）之间的转述。
    /// </summary>
    /// <remarks>
    /// 为什么要写这层：<c>IdRules.IsValidStateKey</c> 允许 <c>karma.*</c> 前缀，
    /// 于是数据表里<b>理论上</b>可以有人把 <c>karma.compassion</c> 填进 <c>requiredStateKey</c>（「慈悲 ≥ 30 才开的门」）。
    /// 若账本对它一律返回 0，那扇门会永远打不开，而且看不出为什么。
    /// 所以读口认得这三个键；但**写**只走 <see cref="StoryState.AdjustKarma"/> 的累加语义，
    /// 因为数据表里心念从不出现在 <c>setsStateKeys</c> / <c>objectiveStateKeys</c> 里，只以
    /// <c>karmaChannel</c> + <c>karmaDelta</c> 的形式累加。
    /// </remarks>
    public static class KarmaAxes
    {
        /// <summary>心念轴状态键的前缀，与 <c>IdRules</c> 的状态键规则一致。</summary>
        public const string ChannelPrefix = "karma.";

        public const string CompassionChannel = "karma.compassion";
        public const string TruthChannel = "karma.truth";
        public const string FreedomChannel = "karma.freedom";

        /// <summary>三轴总数，供数组与界面遍历。</summary>
        public const int Count = 3;

        /// <summary>轴对应的状态键，例如 <c>karma.compassion</c>。</summary>
        public static string ChannelKey(this KarmaAxis axis)
        {
            switch (axis)
            {
                case KarmaAxis.Compassion: return CompassionChannel;
                case KarmaAxis.Truth: return TruthChannel;
                case KarmaAxis.Freedom: return FreedomChannel;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "未知的心念轴。");
            }
        }

        /// <summary>这个状态键是不是心念轴。是则回传对应的轴。</summary>
        public static bool TryParseChannel(string stateKey, out KarmaAxis axis)
        {
            switch (stateKey)
            {
                case CompassionChannel:
                    axis = KarmaAxis.Compassion;
                    return true;
                case TruthChannel:
                    axis = KarmaAxis.Truth;
                    return true;
                case FreedomChannel:
                    axis = KarmaAxis.Freedom;
                    return true;
                default:
                    axis = KarmaAxis.Compassion;
                    return false;
            }
        }
    }
}
