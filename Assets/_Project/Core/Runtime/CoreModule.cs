using UnityEngine;
using UnityEngine.InputSystem;

namespace SamsaraWest.Core
{
    /// <summary>Core 层的安装参数。所有可选依赖都在这里显式传入，不做隐式查找。</summary>
    public sealed class CoreOptions
    {
        /// <summary>随机母种子。0 表示使用基于启动时间的种子；存档恢复时由外部覆盖。</summary>
        public ulong MasterSeed;

        public LogLevel MinLogLevel = LogLevel.Info;

        public InputActionAsset InputActions;

        /// <summary>是否使用手动时间（测试 / 战斗重放）。</summary>
        public bool ManualTime;

        /// <summary>是否写入文件日志。自动化测试中建议关闭，避免污染磁盘。</summary>
        public bool EnableFileLog = true;
    }

    /// <summary>Core 层的组合入口。只负责注册服务，不做业务逻辑。</summary>
    public static class CoreModule
    {
        public static void Install(IServiceRegistry registry, CoreOptions options = null)
        {
            options ??= new CoreOptions();

            if (!options.EnableFileLog)
            {
                GameLog.DisableFileSink();
            }

            GameLog.EnsureInstalled();
            GameLog.Configure(options.MinLogLevel);

            // 事件总线最先注册：之后的服务在 OnRegistered 里即可发布事件。
            registry.Register<IEventBus>(new EventBus());
            registry.Register<ITimeService>(new TimeService(options.ManualTime));
            registry.Register<IRandomService>(new RandomService(options.MasterSeed));
            registry.Register<IPoolService>(new PoolService());
            registry.Register<IInputService>(new InputService(options.InputActions));

            GameLog.Info(LogChannel.Core, "Core 层服务注册完成。");
        }

        /// <summary>为未指定种子的启动生成一个可记录的种子。</summary>
        public static ulong CreateSeedFromClock()
        {
            var ticks = (ulong)System.DateTime.UtcNow.Ticks;
            return ticks ^ (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// 启动自检：把当前已注册的服务契约按名字列出，写进日志便于排查「哪个模块没装上」。
        /// 不打印任何实例数据，避免日志泄漏与堆分配。
        /// </summary>
        public static string Describe(IServiceRegistry registry)
        {
            if (registry == null)
            {
                return "服务注册表尚未初始化。";
            }

            var names = new System.Collections.Generic.List<string>(registry.RegisteredTypes.Count);
            foreach (var type in registry.RegisteredTypes)
            {
                names.Add(type.Name);
            }

            names.Sort(System.StringComparer.Ordinal);
            return $"已注册服务契约 {names.Count} 个：{string.Join("、", names)}";
        }
    }
}
