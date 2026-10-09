using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamsaraWest.Rendering
{
    /// <summary>
    /// 一条「素材键 → 某张贴图上的一块矩形」的绑定。
    /// </summary>
    /// <remarks>
    /// <b>为什么存贴图 + 矩形，而不是直接存 <see cref="Sprite"/></b>：贴图与矩形都是能正常序列化的值，
    /// <see cref="Sprite"/> 却是运行时对象，存进资产只会变成空引用。而且素材随时会改内容（ADR-032），
    /// 存矩形意味着换图只要重跑一次构建器，不必重建任何 Sprite 资产。
    /// </remarks>
    [Serializable]
    public struct SpriteBinding
    {
        [SerializeField] private string _key;
        [SerializeField] private Texture2D _texture;

        /// <summary>贴图内的矩形。<b>左下原点</b>（Unity 口径）。</summary>
        [SerializeField] private Rect _rect;

        /// <summary>归一化轴心。立在地面上的物件是底边中点 (0.5, 0)。</summary>
        [SerializeField] private Vector2 _pivot;

        [SerializeField] private float _pixelsPerUnit;

        public SpriteBinding(string key, Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            _key = key;
            _texture = texture;
            _rect = rect;
            _pivot = pivot;
            _pixelsPerUnit = pixelsPerUnit;
        }

        public string Key => _key;

        public Texture2D Texture => _texture;

        public Rect Rect => _rect;

        public Vector2 Pivot => _pivot;

        public float PixelsPerUnit => _pixelsPerUnit;

        /// <summary>这条绑定能不能切出东西来。</summary>
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(_key) || _texture == null)
                {
                    return false;
                }

                if (_rect.width <= 0f || _rect.height <= 0f)
                {
                    return false;
                }

                // 矩形必须落在贴图里。素材换图后尺寸变了，旧矩形就会越界——这时宁可当它没有，
                // 也不要让 Sprite.Create 抛异常把整层渲染带崩。
                return _rect.x >= 0f
                    && _rect.y >= 0f
                    && _rect.xMax <= _texture.width
                    && _rect.yMax <= _texture.height;
            }
        }

        /// <summary>切出精灵。无效绑定返回 null。</summary>
        public Sprite CreateSprite()
        {
            if (!IsValid)
            {
                return null;
            }

            var pixelsPerUnit = _pixelsPerUnit <= 0f ? GridGeometry.BaselinePixelsPerUnit : _pixelsPerUnit;

            // FullRect 而非 Tight：像素画要的是老老实实的矩形网格，紧包网格会在边缘切掉一两列像素。
            return Sprite.Create(_texture, _rect, _pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }

    /// <summary>
    /// 素材键 → 精灵的目录。由编辑器工具 <c>SpriteCatalogBuilder</c> 生成，**不要手工维护**。
    /// </summary>
    /// <remarks>
    /// <b>为什么放在 Resources 下、而不装进服务注册表</b>：这是渲染层自己的素材索引，美术改一次就要重生成一次。
    /// 装成服务等于让组合根（核心流程层）反过来知道「美术目录长什么样」，方向是反的；
    /// 渲染层是叶子，它自己的资产自己找。
    /// </remarks>
    [CreateAssetMenu(menuName = "SamsaraWest/精灵目录", fileName = "SpriteCatalog")]
    public sealed class SpriteCatalog : ScriptableObject
    {
        public const string ResourcesName = "SpriteCatalog";

        public const string AssetPath = "Assets/_Project/Rendering/Resources/" + ResourcesName + ".asset";

        [SerializeField]
        [Tooltip("由 Tools > SamsaraWest > 重建精灵目录 生成，不要手工维护。")]
        private SpriteBinding[] _bindings = Array.Empty<SpriteBinding>();

        private Dictionary<string, int> _index;
        private Dictionary<string, Sprite> _resolved;

        public int Count => _bindings == null ? 0 : _bindings.Length;

        public IReadOnlyList<SpriteBinding> Bindings =>
            _bindings ?? (IReadOnlyList<SpriteBinding>)Array.Empty<SpriteBinding>();

        /// <summary>从 <c>Resources</c> 取目录。没生成过时返回 null，调用方按「一件素材都没有」处理。</summary>
        public static SpriteCatalog Load()
        {
            return Resources.Load<SpriteCatalog>(ResourcesName);
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            EnsureIndex();
            return _index.ContainsKey(key);
        }

        /// <summary>按键取精灵。查不到返回 null——缺素材是常态，不是异常。</summary>
        public Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_resolved == null)
            {
                _resolved = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            }

            if (_resolved.TryGetValue(key, out var cached))
            {
                return cached;
            }

            EnsureIndex();
            if (!_index.TryGetValue(key, out var slot))
            {
                return null;
            }

            var sprite = _bindings[slot].CreateSprite();
            if (sprite != null)
            {
                _resolved[key] = sprite;
            }

            return sprite;
        }

        /// <summary>编辑器构建器使用：整体替换绑定并清掉缓存。</summary>
        public void SetBindings(SpriteBinding[] bindings)
        {
            _bindings = bindings ?? Array.Empty<SpriteBinding>();
            ClearCache();
        }

        /// <summary>丢掉查出来的缓存。域重载或贴图重导入后必须调。</summary>
        public void ClearCache()
        {
            _index = null;
            _resolved = null;
        }

        private void EnsureIndex()
        {
            if (_index != null)
            {
                return;
            }

            _index = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_bindings == null)
            {
                return;
            }

            for (var i = 0; i < _bindings.Length; i++)
            {
                var key = _bindings[i].Key;
                if (string.IsNullOrEmpty(key) || _index.ContainsKey(key))
                {
                    // 重复键保留先出现的那条：构建器会另外报出来，运行时只需不抖。
                    continue;
                }

                _index[key] = i;
            }
        }
    }
}
