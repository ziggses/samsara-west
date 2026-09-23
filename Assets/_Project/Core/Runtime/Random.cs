using System;
using System.Collections.Generic;

namespace SamsaraWest.Core
{
    /// <summary>
    /// 一条独立的随机流。同一种子必然产生同一序列，且各流之间互不干扰。
    /// </summary>
    public interface IRandomStream
    {
        string Name { get; }

        uint NextUInt();

        /// <summary>返回 <c>[0, 1)</c>。</summary>
        float NextFloat();

        /// <summary>返回 <c>[minInclusive, maxExclusive)</c>。</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>返回 <c>[0, maxExclusive)</c>。</summary>
        int NextInt(int maxExclusive);

        /// <summary>返回 <c>[0, 1)</c> 的 double，精度高于 <see cref="NextFloat"/>。</summary>
        double NextDouble();

        bool Chance(float probability);

        T Pick<T>(IReadOnlyList<T> source);

        void Shuffle<T>(IList<T> source);

        /// <summary>导出当前内部状态，用于存档与战斗重放。</summary>
        RandomStreamState Snapshot();

        void Restore(RandomStreamState state);
    }

    [Serializable]
    public struct RandomStreamState
    {
        public string Name;
        public ulong State;
        public ulong Increment;
        public ulong DrawCount;

        public override string ToString() => $"{Name}@{DrawCount}:{State:X16}";
    }

    /// <summary>PCG-XSH-RR 32 位生成器：状态小、周期大、统计性质好，且可精确快照。</summary>
    public sealed class PcgRandomStream : IRandomStream
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment;
        private ulong _drawCount;

        public PcgRandomStream(string name, ulong seed)
        {
            Name = string.IsNullOrEmpty(name) ? "unnamed" : name;

            // 用 splitmix64 把「母种子 + 流名」扩散成互不相关的初始状态与增量，
            // 保证不同流的第一发数值不重合，也保证流名不区分大小写以外的任何环境因素。
            var nameHash = Fnv1a(Name);
            var mixed = SplitMix64(seed ^ nameHash);
            _increment = (SplitMix64(mixed) << 1) | 1UL;

            _state = 0UL;
            NextUInt();
            _state += mixed;
            NextUInt();
            _drawCount = 0UL;
        }

        public string Name { get; }

        public ulong DrawCount => _drawCount;

        public uint NextUInt()
        {
            var previous = _state;
            _state = unchecked(previous * Multiplier + _increment);
            _drawCount++;

            var xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
            var rotation = (int)(previous >> 59);
            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }

        public float NextFloat()
        {
            // 取高 24 位，落在 [0, 1) 且不产生 1.0f。
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "上限必须大于下限。");
            }

            var range = (uint)(maxExclusive - minInclusive);
            var bound = uint.MaxValue - (uint.MaxValue % range);
            uint draw;
            do
            {
                draw = NextUInt();
            }
            while (draw >= bound);

            return minInclusive + (int)(draw % range);
        }

        public int NextInt(int maxExclusive) => NextInt(0, maxExclusive);

        public double NextDouble()
        {
            var high = (ulong)(NextUInt() >> 5);
            var low = (ulong)(NextUInt() >> 6);
            return (high * 67108864.0 + low) / 9007199254740992.0;
        }

        public bool Chance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            return NextFloat() < probability;
        }

        public T Pick<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Count == 0)
            {
                throw new ArgumentException("无法从空集合中取值。", nameof(source));
            }

            return source[NextInt(source.Count)];
        }

        public void Shuffle<T>(IList<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            for (var i = source.Count - 1; i > 0; i--)
            {
                var j = NextInt(i + 1);
                if (i == j)
                {
                    continue;
                }

                var swap = source[i];
                source[i] = source[j];
                source[j] = swap;
            }
        }

        public RandomStreamState Snapshot() => new RandomStreamState
        {
            Name = Name,
            State = _state,
            Increment = _increment,
            DrawCount = _drawCount,
        };

        public void Restore(RandomStreamState state)
        {
            if (!string.Equals(state.Name, Name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"流名不匹配：期望 {Name}，实际 {state.Name}。", nameof(state));
            }

            _state = state.State;
            _increment = state.Increment;
            _drawCount = state.DrawCount;
        }

        private static ulong Fnv1a(string text)
        {
            var hash = 14695981039346656037UL;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash = unchecked(hash * 1099511628211UL);
            }

            return hash;
        }

        private static ulong SplitMix64(ulong value)
        {
            var z = unchecked(value + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// 可复现随机服务。不同用途使用不同命名流（战斗 / 掉落 / 剧情），
    /// 因此「改动抽卡逻辑」不会污染「剧情判定」的结果。
    /// </summary>
    public interface IRandomService : IService
    {
        ulong MasterSeed { get; }

        IRandomStream GetStream(string name);

        /// <summary>重置母种子并丢弃所有已有流。</summary>
        void Reseed(ulong masterSeed);

        /// <summary>导出全部流状态，用于存档与战斗重放。</summary>
        RandomStreamState[] SnapshotAll();

        void RestoreAll(RandomStreamState[] states);

        IReadOnlyList<string> StreamNames { get; }
    }

    public static class RandomStreams
    {
        public const string Battle = "battle";
        public const string Loot = "loot";
        public const string Narrative = "narrative";
        public const string Exploration = "exploration";
        public const string Economy = "economy";
    }

    public sealed class RandomService : IRandomService
    {
        private readonly Dictionary<string, IRandomStream> _streams = new Dictionary<string, IRandomStream>(StringComparer.Ordinal);

        public RandomService(ulong masterSeed = 0UL)
        {
            MasterSeed = masterSeed;
        }

        public ulong MasterSeed { get; private set; }

        public IReadOnlyList<string> StreamNames
        {
            get
            {
                var names = new List<string>(_streams.Keys);
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        public void OnRegistered(IServiceRegistry registry)
        {
            GameLog.Debug(LogChannel.Core, $"随机服务已就绪，母种子 {MasterSeed}。");
        }

        public void OnUnregistered() => _streams.Clear();

        public IRandomStream GetStream(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("流名不能为空。", nameof(name));
            }

            if (_streams.TryGetValue(name, out var stream))
            {
                return stream;
            }

            stream = new PcgRandomStream(name, MasterSeed);
            _streams[name] = stream;
            return stream;
        }

        public void Reseed(ulong masterSeed)
        {
            MasterSeed = masterSeed;
            _streams.Clear();
        }

        public RandomStreamState[] SnapshotAll()
        {
            var states = new RandomStreamState[_streams.Count];
            var index = 0;
            foreach (var pair in _streams)
            {
                states[index++] = pair.Value.Snapshot();
            }

            Array.Sort(states, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return states;
        }

        public void RestoreAll(RandomStreamState[] states)
        {
            if (states == null)
            {
                return;
            }

            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (string.IsNullOrEmpty(state.Name))
                {
                    continue;
                }

                GetStream(state.Name).Restore(state);
            }
        }
    }
}
