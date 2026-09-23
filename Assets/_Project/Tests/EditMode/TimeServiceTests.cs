using System;
using NUnit.Framework;
using SamsaraWest.Core;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 时间服务是所有系统的唯一时间来源。暂停、倍速与手动推进的语义一旦含糊，
    /// 战斗重放与确定性测试就无从谈起。
    /// </summary>
    public sealed class TimeServiceTests
    {
        [Test]
        public void Tick_AdvancesElapsedAndRealTime()
        {
            var time = new TimeService();

            time.Tick(0.5);

            Assert.AreEqual(0.5, time.Delta, 1e-9);
            Assert.AreEqual(0.5, time.Elapsed, 1e-9);
            Assert.AreEqual(0.5, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Tick_NegativeDelta_IsTreatedAsZero()
        {
            var time = new TimeService();

            time.Tick(-5);

            Assert.AreEqual(0d, time.Delta, 1e-9);
            Assert.AreEqual(0d, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Pause_FreezesGameTimeButKeepsRealTime()
        {
            var time = new TimeService();
            time.Tick(1);
            time.Pause();

            time.Tick(2);

            Assert.IsTrue(time.IsPaused);
            Assert.AreEqual(0d, time.Delta, 1e-9);
            Assert.AreEqual(1d, time.Elapsed, 1e-9, "暂停期间游戏时间不得推进。");
            Assert.AreEqual(3d, time.RealElapsed, 1e-9, "真实时间不受暂停影响，用于界面动画与性能统计。");
        }

        [Test]
        public void Resume_RestartsAdvancing()
        {
            var time = new TimeService();
            time.Pause();
            time.Tick(1);
            time.Resume();

            time.Tick(1);

            Assert.IsFalse(time.IsPaused);
            Assert.AreEqual(1d, time.Elapsed, 1e-9);
        }

        [Test]
        public void Scale_MultipliesGameTimeOnly()
        {
            var time = new TimeService { Scale = 3f };

            time.Tick(2);

            Assert.AreEqual(6d, time.Elapsed, 1e-9);
            Assert.AreEqual(6d, time.Delta, 1e-9);
            Assert.AreEqual(2d, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Scale_IsClampedToZeroHundred()
        {
            var time = new TimeService { Scale = -4f };
            Assert.AreEqual(0f, time.Scale);

            time.Scale = 1000f;
            Assert.AreEqual(100f, time.Scale);

            time.Tick(1);
            Assert.AreEqual(100d, time.Elapsed, 1e-9);
        }

        [Test]
        public void ZeroScale_FreezesGameTimeWithoutPausing()
        {
            var time = new TimeService { Scale = 0f };

            time.Tick(1);

            Assert.IsFalse(time.IsPaused);
            Assert.AreEqual(0d, time.Elapsed, 1e-9);
            Assert.AreEqual(1d, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Advance_InAutomaticMode_Throws()
        {
            var time = new TimeService();

            Assert.Throws<InvalidOperationException>(() => time.Advance(1));
        }

        [Test]
        public void Advance_InManualMode_MovesTimeWithoutExternalDriver()
        {
            var time = new TimeService(manual: true);

            time.Advance(0.25);
            time.Advance(0.25);

            Assert.IsTrue(time.IsManual);
            Assert.AreEqual(0.5d, time.Elapsed, 1e-9);
            Assert.AreEqual(0.5d, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Advance_NegativeSeconds_Throws()
        {
            var time = new TimeService(manual: true);

            Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(-1));
        }

        [Test]
        public void Advance_WhilePaused_KeepsElapsed()
        {
            var time = new TimeService(manual: true);
            time.Pause();

            time.Advance(1);

            Assert.AreEqual(0d, time.Elapsed, 1e-9);
            Assert.AreEqual(1d, time.RealElapsed, 1e-9);
        }

        [Test]
        public void Reset_ClearsEverythingIncludingPauseAndScale()
        {
            var time = new TimeService(manual: true);
            time.Pause();
            time.Scale = 5f;
            time.Advance(2);

            time.Reset();

            Assert.AreEqual(0d, time.Elapsed, 1e-9);
            Assert.AreEqual(0d, time.RealElapsed, 1e-9);
            Assert.AreEqual(0d, time.Delta, 1e-9);
            Assert.IsFalse(time.IsPaused);
            Assert.AreEqual(1f, time.Scale);
        }

        [Test]
        public void OnUnregistered_ResetsTime()
        {
            var time = new TimeService(manual: true);
            time.Advance(3);

            time.OnUnregistered();

            Assert.AreEqual(0d, time.Elapsed, 1e-9, "服务注销后残留的累计时间会污染下一次开局。");
        }
    }
}
