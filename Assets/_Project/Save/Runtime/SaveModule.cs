using SamsaraWest.Core;

namespace SamsaraWest.Save
{
    /// <summary>Save 层的组合入口。</summary>
    public static class SaveModule
    {
        public static void Install(IServiceRegistry registry, string saveDirectory = null)
        {
            registry.Register<ISaveService>(new SaveService(saveDirectory));
        }
    }
}
