using System;
using System.Collections.Generic;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 战斗界面能发起的指令类别。
    /// </summary>
    /// <remarks>
    /// 数值只增不改：它会进入界面状态机与录屏脚本。
    /// 任务书里的联合技还没有口径，因此不占位（同 <see cref="BattleActionKind"/> 的注释）；
    /// 防御已经拍板并落进内核（<see cref="BattleSession.TryDefend"/>），因此有按钮；
    /// 道具同样已落进内核（<see cref="BattleSession.UseItem"/>）。
    /// </remarks>
    public enum BattleCommandId
    {
        /// <summary>使用某个技能。普攻也是技能，走同一条路径。</summary>
        Skill = 0,

        /// <summary>逃跑，整队撤退（主行动）。</summary>
        Flee = 1,

        /// <summary>主动结束回合，放弃剩余行动。</summary>
        EndTurn = 2,

        /// <summary>防御，给自己挂上减伤状态（主行动）。</summary>
        Defend = 3,

        /// <summary>移动到本方的空格子（不占主行动，每回合一次）。</summary>
        Move = 4,

        /// <summary>与同阵营同伴换位（不占主行动，每回合一次）。</summary>
        Swap = 5,

        /// <summary>用一件道具（主行动）。一件道具一个按钮，与「一个技能一个按钮」同画法。</summary>
        Item = 6,
    }

    /// <summary>
    /// 一条可以直接画成按钮的指令。
    /// </summary>
    /// <remarks>
    /// 它是<b>只读快照</b>：内核会不会真的接受，最终仍由 <see cref="BattleSession"/> 说了算。
    /// 这里的 <see cref="Enabled"/> 只是「把必然被拒的按钮先置灰」，
    /// 拒绝理由用 <see cref="BattleCommandRejection"/> 枚举，界面自己查文本键。
    /// </remarks>
    public readonly struct BattleCommandOption
    {
        public BattleCommandOption(
            BattleCommandId id,
            string labelKey,
            string skillId,
            int spiritCost,
            bool enabled,
            BattleCommandRejection rejection)
            : this(id, labelKey, skillId, spiritCost, enabled, rejection, null, 0)
        {
        }

        /// <summary>道具按钮：文案是道具自己的名字，另外带上背包里还剩几个。</summary>
        public BattleCommandOption(BattleCommandId id, string labelKey, string itemId, int itemCount)
            : this(id, labelKey, null, 0, true, BattleCommandRejection.None, itemId, itemCount)
        {
        }

        private BattleCommandOption(
            BattleCommandId id,
            string labelKey,
            string skillId,
            int spiritCost,
            bool enabled,
            BattleCommandRejection rejection,
            string itemId,
            int itemCount)
        {
            Id = id;
            LabelKey = labelKey;
            SkillId = skillId;
            SpiritCost = spiritCost;
            Enabled = enabled;
            Rejection = rejection;
            ItemId = itemId;
            ItemCount = itemCount;
        }

        public BattleCommandId Id { get; }

        /// <summary>按钮文案的文本键。界面不做文案拼接，也不允许有中文兜底。</summary>
        public string LabelKey { get; }

        /// <summary>仅 <see cref="BattleCommandId.Skill"/> 非空。</summary>
        public string SkillId { get; }

        /// <summary>灵力消耗，仅技能有意义；非灵力单位恒为 0。</summary>
        public int SpiritCost { get; }

        /// <summary>仅 <see cref="BattleCommandId.Item"/> 非空。</summary>
        public string ItemId { get; }

        /// <summary>
        /// 背包里还剩几个；仅 <see cref="BattleCommandId.Item"/> 有意义。
        /// 道具按钮只画还有存货的那几件，因此这里恒大于 0。
        /// </summary>
        public int ItemCount { get; }

        public bool Enabled { get; }

        /// <summary>为 <see cref="BattleCommandRejection.None"/> 表示可用，否则是按钮置灰的原因。</summary>
        public BattleCommandRejection Rejection { get; }

        public override string ToString() =>
            $"{Id}:{LabelKey}({Rejection}{(ItemId == null ? string.Empty : $",{ItemId}×{ItemCount}")})";
    }

    /// <summary>状态图标的一枚。堆叠数、剩余回合与是否常驻一起给，界面不再回头找定义。</summary>
    public readonly struct BattleStatusChip
    {
        public BattleStatusChip(
            string statusId,
            string nameKey,
            int stacks,
            int remainingTurns,
            bool isDebuff,
            bool isPermanent = false)
        {
            StatusId = statusId;
            NameKey = nameKey;
            Stacks = stacks;
            RemainingTurns = remainingTurns;
            IsDebuff = isDebuff;
            IsPermanent = isPermanent;
        }

        public string StatusId { get; }

        public string NameKey { get; }

        public int Stacks { get; }

        /// <summary>
        /// 剩余回合。<see cref="IsPermanent"/> 为真时这个数没有意义——常驻状态不会被递减，
        /// 界面该显示的是「常驻」而不是一个到期日。
        /// </summary>
        public int RemainingTurns { get; }

        /// <summary>减益还是增益，界面据此决定配色。</summary>
        public bool IsDebuff { get; }

        /// <summary>常驻：被动挂上来的那类状态，不会自行到期，界面不画剩余回合数。</summary>
        public bool IsPermanent { get; }

        /// <summary>
        /// 常驻状态在诊断串里用一个标记代替剩余回合数：界面脚本禁止中文字面量，
        /// 而「不画到期日」这件事需要一个稳定的、可断言的写法。
        /// </summary>
        public override string ToString() =>
            IsPermanent ? $"{StatusId}x{Stacks}(permanent)" : $"{StatusId}x{Stacks}({RemainingTurns})";
    }

    /// <summary>
    /// 一个单位在界面上的那一行。
    /// </summary>
    /// <remarks>
    /// 刻意做成<b>可变对象、按单位复用</b>：一场战斗里单位的条数固定，刷新只是把新数值写回同一个对象，
    /// 界面绑定一次就够了。每次刷新都 new 一批行会让 uGUI 反复重建、也会把选中的目标弄丢。
    /// </remarks>
    public sealed class BattleUnitRow
    {
        internal readonly List<BattleStatusChip> StatusScratch = new List<BattleStatusChip>(4);

        internal BattleUnitRow()
        {
        }

        public int RuntimeId { get; internal set; }

        public BattleSide Side { get; internal set; }

        public FormationSlot Slot { get; internal set; }

        public string DefinitionId { get; internal set; }

        /// <summary>名字文本键。</summary>
        public string NameKey { get; internal set; }

        public int Health { get; internal set; }

        public int MaxHealth { get; internal set; }

        public int BreakValue { get; internal set; }

        public int BreakThreshold { get; internal set; }

        public bool IsBroken { get; internal set; }

        public bool IsAlive { get; internal set; }

        /// <summary>此刻是否轮到它。</summary>
        public bool IsCurrentActor { get; internal set; }

        /// <summary>被「剥夺行动」的状态控住，这个回合会跳过。</summary>
        public bool IsActionPrevented { get; internal set; }

        public bool UsesSpirit { get; internal set; }

        public int Spirit { get; internal set; }

        public int MaxSpirit { get; internal set; }

        public float HealthRatio => MaxHealth <= 0 ? 0f : (float)Health / MaxHealth;

        /// <summary>护体条比例，0–1。破防阈值取 0 时（不吃护体的单位）恒为 0。</summary>
        public float BreakRatio =>
            BreakThreshold <= 0 ? 0f : Mathf.Clamp01((float)BreakValue / BreakThreshold);

        public IReadOnlyList<BattleStatusChip> Statuses => StatusScratch;

        public override string ToString() => $"#{RuntimeId} {DefinitionId} {Health}/{MaxHealth}";
    }

    /// <summary>
    /// 一个敌人头顶的意图预告。
    /// </summary>
    /// <remarks>
    /// 同样按单位复用。<see cref="HasIntent"/> 为假时界面不该画预告图标——
    /// 那时它要么已经倒下，要么不在预览窗口里。
    /// </remarks>
    public sealed class BattleIntentRow
    {
        internal BattleIntentRow()
        {
        }

        public int ActorRuntimeId { get; internal set; }

        public string SkillId { get; internal set; }

        /// <summary>技能名文本键。</summary>
        public string SkillNameKey { get; internal set; }

        /// <summary>真：目标尚未确定（随机单体之类），界面不要画指向某人的箭头。</summary>
        public bool TargetIsRandom { get; internal set; }

        public bool HasIntent { get; internal set; }

        public IReadOnlyList<int> TargetRuntimeIds { get; internal set; } = Array.Empty<int>();

        public override string ToString() => $"#{ActorRuntimeId} {SkillId}";
    }

    /// <summary>
    /// 战斗界面的一份可绑定快照。
    /// </summary>
    /// <remarks>
    /// <para>它<b>不</b>持有 MonoBehaviour，也<b>不</b>碰 uGUI：只做「把 <see cref="BattleSession"/>
    /// 翻译成界面能直接画的字段」这一件事。这样它可以在 EditMode 里被逐条锁死，
    /// 而不必先搭一个场景。</para>
    /// <para><b>不消费随机数</b>：读意图走 <see cref="BattleSession.GetIntent"/>，规划器是纯函数，
    /// 因此界面想刷几次就刷几次，战局不会跟着跑偏。</para>
    /// <para>刷新语义：<see cref="Refresh"/> 会把新数值写回<b>同一批</b>行对象，列表身份不变。</para>
    /// </remarks>
    public sealed class BattleHudModel
    {
        private readonly BattleSession _session;
        private readonly IDefinitionRegistry _registry;
        private readonly List<BattleUnitRow> _rows;
        private readonly List<BattleUnitRow> _playerRows = new List<BattleUnitRow>(FormationSlot.Capacity);
        private readonly List<BattleUnitRow> _enemyRows = new List<BattleUnitRow>(FormationSlot.Capacity);
        private readonly List<BattleCommandOption> _commands = new List<BattleCommandOption>(8);
        private readonly List<FormationSlot> _moveCandidates = new List<FormationSlot>(FormationSlot.Capacity);
        private readonly List<int> _swapCandidates = new List<int>(FormationSlot.Capacity);
        private readonly List<ItemDefinition> _itemScratch = new List<ItemDefinition>(8);
        private readonly List<BattleIntentRow> _intentRows = new List<BattleIntentRow>(FormationSlot.Capacity);
        private readonly Dictionary<int, BattleIntentRow> _intentPool = new Dictionary<int, BattleIntentRow>(FormationSlot.Capacity);

        public BattleHudModel(BattleSession session, IDefinitionRegistry registry)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            var units = _session.Units;
            _rows = new List<BattleUnitRow>(units.Count);
            for (var i = 0; i < units.Count; i++)
            {
                _rows.Add(new BattleUnitRow());
            }

            Refresh();
        }

        public BattleOutcome Outcome => _session.Outcome;

        public bool IsFinished => _session.IsFinished;

        public TurnPhase Phase => _session.Phase;

        public int RoundNumber => _session.RoundNumber;

        public int ActionCount => _session.ActionCount;

        /// <summary>此刻发起逃跑的成功率，0–1。界面照抄，不要自己再算一遍。</summary>
        public float EscapeChance => _session.EscapeChance;

        /// <summary>此刻待行动的单位的编号；没有时为 -1。</summary>
        public int CurrentActorRuntimeId => _session.CurrentActor == null ? -1 : _session.CurrentActor.RuntimeId;

        /// <summary>我方单位行，顺序与建队一致。</summary>
        public IReadOnlyList<BattleUnitRow> PlayerRows => _playerRows;

        /// <summary>敌方单位行，顺序与建队一致。</summary>
        public IReadOnlyList<BattleUnitRow> EnemyRows => _enemyRows;

        /// <summary>场上全部单位行，我方在前。</summary>
        public IReadOnlyList<BattleUnitRow> Rows => _rows;

        /// <summary>
        /// 当前待行动的我方单位能发起的指令；轮不到我方时为空。
        /// 主行动用掉之后仍然非空——那时给的是「移动／换位／结束回合」，不是空列表。
        /// </summary>
        public IReadOnlyList<BattleCommandOption> Commands => _commands;

        /// <summary>可落脚的格子（本方空格），按阵型序号升序；不能移动时为空。</summary>
        public IReadOnlyList<FormationSlot> MoveCandidates => _moveCandidates;

        /// <summary>可换位的同伴编号，升序；不能换位时为空。</summary>
        public IReadOnlyList<int> SwapCandidates => _swapCandidates;

        /// <summary>场上敌人的意图预告，只含有意图的那些。</summary>
        public IReadOnlyList<BattleIntentRow> IntentRows => _intentRows;

        /// <summary>按单位编号取意图行；没有则返回 null。</summary>
        public BattleIntentRow FindIntent(int runtimeId) =>
            _intentPool.TryGetValue(runtimeId, out var row) ? row : null;

        /// <summary>按单位编号取单位行；没有则返回 null。</summary>
        public BattleUnitRow FindRow(int runtimeId)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].RuntimeId == runtimeId)
                {
                    return _rows[i];
                }
            }

            return null;
        }

        /// <summary>把 <see cref="BattleSession"/> 的当前状态写回快照。每一手结算后调用一次即可。</summary>
        public void Refresh()
        {
            RefreshUnits();
            RefreshIntents();
            RefreshMoveOptions();
            RefreshCommands();
        }

        private void RefreshUnits()
        {
            var units = _session.Units;
            var current = _session.CurrentActor;

            _playerRows.Clear();
            _enemyRows.Clear();

            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                var row = _rows[i];

                row.RuntimeId = unit.RuntimeId;
                row.Side = unit.Side;
                row.Slot = unit.Slot;
                row.DefinitionId = unit.DefinitionId;
                row.NameKey = unit.DisplayNameKey;
                row.Health = unit.Health;
                row.MaxHealth = unit.MaxHealth;
                row.BreakValue = unit.BreakValue;
                row.BreakThreshold = unit.BreakThreshold;
                row.IsBroken = unit.IsBroken;
                row.IsAlive = unit.IsAlive;
                row.IsCurrentActor = ReferenceEquals(unit, current);
                row.IsActionPrevented = unit.IsActionPrevented;
                row.UsesSpirit = unit.UsesSpirit;
                row.Spirit = unit.Spirit;
                row.MaxSpirit = unit.MaxSpirit;

                var chips = row.StatusScratch;
                chips.Clear();
                var statuses = unit.Statuses;
                for (var s = 0; s < statuses.Count; s++)
                {
                    var status = statuses[s];
                    chips.Add(new BattleStatusChip(
                        status.StatusId,
                        status.DisplayNameKey,
                        status.Stacks,
                        status.RemainingTurns,
                        status.IsDebuff,
                        status.IsPermanent));
                }

                if (unit.Side == BattleSide.Player)
                {
                    _playerRows.Add(row);
                }
                else
                {
                    _enemyRows.Add(row);
                }
            }
        }

        private void RefreshIntents()
        {
            _intentRows.Clear();

            if (_session.IsFinished)
            {
                return;
            }

            for (var i = 0; i < _enemyRows.Count; i++)
            {
                var enemy = _enemyRows[i];
                var intent = _session.GetIntent(enemy.RuntimeId);
                if (!intent.HasIntent)
                {
                    continue;
                }

                if (!_intentPool.TryGetValue(enemy.RuntimeId, out var row))
                {
                    row = new BattleIntentRow { ActorRuntimeId = enemy.RuntimeId };
                    _intentPool.Add(enemy.RuntimeId, row);
                }

                row.SkillId = intent.SkillId;
                row.SkillNameKey = intent.SkillDisplayNameKey;
                row.TargetIsRandom = intent.TargetIsRandom;
                row.HasIntent = true;
                row.TargetRuntimeIds = intent.TargetRuntimeIds;

                _intentRows.Add(row);
            }
        }

        /// <summary>
        /// 算一遍「能挪到哪、能跟谁换」。
        /// </summary>
        /// <remarks>
        /// 候选一律向内核要（<see cref="BattleSession.CollectMoveDestinations"/> 与
        /// <see cref="BattleSession.CollectSwapPartners"/>），界面重算一遍就会出现两套判据。
        /// </remarks>
        private void RefreshMoveOptions()
        {
            _moveCandidates.Clear();
            _swapCandidates.Clear();

            if (_session.IsFinished || !_session.CanMoveOrSwap)
            {
                return;
            }

            _session.CollectMoveDestinations(_moveCandidates);
            _session.CollectSwapPartners(_swapCandidates);
        }

        private void RefreshCommands()
        {
            _commands.Clear();

            var actor = _session.CurrentActor;
            if (_session.IsFinished || actor == null || actor.Side != BattleSide.Player)
            {
                return;
            }

            // 主行动还没用掉：技能、防御、逃跑都是「打出去」的那一手。
            // 打完其中任何一手，内核就进 MoveOrSwap，这里随之换成下面那一批按钮。
            if (_session.Phase == TurnPhase.MainAction)
            {
                AppendSkillCommands(actor);

                // 道具与技能同属「打出去的那一手」，排在技能后面、防御前面：
                // 它同样占掉主行动，因此一回合只可能出现一次。
                AppendItemCommands();

                // 防御与逃跑是「不打」的那两手，排在技能后面：代价都落在占掉主行动上。
                // 防御永远可用（不吃灵力、不进冷却），内核只在相位不对时才拒。
                _commands.Add(new BattleCommandOption(
                    BattleCommandId.Defend,
                    SamsaraWest.Localization.LocalizationKeys.UI_BATTLE_COMMAND_DEFEND,
                    string.Empty,
                    0,
                    true,
                    BattleCommandRejection.None));

                _commands.Add(new BattleCommandOption(
                    BattleCommandId.Flee,
                    SamsaraWest.Localization.LocalizationKeys.UI_BATTLE_COMMAND_FLEE,
                    string.Empty,
                    0,
                    true,
                    BattleCommandRejection.None));
            }

            // 移动与换位不占主行动，因此主行动前后都可能给：前面可以先挪一步再出招，
            // 后面还剩那一次。候选为空就干脆不给按钮——没地方可去，点了也是白点。
            if (_session.CanMoveOrSwap)
            {
                if (_moveCandidates.Count > 0)
                {
                    _commands.Add(new BattleCommandOption(
                        BattleCommandId.Move,
                        SamsaraWest.Localization.LocalizationKeys.UI_BATTLE_COMMAND_MOVE,
                        string.Empty,
                        0,
                        true,
                        BattleCommandRejection.None));
                }

                if (_swapCandidates.Count > 0)
                {
                    _commands.Add(new BattleCommandOption(
                        BattleCommandId.Swap,
                        SamsaraWest.Localization.LocalizationKeys.UI_BATTLE_COMMAND_SWAP,
                        string.Empty,
                        0,
                        true,
                        BattleCommandRejection.None));
                }
            }

            // 结束回合两个相位都得给：主行动前是「这一手不打」，移动窗口里是「不挪了」。
            // 少一个，玩家就会停在「有阶段、没按钮」的空转里。
            _commands.Add(new BattleCommandOption(
                BattleCommandId.EndTurn,
                SamsaraWest.Localization.LocalizationKeys.UI_BATTLE_COMMAND_ENDTURN,
                string.Empty,
                0,
                true,
                BattleCommandRejection.None));
        }

        /// <summary>把当前单位会的技能逐个翻成按钮，置灰原因按内核的判定顺序写。</summary>
        private void AppendSkillCommands(BattleUnit actor)
        {
            var skillIds = actor.SkillIds;
            for (var i = 0; i < skillIds.Count; i++)
            {
                var skillId = skillIds[i];

                var labelKey = skillId;
                var spiritCost = 0;
                if (_registry.TryGet(skillId, out SkillDefinition skill))
                {
                    labelKey = skill.DisplayNameKey;
                    spiritCost = skill.SpiritCost;
                }

                // 拒绝理由的先后与 BattleSession.UseSkill 的判定顺序一致，
                // 免得界面把「灵力不够」标在「还在冷却」的技能上。
                var rejection = BattleCommandRejection.None;
                if (!actor.IsSkillReady(skillId))
                {
                    rejection = BattleCommandRejection.SkillOnCooldown;
                }
                else if (actor.UsesSpirit && actor.Spirit < spiritCost)
                {
                    rejection = BattleCommandRejection.NotEnoughSpirit;
                }

                _commands.Add(new BattleCommandOption(
                    BattleCommandId.Skill,
                    labelKey,
                    skillId,
                    spiritCost,
                    rejection == BattleCommandRejection.None,
                    rejection));
            }
        }

        /// <summary>
        /// 把背包里<b>还有存货</b>的、战斗内可用的道具逐个翻成按钮。
        /// </summary>
        /// <remarks>
        /// <para>只画还有存货的那几件：与「没地方可去就不给移动按钮」同一条口径——
        /// 点了必然被拒的按钮不该占着位置。没接背包（<see cref="BattleSetup.Inventory"/>
        /// 为 null）时一件都不画。</para>
        /// <para>文案用道具自己的 <see cref="ItemDefinition.DisplayNameKey"/>：
        /// 一件道具一个按钮，与「一个技能一个按钮」是同一套画法，
        /// 因此不需要给「道具」再单开一个二级菜单。</para>
        /// <para>永远可用：道具不吃灵力、不进冷却，目标又恒有（至少能对自己用），
        /// 因此没有「必然被拒」的余地，<see cref="BattleCommandOption.Rejection"/> 恒为
        /// <see cref="BattleCommandRejection.None"/>。</para>
        /// <para>顺序按道具 ID 升序，不跟着数据目录的加载顺序走——
        /// 按钮顺序要是随加载顺序漂，录屏脚本与截图对不上。</para>
        /// </remarks>
        private void AppendItemCommands()
        {
            var inventory = _session.Inventory;
            if (inventory == null)
            {
                return;
            }

            _itemScratch.Clear();
            foreach (var item in _registry.OfKind<ItemDefinition>())
            {
                if (item == null || string.IsNullOrEmpty(item.Id) || !item.UsableInBattle)
                {
                    continue;
                }

                if (inventory.CountOf(item.Id) <= 0)
                {
                    continue;
                }

                _itemScratch.Add(item);
            }

            _itemScratch.Sort(CompareItemsById);

            for (var i = 0; i < _itemScratch.Count; i++)
            {
                var item = _itemScratch[i];
                _commands.Add(new BattleCommandOption(
                    BattleCommandId.Item,
                    item.DisplayNameKey,
                    item.Id,
                    inventory.CountOf(item.Id)));
            }
        }

        private static int CompareItemsById(ItemDefinition left, ItemDefinition right) =>
            string.CompareOrdinal(left.Id, right.Id);
    }
}
