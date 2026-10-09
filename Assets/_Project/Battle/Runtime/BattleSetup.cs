using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>一个参战单位的「建队单」：要哪个定义、站哪一格。它不含任何运行期状态。</summary>
    public readonly struct BattleUnitBlueprint
    {
        public BattleUnitBlueprint(string definitionId, FormationSlot slot)
        {
            DefinitionId = definitionId;
            Slot = slot;
        }

        public string DefinitionId { get; }

        public FormationSlot Slot { get; }

        public override string ToString() => $"{DefinitionId}@{Slot}";
    }

    /// <summary>
    /// 一场战斗的入场清单：双方阵容 + 演出用的键。
    /// </summary>
    /// <remarks>
    /// 它是「怎么打」与「打什么」的分界：<see cref="BattleSession"/> 只认这份清单，
    /// 不认 <see cref="EncounterDefinition"/>，于是测试里可以用两行代码拼出任意阵容，
    /// 不必先造一张遭遇资产。
    /// </remarks>
    public sealed class BattleSetup
    {
        public const int MaxUnitsPerSide = FormationSlot.Capacity;

        public BattleSetup(
            string encounterId,
            IReadOnlyList<BattleUnitBlueprint> party,
            IReadOnlyList<BattleUnitBlueprint> enemies,
            bool isBoss = false,
            string backgroundKey = null,
            string bgmKey = null,
            IBattleInventory inventory = null,
            IBattleLoadout loadout = null,
            IReadOnlyList<string> bossPhaseIds = null)
        {
            EncounterId = encounterId;
            Party = party ?? Array.Empty<BattleUnitBlueprint>();
            Enemies = enemies ?? Array.Empty<BattleUnitBlueprint>();
            IsBoss = isBoss;
            BackgroundKey = backgroundKey;
            BgmKey = bgmKey;
            Inventory = inventory;
            Loadout = loadout;
            BossPhaseIds = bossPhaseIds ?? Array.Empty<string>();
        }

        /// <summary>遭遇 ID，来自 <c>ENC_CH01_001</c> 这类既有约定。手工构造时可以为空。</summary>
        public string EncounterId { get; }

        public IReadOnlyList<BattleUnitBlueprint> Party { get; }

        public IReadOnlyList<BattleUnitBlueprint> Enemies { get; }

        public bool IsBoss { get; }

        /// <summary>战斗背景资源键，交给界面层解析。</summary>
        public string BackgroundKey { get; }

        /// <summary>战斗音乐资源键，交给音频层解析。</summary>
        public string BgmKey { get; }

        /// <summary>
        /// 这一战能吃到哪些道具。为 null 表示「这场没有道具可用」——
        /// 界面据此不画道具按钮，内核遇到用道具的指令直接给 <see cref="BattleCommandRejection.ItemOutOfStock"/>。
        /// </summary>
        /// <remarks>
        /// 它是<b>引用</b>而不是清单：背包是活的，用掉一个之后数量要留在原处，
        /// 所以这里持的是 <see cref="IBattleInventory"/> 本身，不是一份拷贝。
        /// </remarks>
        public IBattleInventory Inventory { get; }

        /// <summary>
        /// 开战时每个成员身上带了什么装备与经文。为 null 表示「这一战按裸装打」——
        /// 所有成员的数值都取 <see cref="CharacterDefinition"/> 的基础值。
        /// </summary>
        /// <remarks>
        /// 装备加成在<b>建单位那一刻</b>就并进 <see cref="BattleUnit"/> 的生命／灵力／攻／防／速／护体
        /// （口径见 <see cref="CharacterStatsResolver"/>），之后换装不会回头改动进行中的战斗。
        /// </remarks>
        public IBattleLoadout Loadout { get; }

        /// <summary>
        /// 这一战按哪几档来打（<c>bossphases.csv</c> 的 ID）。空表示「这场没有阶段机」——
        /// 敌方就算标了 <c>isBoss</c>，也只是一场普通敌袭，见 ADR-030。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="Party"/>／<see cref="Enemies"/> 同一条口径：<see cref="BattleSession"/> 只认这份清单，
        /// 不认 <see cref="EncounterDefinition"/>。因此测试里可以手工拼一档出来，
        /// 不必先造遭遇资产与四张阶段资产。
        /// </remarks>
        public IReadOnlyList<string> BossPhaseIds { get; }

        /// <summary>
        /// 入场清单自检。不合法时返回 false 并把原因写进 <paramref name="error"/>，
        /// 由调用方决定是报错还是降级——内核不替调用方吞掉配置错误。
        /// </summary>
        public bool Validate(out string error)
        {
            if (Party.Count == 0)
            {
                error = "我方阵容为空，无法开战。";
                return false;
            }

            if (Enemies.Count == 0)
            {
                error = "敌方阵容为空，无法开战。";
                return false;
            }

            if (Party.Count > MaxUnitsPerSide)
            {
                error = $"我方 {Party.Count} 个单位超出 3×2 阵型容量 {MaxUnitsPerSide}。";
                return false;
            }

            if (Enemies.Count > MaxUnitsPerSide)
            {
                error = $"敌方 {Enemies.Count} 个单位超出 3×2 阵型容量 {MaxUnitsPerSide}。";
                return false;
            }

            if (!ValidateSlots(Party, "我方", out error))
            {
                return false;
            }

            if (!ValidateSlots(Enemies, "敌方", out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool ValidateSlots(IReadOnlyList<BattleUnitBlueprint> units, string sideLabel, out string error)
        {
            var used = new HashSet<int>();
            for (var i = 0; i < units.Count; i++)
            {
                var blueprint = units[i];
                if (string.IsNullOrWhiteSpace(blueprint.DefinitionId))
                {
                    error = $"{sideLabel}第 {i + 1} 个出场位没有指定定义 ID。";
                    return false;
                }

                if (!used.Add(blueprint.Slot.Index))
                {
                    error = $"{sideLabel}阵型里 {blueprint.Slot} 被占用了两次。";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// 把数据定义装配成入场清单与运行期单位。
    /// </summary>
    /// <remarks>
    /// 落位口径由数据表定死：<c>encounters.csv</c> 的表头写明「enemyIds 顺序即布阵顺序，
    /// 前三个为前排」，因此这里直接按顺序落位，不去解析 <c>formation</c> 备注列。
    /// </remarks>
    public static class BattleFactory
    {
        /// <summary>
        /// 由遭遇定义 + 我方角色 ID 列表生成入场清单。
        /// </summary>
        /// <param name="encounter">遭遇定义。</param>
        /// <param name="partyCharacterIds">
        /// 我方角色的定义 ID，按<b>玩家决定的出场顺序</b>给。四人队伍时前三个进前排。
        /// </param>
        /// <param name="inventory">
        /// 这一战能吃到哪些道具。省略表示「这场没有道具可用」。
        /// 正式流程会传存档背包；诊断层传一个现搭的 <see cref="BattleInventory"/>。
        /// </param>
        /// <param name="loadout">
        /// 我方成员的在身装备与经文。省略表示「按裸装打」。
        /// 正式流程会传存档的 <c>EquipmentAssignment</c>；诊断层传一个现搭的 <see cref="BattleLoadout"/>。
        /// </param>
        public static BattleSetup FromEncounter(
            EncounterDefinition encounter,
            IReadOnlyList<string> partyCharacterIds,
            IBattleInventory inventory = null,
            IBattleLoadout loadout = null)
        {
            if (encounter == null)
            {
                throw new ArgumentNullException(nameof(encounter));
            }

            if (partyCharacterIds == null || partyCharacterIds.Count == 0)
            {
                throw new ArgumentException("我方阵容不能为空。", nameof(partyCharacterIds));
            }

            var party = new List<BattleUnitBlueprint>(partyCharacterIds.Count);
            for (var i = 0; i < partyCharacterIds.Count; i++)
            {
                party.Add(new BattleUnitBlueprint(partyCharacterIds[i], BattleFormation.SlotForIndex(i)));
            }

            var enemyIds = encounter.EnemyIds ?? Array.Empty<string>();
            if (enemyIds.Length > BattleSetup.MaxUnitsPerSide)
            {
                GameLog.Error(
                    LogChannel.Battle,
                    $"遭遇 {encounter.Id} 配了 {enemyIds.Length} 个敌人，超出 3×2 阵型容量 " +
                    $"{BattleSetup.MaxUnitsPerSide}，多余的单位会被丢弃。",
                    encounter.Id);
            }

            var count = Mathf.Min(enemyIds.Length, BattleSetup.MaxUnitsPerSide);
            var enemies = new List<BattleUnitBlueprint>(count);
            for (var i = 0; i < count; i++)
            {
                enemies.Add(new BattleUnitBlueprint(enemyIds[i], BattleFormation.SlotForIndex(i)));
            }

            return new BattleSetup(
                encounter.Id,
                party,
                enemies,
                encounter.IsBoss,
                encounter.BackgroundKey,
                encounter.BgmKey,
                inventory,
                loadout,
                encounter.BossPhaseIds);
        }

        /// <summary>
        /// 骨架期的占位队伍：取所有可操作角色，按 ID 排序。
        /// </summary>
        /// <remarks>
        /// 排序是为了可复现——正式队伍编成属于成长模块（Progression）的职责，
        /// 骨架期只需一支固定的四人队，让测试和原型有稳定的阵容可用。
        /// </remarks>
        public static IReadOnlyList<string> DefaultParty(IDefinitionRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var ids = new List<string>(4);
            foreach (var character in registry.OfKind<CharacterDefinition>())
            {
                if (character != null && character.IsPlayable && !string.IsNullOrEmpty(character.Id))
                {
                    ids.Add(character.Id);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>
        /// 按定义造一个运行期单位。定义缺失或类型对不上时返回 null 并记错误日志。
        /// </summary>
        /// <param name="loadout">
        /// 我方成员的在身装备与经文，省略表示裸装。只对角色生效——敌人不穿装备，
        /// 因此敌方阵容传不传它都一样。在身清单不只带数值：它带的技能并进单位技能清单（ADR-019），
        /// 带的被动在这一刻施加成常驻状态（ADR-020）。
        /// </param>
        public static BattleUnit CreateUnit(
            IDefinitionRegistry registry,
            BattleSide side,
            int runtimeId,
            int formationIndex,
            in BattleUnitBlueprint blueprint,
            IBattleLoadout loadout = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (!registry.TryGet(blueprint.DefinitionId, out var definition) || definition == null)
            {
                GameLog.Error(
                    LogChannel.Battle,
                    $"找不到定义 '{blueprint.DefinitionId}'，该出场位会被跳过。",
                    blueprint.DefinitionId);
                return null;
            }

            switch (definition)
            {
                case CharacterDefinition character:

                    // 装备与经文的加成、连同它们带来的技能与被动，都在建单位这一刻并进单位（进场快照），
                    // 之后战斗只看单位自身的数、技能清单与身上的状态。
                    var stats = ResolveCharacterStats(registry, character, loadout);
                    var unit = new BattleUnit(
                        runtimeId,
                        side,
                        formationIndex,
                        blueprint.Slot,
                        character.Id,
                        character.DisplayNameKey,
                        character.Kind,
                        stats.MaxHealth,
                        stats.MaxSpirit,
                        stats.Attack,
                        stats.Defense,
                        stats.Speed,
                        character.Element,
                        stats.BreakThreshold,
                        // 清单里已经含「自带 + 在身带来的」，战斗侧不再自己拼一份（口径见 ADR-019）。
                        stats.SkillIds,
                        isBoss: false,
                        stats.SpiritRegenPerTurn,
                        stats.HealthCostPerTurn);

                    // 归位在挂被动<b>之前</b>：归位会清空状态清单，反过来写就等于把刚挂上的被动抹掉。
                    // 于是「建单位那一刻」是全套的——数值、技能清单、开战归位、再接被动，一次做完。
                    unit.PrepareForBattle();

                    // 被动只在这一刻施加一次：此后它就是身上的一条普通状态，与别的状态同权（见 ADR-020）。
                    ApplyPassives(registry, unit, stats.PassiveIds, character.Id);
                    return unit;

                case EnemyDefinition enemy:
                    var enemyUnit = new BattleUnit(
                        runtimeId,
                        side,
                        formationIndex,
                        blueprint.Slot,
                        enemy.Id,
                        enemy.DisplayNameKey,
                        enemy.Kind,
                        enemy.MaxHealth,
                        maxSpirit: 0,
                        enemy.Attack,
                        enemy.Defense,
                        enemy.Speed,
                        enemy.Element,
                        enemy.BreakThreshold,
                        enemy.SkillIds,
                        enemy.IsBoss,
                        // 敌人不穿经文：每回合的回灵与苦修都只有我方成员可能非零。
                        spiritRegenPerTurn: 0,
                        healthCostPerTurn: 0);

                    // 敌人没有被动可挂，归位之后就是最终形态。
                    enemyUnit.PrepareForBattle();
                    return enemyUnit;

                default:
                    GameLog.Error(
                        LogChannel.Battle,
                        $"定义 '{blueprint.DefinitionId}' 是 {definition.Kind}，战斗只接受角色或敌人。",
                        blueprint.DefinitionId);
                    return null;
            }
        }

        /// <summary>
        /// 按在身清单算这名成员的有效数值；没穿东西时直接返回基础值，连报告都不建。
        /// </summary>
        /// <remarks>
        /// 聚合结论在开战这一刻就定型，所以每一条都记进日志：装备配错、限定角色不符、定义缺失
        /// 都是<b>数据问题</b>，应该在开战时暴露出来，而不是让玩家看到一个少了几点攻击的隐身数值。
        /// 单件坏数据只跳过那一件，战斗照样打得下去——不让一处配置错误变成打不开的战斗。
        /// </remarks>
        private static CharacterStatsSnapshot ResolveCharacterStats(
            IDefinitionRegistry registry,
            CharacterDefinition character,
            IBattleLoadout loadout)
        {
            var entries = loadout?.LoadoutOf(character.Id);
            if (entries == null || entries.Count == 0)
            {
                return CharacterStatsResolver.BaseOf(character);
            }

            var report = new ValidationReport();
            var stats = CharacterStatsResolver.Resolve(character, entries, registry, report);
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var issue = report.Issues[i];
                var message = $"成员 {character.Id} 的在身装备：{issue.Message}";
                if (issue.Severity == ValidationSeverity.Error)
                {
                    GameLog.Error(LogChannel.Battle, message, character.Id);
                }
                else
                {
                    GameLog.Warn(LogChannel.Battle, message, character.Id);
                }
            }

            return stats;
        }

        /// <summary>
        /// 把进场画像里那几条被动逐条施加成<b>常驻状态</b>。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 被动的身份在 <c>passives.csv</c>，效果数值在它指向的那条状态上（口径见 ADR-020）：
        /// 因此这里要做两跳查表——被动 → 状态。施加之后就结束：这条状态与场上任何一条状态完全同权，
        /// 参与修正聚合、参与每回合结算、写进单位的状态清单，也照旧进得了将来任何按清单挑选的移除机制；
        /// 唯一的区别是它常驻不到期。
        /// </para>
        /// <para>
        /// 只作用携带者本人——不广播给队友，也不碰敌人。坏引用（被动查不到、承载状态查不到）
        /// 只跳过那一条并记错误日志，不让一处数据错误变成打不开的战斗。
        /// </para>
        /// </remarks>
        private static void ApplyPassives(
            IDefinitionRegistry registry,
            BattleUnit unit,
            string[] passiveIds,
            string characterId)
        {
            if (unit == null || passiveIds == null || passiveIds.Length == 0)
            {
                return;
            }

            for (var i = 0; i < passiveIds.Length; i++)
            {
                var passiveId = passiveIds[i];
                if (string.IsNullOrWhiteSpace(passiveId))
                {
                    continue;
                }

                if (!registry.TryGet(passiveId, out var mounted) || mounted == null)
                {
                    GameLog.Error(
                        LogChannel.Battle,
                        $"成员 {characterId} 的被动 '{passiveId}' 在数据表里找不到，已跳过。",
                        passiveId);
                    continue;
                }

                if (!(mounted is PassiveDefinition passive))
                {
                    GameLog.Error(
                        LogChannel.Battle,
                        $"成员 {characterId} 的 '{passiveId}' 是 {mounted.Kind}，被动栏只接受被动，已跳过。",
                        passiveId);
                    continue;
                }

                if (!registry.TryGet(passive.StatusId, out var carried) || !(carried is StatusDefinition status))
                {
                    GameLog.Error(
                        LogChannel.Battle,
                        $"被动 '{passiveId}' 指向的状态 '{passive.StatusId}' 在数据表里找不到或不是状态，已跳过。",
                        passiveId);
                    continue;
                }

                unit.ApplyStatus(status);
            }
        }
    }
}
