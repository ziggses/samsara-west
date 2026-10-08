using System;
using SamsaraWest.Core;

namespace SamsaraWest.Narrative
{
    /// <summary>Narrative 层的组合入口，与 <c>DataModule.Install</c>／<c>ExplorationModule.Install</c> 同一种写法。</summary>
    public static class NarrativeModule
    {
        /// <summary>装剧情状态账。它只依赖 Core（事件总线，软依赖），不需要 Data。</summary>
        public static void Install(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IStoryState>(new StoryState());
        }
    }
}
