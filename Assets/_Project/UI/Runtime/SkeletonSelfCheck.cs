using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Localization;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 一次骨架自检的结果。它是纯数据，不含任何绘制逻辑，因此可以被断言逐行检查。
    /// </summary>
    public sealed class SkeletonSelfCheckReport
    {
        public SkeletonSelfCheckReport(
            bool servicesReady,
            bool localizationAvailable,
            IReadOnlyList<string> lines,
            IReadOnlyList<string> missingServices)
        {
            ServicesReady = servicesReady;
            LocalizationAvailable = localizationAvailable;
            Lines = lines;
            MissingServices = missingServices;
        }

        /// <summary>服务注册表是否已安装（即 Flow.GameBootstrap 是否跑过）。</summary>
        public bool ServicesReady { get; }

        /// <summary>文本服务是否可用。不可用时自检会退化成显示裸键名，而不是抛异常或留白。</summary>
        public bool LocalizationAvailable { get; }

        public IReadOnlyList<string> Lines { get; }

        /// <summary>UI 层依赖、但当前解析不到的契约名。</summary>
        public IReadOnlyList<string> MissingServices { get; }

        public bool IsHealthy => ServicesReady && MissingServices.Count == 0;
    }

    /// <summary>
    /// 骨架自检（FND-01 的可见面）。骨架期工程没有任何画面内容，运行起来就是一块纯黑，
    /// 「一切正常」和「一启动就挂了」在屏幕上一模一样 —— 这个类存在的唯一理由就是把这件事分开。
    ///
    /// 纪律（与 ADR-014 的边界一致）：
    /// 1. <b>只读，不写</b>：不注册服务、不改全局状态；服务缺失时如实报告缺失，不抛异常、不静默降级。
    /// 2. <b>不产出界面资产</b>：不使用预制体与场景资源，全部内容在运行期由代码生成，
    ///    因此不触碰「骨架期只锁约定、不产出界面资产」这条边界。
    /// 3. <b>文案全走文本键</b>：这里不允许出现中文字面量（会被硬编码中文扫描拦下），
    ///    文本服务缺席时退化为显示键名本身，保证最坏情况下也还有东西可看。
    /// </summary>
    public static class SkeletonSelfCheck
    {
        /// <summary>
        /// UI 层必须解析得到的三个契约。缺任何一个都说明启动流程没跑完，
        /// 不是「界面暂时没数据」这种可以静默接受的情况。
        /// </summary>
        private static readonly Type[] RequiredContracts =
        {
            typeof(ILocalizationService),
            typeof(IDefinitionRegistry),
            typeof(IRandomService),
        };

        public static SkeletonSelfCheckReport Build()
        {
            var lines = new List<string>(12);
            var missing = new List<string>(RequiredContracts.Length);
            var registry = GameServices.IsReady ? GameServices.Registry : null;

            ILocalizationService localization = null;
            if (registry != null)
            {
                registry.TryResolve(out localization);
            }

            for (var i = 0; i < RequiredContracts.Length; i++)
            {
                if (registry == null || !IsRegistered(registry, RequiredContracts[i]))
                {
                    missing.Add(RequiredContracts[i].Name);
                }
            }

            lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_TITLE));
            lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_GREETING));

            if (registry == null)
            {
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_NOTREADY));
            }
            else
            {
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_SERVICES, registry.RegisteredTypes.Count));
            }

            if (registry != null && registry.TryResolve(out IDefinitionRegistry definitions))
            {
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_DEFINITIONS, definitions.Count));
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_DUPLICATES, definitions.Duplicates.Count));
            }

            if (localization != null)
            {
                lines.Add(Text(
                    localization,
                    LocalizationKeys.UI_SKELETON_TEXTS,
                    localization.KeyCount,
                    localization.Language,
                    localization.MissingKeys.Count));
            }

            if (registry != null && registry.TryResolve(out IRandomService random))
            {
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_SEED, random.MasterSeed.ToString("X16")));
            }

            lines.Add(BuildPipelineLine(localization, out var pipelineName));
            lines.Add(Text(
                localization,
                LocalizationKeys.UI_SKELETON_RESOLUTION,
                Screen.width,
                Screen.height,
                SystemInfo.graphicsDeviceType.ToString()));

            if (missing.Count > 0)
            {
                lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_MISSING, string.Join(", ", missing)));
            }

            lines.Add(Text(localization, LocalizationKeys.UI_SKELETON_HINT));

            return new SkeletonSelfCheckReport(registry != null, localization != null, lines, missing);
        }

        /// <summary>
        /// 图形管线走的是资产而不是代码常量，一旦 URP 资产没挂上，工程会静默回落到内置管线
        /// （画面依然能出，只是所有 URP 相关设置全部失效）。这一行就是为了让那种回落当场可见。
        /// </summary>
        private static string BuildPipelineLine(ILocalizationService localization, out string pipelineName)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            pipelineName = pipeline == null ? null : pipeline.name;

            return pipeline == null
                ? Text(localization, LocalizationKeys.UI_SKELETON_PIPELINE_NONE)
                : Text(localization, LocalizationKeys.UI_SKELETON_PIPELINE, pipelineName);
        }

        private static bool IsRegistered(IServiceRegistry registry, Type contract)
        {
            foreach (var registered in registry.RegisteredTypes)
            {
                if (registered == contract)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取词。文本服务缺席时返回键名本身：键名是 ASCII，任何字体都画得出来，
        /// 比留白或抛异常都更有用。
        /// </summary>
        private static string Text(ILocalizationService localization, string key, params object[] args)
        {
            if (localization == null)
            {
                return key;
            }

            if (args == null || args.Length == 0)
            {
                return localization.Get(key);
            }

            return localization.Format(key, args);
        }
    }
}
