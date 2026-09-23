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

        /// <summary>已装备定义 ID，与队伍成员一一对应。</summary>
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
                EquippedItemIds = new List<string>(EquippedItemIds),
            };
        }
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
