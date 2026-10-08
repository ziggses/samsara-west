using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Narrative;
using SamsaraWest.Save;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 存档搬运的实现：<see cref="ISaveCoordinator"/> 接到的活都在这里干。
    /// </summary>
    /// <remarks>
    /// <para>它为什么必须长在组合根：左手是探索（人站在哪张图的哪一格）、右手是剧情（那本状态账）、
    /// 兜里还有随机服务的母种子，而这三样都<b>不许</b>被 <c>Save</c> 认识。
    /// 与三条 Link 同一处境，区别只在它是「按接口被调用」而不是「订阅事件」。</para>
    ///
    /// <para>两处顺序是有意的：<b>先校验再动状态</b>（目标图查不到时整体拒绝，
    /// 免得留下「账本换了、人还在旧图上」这种半截状态），以及<b>先复位母种子再进图</b>
    /// （探索流是在进图那一刻取的，晚一步复位就落在旧种子的序列上）。</para>
    /// </remarks>
    public sealed class SaveCoordinator : ISaveCoordinator
    {
        private readonly ISaveService _saves;
        private readonly IStoryState _state;
        private readonly IExplorationService _exploration;
        private readonly IDefinitionRegistry _definitions;
        private readonly IRandomService _random;

        /// <param name="saves">存档读写（落盘与校验）。</param>
        /// <param name="state">剧情状态账：整本进、整本出。</param>
        /// <param name="exploration">探索服务：位置从它来，读档也回到它身上。</param>
        /// <param name="definitions">定义目录；给了就先查「存档指向的图还在不在」再动状态。</param>
        /// <param name="random">随机服务；给了就搬运主种子（缺省则不搬，读档后随机序列不会回到原处）。</param>
        public SaveCoordinator(
            ISaveService saves,
            IStoryState state,
            IExplorationService exploration,
            IDefinitionRegistry definitions = null,
            IRandomService random = null)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _exploration = exploration ?? throw new ArgumentNullException(nameof(exploration));
            _definitions = definitions;
            _random = random;
        }

        /// <summary>最近一次成功存／读的槽位；一次都没动过时为 -1。诊断面板显示用。</summary>
        public int LastSlot { get; private set; } = -1;

        public SaveData Capture()
        {
            var session = _exploration.Current;

            var data = new SaveData
            {
                // 内存里的快照不带版本号，与 TrySave 同一处境：由存档服务盖章。
                // 不盖的话 Validate 会以「版本号 0 不在支持范围」整体拒绝——
                // 于是 Apply 吃自己刚采集的档都会失败，而只有「经过一次落盘的档」才进得来。
                Version = _saves.CurrentVersion,

                // 不在图上时地图留空、坐标归零：存档里「没有位置」是有意义的，
                // 而 (0,0) 恰好会读成「在第一张图的左上角」——两者必须分得开。
                MapId = session == null ? null : session.MapId,
                GridX = session == null ? 0 : session.Position.X,
                GridY = session == null ? 0 : session.Position.Y,
                RandomSeed = _random == null ? 0UL : _random.MasterSeed,
                KarmaCompassion = _state.GetKarma(KarmaAxis.Compassion),
                KarmaTruth = _state.GetKarma(KarmaAxis.Truth),
                KarmaFreedom = _state.GetKarma(KarmaAxis.Freedom),
            };

            // 键按序排：字典的遍历顺序不保证，而「同一份状态两次采集得到同样的键序」
            // 是存档最便宜的回归防线（也让用例能直接比两份 Capture 的结果）。
            var keys = new List<string>(_state.Values.Count);
            foreach (var pair in _state.Values)
            {
                keys.Add(pair.Key);
            }

            keys.Sort(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                data.FlagKeys.Add(keys[i]);
                data.FlagValues.Add(_state.Values[keys[i]]);
            }

            GameLog.Info(
                LogChannel.Save,
                $"已采集运行状态：地图 {data.MapId ?? "（不在图上）"}，状态键 {data.FlagKeys.Count} 个，主种子 {data.RandomSeed}。");

            return data;
        }

        public bool Apply(SaveData data)
        {
            if (data == null)
            {
                GameLog.Error(LogChannel.Save, "拒绝把空存档灌回运行期。");
                return false;
            }

            var report = _saves.Validate(data);
            if (!report.IsValid)
            {
                for (var i = 0; i < report.Problems.Count; i++)
                {
                    GameLog.Error(LogChannel.Save, $"读档被拒：{report.Problems[i]}");
                }

                return false;
            }

            // 先查图再动状态：目标图不在目录里时整体拒绝，而不是「账本已经换了、人还站在旧图上」。
            var hasMap = !string.IsNullOrEmpty(data.MapId);
            if (hasMap && _definitions != null && !_definitions.TryGet(data.MapId, out MapDefinition _))
            {
                GameLog.Error(
                    LogChannel.Save,
                    $"存档指向的地图 {data.MapId} 不在定义目录里，已整体拒绝读档。",
                    data.MapId);
                return false;
            }

            // 账本整本换掉：读档是「换成另一份状态」，不是「在当前状态上叠加」。
            // 上一局留下的键若跟着过来，只有玩家读档之后才会发现。
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            var keys = data.FlagKeys ?? new List<string>();
            var flags = data.FlagValues ?? new List<int>();
            var count = Math.Min(keys.Count, flags.Count);
            for (var i = 0; i < count; i++)
            {
                values[keys[i]] = flags[i];
            }

            _state.Restore(values, data.KarmaCompassion, data.KarmaTruth, data.KarmaFreedom);

            // 母种子先复位、再进图：探索流是进图那一刻取的（ExplorationService.EnterMapCore），
            // 晚一步复位，这次进图就落在旧种子的序列上了。
            if (_random != null && data.RandomSeed != 0UL)
            {
                _random.Reseed(data.RandomSeed);
            }

            if (!hasMap)
            {
                // 存档本来就是「还没进图」的状态：离图，而不是替玩家留在旧图上。
                _exploration.LeaveMap();
                GameLog.Info(LogChannel.Save, $"已读档：不在任何地图上，状态键 {count} 个。");
                return true;
            }

            var session = _exploration.EnterMap(data.MapId, new GridPosition(data.GridX, data.GridY));
            if (session == null)
            {
                // 进图失败只可能是「查表时还认得、建会话时又不认了」这类异常；
                // 账本与种子已经灌回去了，如实报出来而不是假装成功。
                GameLog.Error(
                    LogChannel.Save,
                    $"读档时进图失败：{data.MapId}。账本与主种子已灌回，位置没有恢复。",
                    data.MapId);
                return false;
            }

            GameLog.Info(
                LogChannel.Save,
                $"已读档：地图 {session.MapId}，落点 {session.Position}，状态键 {count} 个，主种子 {data.RandomSeed}。",
                session.MapId);

            return true;
        }

        public bool SaveToSlot(int slot)
        {
            var data = Capture();
            if (!_saves.TrySave(slot, data))
            {
                return false;
            }

            LastSlot = slot;
            GameLog.Info(
                LogChannel.Save,
                $"已保存到槽位 {slot}（状态键 {data.FlagKeys.Count} 个，地图 {data.MapId ?? "（不在图上）"}）。");
            return true;
        }

        public bool LoadFromSlot(int slot)
        {
            if (!_saves.TryLoad(slot, out var data))
            {
                return false;
            }

            if (!Apply(data))
            {
                return false;
            }

            LastSlot = slot;
            return true;
        }

        /// <summary>
        /// 没有要注册的东西：这个实现是按接口被调用的，不做任何订阅。
        /// </summary>
        /// <remarks>
        /// 它仍然实现 <see cref="IService"/>，因为界面是<b>从服务注册表按接口</b>拿到它的——
        /// 与组合根之间的那条线只有「注册」这一种连法，别的连法都会让界面认识 <c>Flow</c>。
        /// </remarks>
        public void OnRegistered(IServiceRegistry registry)
        {
        }

        public void OnUnregistered()
        {
        }
    }
}
