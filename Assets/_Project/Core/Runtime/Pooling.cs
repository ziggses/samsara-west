using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamsaraWest.Core
{
    public interface IPool<T> where T : class
    {
        int CountInactive { get; }

        int CountActive { get; }

        long TotalCreated { get; }

        T Get();

        void Release(T item);

        void Prewarm(int count);

        void Clear();
    }

    /// <summary>
    /// 非泛型的「可清空」契约。集中管理各类池的 <see cref="PoolService"/> 必须能统一清空，
    /// 但又不想用反射去调用泛型池的 Clear——反射调用泛型类的实例方法在 Unity 编辑器里会直接挂死，
    /// 而且即便不挂也慢得没必要。用一个窄接口替代反射是这类场景最省事也最稳的做法。
    /// </summary>
    public interface IClearablePool
    {
        /// <summary>清空池内闲置实例；已取出的活跃实例由业务方负责归还。</summary>
        void Clear();
    }

    /// <summary>
    /// 通用对象池。运行期不再分配新实例（除首次与池空时），
    /// 用于特效、飘字、敌人实例的复用。
    /// </summary>
    public sealed class ObjectPool<T> : IPool<T>, IClearablePool where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly Stack<T> _inactive;
        private readonly int _maxSize;

        private int _active;

        public ObjectPool(
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int prewarm = 0,
            int maxSize = 256)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxSize = Mathf.Max(1, maxSize);
            _inactive = new Stack<T>(Mathf.Max(4, prewarm));

            if (prewarm > 0)
            {
                Prewarm(prewarm);
            }
        }

        public int CountInactive => _inactive.Count;

        public int CountActive => _active;

        public long TotalCreated { get; private set; }

        public int MaxSize => _maxSize;

        public T Get()
        {
            T item;
            if (_inactive.Count > 0)
            {
                item = _inactive.Pop();
            }
            else
            {
                item = _factory();
                TotalCreated++;
            }

            _active++;
            _onGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null)
            {
                return;
            }

            _onRelease?.Invoke(item);

            if (_active > 0)
            {
                _active--;
            }

            if (_inactive.Count >= _maxSize)
            {
                _onDestroy?.Invoke(item);
                return;
            }

            _inactive.Push(item);
        }

        public void Prewarm(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var item = _factory();
                TotalCreated++;
                _inactive.Push(item);
            }
        }

        public void Clear()
        {
            while (_inactive.Count > 0)
            {
                _onDestroy?.Invoke(_inactive.Pop());
            }

            _active = 0;
        }
    }

    /// <summary>基于 Prefab 的 GameObject 池，覆盖特效、飘字、敌人三类复用需求。</summary>
    public sealed class PrefabPool : IPool<GameObject>, IClearablePool
    {
        private readonly ObjectPool<GameObject> _inner;
        private readonly Transform _parent;

        public PrefabPool(
            GameObject prefab,
            Transform parent = null,
            Action<GameObject> onGet = null,
            Action<GameObject> onRelease = null,
            int prewarm = 0,
            int maxSize = 256)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            Prefab = prefab;
            _parent = parent;

            _inner = new ObjectPool<GameObject>(
                factory: CreateInstance,
                onGet: instance =>
                {
                    instance.SetActive(true);
                    onGet?.Invoke(instance);
                },
                onRelease: instance =>
                {
                    onRelease?.Invoke(instance);
                    instance.SetActive(false);
                    if (_parent != null)
                    {
                        instance.transform.SetParent(_parent, worldPositionStays: false);
                    }
                },
                onDestroy: instance =>
                {
                    if (UnityEngine.Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(instance);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                },
                prewarm: prewarm,
                maxSize: maxSize);
        }

        public GameObject Prefab { get; }

        public int CountInactive => _inner.CountInactive;

        public int CountActive => _inner.CountActive;

        public long TotalCreated => _inner.TotalCreated;

        public GameObject Get() => _inner.Get();

        public void Release(GameObject item) => _inner.Release(item);

        public void Prewarm(int count) => _inner.Prewarm(count);

        public void Clear() => _inner.Clear();

        /// <summary>取出实例并放置到指定位置。</summary>
        public GameObject Spawn(Vector3 position, Quaternion rotation = default)
        {
            var instance = Get();
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        private GameObject CreateInstance()
        {
            var instance = UnityEngine.Object.Instantiate(Prefab, _parent);
            instance.SetActive(false);
            return instance;
        }
    }

    /// <summary>按类型集中管理对象池，避免各模块各自持有私有池。</summary>
    public interface IPoolService : IService
    {
        IPool<T> GetOrCreate<T>(
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int prewarm = 0,
            int maxSize = 256)
            where T : class;

        IPool<GameObject> GetOrCreatePrefab(
            GameObject prefab,
            Transform parent = null,
            int prewarm = 0,
            int maxSize = 256);

        bool TryGet<T>(out IPool<T> pool) where T : class;

        /// <summary>把活跃实例归还到所有池（用于战斗结束、场景切换的兜底清理）。</summary>
        void ClearAll();
    }

    public sealed class PoolService : IPoolService
    {
        private readonly Dictionary<Type, IClearablePool> _pools = new Dictionary<Type, IClearablePool>();
        private readonly Dictionary<GameObject, PrefabPool> _prefabPools = new Dictionary<GameObject, PrefabPool>();

        public void OnRegistered(IServiceRegistry registry)
        {
        }

        public void OnUnregistered() => ClearAll();

        public IPool<T> GetOrCreate<T>(
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int prewarm = 0,
            int maxSize = 256)
            where T : class
        {
            if (_pools.TryGetValue(typeof(T), out var existing))
            {
                return (IPool<T>)existing;
            }

            var pool = new ObjectPool<T>(factory, onGet, onRelease, prewarm: prewarm, maxSize: maxSize);
            _pools[typeof(T)] = pool;
            return pool;
        }

        public IPool<GameObject> GetOrCreatePrefab(
            GameObject prefab,
            Transform parent = null,
            int prewarm = 0,
            int maxSize = 256)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (_prefabPools.TryGetValue(prefab, out var existing))
            {
                return existing;
            }

            var pool = new PrefabPool(prefab, parent, prewarm: prewarm, maxSize: maxSize);
            _prefabPools[prefab] = pool;
            return pool;
        }

        public bool TryGet<T>(out IPool<T> pool) where T : class
        {
            if (_pools.TryGetValue(typeof(T), out var existing))
            {
                pool = (IPool<T>)existing;
                return true;
            }

            pool = null;
            return false;
        }

        public void ClearAll()
        {
            foreach (var pair in _pools)
            {
                pair.Value.Clear();
            }

            foreach (var pair in _prefabPools)
            {
                pair.Value.Clear();
            }

            _pools.Clear();
            _prefabPools.Clear();
        }
    }
}
