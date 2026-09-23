using System;
using UnityEngine;

namespace SamsaraWest.Core
{
    /// <summary>
    /// 时间服务。所有系统统一从这里取时间，禁止直接读 <c>Time.deltaTime</c>，
    /// 从而让暂停、倍速与确定性测试成为可能。
    /// </summary>
    public interface ITimeService : IService
    {
        /// <summary>受暂停与倍率影响的累计游戏时间（秒）。</summary>
        double Elapsed { get; }

        /// <summary>受暂停与倍率影响的本帧时间（秒）。暂停时为 0。</summary>
        double Delta { get; }

        /// <summary>不受暂停与倍率影响的累计真实时间（秒）。</summary>
        double RealElapsed { get; }

        /// <summary>时间倍率，取值 <c>[0, 100]</c>。</summary>
        float Scale { get; set; }

        bool IsPaused { get; }

        /// <summary>手动模式：时间只在调用 <see cref="Advance"/> 时推进，供测试与战斗重放使用。</summary>
        bool IsManual { get; }

        void Pause();

        void Resume();

        /// <summary>仅在手动模式下推进时间。</summary>
        void Advance(double seconds);

        /// <summary>由驱动者（Flow 层的 GameClockDriver）每帧调用。</summary>
        void Tick(double unscaledDeltaSeconds);

        void Reset();
    }

    public sealed class TimeService : ITimeService
    {
        private const float MaxScale = 100f;

        private float _scale = 1f;
        private double _elapsed;
        private double _realElapsed;
        private double _delta;

        public TimeService(bool manual = false)
        {
            IsManual = manual;
        }

        public double Elapsed => _elapsed;

        public double Delta => _delta;

        public double RealElapsed => _realElapsed;

        public bool IsPaused { get; private set; }

        public bool IsManual { get; }

        public float Scale
        {
            get => _scale;
            set => _scale = Mathf.Clamp(value, 0f, MaxScale);
        }

        public void OnRegistered(IServiceRegistry registry)
        {
        }

        public void OnUnregistered() => Reset();

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;

        public void Advance(double seconds)
        {
            if (!IsManual)
            {
                throw new InvalidOperationException(
                    "时间服务当前为自动模式，不能手动推进。请在测试或重放场景中以 manual: true 构造。");
            }

            if (seconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            Tick(seconds);
        }

        public void Tick(double unscaledDeltaSeconds)
        {
            if (unscaledDeltaSeconds < 0d)
            {
                unscaledDeltaSeconds = 0d;
            }

            _realElapsed += unscaledDeltaSeconds;

            if (IsPaused)
            {
                _delta = 0d;
                return;
            }

            _delta = unscaledDeltaSeconds * _scale;
            _elapsed += _delta;
        }

        public void Reset()
        {
            _elapsed = 0d;
            _realElapsed = 0d;
            _delta = 0d;
            IsPaused = false;
            _scale = 1f;
        }
    }

    /// <summary>把 Unity 帧时间喂给 <see cref="ITimeService"/> 的驱动者。优先级设高以确保其它系统读到本帧时间。</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class GameClockDriver : MonoBehaviour
    {
        private ITimeService _time;

        private void Update()
        {
            if (_time == null)
            {
                if (!GameServices.IsReady || !GameServices.Registry.TryResolve(out _time))
                {
                    return;
                }
            }

            if (_time.IsManual)
            {
                return;
            }

            _time.Tick(UnityEngine.Time.unscaledDeltaTime);
        }
    }
}
