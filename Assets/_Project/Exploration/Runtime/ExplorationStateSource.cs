using System;
using System.Collections.Generic;
using SamsaraWest.Core;

namespace SamsaraWest.Exploration
{
    /// <summary>
    /// 剧情状态读取契约（只读）。
    /// </summary>
    /// <remarks>
    /// 交互物上的 <c>requiredStateKey</c> / <c>requiredOperator</c> / <c>requiredValue</c>
    /// 要问一句「这个键现在是多少」。但这本账<b>不属于探索</b>：写它的是剧情与存档，
    /// 而那两个模块今天还是空的，所以探索只提出这个只读问题，由上层注入实现。
    ///
    /// 将来剧情状态服务落地，若成长／任务也要读同一份账，这份契约应当提升到 Core、
    /// 或由剧情侧拥有后供探索引用。本轮不预建一个只有探索在用的全局状态服务——
    /// 免得空模块先长出一个假的所有者。
    /// </remarks>
    public interface IExplorationStateSource
    {
        /// <summary>读一个状态键的当前整数取值。未设置的键应当返回 0。</summary>
        int GetValue(string stateKey);
    }

    /// <summary>
    /// 没有注入状态源时的替身：所有键都读作 0，每个键只警告一次。
    /// </summary>
    /// <remarks>
    /// 为什么不抛异常：缺状态源只该让「带条件的交互物」退化成隐藏或不可交互，
    /// 不该让整条探索链崩掉。为什么必须警告：静默当作「满足条件」会把剧情道具提前放出来，
    /// 那种错只有在玩家存档里才会被发现。
    /// </remarks>
    public sealed class MissingExplorationStateSource : IExplorationStateSource
    {
        public static readonly MissingExplorationStateSource Instance = new MissingExplorationStateSource();

        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public int GetValue(string stateKey)
        {
            if (!string.IsNullOrEmpty(stateKey) && _warned.Add(stateKey))
            {
                GameLog.Warn(
                    LogChannel.Exploration,
                    $"剧情状态源尚未落地，条件键 {stateKey} 一律按 0 处理（依赖它的交互物会保持隐藏或不可交互）。");
            }

            return 0;
        }
    }
}
