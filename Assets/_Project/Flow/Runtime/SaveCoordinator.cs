using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Economy;
using SamsaraWest.Equipment;
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
    /// 手上还拎着钱袋与背包、身上还穿着装备与经文、兜里还有随机服务的母种子，
    /// 而这些都<b>不许</b>被 <c>Save</c> 认识。
    /// 与三条 Link 同一处境，区别只在它是「按接口被调用」而不是「订阅事件」。</para>
    ///
    /// <para>两处顺序是有意的：<b>先校验再动状态</b>（目标图查不到时整体拒绝，
    /// 免得留下「账本换了、人还在旧图上」这种半截状态），以及<b>先复位母种子再进图</b>
    /// （探索流是在进图那一刻取的，晚一步复位就落在旧种子的序列上）。</para>
    ///
    /// <para><b>钱与物是第二十一轮接上的</b>：<c>SaveData</c> 里 <c>Gold</c> 与
    /// <c>InventoryItemIds/Counts</c> 早就备好了字段，缺的从来是运行期真源（ADR-025 的代价栏）。
    /// 经济模块落地之后，这里才第一次有东西可搬——搬运方式与状态账完全一样：整本进、整本出。</para>
    ///
    /// <para><b>在身清单是第二十二轮接上的</b>（ADR-028）：<c>EquipmentAssignment</c> 字段的来头更早
    /// （v3 就留好了，ADR-015），缺的同样是运行期真源。三组东西的搬法一模一样，所以这里没有第四种写法。</para>
    /// </remarks>
    public sealed class SaveCoordinator : ISaveCoordinator
    {
        private readonly ISaveService _saves;
        private readonly IStoryState _state;
        private readonly IExplorationService _exploration;
        private readonly IDefinitionRegistry _definitions;
        private readonly IRandomService _random;
        private readonly IEconomyService _economy;
        private readonly IEquipmentService _equipment;

        /// <param name="saves">存档读写（落盘与校验）。</param>
        /// <param name="state">剧情状态账：整本进、整本出。</param>
        /// <param name="exploration">探索服务：位置从它来，读档也回到它身上。</param>
        /// <param name="definitions">定义目录；给了就先查「存档指向的图还在不在」再动状态。</param>
        /// <param name="random">随机服务；给了就搬运主种子（缺省则不搬，读档后随机序列不会回到原处）。</param>
        /// <param name="economy">
        /// 钱袋与背包；给了就搬运金钱与物品（缺省则不搬，钱与物不进档、读档也换不回来）。
        /// </param>
        /// <param name="equipment">
        /// 在身清单；给了就搬运「谁穿了什么」（缺省则不搬，一身装备不进档、读档也换不回来）。
        /// </param>
        public SaveCoordinator(
            ISaveService saves,
            IStoryState state,
            IExplorationService exploration,
            IDefinitionRegistry definitions = null,
            IRandomService random = null,
            IEconomyService economy = null,
            IEquipmentService equipment = null)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _exploration = exploration ?? throw new ArgumentNullException(nameof(exploration));
            _definitions = definitions;
            _random = random;
            _economy = economy;
            _equipment = equipment;
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

            // 钱与物：这两个字段在 SaveData 里躺了很久，第二十一轮之前没有任何运行期真源可搬
            // （经济模块还没有实现），所以一直是空的。
            if (_economy != null)
            {
                data.Gold = _economy.Gold;

                // 顺序不必再排：IEconomyService.Items 本身就是按 ID 排序的快照，
                // 与上面状态键同一个理由——同一份状态两次采集必须得到同样的排列。
                var stacks = _economy.Items;
                for (var i = 0; i < stacks.Count; i++)
                {
                    data.InventoryItemIds.Add(stacks[i].ItemId);
                    data.InventoryItemCounts.Add(stacks[i].Count);
                }
            }

            // 在身清单：字段同样是「早就在档里躺着、缺个真源」，第二十二轮才有第一个持有者（ADR-028）。
            // 顺序也不必再排：IEquipmentService.Snapshot 本身就是按「成员 ID → 栏位展示顺序」排好的，
            // 理由同上——同一身装备两次采集必须得到同样的排列，否则会写出两份「内容相同、顺序不同」的档。
            if (_equipment != null)
            {
                var equipped = _equipment.Snapshot;
                for (var i = 0; i < equipped.Count; i++)
                {
                    data.Equipment.Add(new EquipmentAssignment
                    {
                        CharacterId = equipped[i].CharacterId,
                        SlotId = equipped[i].SlotId,
                        ItemId = equipped[i].ItemId,
                    });
                }
            }

            GameLog.Info(
                LogChannel.Save,
                $"已采集运行状态：地图 {data.MapId ?? "（不在图上）"}，状态键 {data.FlagKeys.Count} 个，"
                + $"在身 {data.Equipment.Count} 件，主种子 {data.RandomSeed}。");

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

            // 钱与物跟账本一起整本换掉：读档是「换成另一份状态」，不是「往现在的钱袋里加」。
            if (_economy != null)
            {
                var itemIds = data.InventoryItemIds ?? new List<string>();
                var itemCounts = data.InventoryItemCounts ?? new List<int>();
                var stackCount = Math.Min(itemIds.Count, itemCounts.Count);
                var stacks = new List<ItemStack>(stackCount);
                for (var i = 0; i < stackCount; i++)
                {
                    stacks.Add(new ItemStack(itemIds[i], itemCounts[i]));
                }

                _economy.Restore(data.Gold, stacks);
            }

            // 在身清单跟账本、钱物一起整本换掉：读档是「换成存档里那一身」，
            // 不是在现在这一身外面再套一件。进不来的条目（栏位名认不出、定义查不到、
            // 限定角色不符、或数据表已经改过）由服务自己跳过并留错误日志——
            // 半身装备不该让整份存档读不进来。
            if (_equipment != null)
            {
                var assigned = data.Equipment ?? new List<EquipmentAssignment>();
                var equipped = new List<EquippedItem>(assigned.Count);
                for (var i = 0; i < assigned.Count; i++)
                {
                    if (assigned[i] == null)
                    {
                        continue;
                    }

                    equipped.Add(new EquippedItem(assigned[i].CharacterId, assigned[i].SlotId, assigned[i].ItemId));
                }

                _equipment.Restore(equipped);
            }

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
                // 账本、钱袋与种子已经灌回去了，如实报出来而不是假装成功。
                GameLog.Error(
                    LogChannel.Save,
                    $"读档时进图失败：{data.MapId}。账本、钱袋与主种子已灌回，位置没有恢复。",
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
