using SamsaraWest.Core;

namespace SamsaraWest.Data
{
    /// <summary>Data 层的组合入口。</summary>
    public static class DataModule
    {
        public static void Install(IServiceRegistry registry, DefinitionCatalog catalog)
        {
            registry.Register<IDefinitionRegistry>(new DefinitionRegistry(catalog));
        }
    }
}
