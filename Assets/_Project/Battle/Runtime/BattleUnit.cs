using System;
using System.Collections.Generic;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 一个战斗单位的运行期状态：生命、灵力、护体值、状态、冷却、行动值与站位。
    /// </summary>
    /// <remarks>
    /// 纪律：这个类<b>不含任何决策</b>。它只说「能不能」和「做了之后变成什么」，
    /// 不说「该做什么」——目标选择与技能挑选都在 <c>BattleSession</c> 与 <c>EnemyIntentPlanner</c> 里。
    /// 这样伤害与状态的规则可以被测试穷举，而 AI 的改动不会顺带改掉数值口径。
    ///
    /// 单位本身不持有 <see cref="ScriptableObject"/> 之外的 Unity 对象，也不读时间、不掷随机，
    /// 因此整场战斗可以在 EditMode 里跑完。
    /// </remarks>
    public sealed class BattleUnit
    {
        private readonly List<BattleStatusInstance> _statuses = new List<BattleStatusInstance>(4);
        private readonly Dictionary<string, int> _cooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _cooledThisTurn = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _cooldownScratch = new List<string>(4);
        private readonly string[] _skillIds;

        internal BattleUnit(
            int runtimeId,
            BattleSide side,
            int formationIndex,
            FormationSlot slot,
            string definitionId,
            string displayNameKey,
            DefinitionKind kind,
            int maxHealth,
            int maxSpirit,
            int attack,
            int defense,
            int speed,
            FiveElement element,
            int breakThreshold,
            string[] skillIds,
            bool isBoss,
            int spiritRegenPerTurn,
            int healthCostPerTurn)
        {
            RuntimeId = runtimeId;
            Side = side;
            FormationIndex = formationIndex;
            Slot = slot;
            DefinitionId = definitionId;
            DisplayNameKey = displayNameKey;
            Kind = kind;
            MaxHealth = Mathf.Max(1, maxHealth);
            MaxSpirit = Mathf.Max(0, maxSpirit);
            BaseAttack = Mathf.Max(0, attack);
            BaseDefense = Mathf.Max(0, defense);
            BaseSpeed = Mathf.Max(1, speed);
            Element = element;
            BreakThreshold = Mathf.Max(1, breakThreshold);
            _skillIds = skillIds ?? Array.Empty<string>();
            IsBoss = isBoss;

            // 每回合效果不是上限类的数，只需非负：负的回灵等于每次白掉几口血，没有这样的设计。
            SpiritRegenPerTurn = Mathf.Max(0, spiritRegenPerTurn);
            HealthCostPerTurn = Mathf.Max(0, healthCostPerTurn);

            Health = MaxHealth;
            Spirit = MaxSpirit;
            BreakValue = BreakThreshold;
        }

        /// <summary>局内编号，从 0 起。它是事件、日志与界面定位单位的唯一钥匙。</summary>
        public int RuntimeId { get; }

        public BattleSide Side { get; }

        /// <summary>
        /// 建队顺序（我方在前、敌方在后），同时用作行动值相同时的平局判据。
        /// 口径来自 Docs/战斗数值-v1.md：「同速按布阵顺序」。
        /// </summary>
        public int FormationIndex { get; internal set; }

        /// <summary>当前站位。换位会改写它，因此技能的 Column / Row 命中范围随之变化。</summary>
        public FormationSlot Slot { get; internal set; }

        public string DefinitionId { get; }

        /// <summary>单位名称的文本键。内核不持有中文文案。</summary>
        public string DisplayNameKey { get; }

        public DefinitionKind Kind { get; }

        public bool IsBoss { get; }

        /// <summary>
        /// 这个单位会哪几手：角色自带技能 + 在身装备与经文带来的（见 ADR-019）。
        /// </summary>
        /// <remarks>
        /// 顺序<b>就是优先级</b>：自带技能在前、挂载的在后，规划器的平局判据与界面的按钮顺序都读它。
        /// 挂载来的技能与自带技能在运行期<b>没有区别</b>——同一份清单、同一个选招入口、同一套消耗与冷却。
        /// </remarks>
        public IReadOnlyList<string> SkillIds => _skillIds;

        public int MaxHealth { get; }

        public int Health { get; private set; }

        public bool IsAlive => Health > 0;

        public int MaxSpirit { get; }

        public int Spirit { get; private set; }

        public int BaseAttack { get; }

        public int BaseDefense { get; }

        public int BaseSpeed { get; }

        public FiveElement Element { get; }

        /// <summary>护体值上限。</summary>
        public int BreakThreshold { get; }

        /// <summary>
        /// 每个「自己的回合」结束时回复的灵力（来自经文）。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="AttackModifier"/> 这类状态修正不同，它是<b>进场快照</b>的一部分：
        /// 换装不改进行中的战斗（见 <c>CharacterStatsResolver</c>）。结算时机在战斗侧，
        /// 与状态持续伤害、破防计时同挂「自己的回合收尾」那一处。
        /// </remarks>
        public int SpiritRegenPerTurn { get; }

        /// <summary>每个「自己的回合」结束时流失的生命（苦修）。0 表示这本经文不要代价。</summary>
        public int HealthCostPerTurn { get; }

        /// <summary>当前护体值。归零即进入破防。</summary>
        public int BreakValue { get; private set; }

        public bool IsBroken { get; private set; }

        /// <summary>破防还剩几个「自己的回合」。归零时护体值重置为上限。</summary>
        public int BrokenTurnsRemaining { get; private set; }

        /// <summary>行动值，值越小越先行动。由 <c>ActionQueue</c> 维护。</summary>
        public int ActionValue { get; internal set; }

        /// <summary>该单位已经完成过多少次自己的回合。回合数的定义依赖它。</summary>
        public int TurnsTaken { get; internal set; }

        public IReadOnlyList<BattleStatusInstance> Statuses => _statuses;

        /// <summary>
        /// 敌方单位不走灵力消耗：<c>EnemyDefinition</c> 没有灵力字段，
        /// 强行给敌人记灵力等于凭空造一条数据表里没有的口径。
        /// </summary>
        public bool UsesSpirit => Side == BattleSide.Player;

        public float AttackModifier => SumModifier(attack: true, defense: false, speed: false);

        public float DefenseModifier => SumModifier(attack: false, defense: true, speed: false);

        public float SpeedModifier => SumModifier(attack: false, defense: false, speed: true);

        /// <summary>承伤倍率（乘区），中性值 1。</summary>
        public float IncomingDamageMultiplier
        {
            get
            {
                var product = 1f;
                for (var i = 0; i < _statuses.Count; i++)
                {
                    product *= _statuses[i].IncomingDamageModifier;
                }

                return product;
            }
        }

        /// <summary>护体削减倍率（乘区），中性值 1。</summary>
        public float BreakDamageMultiplier
        {
            get
            {
                var product = 1f;
                for (var i = 0; i < _statuses.Count; i++)
                {
                    product *= _statuses[i].BreakDamageModifier;
                }

                return product;
            }
        }

        public int EffectiveAttack => Mathf.Max(0, Mathf.RoundToInt(BaseAttack * (1f + AttackModifier)));

        public int EffectiveDefense => Mathf.Max(0, Mathf.RoundToInt(BaseDefense * (1f + DefenseModifier)));

        public int EffectiveSpeed => Mathf.Max(1, Mathf.RoundToInt(BaseSpeed * (1f + SpeedModifier)));

        /// <summary>是否被「剥夺行动」的状态控住（眩晕）。</summary>
        public bool IsActionPrevented
        {
            get
            {
                for (var i = 0; i < _statuses.Count; i++)
                {
                    if (_statuses[i].Definition.PreventsAction)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 生命比例，0–1，给界面血条用。
        /// </summary>
        /// <remarks>
        /// 规划器<b>不再</b>用它选目标：威胁评估看的是伤害期望，与目标还剩多少血无关
        /// （见 Docs/战斗内核-v1.md 第 4 节「不挑残血」一条）。
        /// </remarks>
        public float HealthRatio => MaxHealth <= 0 ? 0f : (float)Health / MaxHealth;

        public bool HasSkill(string skillId) =>
            !string.IsNullOrEmpty(skillId) && Array.IndexOf(_skillIds, skillId) >= 0;

        /// <summary>技能剩余冷却回合，0 表示可用。</summary>
        public int GetCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                return 0;
            }

            return _cooldowns.TryGetValue(skillId, out var turns) ? Mathf.Max(0, turns) : 0;
        }

        public bool IsSkillReady(string skillId) => GetCooldown(skillId) == 0;

        public BattleStatusInstance FindStatus(string statusId)
        {
            if (string.IsNullOrEmpty(statusId))
            {
                return null;
            }

            for (var i = 0; i < _statuses.Count; i++)
            {
                if (string.Equals(_statuses[i].StatusId, statusId, StringComparison.Ordinal))
                {
                    return _statuses[i];
                }
            }

            return null;
        }

        public override string ToString() =>
            $"#{RuntimeId} {DefinitionId} {Health}/{MaxHealth} 护体 {BreakValue}/{BreakThreshold} {Slot}";

        /// <summary>开战前的归位：满血满灵、护体满、无状态、无冷却。</summary>
        internal void PrepareForBattle()
        {
            Health = MaxHealth;
            Spirit = MaxSpirit;
            BreakValue = BreakThreshold;
            IsBroken = false;
            BrokenTurnsRemaining = 0;
            ActionValue = 0;
            TurnsTaken = 0;
            _statuses.Clear();
            _cooldowns.Clear();
            _cooledThisTurn.Clear();
        }

        /// <summary>扣血增血共用的入口，返回真正生效的量（会被上下限夹住）。</summary>
        internal int ApplyDamage(int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return 0;
            }

            var applied = Mathf.Min(Health, amount);
            Health -= applied;
            return applied;
        }

        /// <summary>治疗，返回真正生效的量。已经倒下的单位不会被治疗拉起来（复活另有其道）。</summary>
        internal int Heal(int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return 0;
            }

            var applied = Mathf.Min(MaxHealth - Health, amount);
            Health += applied;
            return applied;
        }

        internal bool TryPaySpirit(int cost)
        {
            if (cost <= 0 || !UsesSpirit)
            {
                return true;
            }

            if (Spirit < cost)
            {
                return false;
            }

            Spirit -= cost;
            return true;
        }

        /// <summary>
        /// 回灵，返回真正生效的量（会被灵力上限夹住）。与 <see cref="Heal"/> 对称：
        /// 已经倒下的单位不回灵，敌人不走灵力因此恒为 0（<see cref="UsesSpirit"/>）。
        /// </summary>
        internal int RestoreSpirit(int amount)
        {
            if (amount <= 0 || !UsesSpirit || !IsAlive)
            {
                return 0;
            }

            var applied = Mathf.Min(MaxSpirit - Spirit, amount);
            Spirit += applied;
            return applied;
        }

        /// <summary>
        /// 解除身上的全部负面状态，把被摘掉的那些写进 <paramref name="removedSink"/>，
        /// 返回解除的个数。增益一律保留——「祛毒」不该顺手把守势也洗掉。
        /// </summary>
        /// <remarks>
        /// 从后往前遍历并当场移除，与 <see cref="TickStatuses"/> 的写法一致；
        /// 倒序是为了让「移除即重排」不会漏掉元素。
        /// </remarks>
        internal int RemoveDebuffs(List<BattleStatusInstance> removedSink)
        {
            var removed = 0;
            for (var i = _statuses.Count - 1; i >= 0; i--)
            {
                var status = _statuses[i];
                if (!status.IsDebuff)
                {
                    continue;
                }

                _statuses.RemoveAt(i);
                removedSink?.Add(status);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// 记冷却。当回合结束时<b>不</b>递减这一次记下的冷却，
        /// 于是 <c>cooldownTurns = 3</c> 恰好等于「用完之后还要等 3 个自己的回合」。
        /// </summary>
        internal void StartCooldown(string skillId, int turns)
        {
            if (string.IsNullOrEmpty(skillId) || turns <= 0)
            {
                return;
            }

            _cooldowns[skillId] = turns;
            _cooledThisTurn.Add(skillId);
        }

        /// <summary>自己的回合结束时递减冷却。</summary>
        /// <remarks>
        /// 先把键抄一份再改值：<see cref="Dictionary{TKey,TValue}"/> 的索引器赋值（即使是赋给已存在的键）
        /// 会推进它的内部版本号，边遍历边递减会当场抛 <c>Collection was modified</c>。
        /// 这个坑只有「冷却跨过一手」才会踩到，所以用例里必须让同一场战斗走过两个冷却回合。
        /// </remarks>
        internal void TickCooldowns()
        {
            if (_cooldowns.Count > 0)
            {
                _cooldownScratch.Clear();
                foreach (var pair in _cooldowns)
                {
                    _cooldownScratch.Add(pair.Key);
                }

                for (var i = 0; i < _cooldownScratch.Count; i++)
                {
                    var skillId = _cooldownScratch[i];
                    if (_cooledThisTurn.Contains(skillId))
                    {
                        continue;
                    }

                    var remaining = _cooldowns[skillId];
                    if (remaining <= 1)
                    {
                        _cooldowns.Remove(skillId);
                    }
                    else
                    {
                        _cooldowns[skillId] = remaining - 1;
                    }
                }
            }

            _cooledThisTurn.Clear();
        }

        /// <summary>
        /// 施加状态，返回这次到底发生了什么（新挂 / 叠层 / 刷新 / 顶替）。
        /// </summary>
        internal StatusChangeKind ApplyStatus(StatusDefinition definition, int stacks = 1)
        {
            if (definition == null)
            {
                return StatusChangeKind.Refreshed;
            }

            var duration = Mathf.Max(1, definition.DurationTurns);
            var maxStacks = Mathf.Max(1, definition.MaxStacks);
            var existing = FindStatus(definition.Id);

            if (existing == null)
            {
                _statuses.Add(new BattleStatusInstance(definition, Mathf.Min(stacks, maxStacks), duration));
                return StatusChangeKind.Applied;
            }

            switch (definition.StackRule)
            {
                case StackRule.Stackable:
                    existing.RemainingTurns = Mathf.Max(existing.RemainingTurns, duration);
                    if (existing.Stacks < maxStacks)
                    {
                        existing.Stacks++;
                        return StatusChangeKind.Stacked;
                    }

                    return StatusChangeKind.Refreshed;

                case StackRule.StrongestOnly:
                    // 只保留最强的一次：更强就顶掉，否则只刷新时长。
                    if (BattleStatusInstance.MagnitudeOf(definition) > existing.Magnitude)
                    {
                        _statuses.Remove(existing);
                        _statuses.Add(new BattleStatusInstance(definition, 1, duration));
                        return StatusChangeKind.Replaced;
                    }

                    existing.RemainingTurns = Mathf.Max(existing.RemainingTurns, duration);
                    return StatusChangeKind.Refreshed;

                default:
                    existing.RemainingTurns = Mathf.Max(existing.RemainingTurns, duration);
                    existing.Stacks = 1;
                    return StatusChangeKind.Refreshed;
            }
        }

        /// <summary>
        /// 自己的回合结束时的状态结算，返回本回合因状态产生的净生命变化（负数即掉血）。
        /// 生命增减<b>不</b>走伤害公式：灼烧是「持续掉血」，不该再被防御力吃一遍。
        /// 先结算再递减时长，于是「1 回合的眩晕」还能正好控住一个完整回合。
        /// </summary>
        internal int TickStatuses(List<BattleStatusInstance> expiredSink)
        {
            var delta = 0;
            for (var i = 0; i < _statuses.Count; i++)
            {
                delta += _statuses[i].HealthDeltaPerTurn;
            }

            if (delta < 0)
            {
                ApplyDamage(-delta);
            }
            else if (delta > 0)
            {
                Heal(delta);
            }

            for (var i = _statuses.Count - 1; i >= 0; i--)
            {
                var status = _statuses[i];
                status.RemainingTurns--;
                if (status.RemainingTurns <= 0)
                {
                    _statuses.RemoveAt(i);
                    expiredSink?.Add(status);
                }
            }

            return delta;
        }

        /// <summary>
        /// 削减护体值。返回「这一次是否刚好打空」。
        /// </summary>
        /// <remarks>
        /// 已经破防的单位不再累计削减：它的护体值此刻是 0，没有可削的东西，
        /// 护体值要等破防结束才重置（重置时机见 <see cref="TickBrokenTurn"/>）。
        /// </remarks>
        internal bool ApplyBreakDamage(int amount)
        {
            if (amount <= 0 || IsBroken || !IsAlive)
            {
                return false;
            }

            BreakValue = Mathf.Max(0, BreakValue - amount);
            return BreakValue == 0;
        }

        /// <summary>进入破防。持续时间取配置里的回合数。</summary>
        internal void EnterBroken(int durationTurns)
        {
            IsBroken = true;
            BrokenTurnsRemaining = Mathf.Max(1, durationTurns);
        }

        /// <summary>
        /// 自己的回合结束时推进破防计时，返回「这一次是否刚好恢复」。
        /// </summary>
        /// <remarks>
        /// 护体重置时机由这里定死：<b>破防结束的那一次结算</b>把护体值重置为上限，
        /// 于是下一次破防又得从头攒满一轮，而破防期间不会再被削减。
        /// </remarks>
        internal bool TickBrokenTurn()
        {
            if (!IsBroken)
            {
                return false;
            }

            BrokenTurnsRemaining--;
            if (BrokenTurnsRemaining > 0)
            {
                return false;
            }

            IsBroken = false;
            BrokenTurnsRemaining = 0;
            BreakValue = BreakThreshold;
            return true;
        }

        /// <summary>
        /// 换位／移动的落点写入。调用方负责保证这一格是合法空位或合法的交换对象。
        /// </summary>
        /// <remarks>
        /// <see cref="FormationIndex"/> <b>不</b>跟着改：它记的是「布阵顺序」，
        /// 也就是开战那一刻的排位。行动值同值时用它破平局，因此它必须整场稳定，
        /// 否则换一次位就会悄悄改掉后续所有同速单位的出手顺序。
        /// </remarks>
        internal void OccupySlot(FormationSlot slot)
        {
            Slot = slot;
        }

        /// <summary>清掉这一次记下的「刚冷却」标记，让它从下一个回合开始递减。</summary>
        internal void ResetActions()
        {
            ActionValue = 0;
            TurnsTaken = 0;
            _cooledThisTurn.Clear();
        }

        private float SumModifier(bool attack, bool defense, bool speed)
        {
            var total = 0f;
            for (var i = 0; i < _statuses.Count; i++)
            {
                var status = _statuses[i];
                if (attack)
                {
                    total += status.AttackModifier;
                }

                if (defense)
                {
                    total += status.DefenseModifier;
                }

                if (speed)
                {
                    total += status.SpeedModifier;
                }
            }

            return total;
        }
    }
}
