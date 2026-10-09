using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SamsaraWest.Editor.Rendering
{
    /// <summary>
    /// 读美术侧交过来的图集 JSON（<c>*_元素图集.json</c>、<c>角色图集.json</c>、<c>spritesheet.json</c>）。
    /// </summary>
    /// <remarks>
    /// <b>为什么不直接用 JsonUtility</b>：这些文件的 <c>frames</c> 是<b>对象</b>（以帧名为键），
    /// 而 JsonUtility 只认数组，字典形状它读不了；换 Newtonsoft 又要多引一个包。
    /// 这里要的字段固定、且都是标量，所以自己走一遍括号配平就够了。
    ///
    /// <b>坐标口径</b>：JSON 里的 <c>x</c>/<c>y</c> 是<b>左上原点</b>（美术侧是 PIL／Canvas 那套），
    /// Unity 的 <see cref="Rect"/> 是左下原点。<see cref="Frame.ToRect"/> 负责翻转。
    /// 这个口径当初是拿图集的像素不透明度验出来的（左上解释下元素区域覆盖率 0.26–0.88，左下≈0），不是猜的。
    /// </remarks>
    internal static class AtlasJsonReader
    {
        /// <summary>图集里的一帧。</summary>
        internal sealed class Frame
        {
            public string Name;

            public int X;

            public int Y;

            public int Width;

            public int Height;

            /// <summary>数据侧的素材键。武器图集没有这个字段，为 null。</summary>
            public string SpriteKey;

            public string Anchor;

            /// <summary>翻成 Unity 的左下原点矩形。</summary>
            public Rect ToRect(int atlasHeight)
            {
                return new Rect(X, atlasHeight - (Y + Height), Width, Height);
            }
        }

        /// <summary>一张图集。</summary>
        internal sealed class Atlas
        {
            public string SourcePath;

            /// <summary>
            /// 同目录下的贴图文件名。元素图集写 <c>atlas</c>，剑类武器图集写 <c>image</c>，
            /// 法杖/刀类是双层图集，本体层写 <c>body</c>。
            /// </summary>
            public string ImageFile;

            public readonly List<Frame> Frames = new List<Frame>();
        }

        /// <summary>读一张图集。文件缺失或设结构对不上时返回 false，并把原因写进 <paramref name="error"/>。</summary>
        public static bool TryRead(string jsonPath, out Atlas atlas, out string error)
        {
            atlas = null;
            error = null;

            string text;
            try
            {
                text = System.IO.File.ReadAllText(jsonPath);
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }

            // 连 frames 字段都没有的，不是图集——调色板、manifest、对白表都长这样，
            // 它们本来就堆在素材目录里。这里安静地跳过：逐个报「读不了」会把真正的问题淹掉。
            if (!Regex.IsMatch(text, "\"frames\"\\s*:"))
            {
                return false;
            }

            var frames = ExtractChildObjects(text, "frames");
            if (frames == null || frames.Count == 0)
            {
                // 有 frames 却一帧都读不出来，那是结构对不上，得报。
                error = "有 frames 字段，但一帧都没解析出来。";
                return false;
            }

            atlas = new Atlas
            {
                SourcePath = jsonPath,
                // 双层武器图集（法杖/刀类）没有 atlas/image，只有 body+effect：绑定取本体层——
                // 素材报告写明 body「可直接当图标用」；effect 是叠在武器上的半透明光晕，
                // 数据侧没有它的键，先不绑，等有需求再按独立键（如 *.fx）接。
                // 优先级 atlas > image > body：三层都写时按更明确的字段来。
                ImageFile = ExtractString(text, "atlas") ?? ExtractString(text, "image") ?? ExtractString(text, "body"),
            };

            foreach (var pair in frames)
            {
                atlas.Frames.Add(new Frame
                {
                    Name = pair.Key,
                    X = ExtractInt(pair.Value, "x"),
                    Y = ExtractInt(pair.Value, "y"),
                    Width = ExtractInt(pair.Value, "w"),
                    Height = ExtractInt(pair.Value, "h"),
                    SpriteKey = ExtractString(pair.Value, "spriteKey"),
                    Anchor = ExtractString(pair.Value, "anchor"),
                });
            }

            return true;
        }

        /// <summary>把锚点名翻成归一化轴心。</summary>
        public static Vector2 PivotFor(string anchor)
        {
            switch (anchor)
            {
                case "bottom-left":
                    return new Vector2(0f, 0f);
                case "center":
                    return new Vector2(0.5f, 0.5f);
                case "top-center":
                    return new Vector2(0.5f, 1f);
                default:
                    return new Vector2(0.5f, 0f);
            }
        }

        /// <summary>
        /// 取出 <paramref name="key"/> 那个对象的直接子对象，返回「子对象名 → 子对象原文」。
        /// 只认值是对象的子项；值是标量的（如 <c>"cell": 76</c>）直接跳过。
        /// </summary>
        private static List<KeyValuePair<string, string>> ExtractChildObjects(string text, string key)
        {
            var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:");
            if (!match.Success)
            {
                return null;
            }

            var open = text.IndexOf('{', match.Index + match.Length);
            if (open < 0)
            {
                return null;
            }

            var close = MatchBrace(text, open);
            if (close < 0)
            {
                return null;
            }

            var result = new List<KeyValuePair<string, string>>();
            var index = open + 1;
            string pendingName = null;

            while (index < close)
            {
                var current = text[index];

                if (current == '"')
                {
                    // 这一层里出现的字符串只可能是子对象名（子对象内部被整体跳过）。
                    pendingName = ReadString(text, ref index);
                    continue;
                }

                if (current == '{')
                {
                    var end = MatchBrace(text, index);
                    if (end < 0)
                    {
                        break;
                    }

                    if (pendingName != null)
                    {
                        result.Add(new KeyValuePair<string, string>(pendingName, text.Substring(index, end - index + 1)));
                        pendingName = null;
                    }

                    index = end + 1;
                    continue;
                }

                index++;
            }

            return result;
        }

        /// <summary>从 <paramref name="openIndex"/> 的 <c>{</c> 找到与之配对的 <c>}</c>；找不到返回 -1。</summary>
        private static int MatchBrace(string text, int openIndex)
        {
            var depth = 0;
            var index = openIndex;

            while (index < text.Length)
            {
                var current = text[index];

                if (current == '"')
                {
                    ReadString(text, ref index);
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    index++;
                    continue;
                }

                if (current == '}')
                {
                    depth--;
                    index++;
                    if (depth == 0)
                    {
                        return index - 1;
                    }

                    continue;
                }

                index++;
            }

            return -1;
        }

        /// <summary>读一个 JSON 字符串。<paramref name="index"/> 必须停在开引号上，返回时停在闭引号之后。</summary>
        private static string ReadString(string text, ref int index)
        {
            var builder = new StringBuilder();
            index++;

            while (index < text.Length)
            {
                var current = text[index];

                if (current == '\\')
                {
                    index++;
                    if (index < text.Length)
                    {
                        builder.Append(text[index]);
                        index++;
                    }

                    continue;
                }

                if (current == '"')
                {
                    index++;
                    break;
                }

                builder.Append(current);
                index++;
            }

            return builder.ToString();
        }

        private static int ExtractInt(string body, string key)
        {
            var match = Regex.Match(body, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            if (!match.Success)
            {
                return 0;
            }

            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        /// <summary>取字符串字段。值是 <c>null</c>（没写引号）时返回 null。</summary>
        private static string ExtractString(string body, string key)
        {
            var match = Regex.Match(body, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
