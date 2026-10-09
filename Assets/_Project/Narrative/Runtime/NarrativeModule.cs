using System;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Narrative
{
    /// <summary>Narrative 层的组合入口，与 <c>DataModule.Install</c>／<c>ExplorationModule.Install</c> 同一种写法。</summary>
    public static class NarrativeModule
    {
        /// <summary>装剧情状态账；给了定义目录就再装剧情节点推进器。</summary>
        /// <remarks>
        /// 账本只依赖 Core（事件总线是软依赖），本来不需要 Data——这也是它一直排在探索之前的原因。
        /// 推进器要按 <c>inkKnotName</c> 找后继节点，就得读对话定义，于是多了一个依赖。
        /// 因此 <paramref name="definitions"/> 做成可选参数：
        /// 只想验证账本的调用点（老代码、单元测试）传 null，就只装账本，
        /// <c>IDialogueService</c> 解析不到——而不是装一个读不到任何节点的空壳服务。
        /// </remarks>
        public static void Install(IServiceRegistry registry, IDefinitionRegistry definitions = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IStoryState>(new StoryState());

            if (definitions == null)
            {
                GameLog.Info(LogChannel.Narrative, "未提供定义目录，剧情节点推进器未安装（只有状态账）。");
                return;
            }

            registry.Register<IDialogueService>(new DialogueService(definitions));
        }
    }
}
