using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 战斗内核测试的建场器。
    /// </summary>
    /// <remarks>
    /// 战斗内核的默认数值（<see cref="BattleConfig"/>）与数据定义（角色／敌人／技能／状态）
    /// 都是<b>资产</b>，测试里不可能靠读 CSV 拿到「刚好能验证某条规则」的数值。
    /// 所以这里用反射直接把字段写成测试需要的值：反射只出现在测试程序集里，
    /// 运行时代码一个反射调用都没有。
    ///
    /// 用法：<c>using var lab = new BattleLab();</c>——退出作用域时自动销毁所有临时资产，
    /// 避免 EditMode 下 ScriptableObject 泄漏到后续用例。
    /// </remarks>
    internal sealed class BattleLab : IDisposable
    {
        /// <summary>测试统一使用的母种子。换种子会让「可复现」类断言失去意义。</summary>
        internal const ulong DefaultSeed = 20240924UL;

        /// <summary>本地化键前缀。数据校验会拒绝中文，测试键同样只用 ASCII。</summary>
        private const string KeyPrefix = "test.battle.";

        private readonly List<Object> _created = new List<Object>();

        private readonly List<DefinitionBase> _definitions = new List<DefinitionBase>();

        internal BattleLab()
        {
            Config = BattleConfig.CreateDefault();
            _created.Add(Config);
        }

        /// <summary>本场测试使用的战斗数值配置；默认取 <see cref="BattleConfig.CreateDefault"/> 并在建场器内登记销毁。</summary>
        internal BattleConfig Config { get; }

        /// <summary>已登记的定义，供 <see cref="Registry"/> 装配。</summary>
        internal IReadOnlyList<DefinitionBase> Definitions => _definitions;

        /// <summary>造一个攻击技能。默认单体、物理、无冷却，数值刻意取「能一眼算出来」的整数。</summary>
        internal SkillDefinition AttackSkill(
            string id,
            int power = 20,
            int breakDamage = 10,
            int hitCount = 1,
            TargetRule target = TargetRule.SingleEnemy,
            FiveElement element = FiveElement.None,
            int cooldownTurns = 0,
            int spiritCost = 0)
        {
            var skill = New<SkillDefinition>(id, "atk");
            Set(skill, "_power", power);
            Set(skill, "_breakDamage", breakDamage);
            Set(skill, "_hitCount", hitCount);
            Set(skill, "_target", target);
            Set(skill, "_element", element);
            Set(skill, "_nature", DamageNature.Physical);
            Set(skill, "_cooldownTurns", cooldownTurns);
            Set(skill, "_spiritCost", spiritCost);
            Set(skill, "_healPower", 0);
            Set(skill, "_statusChance", 0f);
            return skill;
        }

        /// <summary>造一个纯辅助技能。<c>power = 0</c> 是「不进伤害公式」的判据，治疗量只看 <paramref name="healPower"/>。</summary>
        internal SkillDefinition HealingSkill(
            string id,
            int healPower,
            TargetRule target = TargetRule.SingleAlly,
            int cooldownTurns = 0)
        {
            var skill = New<SkillDefinition>(id, "heal");
            Set(skill, "_power", 0);
            Set(skill, "_breakDamage", 0);
            Set(skill, "_hitCount", 1);
            Set(skill, "_target", target);
            Set(skill, "_cooldownTurns", cooldownTurns);
            Set(skill, "_healPower", healPower);
            Set(skill, "_statusChance", 0f);
            return skill;
        }

        /// <summary>造一个只负责挂状态的技能。<c>power = 0</c>，命中率默认 100%（<c>Chance(1f)</c> 不消耗随机数）。</summary>
        internal SkillDefinition StatusSkill(
            string id,
            string appliedStatusId,
            float statusChance = 1f,
            TargetRule target = TargetRule.SingleEnemy,
            int breakDamage = 0)
        {
            var skill = New<SkillDefinition>(id, "status");
            Set(skill, "_power", 0);
            Set(skill, "_breakDamage", breakDamage);
            Set(skill, "_hitCount", 1);
            Set(skill, "_target", target);
            Set(skill, "_healPower", 0);
            Set(skill, "_appliedStatusId", appliedStatusId);
            Set(skill, "_statusChance", statusChance);
            return skill;
        }

        /// <summary>
        /// 造一个状态定义。
        /// </summary>
        /// <remarks>
        /// 两组修正量的中性值不一样，默认值必须分别给对，否则会造出「什么都不写就 +100% 攻防」的假状态：
        /// <list type="bullet">
        /// <item><description>攻／防／速是<b>加算</b>进 <c>1 + modifier</c> 的，中性值 <b>0</b>；</description></item>
        /// <item><description><c>incomingDamageModifier</c> 与 <c>breakDamageModifier</c> 是<b>乘区</b>，中性值 <b>1</b>；
        /// 资产默认值是 0，只有 CSV 会写成 1，测试里漏给就会造成「承伤 0 倍」。</description></item>
        /// </list>
        /// </remarks>
        internal StatusDefinition Status(
            string id,
            int durationTurns = 2,
            StackRule stackRule = StackRule.Refresh,
            int maxStacks = 1,
            bool preventsAction = false,
            bool isDebuff = true,
            int healthDeltaPerTurn = 0,
            float attackModifier = 0f,
            float defenseModifier = 0f,
            float speedModifier = 0f,
            float incomingDamageModifier = 1f,
            float breakDamageModifier = 1f,
            bool permanent = false)
        {
            var status = New<StatusDefinition>(id, "sts");
            Set(status, "_durationTurns", durationTurns);
            Set(status, "_stackRule", stackRule);
            Set(status, "_maxStacks", maxStacks);
            Set(status, "_preventsAction", preventsAction);
            Set(status, "_isDebuff", isDebuff);
            Set(status, "_healthDeltaPerTurn", healthDeltaPerTurn);
            Set(status, "_attackModifier", attackModifier);
            Set(status, "_defenseModifier", defenseModifier);
            Set(status, "_speedModifier", speedModifier);
            Set(status, "_incomingDamageModifier", incomingDamageModifier);
            Set(status, "_breakDamageModifier", breakDamageModifier);
            Set(status, "_permanent", permanent);
            return status;
        }

        /// <summary>
        /// 造一条被动。
        /// </summary>
        /// <remarks>
        /// 被动只登记身份与来源，效果落在 <paramref name="statusId"/> 指向的状态上（见 ADR-020）。
        /// 因此造被动之前得先把那条状态造出来并登记进同一个建场器。
        /// </remarks>
        internal PassiveDefinition Passive(string id, string statusId)
        {
            var passive = New<PassiveDefinition>(id, "psv");
            Set(passive, "_statusId", statusId);
            return passive;
        }

        internal CharacterDefinition Character(
            string id,
            int maxHealth,
            int attack,
            int defense,
            int speed,
            FiveElement element = FiveElement.None,
            int breakThreshold = 30,
            int maxSpirit = 50,
            params string[] skillIds)
        {
            var character = New<CharacterDefinition>(id, "chr");
            Set(character, "_maxHealth", maxHealth);
            Set(character, "_maxSpirit", maxSpirit);
            Set(character, "_attack", attack);
            Set(character, "_defense", defense);
            Set(character, "_speed", speed);
            Set(character, "_element", element);
            Set(character, "_breakThreshold", breakThreshold);
            Set(character, "_startingSkillIds", skillIds ?? Array.Empty<string>());
            Set(character, "_isPlayable", true);
            return character;
        }

        internal EnemyDefinition Enemy(
            string id,
            int maxHealth,
            int attack,
            int defense,
            int speed,
            FiveElement element = FiveElement.None,
            int breakThreshold = 20,
            bool isBoss = false,
            params string[] skillIds)
        {
            var enemy = New<EnemyDefinition>(id, "enm");
            Set(enemy, "_maxHealth", maxHealth);
            Set(enemy, "_attack", attack);
            Set(enemy, "_defense", defense);
            Set(enemy, "_speed", speed);
            Set(enemy, "_element", element);
            Set(enemy, "_breakThreshold", breakThreshold);
            Set(enemy, "_weakToElement", FiveElement.None);
            Set(enemy, "_skillIds", skillIds ?? Array.Empty<string>());
            Set(enemy, "_isBoss", isBoss);
            return enemy;
        }

        /// <summary>
        /// 造一件战斗内可用的消耗品。
        /// </summary>
        /// <remarks>
        /// <c>stackLimit</c> 默认 99：测试关心的是「用掉之后少一个」，不是堆叠规则。
        /// <c>usableInField</c> 默认 <c>false</c>——本版的口径就是「道具只在战斗内用」。
        /// </remarks>
        internal ItemDefinition Item(
            string id,
            string effectKey,
            int effectMagnitude = 0,
            bool usableInBattle = true,
            bool isConsumedOnUse = true,
            string displayNameKey = null)
        {
            var item = New<ItemDefinition>(id, "itm");
            if (!string.IsNullOrEmpty(displayNameKey))
            {
                Set(item, "_displayNameKey", displayNameKey);
            }

            Set(item, "_category", ItemCategory.Consumable);
            Set(item, "_tier", RarityTier.Common);
            Set(item, "_stackLimit", 99);
            Set(item, "_price", 0);
            Set(item, "_usableInBattle", usableInBattle);
            Set(item, "_usableInField", false);
            Set(item, "_isConsumedOnUse", isConsumedOnUse);
            Set(item, "_effectKey", effectKey);
            Set(item, "_effectMagnitude", effectMagnitude);
            Set(item, "_effectDurationTurns", 0);
            Set(item, "_effectSkillId", null);
            Set(item, "_spriteKey", null);
            return item;
        }

        /// <summary>
        /// 造一件装备。
        /// </summary>
        /// <remarks>
        /// <c>requiredLevel</c> 默认给 1 而不是数据表里常见的 5：角色目前没有等级来源，
        /// 等级校验无处可施（见 <c>CharacterStatsResolver</c> 的口径说明）。想钉住「等级暂不参与校验」的用例，
        /// 显式传一个大数，让它在等级系统落地那天立刻变红。
        /// </remarks>
        internal EquipmentDefinition Equipment(
            string id,
            EquipmentSlot slot = EquipmentSlot.Weapon,
            int attackBonus = 0,
            int defenseBonus = 0,
            int speedBonus = 0,
            int healthBonus = 0,
            int spiritBonus = 0,
            int breakDamageBonus = 0,
            int requiredLevel = 1,
            string passiveSkillId = null,
            string passiveId = null,
            params string[] allowedCharacterIds)
        {
            var equipment = New<EquipmentDefinition>(id, "eqp");
            Set(equipment, "_slot", slot);
            Set(equipment, "_tier", RarityTier.Common);
            Set(equipment, "_attackBonus", attackBonus);
            Set(equipment, "_defenseBonus", defenseBonus);
            Set(equipment, "_speedBonus", speedBonus);
            Set(equipment, "_healthBonus", healthBonus);
            Set(equipment, "_spiritBonus", spiritBonus);
            Set(equipment, "_element", FiveElement.None);
            Set(equipment, "_resistElement", FiveElement.None);
            Set(equipment, "_breakDamageBonus", breakDamageBonus);
            Set(equipment, "_passiveSkillId", passiveSkillId);
            Set(equipment, "_passiveId", passiveId);
            Set(equipment, "_requiredLevel", requiredLevel);
            Set(equipment, "_price", 0);
            Set(equipment, "_forgeRecipeId", null);
            Set(equipment, "_spriteKey", null);
            Set(equipment, "_allowedCharacterIds", allowedCharacterIds ?? Array.Empty<string>());
            return equipment;
        }

        /// <summary>造一本经文。占用「经文」栏位，数值同样按加算进有效属性。</summary>
        internal SutraDefinition Sutra(
            string id,
            int attackBonus = 0,
            int defenseBonus = 0,
            int healthBonus = 0,
            int spiritBonus = 0,
            int breakThresholdBonus = 0,
            int spiritRegenPerTurn = 0,
            int healthCostPerTurn = 0,
            string passiveSkillId = null,
            string passiveId = null)
        {
            var sutra = New<SutraDefinition>(id, "sut");
            Set(sutra, "_tier", RarityTier.Common);
            Set(sutra, "_mantraTypeKey", KeyPrefix + "sut.mind");
            Set(sutra, "_attackBonus", attackBonus);
            Set(sutra, "_defenseBonus", defenseBonus);
            Set(sutra, "_healthBonus", healthBonus);
            Set(sutra, "_spiritBonus", spiritBonus);
            Set(sutra, "_breakThresholdBonus", breakThresholdBonus);
            Set(sutra, "_spiritRegenPerTurn", spiritRegenPerTurn);
            Set(sutra, "_element", FiveElement.None);
            Set(sutra, "_passiveSkillId", passiveSkillId);
            Set(sutra, "_passiveId", passiveId);
            Set(sutra, "_requiredLevel", 1);
            Set(sutra, "_price", 0);
            Set(sutra, "_healthCostPerTurn", healthCostPerTurn);
            Set(sutra, "_spriteKey", null);
            return sutra;
        }

        /// <summary>把已登记的定义装进数据目录，返回运行期查询入口。</summary>
        internal IDefinitionRegistry Registry()
        {
            // ScriptableObject.CreateInstance 而不是 Object.CreateInstance：
            // UnityEngine.Object 上根本没有这个静态方法，写错只会在测试程序集里编译失败。
            var catalog = ScriptableObject.CreateInstance<DefinitionCatalog>();
            _created.Add(catalog);
            catalog.SetDefinitions(_definitions);
            catalog.Rebuild();
            return new DefinitionRegistry(catalog);
        }

        /// <summary>造一条独立的战斗随机流。两个流同名同种子必然给出同一序列。</summary>
        internal static IRandomStream Stream(ulong seed = DefaultSeed) =>
            new PcgRandomStream(RandomStreams.Battle, seed);

        /// <summary>造一个事件总线。需要断言事件的用例自己订阅，不需要的可以不传。</summary>
        internal static IEventBus Bus() => new EventBus();

        /// <summary>反射写入私有序列化字段；字段名写错时立即失败，而不是静默造出一个「默认值定义」。</summary>
        internal static void SetPrivate(object target, string fieldName, object value)
        {
            Assert.IsNotNull(target, "反射写入的目标不能为空。");

            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"{target.GetType().Name} 上找不到字段 '{fieldName}'，测试建场器与数据定义已经脱节。");

            if (value != null && !field.FieldType.IsInstanceOfType(value))
            {
                Assert.Fail(
                    $"字段 '{fieldName}' 期望 {field.FieldType.Name}，实际传入 {value.GetType().Name}。");
            }

            field.SetValue(target, value);
        }

        internal static T GetPrivate<T>(object target, string fieldName)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"{target.GetType().Name} 上找不到字段 '{fieldName}'。");
            return (T)field.GetValue(target);
        }

        public void Dispose()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            _definitions.Clear();
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            var current = type;
            while (current != null)
            {
                var field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                current = current.BaseType;
            }

            return null;
        }

        private T New<T>(string id, string keySuffix) where T : DefinitionBase
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = id;
            _created.Add(definition);
            _definitions.Add(definition);

            SetPrivate(definition, "_id", id);
            SetPrivate(definition, "_displayNameKey", KeyPrefix + keySuffix + ".name");
            SetPrivate(definition, "_descriptionKey", KeyPrefix + keySuffix + ".desc");
            SetPrivate(definition, "_tags", Array.Empty<string>());
            SetPrivate(definition, "_version", 1);
            return definition;
        }

        private static void Set(object target, string fieldName, object value) => SetPrivate(target, fieldName, value);
    }
}
