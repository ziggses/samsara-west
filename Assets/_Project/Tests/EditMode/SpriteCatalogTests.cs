using NUnit.Framework;
using SamsaraWest.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 精灵目录：素材键怎么切出精灵、缺口怎么表达。
    /// </summary>
    /// <remarks>
    /// 这里直接用内存里造的贴图，不走 <c>Assets/_External</c>——素材是外挂目录、不在库里，
    /// 测试不能依赖它存在。切图与查表这两件事与「素材从哪来」无关。
    /// </remarks>
    public sealed class SpriteCatalogTests
    {
        private const string KeyA = "sprite.int.test.a";
        private const string KeyB = "sprite.int.test.b";

        private Texture2D _texture;
        private SpriteCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            _texture.SetPixels32(new Color32[64 * 64]);
            _texture.Apply(false, false);

            _catalog = ScriptableObject.CreateInstance<SpriteCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }

            if (_texture != null)
            {
                Object.DestroyImmediate(_texture);
            }
        }

        [Test]
        public void EmptyCatalog_AnswersNothingRatherThanThrowing()
        {
            Assert.AreEqual(0, _catalog.Count);
            Assert.IsFalse(_catalog.Has(KeyA));
            Assert.IsNull(_catalog.Get(KeyA));
            Assert.IsNull(_catalog.Get(null));
            Assert.IsNull(_catalog.Get(string.Empty));

            _catalog.SetBindings(null);
            Assert.AreEqual(0, _catalog.Count);
        }

        [Test]
        public void Get_SlicesTheRectAtThePivotWithTheBaselinePixelsPerUnit()
        {
            Bind(KeyA, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0f));

            var sprite = _catalog.Get(KeyA);

            Assert.IsNotNull(sprite);
            Assert.AreSame(_texture, sprite.texture, "切出来的精灵必须指向同一张贴图，而不是复制一份。");
            Assert.AreEqual(new Rect(0f, 0f, 32f, 32f), sprite.rect);
            Assert.AreEqual(new Vector2(16f, 0f), sprite.pivot, "底边中点的轴心在像素上是 (16,0)。");
            Assert.AreEqual(GridGeometry.BaselinePixelsPerUnit, sprite.pixelsPerUnit, "PPU 必须钉在 32，否则一格画出来不是一格。");
        }

        [Test]
        public void Get_CachesTheResolvedSprite()
        {
            Bind(KeyA, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0f));

            var first = _catalog.Get(KeyA);
            var second = _catalog.Get(KeyA);

            Assert.AreSame(first, second, "同一把键每帧都重切一张精灵会白造垃圾。");
        }

        [Test]
        public void ClearCache_RebuildsTheSprite()
        {
            Bind(KeyA, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0f));

            var before = _catalog.Get(KeyA);
            _catalog.ClearCache();
            var after = _catalog.Get(KeyA);

            Assert.AreNotSame(before, after, "换过素材之后必须能真的重切，而不是继续发旧的那张。");
            Assert.AreEqual(before.rect, after.rect);
        }

        [Test]
        public void Get_ReturnsNullWhenTheRectFellOffTheTexture()
        {
            // 素材换图后尺寸变小，旧矩形越界：这时宁可当作没有，也不让 Sprite.Create 把整层渲染带崩。
            Bind(KeyA, new Rect(32f, 32f, 64f, 64f), new Vector2(0.5f, 0f));

            Assert.IsTrue(_catalog.Has(KeyA), "绑定还在，所以 Has 说「有」。");
            Assert.IsNull(_catalog.Get(KeyA), "但切不出来，所以 Get 必须给 null。");
        }

        [Test]
        public void SetBindings_KeepsTheFirstEntryWhenAKeyIsDuplicated()
        {
            _catalog.SetBindings(new[]
            {
                new SpriteBinding(KeyA, _texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0f), GridGeometry.BaselinePixelsPerUnit),
                new SpriteBinding(KeyA, _texture, new Rect(32f, 0f, 32f, 32f), new Vector2(0.5f, 0f), GridGeometry.BaselinePixelsPerUnit),
                new SpriteBinding(KeyB, _texture, new Rect(0f, 32f, 32f, 32f), new Vector2(0f, 0f), GridGeometry.BaselinePixelsPerUnit),
            });

            Assert.AreEqual(3, _catalog.Count, "重复键不合并条数，只是查表时取先出现的那条。");
            Assert.AreEqual(new Rect(0f, 0f, 32f, 32f), _catalog.Get(KeyA).rect, "重复键保留先出现的那条，运行时结果才不抖。");
            Assert.AreEqual(new Rect(0f, 32f, 32f, 32f), _catalog.Get(KeyB).rect);
        }

        private void Bind(string key, Rect rect, Vector2 pivot) =>
            _catalog.SetBindings(new[]
            {
                new SpriteBinding(key, _texture, rect, pivot, GridGeometry.BaselinePixelsPerUnit),
            });
    }
}
