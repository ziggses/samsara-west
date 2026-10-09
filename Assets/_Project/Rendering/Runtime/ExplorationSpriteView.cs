using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using UnityEngine;

namespace SamsaraWest.Rendering
{
    /// <summary>
    /// 探索画面的精灵层：把当前这张图铺成真的像素画——底图、交互物、玩家。
    /// </summary>
    /// <remarks>
    /// <b>与 <c>ExplorationScreenView</c> 的关系</b>：那是同一件事的另一种画法（IMGUI 画 ASCII 格子图）。
    /// 输入、对白、诊断文字仍归那一层；这一层只管画面。两层同时画会叠在一起，
    /// 所以 <see cref="IsActive"/> 为真时界面层跳过 ASCII 网格（ADR-032）。
    ///
    /// <b>素材缺口是常态</b>：首章只有 CH01_MAP01 交付了底图与元素，其余四张图的数据有、
    /// 素材没有。缺底图时铺一块按网格尺寸的暗色（让「图有多大」看得见），
    /// 缺交互物素材时记进 <see cref="MissingKeys"/> 而不画占位——满屏洋红方块比留白更难看。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ExplorationSpriteView : MonoBehaviour
    {
        public const string RootName = "SamsaraWest 精灵层";

        /// <summary>底图排在最底下。</summary>
        private const int BackgroundSortingOrder = -1000;

        /// <summary>缺底图时那块底色压在真底图之上、所有物件之下。</summary>
        private const int MissingBackgroundSortingOrder = -900;

        /// <summary>缺底图时铺的暗色。</summary>
        private static readonly Color32 MissingBackgroundColor = new Color32(0x14, 0x18, 0x24, 0xFF);

        private static ExplorationSpriteView _instance;

        /// <summary>关掉后连创建都不再创建。测试用它与界面层的开关对齐。</summary>
        public static bool Enabled { get; set; } = true;

        [SerializeField]
        [Tooltip("每张图最多报一次「哪些素材键没接上」。缺素材是常态，刷屏反而没人看。")]
        private bool _logMissingSprites = true;

        private IExplorationService _exploration;
        private IDefinitionRegistry _definitions;
        private IServiceRegistry _registryRef;

        private SpriteCatalog _catalog;
        private CameraFollowRig _cameraRig;

        private Transform _root;
        private SpriteRenderer _background;
        private SpriteRenderer _player;

        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private readonly List<string> _renderedKeys = new List<string>();
        private readonly List<string> _missingKeys = new List<string>();

        private string _renderedMapId;
        private int _renderedSignature;
        private string _loggedMissingForMapId;
        private int _used;

        public static ExplorationSpriteView Instance => _instance;

        /// <summary>精灵层正把地图画在屏幕上。界面层据此决定要不要再画一遍 ASCII 网格。</summary>
        public static bool IsActive => _instance != null && Enabled && _instance.IsVisible;

        public bool IsVisible { get; private set; }

        /// <summary>当前画的是哪张图；没画时为 null。</summary>
        public string MapId => _renderedMapId;

        /// <summary>当前这张图用的底图素材键。</summary>
        public string BackgroundKey { get; private set; }

        /// <summary>这一帧画出来的交互物素材键（按数据顺序，可能重复）。</summary>
        public IReadOnlyList<string> RenderedKeys => _renderedKeys;

        /// <summary>数据要了、目录里没有的素材键。</summary>
        public IReadOnlyList<string> MissingKeys => _missingKeys;

        /// <summary>已建出来的精灵对象数（含底图与玩家）。池子只增不减，用来看有没有反复重建。</summary>
        public int SpriteRendererCount => _pool.Count;

        public Vector3 PlayerWorldPosition => _player == null ? Vector3.zero : _player.transform.position;

        public CameraFollowRig CameraRig => _cameraRig;

        public SpriteCatalog Catalog => _catalog;

        /// <summary>取实例；没有就建一个。</summary>
        public static ExplorationSpriteView EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (!Enabled)
            {
                return null;
            }

            var host = new GameObject(RootName);
            DontDestroyOnLoad(host);
            return host.AddComponent<ExplorationSpriteView>();
        }

        /// <summary>立即重画一遍。测试用它跳过帧循环，正式代码走 <see cref="Update"/>。</summary>
        public void Refresh()
        {
            if (!Enabled)
            {
                Hide();
                return;
            }

            EnsureServices();

            var session = _exploration == null ? null : _exploration.Current;
            if (session == null)
            {
                Hide();
                return;
            }

            var signature = SignatureOf(session);
            if (signature != _renderedSignature)
            {
                Rebuild(session, signature);
                return;
            }

            LayoutPlayer(session);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (!Application.isPlaying || _instance != null || !Enabled)
            {
                return;
            }

            EnsureCreated();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            var world = new GameObject("World");
            world.transform.SetParent(transform, false);
            _root = world.transform;

            _background = CreateRenderer("Background", BackgroundSortingOrder);
            _player = CreateRenderer("Player", 0);
            _player.sprite = PlaceholderSprites.Player;
            _player.gameObject.SetActive(false);

            GameLog.Info(LogChannel.Rendering, "探索精灵层已安装。");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            Refresh();
        }

        /// <summary>
        /// 当前画面的指纹：换图、或可见交互物集合变了，都要重画。
        /// </summary>
        /// <remarks>
        /// 交互物集合会<b>在同一张图里</b>变（打过仗之后才显出的血迹、印记），
        /// 所以不能只看 <c>MapId</c>。用哈希而不是拼字符串：每帧拼一次会白造垃圾。
        /// </remarks>
        private static int SignatureOf(ExplorationSession session)
        {
            var visible = session.Grid.VisibleInteractables;
            var hash = session.MapId == null ? 17 : session.MapId.GetHashCode();
            hash = (hash * 31) + visible.Count;
            for (var i = 0; i < visible.Count; i++)
            {
                hash = (hash * 31) + (visible[i].Id == null ? 0 : visible[i].Id.GetHashCode());
            }

            return hash;
        }

        /// <summary>按 y 排前后：y 越小越靠前（越靠近镜头）。同格的用 <paramref name="bias"/> 分先后。</summary>
        private static int SortingOrderFor(MapDefinition map, GridPosition cell, int bias)
        {
            var height = map == null ? 1 : Mathf.Max(1, map.GridHeight);
            return ((height - cell.Y) * 4) + bias;
        }

        private SpriteRenderer CreateRenderer(string name, int sortingOrder)
        {
            var host = new GameObject(name);
            host.transform.SetParent(_root, false);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private SpriteRenderer Take()
        {
            if (_used < _pool.Count)
            {
                var reused = _pool[_used];
                _used++;
                reused.gameObject.SetActive(true);
                return reused;
            }

            var renderer = CreateRenderer("Element", 0);
            _pool.Add(renderer);
            _used++;
            return renderer;
        }

        private void ReleaseUnused()
        {
            for (var i = _used; i < _pool.Count; i++)
            {
                _pool[i].gameObject.SetActive(false);
            }
        }

        private void Rebuild(ExplorationSession session, int signature)
        {
            _renderedMapId = session.MapId;
            _renderedSignature = signature;
            _renderedKeys.Clear();
            _missingKeys.Clear();
            _used = 0;

            _definitions.TryGet(session.MapId, out MapDefinition map);

            DrawBackground(map);

            var visible = session.Grid.VisibleInteractables;
            for (var i = 0; i < visible.Count; i++)
            {
                DrawInteractable(map, visible[i]);
            }

            ReleaseUnused();
            LayoutPlayer(session);

            // 换图时镜头必须直接吸附：跟随器的目标始终是同一条玩家对象，
            // 光靠 Follow 换不掉位置，得显式吸一次。
            SnapCamera();

            LogMissingOnce();
            _root.gameObject.SetActive(true);
            IsVisible = true;
        }

        private void DrawBackground(MapDefinition map)
        {
            BackgroundKey = map == null ? null : map.BackgroundSpriteKey;

            var sprite = _catalog == null || string.IsNullOrEmpty(BackgroundKey)
                ? null
                : _catalog.Get(BackgroundKey);

            if (sprite != null)
            {
                _background.sprite = sprite;
                _background.color = Color.white;
                _background.transform.localScale = Vector3.one;
                _background.transform.position = GridGeometry.MapOrigin(map);
                _background.sortingOrder = BackgroundSortingOrder;
                _background.gameObject.SetActive(true);

                var texture = sprite.texture;
                if (texture != null && !GridGeometry.BackgroundMatchesGrid(map, texture.width, texture.height))
                {
                    GameLog.Warn(
                        LogChannel.Rendering,
                        $"底图 {texture.width}×{texture.height} 与网格 {map.GridWidth}×{map.GridHeight} 格不成 1:1，位置按原点对齐、不做拉伸。",
                        map.Id);
                }

                return;
            }

            // 没底图：铺一块按网格尺寸的暗色。空白会让人以为渲染层没生效，
            // 而一块有边界的暗底能直接告诉你「图是这么大」。
            _background.sprite = PlaceholderSprites.Flat;
            _background.color = MissingBackgroundColor;
            _background.transform.position = GridGeometry.MapCenter(map);
            var size = GridGeometry.MapSize(map);
            _background.transform.localScale = new Vector3(size.x, size.y, 1f);
            _background.sortingOrder = MissingBackgroundSortingOrder;
            _background.gameObject.SetActive(true);

            AddMissing(BackgroundKey);
        }

        private void DrawInteractable(MapDefinition map, InteractableDefinition interactable)
        {
            var key = interactable.InteractSpriteKey;
            var sprite = _catalog == null || string.IsNullOrEmpty(key) ? null : _catalog.Get(key);

            if (sprite == null)
            {
                AddMissing(key);
                return;
            }

            var cell = new GridPosition(interactable.GridX, interactable.GridY);
            var renderer = Take();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingOrderFor(map, cell, 0);
            renderer.transform.localScale = Vector3.one;
            renderer.transform.position = GridGeometry.TileBottomCenter(map, cell);
            _renderedKeys.Add(key);
        }

        private void LayoutPlayer(ExplorationSession session)
        {
            _definitions.TryGet(session.MapId, out MapDefinition map);

            var cell = session.Position;
            _player.sprite = PlaceholderSprites.Player;
            _player.transform.position = GridGeometry.TileBottomCenter(map, cell);
            _player.sortingOrder = SortingOrderFor(map, cell, 1);
            _player.gameObject.SetActive(true);

            _root.gameObject.SetActive(true);
            IsVisible = true;

            EnsureCameraRig();
            if (_cameraRig != null)
            {
                _cameraRig.Follow(_player.transform);
            }
        }

        private void SnapCamera()
        {
            EnsureCameraRig();
            if (_cameraRig != null)
            {
                _cameraRig.Follow(_player.transform);
                _cameraRig.SnapTo(_player.transform.position);
            }
        }

        private void EnsureCameraRig()
        {
            if (_cameraRig != null)
            {
                return;
            }

            _cameraRig = CameraFollowRig.Ensure(Camera.main);
        }

        private void AddMissing(string key)
        {
            if (string.IsNullOrEmpty(key) || _missingKeys.Contains(key))
            {
                return;
            }

            _missingKeys.Add(key);
        }

        private void LogMissingOnce()
        {
            if (!_logMissingSprites || _missingKeys.Count == 0 || _loggedMissingForMapId == _renderedMapId)
            {
                return;
            }

            _loggedMissingForMapId = _renderedMapId;
            GameLog.Info(
                LogChannel.Rendering,
                $"地图 {_renderedMapId} 有 {_missingKeys.Count} 个素材键还没接上：{string.Join(",", _missingKeys)}。",
                _renderedMapId);
        }

        private void Hide()
        {
            if (!IsVisible && _renderedMapId == null)
            {
                return;
            }

            IsVisible = false;
            _renderedMapId = null;
            _renderedSignature = 0;
            _renderedKeys.Clear();
            _used = 0;

            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }

            if (_cameraRig != null)
            {
                _cameraRig.Clear();
            }
        }

        private void EnsureServices()
        {
            if (!GameServices.IsReady)
            {
                DropServices();
                return;
            }

            var registry = GameServices.Registry;
            if (ReferenceEquals(registry, _registryRef) && _definitions != null)
            {
                return;
            }

            // 注册表换了一张就说明服务重装过，旧引用全部作废。
            DropServices();

            if (!registry.TryResolve(out _exploration) || _exploration == null)
            {
                return;
            }

            if (!registry.TryResolve(out _definitions) || _definitions == null)
            {
                _exploration = null;
                return;
            }

            _registryRef = registry;
            _catalog = SpriteCatalog.Load();

            if (_catalog == null)
            {
                GameLog.Warn(
                    LogChannel.Rendering,
                    "精灵目录还没生成，探索画面只有占位方块。跑一次 Tools > SamsaraWest > 重建精灵目录。");
            }
        }

        private void DropServices()
        {
            _registryRef = null;
            _exploration = null;
            _definitions = null;
        }
    }
}
