using UnityEngine;

namespace SamsaraWest.Rendering
{
    /// <summary>
    /// 程序生成的占位精灵。素材没交付时用它，保证「有人在跑」看得见，而不是一片空白。
    /// </summary>
    /// <remarks>
    /// <b>为什么不在仓库里放占位图 PNG</b>：一是仓库里每多一张图就多一份要维护的二进制，
    /// 二是程序生成能保证尺寸与 PPU 严格对齐网格。三张图各自带明显的辨识特征，
    /// 一眼就能分清「这是占位」还是「素材已经接上了」。
    /// </remarks>
    public static class PlaceholderSprites
    {
        /// <summary>占位精灵的边长，等于一格。</summary>
        public const int Size = GridGeometry.BaselineTilePixelSize;

        private static Sprite _player;
        private static Texture2D _playerTexture;
        private static Sprite _missing;
        private static Texture2D _missingTexture;
        private static Sprite _flat;
        private static Texture2D _flatTexture;

        /// <summary>玩家：金边的方块，一眼能认出「这是我的位置」。</summary>
        public static Sprite Player
        {
            get
            {
                if (_player == null)
                {
                    _playerTexture = BuildPlayerTexture();
                    _player = Sprite.Create(
                        _playerTexture,
                        new Rect(0f, 0f, Size, Size),
                        new Vector2(0.5f, 0f),
                        GridGeometry.BaselinePixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    _player.name = "placeholder.player";
                }

                return _player;
            }
        }

        /// <summary>缺素材的记号：洋红方框加叉。看见它就说明这张图的某个素材键没接上。</summary>
        public static Sprite Missing
        {
            get
            {
                if (_missing == null)
                {
                    _missingTexture = BuildMissingTexture();
                    _missing = Sprite.Create(
                        _missingTexture,
                        new Rect(0f, 0f, Size, Size),
                        new Vector2(0.5f, 0f),
                        GridGeometry.BaselinePixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    _missing.name = "placeholder.missing";
                }

                return _missing;
            }
        }

        /// <summary>
        /// 1×1 的纯白方块，PPU 取 1，于是<b>缩放的数值就等于世界尺寸</b>。
        /// </summary>
        /// <remarks>
        /// 给「没有底图」时铺一块按网格尺寸的底色用。用 PPU 1 而不是 32，是为了让
        /// <c>localScale = 网格尺寸</c> 直接可读，不必再乘一遍瓦片像素。
        /// </remarks>
        public static Sprite Flat
        {
            get
            {
                if (_flat == null)
                {
                    _flatTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "placeholder.flat",
                    };
                    _flatTexture.SetPixel(0, 0, Color.white);
                    _flatTexture.Apply(false, false);
                    _flat = Sprite.Create(
                        _flatTexture,
                        new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f),
                        1f,
                        0,
                        SpriteMeshType.FullRect);
                    _flat.name = "placeholder.flat";
                }

                return _flat;
            }
        }

        /// <summary>丢掉缓存。域重载后纹理已经被销毁，再取会拿到「假空」，必须重建。</summary>
        public static void Reset()
        {
            _player = null;
            _missing = null;
            _flat = null;
            _playerTexture = null;
            _missingTexture = null;
            _flatTexture = null;
        }

        private static Texture2D BuildPlayerTexture()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "placeholder.player.texture",
            };

            var gold = new Color32(0xF2, 0xC4, 0x4C, 0xFF);
            var body = new Color32(0x2E, 0x4A, 0x6B, 0xFF);
            var core = new Color32(0xE8, 0xEE, 0xF4, 0xFF);

            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var border = x < 2 || y < 2 || x >= Size - 2 || y >= Size - 2;
                    var inner = x >= 8 && x < Size - 8 && y >= 8 && y < Size - 8;
                    pixels[(y * Size) + x] = border ? gold : (inner ? core : body);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D BuildMissingTexture()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "placeholder.missing.texture",
            };

            var frame = new Color32(0xFF, 0x2D, 0x95, 0xFF);
            var cross = new Color32(0xFF, 0xD1, 0x66, 0xFF);

            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var border = x < 2 || y < 2 || x >= Size - 2 || y >= Size - 2;
                    var diagonal = Mathf.Abs(x - y) <= 1 || Mathf.Abs((x + y) - (Size - 1)) <= 1;
                    pixels[(y * Size) + x] = border ? frame : (diagonal ? cross : new Color32(0, 0, 0, 0));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
