using System.IO;
using NUnit.Framework;
using SamsaraWest.Editor.Rendering;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 图集 JSON 读取器：三种贴图字段（atlas／image／body）的取值口径，以及帧字段与坐标翻转的解析。
    /// </summary>
    /// <remarks>
    /// 用临时目录里的真文件测，不走 <c>Assets/_External</c>：素材是外挂目录、不在库里，
    /// 测试不能依赖它存在——与 <see cref="SpriteCatalogTests"/> 同一条纪律。
    /// </remarks>
    public sealed class AtlasJsonReaderTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "samsara-atlas-reader-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        private string WriteJson(string name, string content)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void TryRead_UsesTheBodyLayer_WhenAtlasAndImageAreAbsent()
        {
            // 法杖/刀类武器的形状：本体层 body + 特效层 effect，没有 atlas/image。
            var path = WriteJson("staves.json", @"
{
  ""body"": ""spritesheet_body.png"",
  ""effect"": ""spritesheet_effect.png"",
  ""cell"": [32, 32],
  ""frames"": {
    ""03_ironwood_staff"": {""x"": 64, ""y"": 0, ""w"": 32, ""h"": 32, ""spriteKey"": ""sprite.eqp.staff.iron""},
    ""21_ruyi_gold_staff"": {""x"": 64, ""y"": 96, ""w"": 32, ""h"": 32, ""spriteKey"": ""sprite.eqp.staff.ruyi""}
  }
}");

            var ok = AtlasJsonReader.TryRead(path, out var atlas, out var error);

            Assert.IsTrue(ok, "双层武器图集应当读得进来：" + error);
            Assert.AreEqual("spritesheet_body.png", atlas.ImageFile, "绑定取本体层 body；effect 是叠加光晕，数据侧没有它的键。");
            Assert.AreEqual(2, atlas.Frames.Count);
            var iron = atlas.Frames.Find(frame => frame.Name == "03_ironwood_staff");
            Assert.IsNotNull(iron);
            Assert.AreEqual("sprite.eqp.staff.iron", iron.SpriteKey);
            Assert.AreEqual(64, iron.X);
            Assert.AreEqual(32, iron.Width);
        }

        [Test]
        public void TryRead_PrefersImageOverBody_WhenBothArePresent()
        {
            // 优先级 atlas > image > body：都写的时候按更明确的字段来，别让 body 抢走单层图集的贴图。
            var path = WriteJson("swords.json", @"
{
  ""image"": ""spritesheet.png"",
  ""body"": ""spritesheet_body.png"",
  ""frames"": {
    ""02_iron_sword"": {""x"": 32, ""y"": 0, ""w"": 32, ""h"": 32}
  }
}");

            var ok = AtlasJsonReader.TryRead(path, out var atlas, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual("spritesheet.png", atlas.ImageFile);
            Assert.IsNull(atlas.Frames[0].SpriteKey, "没写 spriteKey 的帧就是 null，由构建器按「无键帧」报数。");
        }

        [Test]
        public void TryRead_FlipsTopLeftYIntoUnityBottomLeftRect()
        {
            // JSON 里的 y 是左上原点（美术侧 PIL/Canvas 口径），Unity Rect 是左下原点，ToRect 负责翻转。
            var path = WriteJson("flip.json", @"
{
  ""atlas"": ""a.png"",
  ""frames"": {
    ""f"": {""x"": 0, ""y"": 10, ""w"": 32, ""h"": 32}
  }
}");

            var ok = AtlasJsonReader.TryRead(path, out var atlas, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(new Rect(0f, 160 - 10 - 32, 32f, 32f), atlas.Frames[0].ToRect(160));
        }

        [Test]
        public void TryRead_SkipsFilesWithoutFrames()
        {
            // 调色板、manifest、对白表都堆在素材目录里，它们没有 frames 字段：安静跳过，不报错。
            var path = WriteJson("palette.json", @"{""core"": [""#04060A""]}");

            var ok = AtlasJsonReader.TryRead(path, out var atlas, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(atlas);
            Assert.IsNull(error, "「不是图集」不算错误，逐个报会把真正的问题淹掉。");
        }
    }
}
