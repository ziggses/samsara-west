using System;
using System.Collections.Generic;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 一件「在身」的装备：栏位名 + 定义 ID。
    /// </summary>
    /// <remarks>
    /// 栏位用<b>字符串名</b>而不是 <see cref="EquipmentSlot"/>，口径与存档 <c>EquipmentAssignment</c> 一致
    /// （见 <c>Docs/架构决策.md</c> ADR-015）：交来源的一侧（存档、界面、测试建场器）不必持有枚举。
    /// 名称严格对齐 <see cref="EquipmentSlot"/> 的成员名，认不出来的一律按错误处理，而不是猜一个栏位出来。
    /// </remarks>
    public readonly struct LoadoutEntry
    {
        public LoadoutEntry(string slotId, string itemId)
        {
            SlotId = slotId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
        }

        /// <summary>栏位名，例如 <c>Weapon</c>、<c>Sutra</c>。</summary>
        public string SlotId { get; }

        /// <summary>装备或经文的定义 ID，例如 <c>EQP_STAFF_IRON</c>、<c>SUT_STILLNESS</c>。</summary>
        public string ItemId { get; }

        public override string ToString() =>
            string.IsNullOrEmpty(ItemId) ? SlotId : $"{SlotId}={ItemId}";
    }

    /// <summary>
    /// 一个成员的<b>有效数值</b>：角色基础值 + 在身装备 + 经文，合成一次的结果。
    /// </summary>
    /// <remarks>
    /// 这是<b>进场快照</b>而不是活对象：战斗只读它一次，之后的增减一律走状态与伤害公式，
    /// 不回头改这份数值。于是换装只影响<b>下一场</b>战斗，不会把进行中的战斗算乱。
    /// </remarks>
    public readonly struct CharacterStatsSnapshot
    {
        public CharacterStatsSnapshot(
            int maxHealth,
            int maxSpirit,
            int attack,
            int defense,
            int speed,
            int breakThreshold)
        {
            MaxHealth = maxHealth;
            MaxSpirit = maxSpirit;
            Attack = attack;
            Defense = defense;
            Speed = speed;
            BreakThreshold = breakThreshold;
        }

        public int MaxHealth { get; }

        public int MaxSpirit { get; }

        public int Attack { get; }

        public int Defense { get; }

        public int Speed { get; }

        /// <summary>护体值上限，达到后进入破防状态。</summary>
        public int BreakThreshold { get; }

        public override string ToString() =>
            $"生命 {MaxHealth} · 灵力 {MaxSpirit} · 攻 {Attack} · 防 {Defense} · 速 {Speed} · 护体 {BreakThreshold}";
    }

    /// <summary>
    /// 属性聚合的唯一真源：把「角色基础值 + 在身装备 + 经文」算成一份 <see cref="CharacterStatsSnapshot"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>加算口径</b>：装备与经文的 <c>*Bonus</c> 一律<b>直接加</b>在角色基础值上，不乘、不叠乘、不按品阶缩放。
    /// 乘区留给状态与五行关系（见 <c>Docs/战斗数值-v1.md</c>），一层只做一件事，数值才校算得动。
    /// </para>
    /// <para>
    /// <b>夹下限</b>：负加成可以把数值压到下限——生命 1、灵力 0、攻 0、防 0、速 1、护体 1，
    /// 与 <c>BattleUnit</c> 构造时的兜底完全一致。两处都夹是刻意的：聚合层给出主口径，
    /// 单位构造再兜一次，避免别的入口绕过聚合层时造出「速度为 0」的单位。
    /// </para>
    /// <para>
    /// <b>暂不生效的字段</b>（本层不假装算过，留待各自接进伤害公式或回合回路）：
    /// <c>EquipmentDefinition.breakDamageBonus</c>（破防伤害加成）、<c>resistElement</c>（五行承伤减免）、
    /// <c>passiveSkillId</c>（被动技能）、<c>SutraDefinition.spiritRegenPerTurn</c>（每回合回灵）
    /// 与 <c>healthCostPerTurn</c>（苦修扣血），以及两者的 <c>requiredLevel</c>——角色目前没有等级来源，
    /// 因此等级校验无处可施，不臆造一套等级系统。
    /// </para>
    /// <para>
    /// <b>坏数据不毁战斗</b>：栏位对不上、限定角色不符、定义缺失的件一律跳过并记进报告，
    /// 让一场穿了半身错装备的战斗仍然打得下去，同时留下可断言的证据。
    /// </para>
    /// </remarks>
    public static class CharacterStatsResolver
    {
        /// <summary>角色自身的基础数值，不含任何装备加成。</summary>
        public static CharacterStatsSnapshot BaseOf(CharacterDefinition character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            return new CharacterStatsSnapshot(
                character.MaxHealth,
                character.MaxSpirit,
                character.Attack,
                character.Defense,
                character.Speed,
                character.BreakThreshold);
        }

        /// <summary>
        /// 算出一名成员的有效数值。
        /// </summary>
        /// <param name="character">角色定义。</param>
        /// <param name="loadout">
        /// 在身清单。为 <c>null</c> 或空表示这名成员没穿任何东西，此时直接返回基础值，
        /// 连 <paramref name="registry"/> 都不必查——「没装备」是最常见的情形，不该有额外代价。
        /// </param>
        /// <param name="registry">数据查询入口，用于按 ID 取装备与经文定义。</param>
        /// <param name="report">
        /// 校验报告，<b>必填</b>。跳过任何一件都要留下结论，不允许静默算出一个少了加成的数值。
        /// </param>
        public static CharacterStatsSnapshot Resolve(
            CharacterDefinition character,
            IReadOnlyList<LoadoutEntry> loadout,
            IDefinitionRegistry registry,
            ValidationReport report)
        {
            var baseStats = BaseOf(character);
            if (loadout == null || loadout.Count == 0)
            {
                return baseStats;
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry), "在身清单非空时必须能查数据定义。");
            }

            if (report == null)
            {
                throw new ArgumentNullException(nameof(report), "聚合结论必须写进报告，否则错装备会被静默吞掉。");
            }

            var maxHealth = baseStats.MaxHealth;
            var maxSpirit = baseStats.MaxSpirit;
            var attack = baseStats.Attack;
            var defense = baseStats.Defense;
            var speed = baseStats.Speed;
            var breakThreshold = baseStats.BreakThreshold;

            // 一个栏位只算一件：谁先占上算谁的，重复的那件按警告跳过（存档侧同样禁止重复栏位）。
            var usedSlots = new HashSet<EquipmentSlot>();

            for (var i = 0; i < loadout.Count; i++)
            {
                var entry = loadout[i];

                // 空格位：存档里留着一行但已取消装备，是正常状态，不报。
                if (string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.SlotId))
                {
                    report.Error(
                        "LOADOUT_SLOT_EMPTY",
                        $"在身清单第 {i + 1} 项（{entry.ItemId}）没写栏位名，已跳过。",
                        character.Id,
                        fieldName: "slotId");
                    continue;
                }

                if (!TryParseSlot(entry.SlotId, out var slot))
                {
                    report.Error(
                        "LOADOUT_SLOT_UNKNOWN",
                        $"栏位名 '{entry.SlotId}' 不是可用栏位（共 {EquipmentSlots.Count} 个），'{entry.ItemId}' 已跳过。",
                        character.Id,
                        fieldName: "slotId");
                    continue;
                }

                if (!registry.TryGet(entry.ItemId, out var definition) || definition == null)
                {
                    report.Error(
                        "LOADOUT_ITEM_MISSING",
                        $"在身装备 '{entry.ItemId}' 在数据表里找不到，已跳过。",
                        character.Id,
                        fieldName: "itemId");
                    continue;
                }

                // 经文与装备走同一套「栏位必须对得上」的规矩：经文只认 Sutra 槽，
                // 于是「经文戴在武器栏」这类错配在开战时就暴露，而不是悄悄生效。
                EquipmentSlot expectedSlot;
                switch (definition)
                {
                    case EquipmentDefinition equipment:
                        expectedSlot = equipment.Slot;

                        // 限定角色：空数组表示全队可装备（口径见 EquipmentDefinition.AllowedCharacterIds）。
                        var allowed = equipment.AllowedCharacterIds;
                        if (allowed.Length > 0 && Array.IndexOf(allowed, character.Id) < 0)
                        {
                            report.Error(
                                "LOADOUT_CHARACTER_NOT_ALLOWED",
                                $"装备 '{entry.ItemId}' 限定 {string.Join("/", allowed)} 使用，{character.Id} 装备不上，已跳过。",
                                character.Id,
                                fieldName: "allowedCharacterIds");
                            continue;
                        }

                        break;

                    case SutraDefinition:
                        expectedSlot = EquipmentSlot.Sutra;
                        break;

                    default:
                        report.Error(
                            "LOADOUT_KIND_UNSUPPORTED",
                            $"'{entry.ItemId}' 是 {definition.Kind}，装备栏只接受装备与经文，已跳过。",
                            character.Id,
                            fieldName: "itemId");
                        continue;
                }

                if (expectedSlot != slot)
                {
                    report.Error(
                        "LOADOUT_SLOT_MISMATCH",
                        $"'{entry.ItemId}' 属于 {expectedSlot} 栏位，却挂在 {slot} 上，已跳过。",
                        character.Id,
                        fieldName: "slotId");
                    continue;
                }

                if (!usedSlots.Add(slot))
                {
                    report.Warn(
                        "LOADOUT_SLOT_DUPLICATE",
                        $"{slot} 栏位上出现多于一件，'{entry.ItemId}' 被忽略，只算先出现的那件。",
                        character.Id,
                        fieldName: "slotId");
                    continue;
                }

                switch (definition)
                {
                    case EquipmentDefinition equipment:
                        maxHealth += equipment.HealthBonus;
                        maxSpirit += equipment.SpiritBonus;
                        attack += equipment.AttackBonus;
                        defense += equipment.DefenseBonus;
                        speed += equipment.SpeedBonus;
                        break;

                    case SutraDefinition sutra:
                        maxHealth += sutra.HealthBonus;
                        maxSpirit += sutra.SpiritBonus;
                        attack += sutra.AttackBonus;
                        defense += sutra.DefenseBonus;
                        breakThreshold += sutra.BreakThresholdBonus;
                        break;
                }
            }

            return new CharacterStatsSnapshot(
                Math.Max(1, maxHealth),
                Math.Max(0, maxSpirit),
                Math.Max(0, attack),
                Math.Max(0, defense),
                Math.Max(1, speed),
                Math.Max(1, breakThreshold));
        }

        /// <summary>
        /// 解析栏位名。
        /// </summary>
        /// <remarks>
        /// 只认「名字 → 值 → 名字」完全一致的写法：<c>Enum.TryParse</c> 会把 <c>"99"</c>、
        /// <c>"Weapon, Armor"</c> 这类串也解析成功（后者还会凑出 <c>Talisman</c>），
        /// 因此解析完必须回写一次名字比对。大小写敏感，与存档写入的名称严格一致。
        /// </remarks>
        private static bool TryParseSlot(string slotId, out EquipmentSlot slot)
        {
            var trimmed = slotId.Trim();
            if (!Enum.TryParse(trimmed, out slot) || !EquipmentSlots.IsEquippable(slot))
            {
                return false;
            }

            return string.Equals(slot.ToString(), trimmed, StringComparison.Ordinal);
        }
    }
}
