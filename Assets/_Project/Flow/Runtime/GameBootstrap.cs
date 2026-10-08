using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Localization;
using SamsaraWest.Save;
using UnityEngine;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 组合根（Composition Root）。全工程唯一允许 new 出服务并注册的地方。
    ///
    /// 服务本身不挂在场景对象上，因此场景切换不会丢服务；
    /// 其它模块（Battle / Exploration / …）在各自阶段通过 <see cref="ModuleInstalled"/> 挂入，
    /// 避免这个类随章节推进无限膨胀。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("数据资产")]
        [SerializeField] private DefinitionCatalog _definitionCatalog;
        [SerializeField] private LocalizationTable _localizationTable;
        [SerializeField] private Battle.BattleConfig _battleConfig;

        [Header("随机种子")]
        [Tooltip("0 表示每次启动随机取种子；调试复现问题时填固定值。")]
        [SerializeField] private ulong _masterSeed;

        [Header("存档")]
        [Tooltip("留空表示使用 Application.persistentDataPath/saves。")]
        [SerializeField] private string _saveDirectoryOverride;

        [Header("输入")]
        [Tooltip("留空表示只提供接线位，不做实际按键绑定。")]
        [SerializeField] private UnityEngine.InputSystem.InputActionAsset _inputActions;

        private static GameBootstrap _instance;

        /// <summary>已安装的模块名，供启动自检与测试断言。</summary>
        private readonly List<string> _installedModules = new List<string>();

        /// <summary>「探索遇敌 → 进战斗」的接线；它的订阅句柄在 <see cref="OnDestroy"/> 里统一释放。</summary>
        private ExplorationBattleLink _explorationBattleLink;

        /// <summary>「剧情状态变了 → 重建探索可行格」的接线；同样在 <see cref="OnDestroy"/> 里释放。</summary>
        private NarrativeStateLink _narrativeStateLink;

        /// <summary>「走到一扇门 → 换一张图」的接线；同样在 <see cref="OnDestroy"/> 里释放。</summary>
        private MapChangeLink _mapChangeLink;

        /// <summary>已完成的引导次数。PlayMode 测试用它证明「第二次启动没有重复安装」。</summary>
        public static int BootstrapCount { get; private set; }

        public static GameBootstrap Instance => _instance;

        public bool IsReady { get; private set; }

        public ulong MasterSeed { get; private set; }

        public IReadOnlyList<string> InstalledModules => _installedModules;

        public DefinitionCatalog DefinitionCatalog => _definitionCatalog;

        public Battle.BattleConfig BattleConfig => _battleConfig;

        /// <summary>「探索遇敌 → 进战斗」的接线。装在引导末尾，场景销毁时一并释放。</summary>
        public ExplorationBattleLink ExplorationLink => _explorationBattleLink;

        /// <summary>「剧情状态变了 → 重建探索可行格」的接线。同样装在引导末尾。</summary>
        public NarrativeStateLink NarrativeLink => _narrativeStateLink;

        /// <summary>「走到一扇门 → 换一张图」的接线。同样装在引导末尾。</summary>
        public MapChangeLink MapChange => _mapChangeLink;

        /// <summary>其它模块安装自身服务的挂载点。</summary>
        public event Action<IServiceRegistry> ModuleInstalled;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 场景切换后重进启动场景时的重复实例：保留既有的服务，销毁自己。
                GameLog.Info(LogChannel.Flow, "检测到重复的启动器实例，已销毁，服务保持原有实例。", name);
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Bootstrap();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            _instance = null;

            if (_mapChangeLink != null)
            {
                _mapChangeLink.Dispose();
                _mapChangeLink = null;
            }

            if (_narrativeStateLink != null)
            {
                _narrativeStateLink.Dispose();
                _narrativeStateLink = null;
            }

            if (_explorationBattleLink != null)
            {
                _explorationBattleLink.Dispose();
                _explorationBattleLink = null;
            }

            if (IsReady)
            {
                GameLog.Info(LogChannel.Flow, "引导器销毁，开始卸载服务。", name);
                GameServices.Registry.Clear();
                GameServices.Uninstall();
                IsReady = false;
            }
        }

        /// <summary>
        /// 执行全部服务安装。公开是为了让 PlayMode 测试能在不依赖场景的情况下直接调用。
        /// 重复调用是安全的：已安装的模块会被跳过。
        /// </summary>
        public void Bootstrap()
        {
            if (IsReady)
            {
                GameLog.Warn(LogChannel.Flow, "引导已完成，忽略重复调用。", name);
                return;
            }

            if (_definitionCatalog == null)
            {
                GameLog.Error(LogChannel.Flow, "未指定 DefinitionCatalog，数据查询将全部失败。请在启动场景的 GameBootstrap 上挂载资产。", name);
            }

            if (_battleConfig == null)
            {
                GameLog.Warn(LogChannel.Flow, "未指定 BattleConfig，将使用代码默认值。请尽快在编辑器中建立资产。", name);
            }

            MasterSeed = _masterSeed != 0 ? _masterSeed : CoreModule.CreateSeedFromClock();

            var registry = new ServiceRegistry();
            GameServices.Install(registry);

            try
            {
                CoreModule.Install(registry, new CoreOptions
                {
                    MasterSeed = MasterSeed,
                    InputActions = _inputActions,
                });
                Track("Core");

                DataModule.Install(registry, _definitionCatalog);
                Track("Data");

                LocModule.Install(registry, _localizationTable);
                Track("Localization");

                SaveModule.Install(registry, _saveDirectoryOverride);
                Track("Save");

                // 「探索遇敌 → 进战斗」接线的第一步：战斗服务在引导期就位。
                // 它依赖 Core（随机流）与 Data（技能／角色定义），所以排在两者之后。
                Battle.BattleModule.Install(registry, _battleConfig);
                Track("Battle");

                // 剧情状态账就位：任务、对话、结局与探索的条件读的是同一本账。
                // 它只依赖 Core（事件总线是软依赖），装在探索之前——探索要拿它当条件源。
                Narrative.NarrativeModule.Install(registry);
                Track("Narrative");

                // 探索运行时就位：走格、交互、掷遭遇都要读地图与交互物表，所以排在 Data 之后。
                // 条件源由组合根转述：账本在 Narrative，探索只认 IExplorationStateSource，
                // 适配器在这里接上——两边因此都不必认识对方（见 NarrativeStateSourceAdapter）。
                Exploration.ExplorationModule.Install(
                    registry,
                    new NarrativeStateSourceAdapter(registry.Resolve<Narrative.IStoryState>()));
                Track("Exploration");

                ModuleInstalled?.Invoke(registry);

                // 接线最后装：它要同时拿到探索与战斗两个服务。
                // 它不是一个模块（没有服务要注册），所以不计进 InstalledModules —— 那份清单只说「装上了哪些服务」。
                _explorationBattleLink = new ExplorationBattleLink(
                    registry.Resolve<Battle.IBattleService>(),
                    registry.Resolve<IDefinitionRegistry>(),
                    registry.Resolve<IEventBus>(),
                    registry.Resolve<Exploration.IExplorationService>());

                // 剧情状态接线：账本（flag 或心念）一变就重建探索的可行格，让条件交互物当场显隐。
                _narrativeStateLink = new NarrativeStateLink(
                    registry.Resolve<IEventBus>(),
                    registry.Resolve<Exploration.IExplorationService>());

                // 换图接线：探索只会说「这扇门通向 CH01_MAP02」，换不换、落在哪由这里定。
                // 它要同时拿到探索服务与定义目录，所以同样排在模块之后。
                _mapChangeLink = new MapChangeLink(
                    registry.Resolve<IEventBus>(),
                    registry.Resolve<IDefinitionRegistry>(),
                    registry.Resolve<Exploration.IExplorationService>());
            }
            catch (Exception exception)
            {
                GameLog.Fatal(LogChannel.Flow, $"服务安装失败：{exception.Message}", exception, name);
                GameServices.Registry.Clear();
                GameServices.Uninstall();
                throw;
            }

            IsReady = true;
            BootstrapCount++;

            GameLog.Info(
                LogChannel.Flow,
                $"引导完成（第 {BootstrapCount} 次）：已安装 {string.Join("、", _installedModules)}，主种子 {MasterSeed}。",
                name);
            GameLog.Info(LogChannel.Flow, CoreModule.Describe(registry), name);
        }

        private void Track(string moduleName)
        {
            if (!_installedModules.Contains(moduleName))
            {
                _installedModules.Add(moduleName);
            }
        }

        /// <summary>安装阶段传递给各模块的上下文，避免模块之间互相直接引用。</summary>
        public readonly struct InstallContext
        {
            public InstallContext(IServiceRegistry registry, ulong masterSeed)
            {
                Registry = registry;
                MasterSeed = masterSeed;
            }

            public IServiceRegistry Registry { get; }

            public ulong MasterSeed { get; }
        }
    }
}
