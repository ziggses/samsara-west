using SamsaraWest.Core;

namespace SamsaraWest.Localization
{
    /// <summary>Localization 层的组合入口。</summary>
    public static class LocModule
    {
        public static void Install(IServiceRegistry registry, LocalizationTable table)
        {
            registry.Register<ILocalizationService>(new LocalizationService(table));
        }
    }
}
