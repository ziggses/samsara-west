using System;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Equipment
{
    /// <summary>
    /// Equipment 层的组合入口，与 <c>DataModule.Install</c>／<c>EconomyModule.Install</c> 同一种写法。
    /// </summary>
    public static class EquipmentModule
    {
        /// <summary>
        /// 装玩家的在身清单。它只依赖 Core（日志频道）与 Data（角色、装备、经文定义），
        /// 不认识背包、不认识等级、也不认识战斗与存档——要接上的两边都在组合根（见 ADR-028）。
        /// </summary>
        /// <param name="registry">服务注册表。</param>
        /// <param name="definitions">
        /// 定义目录：栏位对不对得上、限定角色允不允许、物品是不是装备或经文，都要问它。
        /// 由组合根解析后传进来（与 <c>ExplorationModule.Install</c> 拿条件源同一种手法），
        /// 这样 Equipment 的依赖关系一眼可见，也不会自己偷偷去摸注册表。
        /// </param>
        public static void Install(IServiceRegistry registry, IDefinitionRegistry definitions)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register<IEquipmentService>(new EquipmentService(definitions));
        }
    }
}
