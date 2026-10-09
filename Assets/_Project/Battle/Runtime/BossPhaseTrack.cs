using System.Collections.Generic;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 一个 Boss 单位的阶段进度（ADR-030）。
    /// </summary>
    /// <remarks>
    /// <para>它只回答「该不该进下一档」，不负责进档之后发生什么——换技能组、写倍率、挂入场状态、
    /// 发事件都在 <see cref="BattleSession"/> 里。分开是因为判定这一半要能单独测：
    /// 给一条生命比例就该问得出答案，不必先把整场战斗推到那个血量。</para>
    /// <para>进度是<b>单调</b>的：只前进、不后退。就算把 Boss 治回满血也回不到上一档——
    /// 档位记的是「已经打到哪一步」，不是「现在处于什么状态」，回头重播一遍台词没有意义。</para>
    /// </remarks>
    public sealed class BossPhaseTrack
    {
        /// <summary>按阈值从高到低排好的档位。本类不改动它，只在多个 Boss 之间共享同一份。</summary>
        private readonly List<BossPhaseDefinition> _phases;

        private int _nextIndex;

        internal BossPhaseTrack(BattleUnit unit, List<BossPhaseDefinition> phases)
        {
            Unit = unit;
            _phases = phases;
        }

        /// <summary>这个进度挂在哪个单位身上。</summary>
        public BattleUnit Unit { get; }

        /// <summary>这张阶段表一共几档。</summary>
        public int PhaseCount => _phases.Count;

        /// <summary>已经进到第几档，取自定义的 <c>phaseIndex</c>（从 1 起）；0 表示还没进任何一档。</summary>
        public int PhaseIndex { get; private set; }

        /// <summary>当前档位的定义；还没进任何一档时为空。</summary>
        public BossPhaseDefinition CurrentPhase { get; private set; }

        /// <summary>还有没有下一档可进。</summary>
        public bool HasNextPhase => _nextIndex < _phases.Count;

        /// <summary>
        /// 若生命比例已跌破下一档的阈值，则前进一档并返回 true；否则原样返回 false。
        /// </summary>
        /// <remarks>
        /// <para>一次只走一档。跨两档的重击要连调两次，于是两档的入场状态与两次演出都会发生，
        /// 而不是静悄悄跳到最后一档——「一次跨档」在剧本上正是最该被看见的那一下。</para>
        /// <para>已倒下的单位不再换档：致命一击之后的那一档没有意义，也不该再播一次台词。</para>
        /// <para>阈值比较取 <c>&lt;=</c>。阶段表把第一档写成 <c>1.0</c> 正是为了在开局那一刻命中，
        /// 于是「开场技能组」与「换档技能组」走的是同一条路，没有特例。</para>
        /// </remarks>
        internal bool TryEnterNextPhase()
        {
            if (!HasNextPhase || !Unit.IsAlive)
            {
                return false;
            }

            var phase = _phases[_nextIndex];
            if (Unit.HealthRatio > phase.HealthThreshold)
            {
                return false;
            }

            _nextIndex++;
            CurrentPhase = phase;
            PhaseIndex = phase.PhaseIndex;
            return true;
        }
    }
}
