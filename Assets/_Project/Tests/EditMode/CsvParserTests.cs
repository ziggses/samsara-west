using NUnit.Framework;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// CSV 是策划唯一的输入面，解析器出错会直接变成「数据静默丢失」。
    /// 这里把 BOM、注释、引号、逗号、换行这几类历史事故固定成断言。
    /// </summary>
    public sealed class CsvParserTests
    {
        [Test]
        public void Parse_StripsUtf8Bom()
        {
            var table = CsvParser.Parse("\uFEFFkey,text\nui.a,甲\n");

            Assert.IsTrue(table.HasColumn("key"), "带 BOM 的首列名不应变成 \"\\uFEFFkey\"。");
            Assert.AreEqual("ui.a", table.Rows[0]["key"]);
        }

        [Test]
        public void Parse_CommentLine_WithCommas_IsNotTreatedAsHeader()
        {
            // 这行曾经被拆成多列并顶替表头，导致本地化导入 0 键。
            var table = CsvParser.Parse("# 列：key,text,note\nkey,text\nui.a,甲\n");

            CollectionAssert.AreEqual(new[] { "key", "text" }, table.Headers);
            Assert.AreEqual(1, table.RowCount);
            Assert.AreEqual("ui.a", table.Rows[0]["key"]);
            Assert.AreEqual("甲", table.Rows[0]["text"]);
        }

        [Test]
        public void Parse_CommentLine_WithLeadingSpaces_IsSkipped()
        {
            var table = CsvParser.Parse("key,text\n   # 缩进注释,忽略\nui.a,甲\n");

            Assert.AreEqual(1, table.RowCount);
        }

        [Test]
        public void Parse_HeaderNames_AreCaseAndWhitespaceInsensitive()
        {
            var table = CsvParser.Parse("  Key , TEXT \nui.a,甲\n");

            CollectionAssert.AreEqual(new[] { "key", "text" }, table.Headers);
            Assert.AreEqual("甲", table.Rows[0]["TEXT"]);
            Assert.AreEqual("甲", table.Rows[0].Get(" text "));
        }

        [Test]
        public void Parse_QuotedField_KeepsComma()
        {
            var table = CsvParser.Parse("key,text\nui.a,\"甲,乙\"\n");

            Assert.AreEqual("甲,乙", table.Rows[0]["text"]);
        }

        [Test]
        public void Parse_QuotedField_KeepsNewline()
        {
            var table = CsvParser.Parse("key,text\nui.a,\"第一行\n第二行\"\nui.b,乙\n");

            Assert.AreEqual(2, table.RowCount);
            StringAssert.Contains("\n", table.Rows[0]["text"]);
            Assert.AreEqual("ui.b", table.Rows[1]["key"], "引号内的换行不得切成两行。");
        }

        [Test]
        public void Parse_EscapedQuotes_AreUnescaped()
        {
            var table = CsvParser.Parse("key,text\nui.a,\"他说\"\"好\"\"\"\n");

            Assert.AreEqual("他说\"好\"", table.Rows[0]["text"]);
        }

        [Test]
        public void Parse_MissingTrailingColumns_FillEmpty()
        {
            var table = CsvParser.Parse("key,text,note\nui.a,甲\n");

            Assert.AreEqual(string.Empty, table.Rows[0]["note"]);
        }

        [Test]
        public void Parse_EmptyRows_AreDropped()
        {
            var table = CsvParser.Parse("key,text\nui.a,甲\n,,\n\n");

            Assert.AreEqual(1, table.RowCount);
        }

        [Test]
        public void Parse_LastRowWithoutNewline_IsKept()
        {
            var table = CsvParser.Parse("key,text\nui.a,甲");

            Assert.AreEqual(1, table.RowCount);
            Assert.AreEqual("甲", table.Rows[0]["text"]);
        }

        [Test]
        public void Parse_TracksLineNumbersForDiagnostics()
        {
            var table = CsvParser.Parse("# 注释\nkey,text\nui.a,甲\nui.b,乙\n");

            Assert.AreEqual(3, table.Rows[0].LineNumber);
            Assert.AreEqual(4, table.Rows[1].LineNumber);
            Assert.AreEqual(0, table.Rows[0].RowIndex);
            Assert.AreEqual(1, table.Rows[1].RowIndex);
        }

        [Test]
        public void Parse_TryGet_DistinguishesMissingColumnFromEmptyValue()
        {
            var table = CsvParser.Parse("key,text\nui.a,\n");

            Assert.IsTrue(table.Rows[0].TryGet("text", out var empty));
            Assert.AreEqual(string.Empty, empty);
            Assert.IsFalse(table.Rows[0].TryGet("note", out _));
        }

        [Test]
        public void Parse_NoHeader_Throws()
        {
            var exception = Assert.Throws<CsvFormatException>(() => CsvParser.Parse("# 只有注释\n"));
            Assert.AreEqual(1, exception.LineNumber);
        }

        [Test]
        public void Parse_NullText_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => CsvParser.Parse(null));
        }

        [Test]
        public void Parse_EmptyText_Throws()
        {
            Assert.Throws<CsvFormatException>(() => CsvParser.Parse(string.Empty));
        }

        [Test]
        public void ComputeHash_IsStableForIdenticalContent()
        {
            var left = CsvParser.Parse("key,text\nui.a,甲\n", "loc.csv");
            var right = CsvParser.Parse("key,text\nui.a,甲\n", "loc.csv");

            Assert.AreEqual(left.ComputeHash(), right.ComputeHash());
        }

        [Test]
        public void ComputeHash_ChangesWhenAnyValueChanges()
        {
            var baseline = CsvParser.Parse("key,text\nui.a,甲\n", "loc.csv");
            var changed = CsvParser.Parse("key,text\nui.a,乙\n", "loc.csv");

            Assert.AreNotEqual(baseline.ComputeHash(), changed.ComputeHash(), "哈希变了才会重新导入，否则增量导入会漏更新。");
        }

        [Test]
        public void ComputeHash_IncludesSourceName()
        {
            var left = CsvParser.Parse("key,text\nui.a,甲\n", "a.csv");
            var right = CsvParser.Parse("key,text\nui.a,甲\n", "b.csv");

            Assert.AreNotEqual(left.ComputeHash(), right.ComputeHash());
        }
    }
}
