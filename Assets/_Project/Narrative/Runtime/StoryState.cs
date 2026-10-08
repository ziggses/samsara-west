using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Narrative
{
    /// <summary>
    /// 剧情状态账的读写契约：一本「状态键 → 整数」的账，外加心念三轴的累加值。
    /// </summary>
    /// <remarks>
    /// 谁用这本账：任务的目标（<c>objectiveStateKeys</c>）、对话的进入条件与写入（<c>requiredStateKey</c> /
    /// <c>setsStateKeys</c>）、结局的判据（<c>requiredStateKeys</c> + 三条 <c>min*</c>）、
    /// 以及探索里「条件未满足就隐藏」的交互物。它们用的是同一套语言，因此共读同一本账。
    ///
    /// 账本落在 <c>Narrative</c> 模块（只依赖 Core 与 Data），读口由组合根按需转述给别的层
    /// （探索的 <c>IExplorationStateSource</c> 就是一例，见 <c>Flow/NarrativeStateSourceAdapter</c>）——
    /// 于是探索不必认识剧情，剧情也不必认识探索。
    /// </remarks>
    public interface IStoryState : IService
    {
        /// <summary>读一个状态键的当前整数取值。没写过的键返回 0；<c>karma.*</c> 转读心念轴。</summary>
        int GetValue(string stateKey);

        /// <summary>
        /// 写一个状态键。键不符合命名规则、或键属于心念轴时，记日志并忽略
        /// （心念只能累加，见 <see cref="AdjustKarma"/>）。值真的变了才发事件。
        /// </summary>
        void SetValue(string stateKey, int value);

        /// <summary>读心念某一轴的当前值。</summary>
        int GetKarma(KarmaAxis axis);

        /// <summary>给心念某一轴累加一个增量（可正可负）。增量为 0 时什么都不做。</summary>
        void AdjustKarma(KarmaAxis axis, int delta);

        /// <summary>已写下的状态键快照（值为 0 的键不在其中）。存档接线读它。</summary>
        IReadOnlyDictionary<string, int> Values { get; }

        /// <summary>
        /// 用一份快照整体替换账本（读档用）：清空既有键、按快照重建、心念三轴按传入值设定。
        /// </summary>
        /// <remarks>
        /// 为什么是「整本替换」而不是逐个 <see cref="SetValue"/>：读档要的是「换成另一份状态」，
        /// 在当前状态上叠加会把上一局的残留键留下——那种残留只有在玩家读档后才会被发现。
        /// 来自快照的坏键与 <c>karma.*</c> 键照旧进不来（记日志并跳过），心念只从三个参数来。
        /// 整本换掉发一条 <see cref="StoryStateRestoredEvent"/>，订阅方据此重建一次。
        /// </remarks>
        void Restore(IReadOnlyDictionary<string, int> values, int compassion, int truth, int freedom);
    }

    /// <inheritdoc cref="IStoryState" />
    /// <remarks>
    /// 全部状态都在内存里，进出存档靠 <c>Flow/SaveCoordinator</c> 搬运（ADR-025）：
    /// 组合根采集 <see cref="Values"/> 与心念，读档时用 <see cref="Restore"/> 整本换回来。
    /// 键的取值不做上下界夹取（心念可正可负，数据层也没有上下界），这是有意的：
    /// 先如实记录，等结局判定的口径拍板后再决定要不要夹。
    /// </remarks>
    public sealed class StoryState : IStoryState
    {
        private readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly int[] _karma = new int[KarmaAxes.Count];
        private IEventBus _eventBus;

        /// <summary>已写下的状态键快照（值为 0 的键不在其中）。存档接线时读它。</summary>
        public IReadOnlyDictionary<string, int> Values => _values;

        /// <summary>已写下的状态键个数，诊断与测试用。</summary>
        public int Count => _values.Count;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // 事件总线是软依赖：没有总线时账本照样能写能读，只是没人收到通知（测试里就不接总线）。
            registry.TryResolve(out _eventBus);

            GameLog.Info(
                LogChannel.Narrative,
                _eventBus == null
                    ? "Narrative 模块已注册；事件总线缺失，状态变化不会通知探索与界面。"
                    : "Narrative 模块已注册，剧情状态账就位。");
        }

        public void OnUnregistered()
        {
            _eventBus = null;
        }

        public int GetValue(string stateKey)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return 0;
            }

            if (KarmaAxes.TryParseChannel(stateKey, out var axis))
            {
                return _karma[(int)axis];
            }

            return _values.TryGetValue(stateKey, out var value) ? value : 0;
        }

        public void SetValue(string stateKey, int value)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                GameLog.Error(LogChannel.Narrative, "写剧情状态时键为空，已忽略。");
                return;
            }

            if (!IdRules.IsValidStateKey(stateKey))
            {
                GameLog.Error(
                    LogChannel.Narrative,
                    $"剧情状态键 '{stateKey}' 不符合命名规则（flag./relation./karma./ending.），已忽略。",
                    stateKey);
                return;
            }

            if (KarmaAxes.TryParseChannel(stateKey, out _))
            {
                GameLog.Warn(
                    LogChannel.Narrative,
                    $"'{stateKey}' 是心念轴，只能用 AdjustKarma 累加；SetValue 已忽略，免得把累加值误当赋值。",
                    stateKey);
                return;
            }

            var previous = GetValue(stateKey);
            if (previous == value)
            {
                // 值没变就不发事件：探索侧靠这条事件重建可行格，重复发会让一次开门引出两条刷新。
                return;
            }

            if (value == 0)
            {
                _values.Remove(stateKey);
            }
            else
            {
                _values[stateKey] = value;
            }

            Publish(new StoryStateChangedEvent(stateKey, previous, value));
        }

        public int GetKarma(KarmaAxis axis) => _karma[(int)axis];

        public void AdjustKarma(KarmaAxis axis, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            var index = (int)axis;
            var previous = _karma[index];
            var current = previous + delta;
            _karma[index] = current;

            Publish(new StoryKarmaChangedEvent(axis.ChannelKey(), previous, current));
        }

        public void Restore(IReadOnlyDictionary<string, int> values, int compassion, int truth, int freedom)
        {
            _values.Clear();

            var restored = 0;
            if (values != null)
            {
                foreach (var pair in values)
                {
                    if (!IdRules.IsValidStateKey(pair.Key))
                    {
                        GameLog.Error(
                            LogChannel.Narrative,
                            $"存档里的剧情状态键 '{pair.Key}' 不符合命名规则，已跳过。",
                            pair.Key);
                        continue;
                    }

                    if (KarmaAxes.TryParseChannel(pair.Key, out _))
                    {
                        GameLog.Warn(
                            LogChannel.Narrative,
                            $"存档里的 '{pair.Key}' 是心念轴，已跳过；心念从存档的三个轴字段恢复。",
                            pair.Key);
                        continue;
                    }

                    if (pair.Value == 0)
                    {
                        // 与快照口径一致：值为 0 的键等于没写过，不在账本里留空记录。
                        continue;
                    }

                    _values[pair.Key] = pair.Value;
                    restored++;
                }
            }

            _karma[(int)KarmaAxis.Compassion] = compassion;
            _karma[(int)KarmaAxis.Truth] = truth;
            _karma[(int)KarmaAxis.Freedom] = freedom;

            Publish(new StoryStateRestoredEvent(restored, compassion, truth, freedom));
        }

        private void Publish<T>(T gameEvent) where T : IGameEvent =>
            _eventBus?.Publish(NarrativeEventChannel.Channel, gameEvent);
    }
}
