using System;
using System.Collections.Generic;

namespace SamsaraWest.Save
{
    /// <summary>
    /// 存档主体。用并列列表而非字典，保证可被 Unity 的 JSON 序列化原样写出。
    /// 新增字段一律追加，不改动既有字段含义，以配合迁移表。
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int Version;

        /// <summary>章节进度，1–8。序章用 0。</summary>
        public int ChapterIndex;

        /// <summary>当前地图 ID，例如 CH01_MAP01。</summary>
        public string MapId;

        /// <summary>玩家所在格坐标。</summary>
        public int GridX;
        public int GridY;

        /// <summary>主随机种子。战斗种子由它派生，保证读档后战斗可复现。</summary>
        public ulong RandomSeed;

        public int Gold;

        /// <summary>游戏累计时长（秒），用于结局结算与成就判定。</summary>
        public double PlayTimeSeconds;

        /// <summary>剧情状态键与数值。</summary>
        public List<string> FlagKeys = new List<string>();
        public List<int> FlagValues = new List<int>();

        /// <summary>心念三轴：compassion / truth / freedom。</summary>
        public int KarmaCompassion;
        public int KarmaTruth;
        public int KarmaFreedom;

        /// <summary>队伍成员定义 ID。</summary>
        public List<string> PartyCharacterIds = new List<string>();

        /// <summary>已完成任务 ID。</summary>
        public List<string> CompletedQuestIds = new List<string>();

        /// <summary>背包：道具 ID 与数量并列。</summary>
        public List<string> InventoryItemIds = new List<string>();
        public List<int> InventoryItemCounts = new List<int>();

        /// <summary>
        /// v3 起的装备分配：一个成员可同时占用多个栏位（最多 8 槽）。
        /// </summary>
        public List<EquipmentAssignment> Equipment = new List<EquipmentAssignment>();

        /// <summary>
        /// 【仅作迁移输入】v2 及更早的装备列表：与队伍成员一一对应，每人一件。
        /// v3 起不再写入新值，加载旧档时由迁移搬进 <see cref="Equipment"/> 并清空。
        /// </summary>
        public List<string> EquippedItemIds = new List<string>();

        public SaveData Clone()
        {
            return new SaveData
            {
                Version = Version,
                ChapterIndex = ChapterIndex,
                MapId = MapId,
                GridX = GridX,
                GridY = GridY,
                RandomSeed = RandomSeed,
                Gold = Gold,
                PlayTimeSeconds = PlayTimeSeconds,
                FlagKeys = new List<string>(FlagKeys),
                FlagValues = new List<int>(FlagValues),
                KarmaCompassion = KarmaCompassion,
                KarmaTruth = KarmaTruth,
                KarmaFreedom = KarmaFreedom,
                PartyCharacterIds = new List<string>(PartyCharacterIds),
                CompletedQuestIds = new List<string>(CompletedQuestIds),
                InventoryItemIds = new List<string>(InventoryItemIds),
                InventoryItemCounts = new List<int>(InventoryItemCounts),
                Equipment = CloneEquipment(Equipment),
                EquippedItemIds = new List<string>(EquippedItemIds),
            };
        }

        private static List<EquipmentAssignment> CloneEquipment(List<EquipmentAssignment> source)
        {
            var copy = new List<EquipmentAssignment>(source?.Count ?? 0);
            if (source == null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    copy.Add(source[i].Clone());
                }
            }

            return copy;
        }
    }

    /// <summary>
    /// 一件已穿戴的装备：成员 × 栏位 → 装备定义 ID。
    /// <para>
    /// 栏位用字符串名（与 <c>Data.EquipmentSlot</c> 的名称一致，例如 <c>Weapon</c>）而非枚举：
    /// <c>Save</c> 只依赖 <c>Core</c>（ADR-001），不得反向依赖 <c>Data</c>。
    /// 这同时让存档在数据层调整栏目时仍可读——无法识别的栏位会被校验拦下，而不是错位。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class EquipmentAssignment
    {
        /// <summary>队伍成员定义 ID，例如 <c>CHR_WUKONG</c>。</summary>
        public string CharacterId;

        /// <summary>栏位名，取值见 <c>EquipmentSlot</c> 的名称，例如 <c>Weapon</c>。</summary>
        public string SlotId;

        /// <summary>装备定义 ID，例如 <c>EQP_STAFF_IRON</c>。</summary>
        public string ItemId;

        public EquipmentAssignment Clone() => new EquipmentAssignment
        {
            CharacterId = CharacterId,
            SlotId = SlotId,
            ItemId = ItemId,
        };
    }

    /// <summary>存档元信息，用于存档界面展示，不必加载完整存档。</summary>
    [Serializable]
    public sealed class SaveSlotInfo
    {
        public int Slot;
        public string SavedAtIso;
        public int Version;
        public int ChapterIndex;
        public double PlayTimeSeconds;
        public bool IsEmpty;
    }

    /// <summary>存档校验结果。损坏的存档必须被发现，而不是让游戏带着残缺状态继续跑。</summary>
    public readonly struct SaveIntegrityReport
    {
        public SaveIntegrityReport(bool isValid, IReadOnlyList<string> problems)
        {
            IsValid = isValid;
            Problems = problems ?? Array.Empty<string>();
        }

        public bool IsValid { get; }

        public IReadOnlyList<string> Problems { get; }

        public static SaveIntegrityReport Valid => new SaveIntegrityReport(true, Array.Empty<string>());
    }
}
