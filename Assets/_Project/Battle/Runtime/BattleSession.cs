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
        private readonly List<BattleStatusInstance> _expiredScratch = new List<BattleStatusInstance>(4);
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

            for (var i = 0; i < _units.Count; i++)
            {
                _units[i].PrepareForBattle();
            }

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
        /// 把战斗一路推到底：敌方自动出手，我方由规划器代打。
        /// </summary>
        /// <remarks>
        /// 它是为测试与「不接界面也能看规则」的原型观察而存在的，
        /// 不是玩法的一部分——真正的战斗必须由玩家的指令驱动。
        /// </remarks>
        /// <param name="maxActions">安全上限，防止规则出 bug 时死循环。</param>
        /// <returns>实际结算的行动手数。</returns>
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

        // ---------------------------------------------------------------- 内部实现

        private void BuildSide(BattleSide side, IReadOnlyList<BattleUnitBlueprint> blueprints, List<BattleUnit> sink)
        {
            for (var i = 0; i < blueprints.Count; i++)
            {
                // 编号与布阵序号都按「我方在前、敌方在后」连续分配：
                // 行动值同值时靠布阵序号破平局，于是同速时我方先动，
                // 与 Docs/战斗数值-v1.md 里那一串校算顺序一致。
                var runtimeId = _units.Count;
                var unit = BattleFactory.CreateUnit(_registry, side, runtimeId, runtimeId, blueprints[i]);
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

            if (actor.TickBrokenTurn())
            {
                Publish(new BattleBreakRecoveredEvent(actor.RuntimeId, actor.Side, actor.BreakValue));
                GameLog.Debug(
                    LogChannel.Battle,
                    $"#{actor.RuntimeId} {actor.DefinitionId} 破防结束，护体值重置为 {actor.BreakValue}。",
                    actor.DefinitionId);
            }

            // 只有「被自己身上的持续伤害打死」才在这里播报死亡；
            // 被技能打死的那一次已经在 ApplySkillToTarget 里播过了，避免同一场死亡报两次。
            if (healthDelta < 0 && !actor.IsAlive)
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

            Outcome = resolved;
            Publish(new BattleEndedEvent(resolved, RoundNumber, ActionCount));
            GameLog.Info(
                LogChannel.Battle,
                $"战斗结束：{resolved}，第 {RoundNumber} 回合、累计 {ActionCount} 手。",
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
                    _config, _registry, unit, _units, _targetScratch);
            }

            _plansDirty = false;
        }

        private void Publish<T>(T gameEvent) where T : IGameEvent =>
            _eventBus?.Publish(BattleEventChannel.Channel, gameEvent);
    }
}
