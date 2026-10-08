using System;
using System.Collections.Generic;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.UI
{
    /// <summary>战斗界面此刻在等什么。</summary>
    /// <remarks>
    /// 界面<b>只</b>认这个状态来决定画什么：它不必知道 ATB、也不必知道敌方的回合是在
    /// <see cref="BattleSession.BeginNextTurn"/> 里走完的。
    /// </remarks>
    public enum BattlePrompt
    {
        /// <summary>还没开始。调用 <see cref="BattleScreenController.Start"/> 进入循环。</summary>
        NotStarted = 0,

        /// <summary>轮到我方下主行动指令。</summary>
        PlayerCommand = 1,

        /// <summary>技能已经选中，等我方点一个目标。</summary>
        PlayerTarget = 2,

        /// <summary>打完了，读 <see cref="BattleScreenController.Session"/> 的结局。</summary>
        BattleEnded = 3,

        /// <summary>循环没能推进（理论上不该发生）。这是一个必须被看见的缺陷信号，不是正常态。</summary>
        Stuck = 4,
    }

    /// <summary>
    /// 最小可玩回路的驱动器：把 <see cref="BattleSession"/> 的回合推进收拢成
    /// 「等我方下令 → 下令 → 敌方自己走完 → 再等我方下令 → 结束」这一条循环。
    /// </summary>
    /// <remarks>
    /// <para>它<b>不</b>是 MonoBehaviour，也不碰 uGUI：界面只读 <see cref="Prompt"/> 决定画什么、
    /// 调 <c>Choose*</c> 提交输入。于是整条循环可以在 EditMode 里跑到底，不必先有场景。</para>
    /// <para><b>关于移动／换位的取舍</b>：内核在主行动之后进入 <see cref="TurnPhase.MoveOrSwap"/>，
    /// 那一步是可选的。最小回路里没有换位界面，于是主行动一结算就替玩家结束回合——
    /// 否则会停在「有阶段、没按钮」的空转状态，玩家卡死。等换位界面做出来，
    /// 把这一处替换成「等玩家决定移动或结束」即可，接口不必改。</para>
    /// <para><b>敌方回合不出现在界面上</b>：这是内核的口径（见 <see cref="BattleSession.BeginNextTurn"/>
    /// 的注释），不是这里偷懒。</para>
    /// </remarks>
    public sealed class BattleScreenController
    {
        /// <summary>
        /// 单次推进最多自动走完多少手。它不是玩法限制，而是死循环的保险丝：
        /// 万一出现「我方被永久控住、敌方又打不死人」这种组合，循环必须有人喊停。
        /// </summary>
        public const int MaxAutoStepsPerPump = 4096;

        private readonly BattleSession _session;
        private readonly IDefinitionRegistry _registry;
        private readonly BattleHudModel _hud;
        private readonly List<BattleUnit> _unitScratch = new List<BattleUnit>(BattleSetup.MaxUnitsPerSide * 2);
        private readonly List<int> _candidateIds = new List<int>(BattleSetup.MaxUnitsPerSide);

        public BattleScreenController(BattleSession session, IDefinitionRegistry registry)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _hud = new BattleHudModel(session, registry);
        }

        public BattleSession Session => _session;

        public BattleHudModel Hud => _hud;

        public BattlePrompt Prompt { get; private set; } = BattlePrompt.NotStarted;

        /// <summary>已选中、正在等目标的技能 ID；不在选目标时为空。</summary>
        public string PendingSkillId { get; private set; }

        /// <summary>合法目标的单位编号，顺序为 <see cref="BattleUnit.RuntimeId"/> 升序。</summary>
        public IReadOnlyList<int> TargetCandidateIds => _candidateIds;

        /// <summary>最近一次交给内核的指令的结论；还没下过令时为 null。</summary>
        public BattleActionResult LastResult { get; private set; }

        /// <summary>
        /// 进入循环：一路自动推进到「等我方下令」或「打完了」。重复调用是安全的。
        /// </summary>
        public void Start()
        {
            if (Prompt == BattlePrompt.PlayerCommand || Prompt == BattlePrompt.PlayerTarget)
            {
                return;
            }

            Pump();
        }

        /// <summary>
        /// 选一个技能。需要指定目标的技能会先进入 <see cref="BattlePrompt.PlayerTarget"/>，
        /// 其余（自身、全体）当场结算。
        /// </summary>
        /// <returns>true 表示这次输入被接受（已进目标选择，或已交给内核）。</returns>
        public bool ChooseSkill(string skillId)
        {
            if (Prompt != BattlePrompt.PlayerCommand || string.IsNullOrEmpty(skillId))
            {
                return false;
            }

            var actor = _session.CurrentActor;
            if (actor == null || actor.Side != BattleSide.Player)
            {
                return false;
            }

            if (!_registry.TryGet(skillId, out SkillDefinition skill) || skill == null)
            {
                // 注意：UI 层的字面量一律是 ASCII，中文只走本地化文本键。
                GameLog.Warn(LogChannel.UI, $"Battle screen asked for unknown skill id '{skillId}'.", skillId);
                return false;
            }

            if (BattleTargeting.NeedsCallerTarget(skill.Target))
            {
                FillCandidates(actor, skill);
                if (_candidateIds.Count == 0)
                {
                    // 一个合法目标都没有：别把玩家留在「选不了也退不出」的界面里，
                    // 直接交给内核，让它的拒绝理由成为唯一的事实来源。
                    ApplySkill(skillId, -1);
                    return true;
                }

                PendingSkillId = skillId;
                Prompt = BattlePrompt.PlayerTarget;
                return true;
            }

            ApplySkill(skillId, -1);
            return true;
        }

        /// <summary>在 <see cref="BattlePrompt.PlayerTarget"/> 下点一个目标，交给内核结算。</summary>
        public bool ChooseTarget(int runtimeId)
        {
            if (Prompt != BattlePrompt.PlayerTarget || PendingSkillId == null)
            {
                return false;
            }

            var legal = false;
            for (var i = 0; i < _candidateIds.Count; i++)
            {
                if (_candidateIds[i] == runtimeId)
                {
                    legal = true;
                    break;
                }
            }

            if (!legal)
            {
                return false;
            }

            ApplySkill(PendingSkillId, runtimeId);
            return true;
        }

        /// <summary>放弃选目标，退回指令选择。</summary>
        public bool CancelTargeting()
        {
            if (Prompt != BattlePrompt.PlayerTarget)
            {
                return false;
            }

            PendingSkillId = null;
            _candidateIds.Clear();
            Prompt = BattlePrompt.PlayerCommand;
            return true;
        }

        /// <summary>逃跑（整队），占一次主行动。</summary>
        public bool ChooseFlee()
        {
            if (Prompt != BattlePrompt.PlayerCommand)
            {
                return false;
            }

            LastResult = _session.TryEscape();
            PendingSkillId = null;
            _candidateIds.Clear();

            if (!_session.IsFinished && _session.Phase == TurnPhase.MainAction)
            {
                // 被拒（主行动还在）：原地等我方重新下令。
                _hud.Refresh();
                Prompt = BattlePrompt.PlayerCommand;
                return true;
            }

            AdvanceAfterMainAction();
            return true;
        }

        /// <summary>
        /// 防御（给自己挂减伤状态），占一次主行动。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="ChooseFlee"/> 同形：一次结算、不选目标。
        /// 它与技能一样会消耗主行动，因此结算之后走同一条 <see cref="AdvanceAfterMainAction"/>。
        /// </remarks>
        public bool ChooseDefend()
        {
            if (Prompt != BattlePrompt.PlayerCommand)
            {
                return false;
            }

            LastResult = _session.TryDefend();
            PendingSkillId = null;
            _candidateIds.Clear();

            if (!_session.IsFinished && _session.Phase == TurnPhase.MainAction)
            {
                // 被拒（主行动还在，例如配置指的状态没登记）：原地等我方重新下令。
                _hud.Refresh();
                Prompt = BattlePrompt.PlayerCommand;
                return true;
            }

            AdvanceAfterMainAction();
            return true;
        }

        /// <summary>放弃剩余行动，直接结束当前单位的回合。</summary>
        public bool ChooseEndTurn()
        {
            if (Prompt != BattlePrompt.PlayerCommand)
            {
                return false;
            }

            PendingSkillId = null;
            _candidateIds.Clear();
            _session.EndTurn();
            Pump();
            return true;
        }

        /// <summary>移动到一个空格（不占主行动，每回合一次）。</summary>
        public bool ChooseMove(FormationSlot destination)
        {
            if (Prompt != BattlePrompt.PlayerCommand)
            {
                return false;
            }

            LastResult = _session.MoveTo(destination);
            _hud.Refresh();
            return true;
        }

        /// <summary>与同阵营同伴换位（不占主行动，每回合一次）。</summary>
        public bool ChooseSwap(int allyRuntimeId)
        {
            if (Prompt != BattlePrompt.PlayerCommand)
            {
                return false;
            }

            LastResult = _session.SwapWith(allyRuntimeId);
            _hud.Refresh();
            return true;
        }

        private void ApplySkill(string skillId, int primaryTargetRuntimeId)
        {
            LastResult = _session.UseSkill(skillId, primaryTargetRuntimeId);
            PendingSkillId = null;
            _candidateIds.Clear();

            if (!_session.IsFinished && _session.Phase == TurnPhase.MainAction)
            {
                // 被内核拒了，主行动还在：原地等我方重新下令。
                _hud.Refresh();
                Prompt = BattlePrompt.PlayerCommand;
                return;
            }

            AdvanceAfterMainAction();
        }

        /// <summary>
        /// 主行动已结算：先替玩家收掉可选的移动阶段，再往下推进。
        /// </summary>
        private void AdvanceAfterMainAction()
        {
            if (!_session.IsFinished && _session.Phase == TurnPhase.MoveOrSwap)
            {
                _session.EndTurn();
            }

            Pump();
        }

        private void Pump()
        {
            var steps = 0;

            while (true)
            {
                if (_session.IsFinished)
                {
                    Prompt = BattlePrompt.BattleEnded;
                    _hud.Refresh();
                    return;
                }

                if (steps++ >= MaxAutoStepsPerPump)
                {
                    GameLog.Error(
                        LogChannel.UI,
                        $"Battle loop advanced {MaxAutoStepsPerPump} actions without reaching a player turn; "
                        + "treat as stuck (permanent action prevention, or the action queue stopped advancing).",
                        _session.Setup.EncounterId);
                    Prompt = BattlePrompt.Stuck;
                    _hud.Refresh();
                    return;
                }

                var actor = _session.BeginNextTurn();
                if (actor == null)
                {
                    // 没人能再动了：内核应当已经把结局收敛掉；没有的话就是缺陷。
                    _hud.Refresh();
                    if (_session.IsFinished)
                    {
                        Prompt = BattlePrompt.BattleEnded;
                        return;
                    }

                    GameLog.Error(
                        LogChannel.UI,
                        "Action queue is empty but the battle has not finished; the screen cannot continue.",
                        _session.Setup.EncounterId);
                    Prompt = BattlePrompt.Stuck;
                    return;
                }

                if (actor.Side == BattleSide.Player && _session.Phase == TurnPhase.MainAction)
                {
                    PendingSkillId = null;
                    _candidateIds.Clear();
                    Prompt = BattlePrompt.PlayerCommand;
                    _hud.Refresh();
                    return;
                }

                // 敌方回合，或被控跳过：内核已经把它走完了，继续推进。
                // 这一段刻意不刷快照：屏幕上没有需要看的过程，停下时刷一次就够了。
            }
        }

        private void FillCandidates(BattleUnit actor, SkillDefinition skill)
        {
            _candidateIds.Clear();
            BattleTargeting.CollectCandidatePrimaries(actor, skill, _session.Units, _unitScratch);
            for (var i = 0; i < _unitScratch.Count; i++)
            {
                _candidateIds.Add(_unitScratch[i].RuntimeId);
            }
        }
    }
}
