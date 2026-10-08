using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 一场战斗的流程内核：行动队列、回合状态机、指令结算、破防与换位。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：一个不依赖场景、不依赖界面、不读时间的纯逻辑对象。
    /// 整场战斗可以在 EditMode 里跑完并断言，这正是「先做内核再做表现」的意义——
    /// 界面接上来的时候，下面这套规则已经被测过了。</para>
    ///
    /// <para><b>回合状态机的形状</b>（口径来自任务书第 2 节「每个角色每回合拥有 1 次主要行动、
    /// 1 次移动或换位」）：
    /// <c>BeginNextTurn</c> 推进到行动值最小的单位，然后</para>
    /// <list type="bullet">
    /// <item><description>我方单位：进入 <see cref="TurnPhase.MainAction"/>，等调用方下指令；
    /// 主行动用掉后进入 <see cref="TurnPhase.MoveOrSwap"/>，还剩下一次移动／换位；</description></item>
    /// <item><description>敌方单位：直接执行预先算好的意图并结束回合，调用方不必也不应替它下指令；</description></item>
    /// <item><description>被「剥夺行动」状态控住的单位：整个回合被跳过，但状态时长照常递减——
    /// 否则眩晕会把自己永久锁住。</description></item>
    /// </list>
    ///
    /// <para><b>可复现性</b>：所有随机都取自注入的「战斗」随机流，且只在真正结算伤害与状态命中时取数。
    /// 选目标、选技能的规划器是纯函数，一次随机数都不消耗，因此界面多问几次意图也不会让战局跑偏。</para>
    /// </remarks>
    public sealed class BattleSession
    {
        private readonly BattleConfig _config;
        private readonly IDefinitionRegistry _registry;
        private readonly IRandomStream _stream;
        private readonly IEventBus _eventBus;
        private readonly BattleSetup _setup;
        private readonly List<BattleUnit> _units;
        private readonly List<BattleUnit> _players;
        private readonly List<BattleUnit> _enemies;
        private readonly List<BattleUnit> _targetScratch = new List<BattleUnit>(FormationSlot.Capacity);
        private readonly List<BattleUnit> _primaryScratch = new List<BattleUnit>(FormationSlot.Capacity);
        private readonly List<BattleStatusInstance> _expiredScratch = new List<BattleStatusInstance>(4);
        private readonly List<BattleStatusInstance> _curedScratch = new List<BattleStatusInstance>(4);
        private readonly List<BattleIntent> _intentScratch = new List<BattleIntent>(FormationSlot.Capacity);
        private readonly Dictionary<int, BattlePlan> _plans = new Dictionary<int, BattlePlan>(12);
        private readonly ActionQueue _queue;

        private bool _mainActionUsed;
        private bool _moveUsed;
        private bool _plansDirty = true;

        public BattleSession(
            BattleConfig config,
            IDefinitionRegistry registry,
            IRandomStream battleStream,
            BattleSetup setup,
            IEventBus eventBus = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _stream = battleStream ?? throw new ArgumentNullException(nameof(battleStream));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _eventBus = eventBus;

            if (!setup.Validate(out var error))
            {
                throw new ArgumentException($"入场清单不合法：{error}", nameof(setup));
            }

            _units = new List<BattleUnit>(setup.Party.Count + setup.Enemies.Count);
            _players = new List<BattleUnit>(setup.Party.Count);
            _enemies = new List<BattleUnit>(setup.Enemies.Count);

            BuildSide(BattleSide.Player, setup.Party, _players);
            BuildSide(BattleSide.Enemy, setup.Enemies, _enemies);

            if (_players.Count == 0 || _enemies.Count == 0)
            {
                throw new ArgumentException(
                    "入场清单里的定义全部解析失败，战斗无法开始；请先检查数据表是否导入了角色与敌人定义。",
                    nameof(setup));
            }

            // 开战归位（满血满灵、无状态、无冷却）已经在建单位那一刻做完了，这里不能再做第二遍：
            // 归位会清空状态清单，而此时身上可能已经挂着被动挂上来的常驻状态（见 ADR-020）。
            _queue = new ActionQueue(_config);
            _queue.Reset(_units);

            Outcome = BattleOutcome.Ongoing;
            Phase = TurnPhase.Idle;

            GameLog.Info(
                LogChannel.Battle,
                $"战斗开始：我方 {_players.Count} 人、敌方 {_enemies.Count} 人，遭遇 {_setup.EncounterId ?? "(手工构造)"}。",
                _setup.EncounterId);

            Publish(new BattleStartedEvent(_setup.EncounterId, _players.Count, _enemies.Count, _setup.IsBoss));
        }

        /// <summary>这场战斗的数值配置。</summary>
        public BattleConfig Config => _config;

        public BattleSetup Setup => _setup;

        /// <summary>
        /// 这一战能吃到哪些道具。为 null 表示这场没有道具可用（界面据此不画道具按钮）。
        /// 它是活背包的引用，用掉一个之后数量当场就少——界面读它就能给出最新的剩余数。
        /// </summary>
        public IBattleInventory Inventory => _setup.Inventory;

        public BattleOutcome Outcome { get; private set; }

        public TurnPhase Phase { get; private set; }

        /// <summary>当前正在行动的单位；还没开始时为 null，回合结束后保留最后一位行动者。</summary>
        public BattleUnit CurrentActor { get; private set; }

        /// <summary>已经结算过多少个单位回合。它是「这场打了几手」的口径。</summary>
        public int ActionCount { get; private set; }

        /// <summary>场上全部单位，我方在前、敌方在后，与建队顺序一致。</summary>
        public IReadOnlyList<BattleUnit> Units => _units;

        public IReadOnlyList<BattleUnit> PlayerUnits => _players;

        public IReadOnlyList<BattleUnit> EnemyUnits => _enemies;

        public bool IsFinished => Outcome != BattleOutcome.Ongoing;

        /// <summary>
        /// 此刻发起逃跑的成功率（0–1）。界面直接显示它，不要自己再按战力算一遍：
        /// 战力权重住在配置里，两边各算一次迟早会算出两个数。
        /// </summary>
        public float EscapeChance =>
            _config.GetEscapeChance(CombatPower(BattleSide.Player), CombatPower(BattleSide.Enemy));

        /// <summary>
        /// 当前回合数。
        /// </summary>
        /// <remarks>
        /// 定义：<c>1 + 存活我方单位中最少的「已行动次数」</c>。
        /// 因为行动顺序由速度决定，全局「第几个行动」并不等同于「第几回合」；
        /// 而 Docs/战斗数值-v1.md 里那句「常规遭遇 6 回合」说的是「四个取经人各打一遍算一回合」。
        /// 这个定义正好与之一致，并且在我方减员时仍然单调递增。
        /// </remarks>
        public int RoundNumber
        {
            get
            {
                var min = int.MaxValue;
                for (var i = 0; i < _players.Count; i++)
                {
                    var unit = _players[i];
                    if (unit.IsAlive && unit.TurnsTaken < min)
                    {
                        min = unit.TurnsTaken;
                    }
                }

                return min == int.MaxValue ? 0 : min + 1;
            }
        }

        /// <summary>
        /// 敌方意图预告：行动队列里接下来 <see cref="BattleConfig.IntentPreviewLead"/> 个行动位中，
        /// 属于敌方的那些单位的意图。
        /// </summary>
        /// <remarks>
        /// 返回的是内部复用列表，内容会在下次读取时被覆盖；需要留存请自行复制。
        /// 想知道某个具体敌人在打算什么，用 <see cref="GetIntent"/>。
        /// </remarks>
        public IReadOnlyList<BattleIntent> EnemyIntents
        {
            get
            {
                _intentScratch.Clear();
                var lead = Mathf.Max(0, _config.IntentPreviewLead);
                if (lead == 0 || Outcome != BattleOutcome.Ongoing)
                {
                    return _intentScratch;
                }

                var order = _queue.PreviewOrder(lead);
                for (var i = 0; i < order.Count; i++)
                {
                    var unit = order[i];
                    if (unit.Side != BattleSide.Enemy)
                    {
                        continue;
                    }

                    var plan = GetPlan(unit);
                    _intentScratch.Add(plan.HasPlan ? plan.Intent : BattleIntent.None(unit));
                }

                return _intentScratch;
            }
        }

        /// <summary>按单位编号取意图。界面画敌方头顶的预告图标时用它。</summary>
        public BattleIntent GetIntent(int runtimeId)
        {
            var unit = FindUnit(runtimeId);
            if (unit == null || !unit.IsAlive || unit.Side != BattleSide.Enemy)
            {
                return BattleIntent.None(unit);
            }

            var plan = GetPlan(unit);
            return plan.HasPlan ? plan.Intent : BattleIntent.None(unit);
        }

        /// <summary>
        /// 推进到下一个行动者。
        /// </summary>
        /// <returns>本次行动的单位；战斗已结束或没有可用行动者时返回 null。</returns>
        /// <remarks>
        /// 若下一个行动者是敌方单位，它会在这里把整个回合走完（出手 + 回合末结算），
        /// 返回时 <see cref="Phase"/> 已经是 <see cref="TurnPhase.Finished"/>。
        /// 这样界面只需处理「轮到我方时下指令」这一种情况。
        /// </remarks>
        public BattleUnit BeginNextTurn()
        {
            if (Outcome != BattleOutcome.Ongoing)
            {
                return null;
            }

            if (Phase == TurnPhase.MainAction || Phase == TurnPhase.MoveOrSwap)
            {
                GameLog.Warn(
                    LogChannel.Battle,
                    $"上一个单位（{CurrentActor?.DefinitionId}）的回合还没结束就要求推进，已忽略。");
                return null;
            }

            var actor = _queue.PeekNext();
            if (actor == null)
            {
                ResolveOutcome();
                return null;
            }

            CurrentActor = actor;
            _mainActionUsed = false;
            _moveUsed = false;
            ActionCount++;

            Publish(new BattleTurnStartedEvent(actor.RuntimeId, actor.Side, RoundNumber));

            if (actor.IsActionPrevented)
            {
                Publish(new BattleTurnSkippedEvent(actor.RuntimeId, actor.Side, FirstPreventingStatusId(actor)));
                GameLog.Debug(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 被控住，本回合跳过。",
                    actor.DefinitionId);
                FinishTurn();
                return actor;
            }

            if (actor.Side == BattleSide.Enemy)
            {
                ExecutePlannedTurn(actor);
                FinishTurn();
                return actor;
            }

            Phase = TurnPhase.MainAction;
            return actor;
        }

        /// <summary>
        /// 用技能（主行动）。
        /// </summary>
        /// <param name="skillId">技能定义 ID。</param>
        /// <param name="primaryTargetRuntimeId">
        /// 主目标编号。单体与列／排技能必须给；全体技能与自身技能可省略。
        /// </param>
        public BattleActionResult UseSkill(string skillId, int primaryTargetRuntimeId = -1)
        {
            var actor = CurrentActor;
            if (Outcome != BattleOutcome.Ongoing)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.BattleFinished, actor);
            }

            if (actor == null || Phase == TurnPhase.Idle)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.NoActiveTurn, actor);
            }

            if (_mainActionUsed || Phase != TurnPhase.MainAction)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.WrongPhase, actor);
            }

            if (!actor.HasSkill(skillId))
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.SkillNotOwned, actor, BattleActionKind.Skill, skillId);
            }

            var skill = BattleActionPlanner.ResolveSkill(_registry, skillId);
            if (skill == null)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.UnknownSkill, actor, BattleActionKind.Skill, skillId);
            }

            if (actor.GetCooldown(skillId) > 0)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.SkillOnCooldown, actor, BattleActionKind.Skill, skillId);
            }

            if (actor.UsesSpirit && actor.Spirit < skill.SpiritCost)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NotEnoughSpirit, actor, BattleActionKind.Skill, skillId);
            }

            BattleUnit primary = null;
            if (primaryTargetRuntimeId >= 0)
            {
                primary = FindUnit(primaryTargetRuntimeId);
            }

            if (BattleTargeting.NeedsCallerTarget(skill.Target))
            {
                if (primary == null)
                {
                    return BattleActionResult.Rejected(
                        BattleCommandRejection.NoValidTarget, actor, BattleActionKind.Skill, skillId);
                }

                if (!primary.IsAlive)
                {
                    return BattleActionResult.Rejected(
                        BattleCommandRejection.TargetDead, actor, BattleActionKind.Skill, skillId);
                }

                var wantHostile = BattleTargeting.RequiresHostilePrimary(skill);
                if ((primary.Side != actor.Side) != wantHostile)
                {
                    return BattleActionResult.Rejected(
                        BattleCommandRejection.TargetSideMismatch, actor, BattleActionKind.Skill, skillId);
                }
            }

            var result = ResolveSkillAction(actor, skill, primary);
            if (result.Success)
            {
                _mainActionUsed = true;

                // 主行动用掉之后只剩一次移动／换位。
                // 少了这一句，TurnPhase.MoveOrSwap 就是一个永远到不了的状态，
                // 而 MoveTo / SwapWith 的是否可用只能靠 _moveUsed 兜住——阶段机形同虚设。
                // 战斗已经结算完毕时不再改阶段，避免「已结束」与「还剩一次移动」同时成立。
                if (Outcome == BattleOutcome.Ongoing)
                {
                    Phase = TurnPhase.MoveOrSwap;
                }
            }

            return result;
        }

        /// <summary>
        /// 移动到本方的空格子（移动或换位，每回合一次，不占用主行动）。
        /// </summary>
        public BattleActionResult MoveTo(FormationSlot destination)
        {
            var guard = CheckMoveCommand();
            if (guard != BattleCommandRejection.None)
            {
                return BattleActionResult.Rejected(guard, CurrentActor, BattleActionKind.Swap);
            }

            var actor = CurrentActor;
            if (!FormationSlot.IsInRange(destination.Column, destination.Row))
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.SlotOutOfRange, actor, BattleActionKind.Swap);
            }

            if (actor.Slot == destination)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.SlotOccupied, actor, BattleActionKind.Swap);
            }

            var occupant = FindUnitAtSlot(actor.Side, destination);
            if (occupant != null)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.SlotOccupied, actor, BattleActionKind.Swap);
            }

            var from = actor.Slot;
            actor.OccupySlot(destination);
            _moveUsed = true;
            _plansDirty = true;

            Publish(new BattleSwappedEvent(actor.RuntimeId, -1, from, destination));
            GameLog.Debug(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 从 {from} 移动到 {destination}。",
                actor.DefinitionId);

            return BattleActionResult.Succeeded(actor, BattleActionKind.Swap, null);
        }

        /// <summary>
        /// 与同阵营的存活同伴交换站位（移动或换位，每回合一次，不占用主行动）。
        /// </summary>
        public BattleActionResult SwapWith(int allyRuntimeId)
        {
            var guard = CheckMoveCommand();
            if (guard != BattleCommandRejection.None)
            {
                return BattleActionResult.Rejected(guard, CurrentActor, BattleActionKind.Swap);
            }

            var actor = CurrentActor;
            var ally = FindUnit(allyRuntimeId);
            if (ally == null || ally == actor || !ally.IsAlive || ally.Side != actor.Side)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.SwapTargetInvalid, actor, BattleActionKind.Swap);
            }

            var actorSlot = actor.Slot;
            var allySlot = ally.Slot;
            actor.OccupySlot(allySlot);
            ally.OccupySlot(actorSlot);
            _moveUsed = true;
            _plansDirty = true;

            Publish(new BattleSwappedEvent(actor.RuntimeId, ally.RuntimeId, actorSlot, allySlot));
            GameLog.Debug(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 与 #{ally.RuntimeId} {ally.DefinitionId} 换位："
                + $"{actorSlot} ↔ {allySlot}。",
                actor.DefinitionId);

            return BattleActionResult.Succeeded(actor, BattleActionKind.Swap, null);
        }

        /// <summary>
        /// 此刻还能不能走一步（移动或换位）。界面据此决定给不给那两颗按钮。
        /// </summary>
        /// <remarks>
        /// 判据就是 <see cref="MoveTo"/>／<see cref="SwapWith"/> 用的那一扇门
        /// （<see cref="CheckMoveCommand"/>），借它一次，而不是在界面里重写「相位对不对、走没用过」——
        /// 两份判据迟早会分家，然后出现「按钮能点、内核不让」。
        /// </remarks>
        public bool CanMoveOrSwap => CheckMoveCommand() == BattleCommandRejection.None;

        /// <summary>
        /// 收集当前行动者能落脚的格子（本方空格），按阵型序号升序。
        /// </summary>
        /// <remarks>
        /// 「这格有没有人」用的是 <see cref="MoveTo"/> 同一个判据，因此界面照抄画出来的候选
        /// 与内核会接受的候选是同一批，界面不需要、也不该自己算格子。
        /// 还没轮到我方时收集结果为空。
        /// </remarks>
        public void CollectMoveDestinations(List<FormationSlot> sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            sink.Clear();

            var actor = CurrentActor;
            if (actor == null || actor.Side != BattleSide.Player)
            {
                return;
            }

            for (var index = 0; index < FormationSlot.Capacity; index++)
            {
                var slot = FormationSlot.FromIndex(index);
                if (slot == actor.Slot)
                {
                    continue;
                }

                if (FindUnitAtSlot(actor.Side, slot) == null)
                {
                    sink.Add(slot);
                }
            }
        }

        /// <summary>
        /// 收集当前行动者能换位的同伴编号（同阵营存活、不含自己），升序。
        /// </summary>
        /// <remarks>
        /// 单位列表本身就是「我方在前、编号升序」建的，直接沿用即可——
        /// 两处各排一次，顺序口径就有了两个来源。
        /// </remarks>
        public void CollectSwapPartners(List<int> sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            sink.Clear();

            var actor = CurrentActor;
            if (actor == null || actor.Side != BattleSide.Player)
            {
                return;
            }

            for (var i = 0; i < _players.Count; i++)
            {
                var ally = _players[i];
                if (ally == actor || !ally.IsAlive)
                {
                    continue;
                }

                sink.Add(ally.RuntimeId);
            }
        }

        /// <summary>
        /// 逃跑（主行动）：整队撤退。
        /// </summary>
        /// <remarks>
        /// 口径（已拍板）：一次掷骰决定整场走人还是失败，判据是敌我战力比
        /// （<see cref="BattleConfig.GetEscapeChance"/>，线性、夹在配置的上下限之间）。
        /// 失败只是白费这一手——敌人照常行动、不额外挨打，与打完技能一样进入
        /// <see cref="TurnPhase.MoveOrSwap"/>。
        /// 成功率随战局变化（战力只算站着的单位），所以界面必须每回合重读
        /// <see cref="EscapeChance"/>，不能在开局缓存一个值。
        /// </remarks>
        public BattleActionResult TryEscape()
        {
            var actor = CurrentActor;
            if (Outcome != BattleOutcome.Ongoing)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.BattleFinished, actor, BattleActionKind.Escape);
            }

            if (actor == null || Phase == TurnPhase.Idle)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NoActiveTurn, actor, BattleActionKind.Escape);
            }

            if (_mainActionUsed || Phase != TurnPhase.MainAction)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.WrongPhase, actor, BattleActionKind.Escape);
            }

            var myPower = CombatPower(BattleSide.Player);
            var enemyPower = CombatPower(BattleSide.Enemy);
            var chance = _config.GetEscapeChance(myPower, enemyPower);
            var roll = _stream.NextFloat();

            // 概率再算一次只是为了把它带进结果与日志：纯函数、不掷随机，两次必然同值。
            var escaped = _config.IsEscapeRoll(roll, myPower, enemyPower);

            var result = BattleActionResult.Succeeded(actor, BattleActionKind.Escape, null);
            result.EscapeChance = chance;
            result.EscapeRoll = roll;
            result.Escaped = escaped;

            _mainActionUsed = true;

            Publish(new BattleEscapeResolvedEvent(
                actor.RuntimeId, escaped, chance, roll, myPower, enemyPower));

            if (!escaped)
            {
                Phase = TurnPhase.MoveOrSwap;
                GameLog.Info(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 逃跑失败（成功率 {chance:P0}，掷出 {roll:F3}），白费一手。",
                    actor.DefinitionId);
                return result;
            }

            Phase = TurnPhase.Finished;
            GameLog.Info(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 逃跑成功（成功率 {chance:P0}，掷出 {roll:F3}），整队脱离战斗。",
                _setup.EncounterId);
            EndBattle(BattleOutcome.PlayerEscaped);

            return result;
        }

        /// <summary>
        /// 防御（主行动）：给自己挂上配置指定的减伤状态。
        /// </summary>
        /// <remarks>
        /// <para>口径（已拍板，批次 4）：减伤 <b>50%</b>、持续 <b>2 回合</b>、<b>占主行动</b>。
        /// 减伤幅度与持续回合<b>不在</b>这里——它们跟着状态表走
        /// （<c>statuses.csv</c> 的 <c>STS_DEFEND</c>：承伤乘区 ×0.5、时长 2），
        /// 本方法只负责「把它挂上去」，于是调数值不用改代码，改挂哪个状态也不用改代码
        /// （见 <see cref="BattleConfig.DefendStatusId"/>）。</para>
        ///
        /// <para><b>与破防怎么互算</b>：破防的 ×1.5 是伤害公式第 5 步的独立乘区，
        /// 防御的 ×0.5 走第 7 步的承伤修正，两者按顺序<b>相乘</b>，净效果 <c>1.5 × 0.5 = 0.75</c>——
        /// 破防中的人举架仍然比平常更疼，但不会白举。</para>
        ///
        /// <para>防御是<b>通用指令</b>，不在技能表里：不吃灵力、不进冷却、不掷任何随机数。
        /// 代价与回报都由「占掉主行动」这一条承担，所以自动战斗的规划器不会主动防御
        /// （规划器只从技能表里选招，见 <see cref="BattleActionPlanner"/>）。</para>
        /// </remarks>
        public BattleActionResult TryDefend()
        {
            var actor = CurrentActor;
            if (Outcome != BattleOutcome.Ongoing)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.BattleFinished, actor, BattleActionKind.Defend);
            }

            if (actor == null || Phase == TurnPhase.Idle)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NoActiveTurn, actor, BattleActionKind.Defend);
            }

            if (_mainActionUsed || Phase != TurnPhase.MainAction)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.WrongPhase, actor, BattleActionKind.Defend);
            }

            if (!_registry.TryGet(_config.DefendStatusId, out StatusDefinition status) || status == null)
            {
                GameLog.Warn(
                    LogChannel.Battle,
                    $"防御指令引用的状态 '{_config.DefendStatusId}' 不在数据表里，本手被拒。",
                    _config.DefendStatusId);
                return BattleActionResult.Rejected(
                    BattleCommandRejection.DefinitionMissing, actor, BattleActionKind.Defend);
            }

            var result = BattleActionResult.Succeeded(actor, BattleActionKind.Defend, null);

            // 结果与技能路径同构：防御也是「对某个目标施加了一次状态」，
            // 只是目标是自己、且没有伤害与掷骰。复用 BattleUnitEffect 让日志与界面
            // 不必为防御再写一套读取方式。
            var effect = result.AddEffect(actor, null);
            effect.TargetWasBroken = actor.IsBroken;

            var change = actor.ApplyStatus(status);
            var applied = actor.FindStatus(status.Id);
            effect.AppliedStatusId = status.Id;
            effect.StatusChange = change;

            Publish(new BattleStatusAppliedEvent(
                actor.RuntimeId,
                status.Id,
                change,
                applied?.Stacks ?? 1,
                applied?.RemainingTurns ?? status.DurationTurns));

            GameLog.Info(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 防御，挂上 {status.Id}"
                + $"（{change}，剩余 {applied?.RemainingTurns ?? status.DurationTurns} 回合）。",
                actor.DefinitionId);

            _mainActionUsed = true;
            _plansDirty = true;

            // 与 UseSkill 同一条：主行动用掉之后只剩一次移动／换位；
            // 战斗已经结算完毕时不再改阶段，避免「已结束」与「还剩一次移动」同时成立。
            if (Outcome == BattleOutcome.Ongoing)
            {
                Phase = TurnPhase.MoveOrSwap;
            }

            return result;
        }

        /// <summary>
        /// 用道具（主行动）。目标是我方单体（含自己），效果由数据表的 <c>effectKey</c> 解释。
        /// </summary>
        /// <remarks>
        /// <para>口径（已拍板，2026-10-08）：<b>占主行动、不吃灵力、一回合一件、扣背包、
        /// 复用既有的治疗／状态通路、只在战斗内用</b>。</para>
        ///
        /// <para><b>为什么没有「本回合已用过道具」的计时器</b>：道具与技能、防御共用同一道主行动门
        /// （<see cref="TurnPhase.MainAction"/> 且主行动尚未用掉），而主行动每回合只有一次。
        /// 再记一个「用过道具没有」的布尔值，得到的是一个永远走不到的分支——
        /// 那是死代码，不是安全网。</para>
        ///
        /// <para><b>不吃灵力</b>：这条路径完全不碰 <c>TryPaySpirit</c>，也不碰冷却与伤害公式。
        /// 代价只有两样：扣掉背包里那一个、占掉这一手。</para>
        ///
        /// <para><b>效果的落点复用既有通路</b>：回血照旧发 <see cref="BattleHealedEvent"/>，
        /// 挂状态照旧发 <see cref="BattleStatusAppliedEvent"/>，祛负面发
        /// <see cref="BattleStatusExpiredEvent"/>——界面读血条与状态图标的那几条通路一行都不用改。
        /// 另外发一条 <see cref="BattleItemUsedEvent"/> 交代「用掉了什么、还剩几个」。</para>
        ///
        /// <para><b>本版只作用于我方单体</b>：伤害类道具需要目标规则、命中段数与五行克制，
        /// 那整套在技能表里（<c>skills.csv</c>）。给道具另开一条伤害通路等于把伤害公式抄第二份，
        /// 所以攻击性道具留到「道具引用技能」那一步再做，本版的效果键只有
        /// <see cref="ItemEffectKeys"/> 里的三种。</para>
        ///
        /// <para>数据错误（效果键不认识）给 <see cref="BattleCommandRejection.ItemEffectUnknown"/>
        /// 并记一条 <c>Warn</c>，<b>不吃掉这一手</b>——与 <see cref="TryDefend"/> 遇到
        /// <see cref="BattleCommandRejection.DefinitionMissing"/> 是同一条口径。</para>
        /// </remarks>
        /// <param name="itemId">数据表里的道具 ID。</param>
        /// <param name="targetRuntimeId">目标的我方单位编号；-1 表示对自己用。</param>
        public BattleActionResult UseItem(string itemId, int targetRuntimeId = -1)
        {
            var actor = CurrentActor;
            if (Outcome != BattleOutcome.Ongoing)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.BattleFinished, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (actor == null || Phase == TurnPhase.Idle)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NoActiveTurn, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (_mainActionUsed || Phase != TurnPhase.MainAction)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.WrongPhase, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (string.IsNullOrEmpty(itemId) ||
                !_registry.TryGet(itemId, out ItemDefinition item) ||
                item == null ||
                !item.UsableInBattle)
            {
                GameLog.Warn(
                    LogChannel.Battle,
                    $"道具 '{itemId}' 不在数据表里，或没有标记 usableInBattle，本手被拒。",
                    itemId);
                return BattleActionResult.Rejected(
                    BattleCommandRejection.ItemNotUsable, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            var inventory = _setup.Inventory;
            if (item.IsConsumedOnUse && (inventory == null || inventory.CountOf(itemId) <= 0))
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.ItemOutOfStock, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            var target = targetRuntimeId < 0 ? actor : FindUnit(targetRuntimeId);
            if (target == null)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NoValidTarget, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (!target.IsAlive)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.TargetDead, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (target.Side != actor.Side)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.TargetSideMismatch, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            if (!ItemEffectKeys.IsKnown(item.EffectKey))
            {
                GameLog.Warn(
                    LogChannel.Battle,
                    $"道具 '{item.Id}' 的效果键 '{item.EffectKey}' 内核不认识（本版只解释 "
                    + $"{ItemEffectKeys.HealHealth}／{ItemEffectKeys.HealSpirit}／{ItemEffectKeys.CureStatus}），本手被拒。",
                    item.Id);
                return BattleActionResult.Rejected(
                    BattleCommandRejection.ItemEffectUnknown, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            // 数据全部验完才扣背包：被拒的指令不该让玩家白白少一个道具。
            if (item.IsConsumedOnUse && !inventory.TryConsume(itemId))
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.ItemOutOfStock, actor, BattleActionKind.UseItem, itemId: itemId);
            }

            var result = BattleActionResult.Succeeded(
                actor, BattleActionKind.UseItem, skillId: null, itemId: item.Id);
            var effect = result.AddEffect(target, null);
            effect.TargetWasBroken = target.IsBroken;

            var amount = ApplyItemEffect(actor, item, target, effect);
            effect.HealthAfter = target.Health;

            Publish(new BattleItemUsedEvent(
                actor.RuntimeId,
                actor.Side,
                item.Id,
                item.EffectKey,
                target.RuntimeId,
                target.Side,
                amount,
                inventory?.CountOf(item.Id) ?? 0));

            GameLog.Info(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 用掉 {item.Id}（{item.EffectKey}）于 "
                + $"#{target.RuntimeId} {target.DefinitionId}，生效 {amount}，"
                + $"背包还剩 {inventory?.CountOf(item.Id) ?? 0}。",
                item.Id);

            _mainActionUsed = true;
            _plansDirty = true;

            // 与 UseSkill／TryDefend 同一条：主行动用掉之后只剩一次移动／换位；
            // 战斗已经结算完毕时不再改阶段。
            if (Outcome == BattleOutcome.Ongoing)
            {
                Phase = TurnPhase.MoveOrSwap;
            }

            return result;
        }

        /// <summary>
        /// 按效果键把这一次道具结算完，返回实际生效量（回血／回灵的点数，或解除的负面状态个数）。
        /// </summary>
        /// <remarks>
        /// 调用方已经保证 <paramref name="item"/> 的效果键是认识的，这里的 <c>default</c>
        /// 因此只是一道编译期兜底，正常走不到。
        /// </remarks>
        private int ApplyItemEffect(BattleUnit actor, ItemDefinition item, BattleUnit target, BattleUnitEffect effect)
        {
            switch (item.EffectKey)
            {
                case ItemEffectKeys.HealHealth:
                    var healed = target.Heal(item.EffectMagnitude);
                    effect.Healing = healed;
                    if (healed > 0)
                    {
                        Publish(new BattleHealedEvent(
                            actor.RuntimeId, target.RuntimeId, item.Id, healed, target.Health));
                    }

                    return healed;

                case ItemEffectKeys.HealSpirit:
                    var restored = target.RestoreSpirit(item.EffectMagnitude);
                    effect.SpiritRestored = restored;
                    return restored;

                case ItemEffectKeys.CureStatus:
                    _curedScratch.Clear();
                    var removed = target.RemoveDebuffs(_curedScratch);
                    effect.CuredDebuffs = removed;
                    for (var i = 0; i < _curedScratch.Count; i++)
                    {
                        Publish(new BattleStatusExpiredEvent(target.RuntimeId, _curedScratch[i].StatusId));
                    }

                    _curedScratch.Clear();
                    return removed;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// 剧情强制结束这场战斗（撤退）。
        /// </summary>
        /// <remarks>
        /// 由剧本调用，不掷骰、不判胜负：它<b>不是失败</b>，流程侧据此回到该战斗之前的剧情节点。
        /// 与全灭、逃跑一样走唯一的结局出口，因此「结局被写下」与
        /// <see cref="BattleEndedEvent"/> 被发出不会只发生一半。
        /// 幂等：战斗已经结束时返回 false，既不改写结局，也不重复发事件。
        /// </remarks>
        /// <returns>true 表示这一次调用结束了战斗；false 表示战斗早已结束。</returns>
        public bool ForceRetreat()
        {
            if (Outcome != BattleOutcome.Ongoing)
            {
                return false;
            }

            Phase = TurnPhase.Finished;
            GameLog.Info(
                LogChannel.Battle,
                "剧情强制撤退：本场按「回到剧情前」收场，不判失败、不结算。",
                _setup.EncounterId);
            EndBattle(BattleOutcome.ForcedRetreat);

            return true;
        }

        /// <summary>结束当前单位的回合，放弃剩余行动。</summary>
        public void EndTurn()
        {
            if (Phase == TurnPhase.Idle || Phase == TurnPhase.Finished)
            {
                GameLog.Warn(LogChannel.Battle, "当前没有待结束的回合，EndTurn 已忽略。");
                return;
            }

            FinishTurn();
        }

        /// <summary>
        /// 自动战斗：把这场仗一路推到底，双方都由规划器代打。
        /// </summary>
        /// <remarks>
        /// 它<b>就是</b>自动战斗，不是测试专用的旁路：我方与敌方走的是<b>同一套</b>
        /// <see cref="BattleActionPlanner"/>，输入是当前战局、输出是「推进到结局」，
        /// 只产出 <see cref="Outcome"/>——不表演、不等待输入。
        /// 「加速播放」（把规划器给出的手按时间轴一手手播出来）属于界面层，内核不提供「快进」概念，
        /// 也不为此另立一套战斗循环。
        /// 战斗已经有结局时立刻返回，不再推进。
        /// </remarks>
        /// <param name="maxActions">单次调用最多推进的手数，用尽即封顶（此时可能仍未分胜负）。</param>
        /// <returns>结束时的 <see cref="ActionCount"/>（本场累计手数），不是本次推进的手数。</returns>
        public int RunToEnd(int maxActions = 512)
        {
            var guard = Mathf.Max(1, maxActions);
            while (Outcome == BattleOutcome.Ongoing && guard-- > 0)
            {
                var actor = BeginNextTurn();
                if (actor == null)
                {
                    break;
                }

                if (Phase != TurnPhase.MainAction)
                {
                    // 敌方或被控住，BeginNextTurn 内部已经把这个回合走完了。
                    continue;
                }

                var plan = GetPlan(actor);
                if (plan.HasPlan)
                {
                    UseSkill(plan.Intent.SkillId, plan.PrimaryTarget?.RuntimeId ?? -1);
                }

                EndTurn();
            }

            return ActionCount;
        }

        /// <summary>按编号找单位；找不到返回 null。</summary>
        public BattleUnit FindUnit(int runtimeId)
        {
            for (var i = 0; i < _units.Count; i++)
            {
                if (_units[i].RuntimeId == runtimeId)
                {
                    return _units[i];
                }
            }

            return null;
        }

        /// <summary>某一侧当前还站着的单位数。</summary>
        public int AliveCount(BattleSide side)
        {
            var list = side == BattleSide.Player ? _players : _enemies;
            var count = 0;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].IsAlive)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 某一侧的战力合计（只算站着的单位）。它只服务逃跑判定，不参与伤害公式。
        /// </summary>
        /// <remarks>
        /// 倒下的人不算战力，于是「打掉一个敌人就更容易跑掉」是自然结果，不需要额外规则。
        /// </remarks>
        private float CombatPower(BattleSide side)
        {
            var list = side == BattleSide.Player ? _players : _enemies;
            var power = 0f;
            for (var i = 0; i < list.Count; i++)
            {
                var unit = list[i];
                if (!unit.IsAlive)
                {
                    continue;
                }

                power += _config.GetCombatPower(
                    unit.EffectiveAttack, unit.EffectiveDefense, unit.EffectiveSpeed, unit.MaxHealth);
            }

            return power;
        }

        // ---------------------------------------------------------------- 内部实现

        private void BuildSide(BattleSide side, IReadOnlyList<BattleUnitBlueprint> blueprints, List<BattleUnit> sink)
        {
            for (var i = 0; i < blueprints.Count; i++)
            {
                // 编号与布阵序号都按「我方在前、敌方在后」连续分配：
                // 行动值同值时靠布阵序号破平局，于是同速时我方先动，
                // 与 Docs/战斗数值-v1.md 里那一串校算顺序一致。
                var runtimeId = _units.Count;
                // 在身装备只作用于我方成员；敌方没穿装备，传进去也无件可查。
                var unit = BattleFactory.CreateUnit(_registry, side, runtimeId, runtimeId, blueprints[i], _setup.Loadout);
                if (unit == null)
                {
                    continue;
                }

                _units.Add(unit);
                sink.Add(unit);
            }
        }

        private BattleCommandRejection CheckMoveCommand()
        {
            if (Outcome != BattleOutcome.Ongoing)
            {
                return BattleCommandRejection.BattleFinished;
            }

            if (CurrentActor == null || Phase == TurnPhase.Idle)
            {
                return BattleCommandRejection.NoActiveTurn;
            }

            if (Phase == TurnPhase.Finished)
            {
                return BattleCommandRejection.WrongPhase;
            }

            if (CurrentActor.Side != BattleSide.Player)
            {
                return BattleCommandRejection.WrongPhase;
            }

            return _moveUsed ? BattleCommandRejection.MoveAlreadyUsed : BattleCommandRejection.None;
        }

        /// <summary>敌方（以及被控住的单位）的自动回合。</summary>
        private void ExecutePlannedTurn(BattleUnit actor)
        {
            var plan = GetPlan(actor);
            if (!plan.HasPlan)
            {
                GameLog.Debug(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 没有可用技能，本回合空过。",
                    actor.DefinitionId);
                return;
            }

            GameLog.Debug(LogChannel.Battle, plan.Intent.ToString(), actor.DefinitionId);
            ResolveSkillAction(actor, BattleActionPlanner.ResolveSkill(_registry, plan.Intent.SkillId), plan.PrimaryTarget);
            _mainActionUsed = true;
        }

        private BattleActionResult ResolveSkillAction(BattleUnit actor, SkillDefinition skill, BattleUnit primary)
        {
            if (skill == null)
            {
                return BattleActionResult.Rejected(BattleCommandRejection.UnknownSkill, actor);
            }

            // 随机目标在这里才掷骰：规划阶段不消耗随机数，见 BattleActionPlanner 的说明。
            if (skill.Target == TargetRule.RandomEnemy)
            {
                primary = DrawRandomOpponent(actor);
                if (primary == null)
                {
                    return BattleActionResult.Rejected(
                        BattleCommandRejection.NoValidTarget, actor, BattleActionKind.Skill, skill.Id);
                }
            }

            var targets = BattleTargeting.Resolve(actor, skill, primary, _units, _targetScratch);
            if (targets.Count == 0)
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NoValidTarget, actor, BattleActionKind.Skill, skill.Id);
            }

            if (!actor.TryPaySpirit(skill.SpiritCost))
            {
                return BattleActionResult.Rejected(
                    BattleCommandRejection.NotEnoughSpirit, actor, BattleActionKind.Skill, skill.Id);
            }

            actor.StartCooldown(skill.Id, skill.CooldownTurns);

            var result = BattleActionResult.Succeeded(actor, BattleActionKind.Skill, skill.Id);
            for (var i = 0; i < targets.Count; i++)
            {
                ApplySkillToTarget(result, actor, skill, targets[i]);
            }

            _plansDirty = true;
            ResolveOutcome();
            return result;
        }

        private void ApplySkillToTarget(BattleActionResult result, BattleUnit actor, SkillDefinition skill, BattleUnit target)
        {
            var wasAlive = target.IsAlive;
            var effect = result.AddEffect(target, skill.Id);
            effect.TargetWasBroken = target.IsBroken;

            if (skill.Power > 0)
            {
                ApplyDamageHits(effect, actor, skill, target);
                ApplyBreakDamage(effect, actor, skill, target);
            }
            else
            {
                // 纯辅助技能（治疗、护盾）不产生任何伤害。
                // 这条判断是必须的：伤害公式有 MinimumDamage = 1 的下限，
                // 若照公式走，「治疗」会反过来打掉队友至少 1 点血。
                effect.BreakValueAfter = target.BreakValue;
            }

            if (skill.HealPower > 0 && target.IsAlive)
            {
                var healed = target.Heal(skill.HealPower);
                if (healed > 0)
                {
                    effect.Healing = healed;
                    Publish(new BattleHealedEvent(actor.RuntimeId, target.RuntimeId, skill.Id, healed, target.Health));
                }
            }

            ApplySkillStatus(effect, actor, skill, target);

            effect.HealthAfter = target.Health;

            if (wasAlive && !target.IsAlive)
            {
                effect.Died = true;
                Publish(new BattleUnitDiedEvent(target.RuntimeId, target.Side, skill.Id));
                GameLog.Info(
                    LogChannel.Battle,
                    $"#{target.RuntimeId} {target.DefinitionId} 被 {actor.DefinitionId} 的 {skill.Id} 击倒。",
                    target.DefinitionId);
            }
        }

        private void ApplyDamageHits(BattleUnitEffect effect, BattleUnit actor, SkillDefinition skill, BattleUnit target)
        {
            var hits = Mathf.Max(1, skill.HitCount);
            for (var i = 0; i < hits; i++)
            {
                if (!target.IsAlive)
                {
                    break;
                }

                // 逐段掷骰，顺序是「先按目标、后按段数」，因此同种子必然重放出同一串数字。
                var roll = _stream.NextFloat();
                var damageResult = DamageCalculator.Compute(
                    _config,
                    new DamageInput(
                        skill.Power,
                        actor.EffectiveAttack,
                        target.EffectiveDefense,
                        skill.Element,
                        target.Element,
                        target.IsBroken,
                        actor.AttackModifier,
                        target.DefenseModifier,
                        target.IncomingDamageMultiplier,
                        hitIndex: i,
                        defenderMaxHealth: target.MaxHealth,
                        criticalRoll: roll));

                var applied = target.ApplyDamage(damageResult.Damage);
                effect.Damage += applied;
                effect.LandedHits++;
                if (damageResult.IsCritical)
                {
                    effect.CriticalHits++;
                }

                if (i == 0)
                {
                    effect.ElementRelation = damageResult.ElementRelation;
                }

                Publish(new BattleDamagedEvent(
                    actor.RuntimeId,
                    target.RuntimeId,
                    target.Side,
                    skill.Id,
                    applied,
                    target.Health,
                    damageResult.IsCritical,
                    damageResult.ElementRelation));
            }
        }

        private void ApplyBreakDamage(BattleUnitEffect effect, BattleUnit actor, SkillDefinition skill, BattleUnit target)
        {
            // 一次行动对同一目标只削一次护体：段数在 ComputeBreakDamage 内部已经乘进去了，
            // 逐段再削一次等于把多段技能的破防效率平方。
            var breakDamage = DamageCalculator.ComputeBreakDamage(
                skill.BreakDamage, skill.HitCount, target.BreakDamageMultiplier);

            if (breakDamage <= 0 || !target.IsAlive)
            {
                effect.BreakValueAfter = target.BreakValue;
                return;
            }

            var before = target.BreakValue;
            var entered = target.ApplyBreakDamage(breakDamage);
            effect.BreakDamage = Math.Max(0, before - target.BreakValue);
            effect.BreakValueAfter = target.BreakValue;

            if (!entered)
            {
                return;
            }

            target.EnterBroken(_config.BrokenDurationTurns);
            effect.EnteredBroken = true;
            Publish(new BattleBrokenEvent(target.RuntimeId, target.Side, _config.BrokenDurationTurns));
            GameLog.Info(
                LogChannel.Battle,
                $"#{target.RuntimeId} {target.DefinitionId} 护体被打空，进入破防 {_config.BrokenDurationTurns} 回合。",
                target.DefinitionId);
        }

        private void ApplySkillStatus(BattleUnitEffect effect, BattleUnit actor, SkillDefinition skill, BattleUnit target)
        {
            if (string.IsNullOrEmpty(skill.AppliedStatusId) || !target.IsAlive)
            {
                return;
            }

            if (!_registry.TryGet(skill.AppliedStatusId, out StatusDefinition status) || status == null)
            {
                GameLog.Warn(
                    LogChannel.Battle,
                    $"技能 {skill.Id} 引用了不存在的状态 '{skill.AppliedStatusId}'，已跳过。",
                    skill.Id);
                return;
            }

            if (!_stream.Chance(skill.StatusChance))
            {
                effect.StatusRollFailed = true;
                return;
            }

            var change = target.ApplyStatus(status);
            var applied = target.FindStatus(status.Id);
            effect.AppliedStatusId = status.Id;
            effect.StatusChange = change;

            Publish(new BattleStatusAppliedEvent(
                target.RuntimeId,
                status.Id,
                change,
                applied?.Stacks ?? 1,
                applied?.RemainingTurns ?? status.DurationTurns));
        }

        /// <summary>随机目标：从「战斗」流里抽一个存活敌人。</summary>
        private BattleUnit DrawRandomOpponent(BattleUnit actor)
        {
            _randomCandidateScratch.Clear();
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (unit.IsAlive && unit.Side != actor.Side)
                {
                    _randomCandidateScratch.Add(unit);
                }
            }

            return _randomCandidateScratch.Count == 0 ? null : _stream.Pick(_randomCandidateScratch);
        }

        private readonly List<BattleUnit> _randomCandidateScratch = new List<BattleUnit>(FormationSlot.Capacity);

        private void FinishTurn()
        {
            var actor = CurrentActor;
            if (actor == null)
            {
                Phase = TurnPhase.Finished;
                return;
            }

            actor.TickCooldowns();

            var healthDelta = actor.TickStatuses(_expiredScratch);
            for (var i = 0; i < _expiredScratch.Count; i++)
            {
                Publish(new BattleStatusExpiredEvent(actor.RuntimeId, _expiredScratch[i].StatusId));
            }

            _expiredScratch.Clear();

            if (healthDelta != 0)
            {
                GameLog.Debug(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 状态结算 {(healthDelta > 0 ? "+" : string.Empty)}{healthDelta} 生命。",
                    actor.DefinitionId);
            }

            // 经文的每回合效果：回灵与苦修失血。与状态结算同属「自己的回合收尾」那一处，
            // 于是「每回合」的口径在整个内核里只有一个：走完自己一手。
            var upkeepHealthDelta = ApplySutraUpkeep(actor);

            if (actor.TickBrokenTurn())
            {
                Publish(new BattleBreakRecoveredEvent(actor.RuntimeId, actor.Side, actor.BreakValue));
                GameLog.Debug(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 破防结束，护体值重置为 {actor.BreakValue}。",
                    actor.DefinitionId);
            }

            // 只有「被自己身上的伤害打死」才在这里播报死亡——状态持续伤害与苦修都算这一路；
            // 被技能打死的那一次已经在 ApplySkillToTarget 里播过了，避免同一场死亡报两次。
            if (healthDelta + upkeepHealthDelta < 0 && !actor.IsAlive)
            {
                Publish(new BattleUnitDiedEvent(actor.RuntimeId, actor.Side, null));
            }

            actor.TurnsTaken++;
            _queue.Advance(actor);
            Publish(new BattleTurnEndedEvent(actor.RuntimeId, actor.Side, actor.ActionValue));

            _mainActionUsed = false;
            _moveUsed = false;
            _plansDirty = true;
            Phase = TurnPhase.Finished;

            ResolveOutcome();
        }

        /// <summary>
        /// 结算经文带来的每回合效果：先回灵，再付苦修的生命代价。返回生命净变化（0 或负数）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 时机是<b>自己的回合收尾</b>，与状态持续伤害、破防计时同挂一处。ATB 行动值制下速度高的单位
        /// 一场仗里走的手数更多，因此「每回合」= 「走完自己一手」——速度买来的不只是出手次数。
        /// 被控跳过的那一手同样结算（<see cref="BeginNextTurn"/> 也会走到这里），与状态、冷却的既有口径一致。
        /// </para>
        /// <para>
        /// <b>苦修不致死</b>：生命代价最多扣到剩 1 点。代价类经文的设计意图是「扣血换增益」的交易，
        /// 不是自杀机制——真让它把人扣死，玩家会看到「因为带了一本经文而在自己回合结束时倒下」，
        /// 而且这一手会被算进我方全灭。这条是刻意的保守口径，放开之前要先拍板。
        /// </para>
        /// <para>
        /// 回灵只对我方有意义：<see cref="BattleUnit.UsesSpirit"/> 为假时回灵恒为 0，而敌人身上本来
        /// 也不会带经文（见 <c>BattleFactory.CreateUnit</c>）。
        /// </para>
        /// </remarks>
        private int ApplySutraUpkeep(BattleUnit actor)
        {
            if (actor.SpiritRegenPerTurn > 0)
            {
                var restored = actor.RestoreSpirit(actor.SpiritRegenPerTurn);
                if (restored > 0)
                {
                    GameLog.Debug(
                        LogChannel.Battle,
                        $"#{actor.RuntimeId} {actor.DefinitionId} 经文回灵 +{restored}（当前 {actor.Spirit}/{actor.MaxSpirit}）。",
                        actor.DefinitionId);
                }
            }

            if (actor.HealthCostPerTurn <= 0 || !actor.IsAlive)
            {
                return 0;
            }

            // 留 1 点生命：苦修可以把自己逼到濒死，但收人头这件事得留给对手。
            var cost = Mathf.Min(actor.HealthCostPerTurn, actor.Health - 1);
            if (cost <= 0)
            {
                return 0;
            }

            var applied = actor.ApplyDamage(cost);
            GameLog.Debug(
                LogChannel.Battle,
                $"#{actor.RuntimeId} {actor.DefinitionId} 苦修失血 -{applied}（当前 {actor.Health}/{actor.MaxHealth}）。",
                actor.DefinitionId);

            return -applied;
        }

        private void ResolveOutcome()
        {
            if (Outcome != BattleOutcome.Ongoing)
            {
                return;
            }

            var resolved = BattleOutcome.Ongoing;
            if (AliveCount(BattleSide.Enemy) == 0)
            {
                resolved = BattleOutcome.PlayerVictory;
            }

            // 双方同归于尽时按我方失败结算：你方无人站着就是失败。
            if (AliveCount(BattleSide.Player) == 0)
            {
                resolved = BattleOutcome.PlayerDefeat;
            }

            if (resolved == BattleOutcome.Ongoing)
            {
                return;
            }

            EndBattle(resolved);
        }

        /// <summary>
        /// 写下结局并收尾。<b>唯一的结局出口</b>：全灭与逃跑都走这里，
        /// 于是「结果被改掉」与「战斗结束事件被发出」不可能只发生一半。
        /// </summary>
        private void EndBattle(BattleOutcome outcome)
        {
            Outcome = outcome;
            Publish(new BattleEndedEvent(outcome, RoundNumber, ActionCount));
            GameLog.Info(
                LogChannel.Battle,
                $"战斗结束：{outcome}，第 {RoundNumber} 回合、累计 {ActionCount} 手。",
                _setup.EncounterId);
        }

        private BattleUnit FindUnitAtSlot(BattleSide side, FormationSlot slot)
        {
            var list = side == BattleSide.Player ? _players : _enemies;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].IsAlive && list[i].Slot == slot)
                {
                    return list[i];
                }
            }

            return null;
        }

        private static string FirstPreventingStatusId(BattleUnit actor)
        {
            var statuses = actor.Statuses;
            for (var i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Definition.PreventsAction)
                {
                    return statuses[i].StatusId;
                }
            }

            return null;
        }

        private BattlePlan GetPlan(BattleUnit actor)
        {
            if (actor == null)
            {
                return BattlePlan.Empty(actor);
            }

            if (_plansDirty)
            {
                EnsurePlans();
            }

            return _plans.TryGetValue(actor.RuntimeId, out var plan) ? plan : BattlePlan.Empty(actor);
        }

        /// <summary>
        /// 重算全部存活单位的意图。战斗里任何一次状态变化都会把它标脏，
        /// 于是玩家看到的预告永远是「此刻的最优解」，而不是上一手留下的过期计划。
        /// </summary>
        private void EnsurePlans()
        {
            _plans.Clear();
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (!unit.IsAlive)
                {
                    continue;
                }

                _plans[unit.RuntimeId] = BattleActionPlanner.Plan(
                    _config, _registry, unit, _units, _targetScratch, _primaryScratch);
            }

            _plansDirty = false;
        }

        private void Publish<T>(T gameEvent) where T : IGameEvent =>
            _eventBus?.Publish(BattleEventChannel.Channel, gameEvent);
    }
}
