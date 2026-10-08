using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 战斗事件。全部走 <see cref="EventChannel.Battle"/>，界面与音频只订阅、不反向读内核状态。
    /// </summary>
    /// <remarks>
    /// 刻意只带 ID 与数值，不带 <see cref="BattleUnit"/> 引用：
    /// 界面在事件回调里拿到的必须是「那一刻的快照」，而不是一个还在变的活对象，
    /// 否则飘字会跟着后续的伤害一起跳。
    /// </remarks>
    public static class BattleEventChannel
    {
        public const EventChannel Channel = EventChannel.Battle;
    }

    /// <summary>战斗开始。</summary>
    public readonly struct BattleStartedEvent : IGameEvent
    {
        public BattleStartedEvent(string encounterId, int playerCount, int enemyCount, bool isBoss)
        {
            EncounterId = encounterId;
            PlayerCount = playerCount;
            EnemyCount = enemyCount;
            IsBoss = isBoss;
        }

        public string EncounterId { get; }

        public int PlayerCount { get; }

        public int EnemyCount { get; }

        public bool IsBoss { get; }
    }

    /// <summary>战斗结束。</summary>
    public readonly struct BattleEndedEvent : IGameEvent
    {
        public BattleEndedEvent(BattleOutcome outcome, int rounds, int actionCount)
        {
            Outcome = outcome;
            Rounds = rounds;
            ActionCount = actionCount;
        }

        public BattleOutcome Outcome { get; }

        /// <summary>结束时结算到的回合数（定义见 <see cref="BattleSession.RoundNumber"/>）。</summary>
        public int Rounds { get; }

        /// <summary>累计结算过的单位回合数，用于校算「这场打了几手」。</summary>
        public int ActionCount { get; }
    }

    /// <summary>某个单位开始行动。</summary>
    public readonly struct BattleTurnStartedEvent : IGameEvent
    {
        public BattleTurnStartedEvent(int runtimeId, BattleSide side, int roundNumber)
        {
            RuntimeId = runtimeId;
            Side = side;
            RoundNumber = roundNumber;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        public int RoundNumber { get; }
    }

    /// <summary>
    /// 某个单位的回合被「剥夺行动」的状态整个跳过（眩晕）。
    /// 单独立一个事件是因为它是玩家最需要看见的反馈：这一个回合是被控掉的，不是没轮到。
    /// </summary>
    public readonly struct BattleTurnSkippedEvent : IGameEvent
    {
        public BattleTurnSkippedEvent(int runtimeId, BattleSide side, string statusId)
        {
            RuntimeId = runtimeId;
            Side = side;
            StatusId = statusId;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        /// <summary>造成跳过的状态 ID。</summary>
        public string StatusId { get; }
    }

    /// <summary>某个单位结束行动。</summary>
    public readonly struct BattleTurnEndedEvent : IGameEvent
    {
        public BattleTurnEndedEvent(int runtimeId, BattleSide side, int actionValue)
        {
            RuntimeId = runtimeId;
            Side = side;
            ActionValue = actionValue;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        /// <summary>推进后的行动值，界面据此画行动条。</summary>
        public int ActionValue { get; }
    }

    /// <summary>一次伤害结算。</summary>
    public readonly struct BattleDamagedEvent : IGameEvent
    {
        public BattleDamagedEvent(
            int sourceRuntimeId,
            int targetRuntimeId,
            BattleSide targetSide,
            string skillId,
            int damage,
            int healthAfter,
            bool isCritical,
            ElementRelation elementRelation)
        {
            SourceRuntimeId = sourceRuntimeId;
            TargetRuntimeId = targetRuntimeId;
            TargetSide = targetSide;
            SkillId = skillId;
            Damage = damage;
            HealthAfter = healthAfter;
            IsCritical = isCritical;
            ElementRelation = elementRelation;
        }

        public int SourceRuntimeId { get; }

        public int TargetRuntimeId { get; }

        public BattleSide TargetSide { get; }

        public string SkillId { get; }

        public int Damage { get; }

        public int HealthAfter { get; }

        public bool IsCritical { get; }

        public ElementRelation ElementRelation { get; }
    }

    /// <summary>一次治疗结算。</summary>
    public readonly struct BattleHealedEvent : IGameEvent
    {
        public BattleHealedEvent(int sourceRuntimeId, int targetRuntimeId, string skillId, int amount, int healthAfter)
        {
            SourceRuntimeId = sourceRuntimeId;
            TargetRuntimeId = targetRuntimeId;
            SkillId = skillId;
            Amount = amount;
            HealthAfter = healthAfter;
        }

        public int SourceRuntimeId { get; }

        public int TargetRuntimeId { get; }

        public string SkillId { get; }

        public int Amount { get; }

        public int HealthAfter { get; }
    }

    /// <summary>
    /// 用掉一件道具。
    /// </summary>
    /// <remarks>
    /// 它<b>不替代</b>效果自己的事件：回血照旧发 <see cref="BattleHealedEvent"/>，
    /// 挂状态照旧发 <see cref="BattleStatusAppliedEvent"/>，祛负面发 <see cref="BattleStatusExpiredEvent"/>，
    /// 于是界面读血条与状态图标的那几条通路一行都不用改。
    /// 这一条只回答「谁、用掉了什么、还剩几个」——战斗日志与录屏回放要的就是这个。
    /// </remarks>
    public readonly struct BattleItemUsedEvent : IGameEvent
    {
        public BattleItemUsedEvent(
            int actorRuntimeId,
            BattleSide actorSide,
            string itemId,
            string effectKey,
            int targetRuntimeId,
            BattleSide targetSide,
            int amount,
            int remaining)
        {
            ActorRuntimeId = actorRuntimeId;
            ActorSide = actorSide;
            ItemId = itemId;
            EffectKey = effectKey;
            TargetRuntimeId = targetRuntimeId;
            TargetSide = targetSide;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>用掉道具的单位。</summary>
        public int ActorRuntimeId { get; }

        public BattleSide ActorSide { get; }

        public string ItemId { get; }

        /// <summary>数据表里那条 <c>effectKey</c>。带上它是为了让「这一手到底做了什么」不必回头查表。</summary>
        public string EffectKey { get; }

        public int TargetRuntimeId { get; }

        public BattleSide TargetSide { get; }

        /// <summary>实际生效量：回血／回灵的数值，或解除的负面状态个数。</summary>
        public int Amount { get; }

        /// <summary>用完之后背包里还剩几个。没接背包时为 0。</summary>
        public int Remaining { get; }
    }

    /// <summary>进入破防。</summary>
    public readonly struct BattleBrokenEvent : IGameEvent
    {
        public BattleBrokenEvent(int runtimeId, BattleSide side, int durationTurns)
        {
            RuntimeId = runtimeId;
            Side = side;
            DurationTurns = durationTurns;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        public int DurationTurns { get; }
    }

    /// <summary>脱离破防、护体值重置。</summary>
    public readonly struct BattleBreakRecoveredEvent : IGameEvent
    {
        public BattleBreakRecoveredEvent(int runtimeId, BattleSide side, int breakValue)
        {
            RuntimeId = runtimeId;
            Side = side;
            BreakValue = breakValue;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        /// <summary>重置后的护体值（等于上限）。</summary>
        public int BreakValue { get; }
    }

    /// <summary>状态挂上、叠层、刷新或被顶替。</summary>
    public readonly struct BattleStatusAppliedEvent : IGameEvent
    {
        public BattleStatusAppliedEvent(int runtimeId, string statusId, StatusChangeKind change, int stacks, int remainingTurns)
        {
            RuntimeId = runtimeId;
            StatusId = statusId;
            Change = change;
            Stacks = stacks;
            RemainingTurns = remainingTurns;
        }

        public int RuntimeId { get; }

        public string StatusId { get; }

        public StatusChangeKind Change { get; }

        public int Stacks { get; }

        public int RemainingTurns { get; }
    }

    /// <summary>
    /// 状态离开这个单位。两种来路：持续时间走完，或被祛负面的道具摘掉。
    /// 界面要做的动作是同一条（撤掉那枚图标），因此不另立事件。
    /// </summary>
    public readonly struct BattleStatusExpiredEvent : IGameEvent
    {
        public BattleStatusExpiredEvent(int runtimeId, string statusId)
        {
            RuntimeId = runtimeId;
            StatusId = statusId;
        }

        public int RuntimeId { get; }

        public string StatusId { get; }
    }

    /// <summary>有单位倒下。</summary>
    public readonly struct BattleUnitDiedEvent : IGameEvent
    {
        public BattleUnitDiedEvent(int runtimeId, BattleSide side, string killerSkillId)
        {
            RuntimeId = runtimeId;
            Side = side;
            KillerSkillId = killerSkillId;
        }

        public int RuntimeId { get; }

        public BattleSide Side { get; }

        /// <summary>致命一击来自哪个技能；被持续伤害打死时为空。</summary>
        public string KillerSkillId { get; }
    }

    /// <summary>换位成功。</summary>
    public readonly struct BattleSwappedEvent : IGameEvent
    {
        public BattleSwappedEvent(int runtimeIdA, int runtimeIdB, FormationSlot slotA, FormationSlot slotB)
        {
            RuntimeIdA = runtimeIdA;
            RuntimeIdB = runtimeIdB;
            SlotA = slotA;
            SlotB = slotB;
        }

        public int RuntimeIdA { get; }

        public int RuntimeIdB { get; }

        public FormationSlot SlotA { get; }

        public FormationSlot SlotB { get; }
    }

    /// <summary>
    /// 一次逃跑的结算结果。
    /// </summary>
    /// <remarks>
    /// 一个事件同时带成功与失败两种结局，因为界面要的东西是一样的：
    /// 「掷了多少、当时成功率多少、成了没有」。成功与失败各发一个事件只会逼订阅方写两次同样的动画。
    /// 带上两侧战力是为了让「为什么是这个概率」当场可查，不必回头翻配置。
    /// </remarks>
    public readonly struct BattleEscapeResolvedEvent : IGameEvent
    {
        public BattleEscapeResolvedEvent(
            int runtimeId,
            bool escaped,
            float chance,
            float roll,
            float playerPower,
            float enemyPower)
        {
            RuntimeId = runtimeId;
            Escaped = escaped;
            Chance = chance;
            Roll = roll;
            PlayerPower = playerPower;
            EnemyPower = enemyPower;
        }

        /// <summary>发起逃跑的单位。</summary>
        public int RuntimeId { get; }

        /// <summary>整队是否脱离战斗。</summary>
        public bool Escaped { get; }

        /// <summary>当时的成功率（0–1）。</summary>
        public float Chance { get; }

        /// <summary>这次掷出的随机数（0–1）。</summary>
        public float Roll { get; }

        /// <summary>我方战力合计（只算站着的单位）。</summary>
        public float PlayerPower { get; }

        /// <summary>敌方战力合计（只算站着的单位）。</summary>
        public float EnemyPower { get; }
    }
}
