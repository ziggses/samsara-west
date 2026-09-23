using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SamsaraWest.Core;
using UnityEngine;

namespace SamsaraWest.Save
{
    /// <summary>
    /// 版本化 JSON 存档。写入走「先写临时文件再原子替换」，
    /// 中途崩溃最多丢失本次写入，不会把好存档写坏。
    /// </summary>
    public interface ISaveService : IService
    {
        int CurrentVersion { get; }

        string SaveDirectory { get; }

        bool Exists(int slot);

        bool TrySave(int slot, SaveData data);

        bool TryLoad(int slot, out SaveData data);

        SaveSlotInfo Describe(int slot);

        bool TryDelete(int slot);

        IReadOnlyList<int> ListSlots();

        /// <summary>校验存档内部一致性（并列数组等长、ID 非空）。</summary>
        SaveIntegrityReport Validate(SaveData data);
    }

    public sealed class SaveService : ISaveService
    {
        /// <summary>当前存档格式版本。破坏性改动时 +1，并在 <see cref="Migrate"/> 中补一条迁移。</summary>
        public const int LatestVersion = 2;

        private readonly string _directory;

        public SaveService(string saveDirectory = null)
        {
            _directory = string.IsNullOrWhiteSpace(saveDirectory)
                ? Path.Combine(Application.persistentDataPath, "saves")
                : saveDirectory;
        }

        public int CurrentVersion => LatestVersion;

        public string SaveDirectory => _directory;

        public void OnRegistered(IServiceRegistry registry)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                GameLog.Info(LogChannel.Save, $"存档目录就绪：{_directory}（格式版本 {LatestVersion}）");
            }
            catch (Exception exception)
            {
                GameLog.Error(LogChannel.Save, $"无法创建存档目录 {_directory}：{exception.Message}");
            }
        }

        public void OnUnregistered()
        {
        }

        public bool Exists(int slot) => File.Exists(PathFor(slot));

        public bool TrySave(int slot, SaveData data)
        {
            if (data == null)
            {
                GameLog.Error(LogChannel.Save, $"拒绝写入空存档（槽位 {slot}）。");
                return false;
            }

            // 先盖章再校验：内存里的存档对象不带版本号时，不能因为它「还没写盘」就拒绝保存。
            data.Version = LatestVersion;

            var report = Validate(data);
            if (!report.IsValid)
            {
                for (var i = 0; i < report.Problems.Count; i++)
                {
                    GameLog.Error(LogChannel.Save, $"存档校验失败：{report.Problems[i]}");
                }

                return false;
            }

            try
            {
                Directory.CreateDirectory(_directory);
                var path = PathFor(slot);
                var temporary = path + ".tmp";

                File.WriteAllText(temporary, JsonUtility.ToJson(data, prettyPrint: true), new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }

                GameLog.Info(LogChannel.Save, $"已写入存档槽位 {slot}（第 {data.ChapterIndex} 章）。");
                return true;
            }
            catch (Exception exception)
            {
                GameLog.Error(LogChannel.Save, $"写入存档槽位 {slot} 失败：{exception.Message}");
                return false;
            }
        }

        public bool TryLoad(int slot, out SaveData data)
        {
            data = null;
            var path = PathFor(slot);

            if (!File.Exists(path))
            {
                GameLog.Warn(LogChannel.Save, $"存档槽位 {slot} 不存在。");
                return false;
            }

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = JsonUtility.FromJson<SaveData>(json);

                if (loaded == null)
                {
                    GameLog.Error(LogChannel.Save, $"存档槽位 {slot} 解析结果为空，文件可能已损坏。");
                    return false;
                }

                var originalVersion = loaded.Version;
                if (!Migrate(loaded))
                {
                    GameLog.Error(LogChannel.Save, $"存档槽位 {slot} 无法从版本 {originalVersion} 迁移到 {LatestVersion}。");
                    return false;
                }

                var report = Validate(loaded);
                if (!report.IsValid)
                {
                    for (var i = 0; i < report.Problems.Count; i++)
                    {
                        GameLog.Error(LogChannel.Save, $"存档槽位 {slot} 完整性检查未通过：{report.Problems[i]}");
                    }

                    return false;
                }

                if (originalVersion != LatestVersion)
                {
                    GameLog.Info(LogChannel.Save, $"存档槽位 {slot} 已从版本 {originalVersion} 迁移到 {LatestVersion}。");
                }

                data = loaded;
                return true;
            }
            catch (Exception exception)
            {
                GameLog.Error(LogChannel.Save, $"读取存档槽位 {slot} 失败：{exception.Message}");
                return false;
            }
        }

        public SaveSlotInfo Describe(int slot)
        {
            var info = new SaveSlotInfo { Slot = slot, IsEmpty = !Exists(slot) };
            if (info.IsEmpty)
            {
                return info;
            }

            try
            {
                var json = File.ReadAllText(PathFor(slot), Encoding.UTF8);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data != null)
                {
                    info.Version = data.Version;
                    info.ChapterIndex = data.ChapterIndex;
                    info.PlayTimeSeconds = data.PlayTimeSeconds;
                }

                info.SavedAtIso = File.GetLastWriteTimeUtc(PathFor(slot)).ToString("O");
            }
            catch (Exception exception)
            {
                GameLog.Warn(LogChannel.Save, $"读取槽位 {slot} 摘要失败：{exception.Message}");
                info.IsEmpty = true;
            }

            return info;
        }

        public bool TryDelete(int slot)
        {
            try
            {
                var path = PathFor(slot);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    GameLog.Info(LogChannel.Save, $"已删除存档槽位 {slot}。");
                }

                return true;
            }
            catch (Exception exception)
            {
                GameLog.Error(LogChannel.Save, $"删除存档槽位 {slot} 失败：{exception.Message}");
                return false;
            }
        }

        public IReadOnlyList<int> ListSlots()
        {
            var slots = new List<int>();
            if (!Directory.Exists(_directory))
            {
                return slots;
            }

            foreach (var file in Directory.GetFiles(_directory, "slot_*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split('_');
                if (parts.Length == 2 && int.TryParse(parts[1], out var slot))
                {
                    slots.Add(slot);
                }
            }

            slots.Sort();
            return slots;
        }

        public SaveIntegrityReport Validate(SaveData data)
        {
            if (data == null)
            {
                return new SaveIntegrityReport(false, new[] { "存档对象为空。" });
            }

            var problems = new List<string>();

            if (data.Version < 1 || data.Version > LatestVersion)
            {
                problems.Add($"版本号 {data.Version} 不在支持范围 1–{LatestVersion}。");
            }

            if (data.ChapterIndex < 0 || data.ChapterIndex > 8)
            {
                problems.Add($"章节序号 {data.ChapterIndex} 越界（允许 0–8）。");
            }

            if (data.FlagKeys.Count != data.FlagValues.Count)
            {
                problems.Add($"FlagKeys 有 {data.FlagKeys.Count} 项，FlagValues 有 {data.FlagValues.Count} 项，必须等长。");
            }

            if (data.InventoryItemIds.Count != data.InventoryItemCounts.Count)
            {
                problems.Add(
                    $"InventoryItemIds 有 {data.InventoryItemIds.Count} 项，InventoryItemCounts 有 {data.InventoryItemCounts.Count} 项，必须等长。");
            }

            for (var i = 0; i < data.FlagKeys.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(data.FlagKeys[i]))
                {
                    problems.Add($"FlagKeys 第 {i + 1} 项为空。");
                }
            }

            if (data.KarmaCompassion < 0 || data.KarmaTruth < 0 || data.KarmaFreedom < 0)
            {
                problems.Add("心念数值不能为负。");
            }

            if (data.Gold < 0)
            {
                problems.Add($"金钱为负（{data.Gold}）。");
            }

            return new SaveIntegrityReport(problems.Count == 0, problems);
        }

        /// <summary>
        /// 旧版本迁移。每一步只负责跨越一个版本，便于单独测试。
        /// 返回 false 表示无法迁移，调用方应拒绝加载而不是带病继续。
        /// </summary>
        private bool Migrate(SaveData data)
        {
            if (data.Version > LatestVersion)
            {
                GameLog.Error(LogChannel.Save, $"存档版本 {data.Version} 高于本程序支持的 {LatestVersion}，请升级游戏。");
                return false;
            }

            if (data.Version < 1)
            {
                // 版本 0 是骨架期未带版本号的存档：兜底补全字段并视为 v1。
                data.Version = 1;
                data.FlagKeys ??= new List<string>();
                data.FlagValues ??= new List<int>();
                data.PartyCharacterIds ??= new List<string>();
                data.CompletedQuestIds ??= new List<string>();
                data.InventoryItemIds ??= new List<string>();
                data.InventoryItemCounts ??= new List<int>();
                data.EquippedItemIds ??= new List<string>();
            }

            if (data.Version == 1)
            {
                // v1 → v2：心念从「单一数值」改为三轴，旧档默认按中立处理。
                if (data.KarmaCompassion < 0)
                {
                    data.KarmaCompassion = 0;
                }

                data.Version = 2;
            }

            data.FlagKeys ??= new List<string>();
            data.FlagValues ??= new List<int>();
            data.PartyCharacterIds ??= new List<string>();
            data.CompletedQuestIds ??= new List<string>();
            data.InventoryItemIds ??= new List<string>();
            data.InventoryItemCounts ??= new List<int>();
            data.EquippedItemIds ??= new List<string>();

            return data.Version == LatestVersion;
        }

        private string PathFor(int slot) => Path.Combine(_directory, $"slot_{slot:00}.json");
    }
}
