using System;
using System.Collections.Generic;
using System.IO;
using SamsaraWest.Data;

namespace SamsaraWest.Editor
{
    /// <summary>一张 CSV 表与一个定义类型之间的绑定关系。</summary>
    public readonly struct DefinitionImportBinding
    {
        public DefinitionImportBinding(string tableName, Type definitionType, string folderName, string displayName)
        {
            TableName = tableName;
            DefinitionType = definitionType;
            FolderName = folderName;
            DisplayName = displayName;
        }

        /// <summary>表名（不含扩展名），例如 characters。</summary>
        public string TableName { get; }

        public Type DefinitionType { get; }

        /// <summary>Definition 资产输出的子目录名。</summary>
        public string FolderName { get; }

        public string DisplayName { get; }

        public string TableAssetPath => $"{SamsaraWestPaths.DataTables}/{TableName}.csv";

        public string OutputFolder => $"{SamsaraWestPaths.DefinitionsRoot}/{FolderName}";
    }

    /// <summary>
    /// 表 → 类型的映射表。刻意写成显式的清单而不是靠命名猜：
    /// 新增一类数据对象时，改这里一行，导入管线与校验工具立刻认识它。
    /// </summary>
    public static class DefinitionImportMap
    {
        private static readonly DefinitionImportBinding[] All =
        {
            new DefinitionImportBinding("characters", typeof(CharacterDefinition), "Character", "角色"),
            new DefinitionImportBinding("skills", typeof(SkillDefinition), "Skill", "技能"),
            new DefinitionImportBinding("statuses", typeof(StatusDefinition), "Status", "状态"),
            new DefinitionImportBinding("enemies", typeof(EnemyDefinition), "Enemy", "敌人"),
            new DefinitionImportBinding("encounters", typeof(EncounterDefinition), "Encounter", "遭遇"),
            new DefinitionImportBinding("bossphases", typeof(BossPhaseDefinition), "BossPhase", "Boss 阶段"),
            new DefinitionImportBinding("items", typeof(ItemDefinition), "Item", "道具"),
            new DefinitionImportBinding("equipment", typeof(EquipmentDefinition), "Equipment", "装备"),
            new DefinitionImportBinding("sutras", typeof(SutraDefinition), "Sutra", "经文"),
            new DefinitionImportBinding("quests", typeof(QuestDefinition), "Quest", "任务"),
            new DefinitionImportBinding("dialogues", typeof(DialogueDefinition), "Dialogue", "对话节点"),
            new DefinitionImportBinding("maps", typeof(MapDefinition), "Map", "地图"),
            new DefinitionImportBinding("interactables", typeof(InteractableDefinition), "Interactable", "可交互物"),
            new DefinitionImportBinding("loottables", typeof(LootTableDefinition), "LootTable", "掉落表"),
            new DefinitionImportBinding("shops", typeof(ShopDefinition), "Shop", "商店"),
            new DefinitionImportBinding("recipes", typeof(RecipeDefinition), "Recipe", "配方"),
            new DefinitionImportBinding("endingconditions", typeof(EndingConditionDefinition), "EndingCondition", "结局条件"),
        };

        public static IReadOnlyList<DefinitionImportBinding> Bindings => All;

        public static bool TryResolve(string tableName, out DefinitionImportBinding binding)
        {
            for (var i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].TableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    binding = All[i];
                    return true;
                }
            }

            binding = default;
            return false;
        }

        /// <summary>目录里存在但没有映射的 CSV，导入时会提示，避免「表填了没人管」。</summary>
        public static List<string> FindUnmappedTables(string absoluteTablesFolder)
        {
            var result = new List<string>();
            if (!Directory.Exists(absoluteTablesFolder))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(absoluteTablesFolder, "*.csv", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!TryResolve(name, out _))
                {
                    result.Add(name);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
