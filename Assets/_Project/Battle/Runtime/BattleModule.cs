using System;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 战斗服务：持有数值配置，负责按入场清单开一场新的战斗，并暴露当前这一场。
    /// </summary>
    /// <remarks>
    /// 为什么不把 <see cref="BattleSession"/> 本身做成服务：一场战斗是有始有终的产物，
    /// 而服务是跨场景存活的长期对象。让服务只负责「开一场 / 记住当前这场」，
    /// 战斗结束后丢掉会话即可，不会把战斗状态粘在全局服务上。
    /// </remarks>
    public interface IBattleService : IService
    {
        BattleConfig Config { get; }

        /// <summary>当前这一场战斗；没有进行中的战斗时为 null。</summary>
        BattleSession Current { get; }

        bool HasActiveBattle { get; }

        /// <summary>按入场清单开一场战斗，并把它设为当前战斗。</summary>
        BattleSession StartBattle(BattleSetup setup);

        /// <summary>丢弃当前战斗。战后结算完成、进入下一段剧情时调用。</summary>
        void EndBattle();
    }

    /// <inheritdoc cref="IBattleService" />
    public sealed class BattleService : IBattleService
    {
        private IDefinitionRegistry _registry;
        private IRandomService _random;
        private IEventBus _eventBus;

        public BattleService(BattleConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public BattleConfig Config { get; }

        public BattleSession Current { get; private set; }

        public bool HasActiveBattle => Current != null && !Current.IsFinished;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // 数据与随机都是硬依赖：没有技能表就打不出伤害，没有随机流就无法复现暴击。
            if (!registry.TryResolve(out _registry) || _registry == null)
            {
                throw new ServiceNotRegisteredException(typeof(IDefinitionRegistry));
            }

            if (!registry.TryResolve(out _random) || _random == null)
            {
                throw new ServiceNotRegisteredException(typeof(IRandomService));
            }

            // 事件总线是软依赖：内核在无总线时照样能跑完（测试里就不接总线）。
            registry.TryResolve(out _eventBus);

            GameLog.Info(
                LogChannel.Battle,
                $"Battle 模块已注册，数值配置为 {(Config == null ? "缺失" : Config.name)}。");
        }

        public void OnUnregistered()
        {
            Current = null;
            _registry = null;
            _random = null;
            _eventBus = null;
        }

        public BattleSession StartBattle(BattleSetup setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException(nameof(setup));
            }

            if (_registry == null || _random == null)
            {
                throw new ServiceNotRegisteredException(typeof(IBattleService));
            }

            // 战斗用「battle」这条命名流：掉落与剧情判定各有自己的流，
            // 于是调平衡改战斗掷骰不会连带改掉剧情分支的结果。
            var stream = _random.GetStream(RandomStreams.Battle);
            Current = new BattleSession(Config, _registry, stream, setup, _eventBus);
            return Current;
        }

        public void EndBattle()
        {
            Current = null;
        }
    }

    /// <summary>Battle 层的组合入口，与 <c>DataModule.Install</c> 保持同一种写法。</summary>
    /// <remarks>
    /// 骨架期<b>尚未</b>由 <c>Flow.GameBootstrap</c> 调用：Bootstrap 的已装模块名单
    /// （Core / Data / Localization / Save）被 PlayMode 断言锁住，而「探索遇敌 → 进战斗」
    /// 这条接线属于后续任务。因此这里先把装配入口准备好并由测试覆盖，
    /// 等接线任务落地时，Bootstrap 里加一行即可，不必改战斗内核。
    /// </remarks>
    public static class BattleModule
    {
        public static void Install(IServiceRegistry registry, BattleConfig config)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IBattleService>(new BattleService(config));
        }
    }
}
