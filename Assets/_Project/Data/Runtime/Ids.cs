using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SamsaraWest.Data
{
    /// <summary>数据种类。与 17 个定义类一一对应。</summary>
    public enum DefinitionKind
    {
        Unknown = 0,
        Character = 1,
        Skill = 2,
        Status = 3,
        Enemy = 4,
        Encounter = 5,
        BossPhase = 6,
        Item = 7,
        Equipment = 8,
        Sutra = 9,
        Quest = 10,
        Dialogue = 11,
        Map = 12,
        Interactable = 13,
        LootTable = 14,
        Shop = 15,
        Recipe = 16,
        EndingCondition = 17,
    }

    /// <summary>
    /// ID 命名规则的唯一真源。规则与 <c>E:\tx2\开发md文件\完整章节剧本\00-总目录与写作规范.md</c>
    /// 中既有的 <c>CH01_MAP01</c> / <c>ENC_CH01_001</c> 约定保持一致，不另造一套。
    /// </summary>
    public static class IdRules
    {
        /// <summary>章节序号，两位数字，例如 01。</summary>
        public const string ChapterSegment = @"CH\d{2}";

        private static readonly Dictionary<DefinitionKind, string> Patterns = new Dictionary<DefinitionKind, string>
        {
            { DefinitionKind.Character, @"^CHR_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Skill, @"^SKL_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Status, @"^STS_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Enemy, @"^ENM_[A-Z0-9_]{2,40}$" },

            // 遭遇：普通 ENC_、精英 EENC_、Boss BENC_，后接章节与三位序号。
            { DefinitionKind.Encounter, @"^(ENC|EENC|BENC)_CH\d{2}_\d{3}$" },
            { DefinitionKind.BossPhase, @"^BSP_CH\d{2}_\d{3}_P\d$" },

            { DefinitionKind.Item, @"^ITM_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Equipment, @"^EQP_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Sutra, @"^SUT_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Quest, @"^QST_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Dialogue, @"^DLG_(CH\d{2}_\d{3}|[A-Z0-9_]{2,40})$" },

            // 地图沿用剧本既有写法：CH01_MAP01。
            { DefinitionKind.Map, @"^CH\d{2}_MAP\d{2}$" },
            { DefinitionKind.Interactable, @"^INT_CH\d{2}_[A-Z0-9_]{2,40}$" },

            { DefinitionKind.LootTable, @"^LUT_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Shop, @"^SHP_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.Recipe, @"^RCP_[A-Z0-9_]{2,40}$" },
            { DefinitionKind.EndingCondition, @"^END_[A-Z0-9_]{2,40}$" },
        };

        private static readonly Dictionary<DefinitionKind, Regex> Compiled = new Dictionary<DefinitionKind, Regex>();

        private static readonly Regex NodePattern = new Regex(
            @"^CH\d{2}_N\d{2,3}_[A-Z0-9_]{2,40}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StateKeyPattern = new Regex(
            @"^(flag|relation|karma|ending)\.[a-z0-9_]+(\.[a-z0-9_]+)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>本地化键：允许小写点分或大写下划线风格，禁止直接写中文。</summary>
        private static readonly Regex LocKeyPattern = new Regex(
            @"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ChinesePattern = new Regex(
            @"[\u4e00-\u9fff\u3400-\u4dbf]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidId(DefinitionKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (!Patterns.TryGetValue(kind, out var pattern))
            {
                return !string.IsNullOrWhiteSpace(id);
            }

            if (!Compiled.TryGetValue(kind, out var regex))
            {
                regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                Compiled[kind] = regex;
            }

            return regex.IsMatch(id);
        }

        public static string GetPattern(DefinitionKind kind) =>
            Patterns.TryGetValue(kind, out var pattern) ? pattern : "<无规则>";

        /// <summary>剧本节点 ID，例如 <c>CH01_N01_ENTRY</c>。</summary>
        public static bool IsValidNarrativeNodeId(string id) =>
            !string.IsNullOrWhiteSpace(id) && NodePattern.IsMatch(id);

        /// <summary>剧情状态变量键，例如 <c>flag.ch01.truth_told</c>、<c>karma.compassion</c>。</summary>
        public static bool IsValidStateKey(string key) =>
            !string.IsNullOrWhiteSpace(key) && StateKeyPattern.IsMatch(key);

        public static bool IsValidLocalizationKey(string key) =>
            !string.IsNullOrWhiteSpace(key) && LocKeyPattern.IsMatch(key);

        /// <summary>是否含中日韩汉字。用于「UI 文本无硬编码中文」的兜底扫描。</summary>
        public static bool ContainsChinese(string text) =>
            !string.IsNullOrEmpty(text) && ChinesePattern.IsMatch(text);

        /// <summary>为本地化键生成编辑器友好的常量名，例如 <c>ui.battle.attack</c> → <c>UI_BATTLE_ATTACK</c>。</summary>
        public static string ToConstantName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            var buffer = new System.Text.StringBuilder(key.Length + 4);
            foreach (var character in key)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer.Append(char.ToUpperInvariant(character));
                }
                else if (character == '.' || character == '_' || character == '-' || character == ' ')
                {
                    if (buffer.Length > 0 && buffer[buffer.Length - 1] != '_')
                    {
                        buffer.Append('_');
                    }
                }
            }

            return buffer.ToString().TrimEnd('_');
        }
    }
}
