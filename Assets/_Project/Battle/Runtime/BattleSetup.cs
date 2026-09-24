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
            string bgmKey = null)
        {
            EncounterId = encounterId;
            Party = party ?? Array.Empty<BattleUnitBlueprint>();
            Enemies = enemies ?? Array.Empty<BattleUnitBlueprint>();
            IsBoss = isBoss;
            BackgroundKey = backgroundKey;
            BgmKey = bgmKey;
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
        public static BattleSetup FromEncounter(
            EncounterDefinition encounter,
            IReadOnlyList<string> partyCharacterIds)
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
                encounter.BgmKey);
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
        public static BattleUnit CreateUnit(
            IDefinitionRegistry registry,
            BattleSide side,
            int runtimeId,
            int formationIndex,
            in BattleUnitBlueprint blueprint)
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
                    return new BattleUnit(
                        runtimeId,
                        side,
                        formationIndex,
                        blueprint.Slot,
                        character.Id,
                        character.DisplayNameKey,
                        character.Kind,
                        character.MaxHealth,
                        character.MaxSpirit,
                        character.Attack,
                        character.Defense,
                        character.Speed,
                        character.Element,
                        character.BreakThreshold,
                        character.StartingSkillIds,
                        isBoss: false);

                case EnemyDefinition enemy:
                    return new BattleUnit(
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
                        enemy.IsBoss);

                default:
                    GameLog.Error(
                        LogChannel.Battle,
                        $"定义 '{blueprint.DefinitionId}' 是 {definition.Kind}，战斗只接受角色或敌人。",
                        blueprint.DefinitionId);
                    return null;
            }
        }
    }
}
