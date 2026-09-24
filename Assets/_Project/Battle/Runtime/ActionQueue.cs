using System.Collections.Generic;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 行动队列（ATB）。每个单位持有一个行动值，值最小者先动；动完之后把自己的
    /// 「下一手间隔」加上去，于是速度高的单位自然插队，而顺序完全可复现。
    /// </summary>
    /// <remarks>
    /// 这是对 <c>DamageCalculator.FindNextActor</c> 的补完：那一个纯函数只做「取当前最小行动值」，
    /// 缺的正是「行动之后怎么排队推进」这一环（Docs/战斗数值-v1.md 第 4 节把这件事记成了未实现）。
    ///
    /// 平局判据用 <see cref="BattleUnit.FormationIndex"/>：布阵顺序在前者先动。
    /// 这与 Docs/战斗数值-v1.md 里「沙僧 833、山魈 833、混世魔王 833 依次行动」的校算结果一致
    /// ——建队时我方在前、敌方在后，同速时我方先。
    /// </remarks>
    internal sealed class ActionQueue
    {
        /// <summary>
        /// 行动值累计到该阈值就整体减去最小值。这不是性能优化而是正确性保险：
        /// 一场长战斗会把行动值推到很大，而它是 int，越界会翻成负数、
        /// 让一个单位突然连动。整体平移不改变任何相对顺序。
        /// </summary>
        private const int NormalizeThreshold = 1_000_000;

        private readonly BattleConfig _config;
        private readonly List<BattleUnit> _units = new List<BattleUnit>(12);
        private readonly List<BattleUnit> _orderScratch = new List<BattleUnit>(12);
        private readonly List<int> _valueScratch = new List<int>(12);

        internal ActionQueue(BattleConfig config)
        {
            _config = config;
        }

        internal void Reset(IReadOnlyList<BattleUnit> units)
        {
            _units.Clear();
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                unit.ActionValue = _config.GetActionValue(unit.EffectiveSpeed);
                _units.Add(unit);
            }
        }

        /// <summary>取下一个行动者。全军覆没时返回 null。</summary>
        internal BattleUnit PeekNext()
        {
            BattleUnit best = null;
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (!unit.IsAlive)
                {
                    continue;
                }

                if (best == null ||
                    unit.ActionValue < best.ActionValue ||
                    (unit.ActionValue == best.ActionValue && unit.FormationIndex < best.FormationIndex))
                {
                    best = unit;
                }
            }

            return best;
        }

        /// <summary>把某个单位的行动值推后一手。</summary>
        internal void Advance(BattleUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            unit.ActionValue += _config.GetActionValue(unit.EffectiveSpeed);
            NormalizeIfNeeded();
        }

        /// <summary>
        /// 预演接下来 <paramref name="count"/> 手的出手顺序，<b>不改动</b>任何状态。
        /// 意图预告靠它回答「下一个动的是谁」。
        /// </summary>
        internal IReadOnlyList<BattleUnit> PreviewOrder(int count)
        {
            _orderScratch.Clear();
            if (count <= 0)
            {
                return _orderScratch;
            }

            _valueScratch.Clear();
            for (var i = 0; i < _units.Count; i++)
            {
                _valueScratch.Add(_units[i].ActionValue);
            }

            for (var step = 0; step < count; step++)
            {
                var bestIndex = -1;
                for (var i = 0; i < _units.Count; i++)
                {
                    var unit = _units[i];
                    if (!unit.IsAlive)
                    {
                        continue;
                    }

                    if (bestIndex < 0 ||
                        _valueScratch[i] < _valueScratch[bestIndex] ||
                        (_valueScratch[i] == _valueScratch[bestIndex] &&
                         unit.FormationIndex < _units[bestIndex].FormationIndex))
                    {
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                var actor = _units[bestIndex];
                _orderScratch.Add(actor);
                _valueScratch[bestIndex] += _config.GetActionValue(actor.EffectiveSpeed);
            }

            return _orderScratch;
        }

        private void NormalizeIfNeeded()
        {
            var min = int.MaxValue;
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (unit.IsAlive && unit.ActionValue < min)
                {
                    min = unit.ActionValue;
                }
            }

            if (min == int.MaxValue || min < NormalizeThreshold)
            {
                return;
            }

            for (var i = 0; i < _units.Count; i++)
            {
                _units[i].ActionValue -= min;
            }
        }
    }
}
