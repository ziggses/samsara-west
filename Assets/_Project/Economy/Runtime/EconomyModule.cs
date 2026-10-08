using System;
using SamsaraWest.Core;

namespace SamsaraWest.Economy
{
    /// <summary>Economy 层的组合入口，与 <c>DataModule.Install</c>／<c>NarrativeModule.Install</c> 同一种写法。</summary>
    public static class EconomyModule
    {
        /// <summary>
        /// 装玩家的钱袋与背包。它只依赖 Core（事件总线，软依赖）与 Data（掉落表、道具），
        /// 不依赖战斗，也不认识存档——两边要接上的地方都在组合根（见 ADR-027）。
        /// </summary>
        public static void Install(IServiceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IEconomyService>(new EconomyService());
        }
    }
}
