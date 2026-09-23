using System.Collections.Generic;
using SamsaraWest.Core;

namespace SamsaraWest.Data
{
    /// <summary>运行期数据查询入口。业务代码只依赖此接口，不直接持有 <see cref="DefinitionCatalog"/>。</summary>
    public interface IDefinitionRegistry : IService
    {
        int Count { get; }

        bool TryGet(string id, out DefinitionBase definition);

        bool TryGet<T>(string id, out T definition) where T : DefinitionBase;

        T Get<T>(string id) where T : DefinitionBase;

        IEnumerable<T> OfKind<T>() where T : DefinitionBase;

        /// <summary>上次索引构建发现的重复 ID。</summary>
        IReadOnlyList<ValidationIssue> Duplicates { get; }
    }

    public sealed class DefinitionRegistry : IDefinitionRegistry
    {
        private readonly DefinitionCatalog _catalog;

        public DefinitionRegistry(DefinitionCatalog catalog)
        {
            _catalog = catalog;
        }

        public int Count => _catalog == null ? 0 : _catalog.IndexedCount;

        public IReadOnlyList<ValidationIssue> Duplicates =>
            _catalog == null
                ? (IReadOnlyList<ValidationIssue>)System.Array.Empty<ValidationIssue>()
                : _catalog.Duplicates;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (_catalog == null)
            {
                GameLog.Error(LogChannel.Data, "数据目录未指定，所有数据查询都会失败。请检查 Flow 启动配置。");
                return;
            }

            var report = new ValidationReport();
            _catalog.Rebuild(report);

            if (report.HasErrors)
            {
                GameLog.Error(LogChannel.Data, $"数据校验未通过：{report.ErrorCount} 个错误、{report.WarningCount} 个警告。");
            }

            GameLog.Info(LogChannel.Data, $"数据注册表就绪，已索引 {Count} 条定义。");
        }

        public void OnUnregistered()
        {
        }

        public bool TryGet(string id, out DefinitionBase definition)
        {
            if (_catalog == null)
            {
                definition = null;
                return false;
            }

            return _catalog.TryGet(id, out definition);
        }

        public bool TryGet<T>(string id, out T definition) where T : DefinitionBase
        {
            if (_catalog == null)
            {
                definition = null;
                return false;
            }

            return _catalog.TryGet(id, out definition);
        }

        public T Get<T>(string id) where T : DefinitionBase
        {
            if (_catalog == null)
            {
                throw new ServiceNotRegisteredException(typeof(IDefinitionRegistry));
            }

            return _catalog.Get<T>(id);
        }

        public IEnumerable<T> OfKind<T>() where T : DefinitionBase =>
            _catalog == null ? System.Array.Empty<T>() : _catalog.OfKind<T>();
    }
}
