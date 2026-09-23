using System;
using System.Collections.Generic;
using System.Text;

namespace SamsaraWest.Data
{
    /// <summary>CSV 中的一行。列名大小写与首尾空白不敏感。</summary>
    public sealed class CsvRow
    {
        private readonly Dictionary<string, string> _values;

        internal CsvRow(int lineNumber, int rowIndex, Dictionary<string, string> values)
        {
            LineNumber = lineNumber;
            RowIndex = rowIndex;
            _values = values;
        }

        /// <summary>源文件中的行号（1 起，含表头），用于报错定位。</summary>
        public int LineNumber { get; }

        /// <summary>数据行序号（0 起，不含表头）。</summary>
        public int RowIndex { get; }

        public IReadOnlyDictionary<string, string> Values => _values;

        public string this[string column] => Get(column);

        public string Get(string column)
        {
            if (string.IsNullOrEmpty(column))
            {
                return string.Empty;
            }

            return _values.TryGetValue(Normalize(column), out var value) ? value : string.Empty;
        }

        public bool TryGet(string column, out string value)
        {
            if (!string.IsNullOrEmpty(column) && _values.TryGetValue(Normalize(column), out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var pair in _values)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal static string Normalize(string column) => column.Trim().ToLowerInvariant();
    }

    /// <summary>解析完成的 CSV 表。</summary>
    public sealed class CsvTable
    {
        private readonly List<CsvRow> _rows = new List<CsvRow>();
        private readonly List<string> _headers = new List<string>();

        internal CsvTable(string sourceName)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }

        public IReadOnlyList<string> Headers => _headers;

        public IReadOnlyList<CsvRow> Rows => _rows;

        public int RowCount => _rows.Count;

        public bool HasColumn(string column) => _headers.Contains(CsvRow.Normalize(column));

        internal void AddHeader(string header) => _headers.Add(CsvRow.Normalize(header));

        internal void AddRow(CsvRow row) => _rows.Add(row);

        /// <summary>内容哈希，用于增量导入时判断「这张表有没有变」。</summary>
        public ulong ComputeHash()
        {
            var builder = new StringBuilder();
            builder.Append(SourceName).Append('|');
            for (var i = 0; i < _headers.Count; i++)
            {
                builder.Append(_headers[i]).Append('\u001f');
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                for (var h = 0; h < _headers.Count; h++)
                {
                    builder.Append(row.Get(_headers[h])).Append('\u001f');
                }

                builder.Append('\u001e');
            }

            return Fnv1a(builder.ToString());
        }

        private static ulong Fnv1a(string text)
        {
            var hash = 14695981039346656037UL;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash = unchecked(hash * 1099511628211UL);
            }

            return hash;
        }
    }

    public sealed class CsvFormatException : Exception
    {
        public CsvFormatException(string message, int lineNumber)
            : base($"CSV 第 {lineNumber} 行格式错误：{message}")
        {
            LineNumber = lineNumber;
        }

        public int LineNumber { get; }
    }

    /// <summary>
    /// RFC 4180 风格的 CSV 解析器：支持引号包裹、字段内逗号与换行、双引号转义、
    /// 以及 UTF-8 BOM。以 <c>#</c> 开头的行视为注释，不计入数据。
    /// </summary>
    public static class CsvParser
    {
        public static CsvTable Parse(string text, string sourceName = "<inline>")
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            var table = new CsvTable(sourceName);
            var fields = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            var line = 1;
            var rowStartLine = 1;
            var hasHeader = false;
            var rowIndex = 0;

            void CommitField()
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            builder.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        if (character == '\n')
                        {
                            line++;
                        }

                        builder.Append(character);
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        inQuotes = true;
                        break;

                    case ',':
                        CommitField();
                        break;

                    case '\r':
                        break;

                    case '\n':
                        CommitField();
                        ConsumeRow();
                        line++;
                        rowStartLine = line;
                        fields.Clear();
                        break;

                    default:
                        builder.Append(character);
                        break;
                }
            }

            if (builder.Length > 0 || fields.Count > 0)
            {
                CommitField();
                ConsumeRow();
            }

            if (!hasHeader)
            {
                throw new CsvFormatException("缺少表头行。", 1);
            }

            return table;

            void ConsumeRow()
            {
                // 注释行：首个字段以 # 开头即整行跳过。
                // 判据不能是「整行只有一个字段」——注释里写 `# 列：key,text,note` 时会被拆成多列，
                // 于是注释被当成表头，真正的表头反被当作数据行，排查起来极难。
                if (fields.Count > 0 && fields[0].TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    return;
                }

                if (!hasHeader)
                {
                    for (var h = 0; h < fields.Count; h++)
                    {
                        table.AddHeader(fields[h]);
                    }

                    hasHeader = true;
                    return;
                }

                var values = new Dictionary<string, string>(fields.Count);
                for (var h = 0; h < table.Headers.Count; h++)
                {
                    var value = h < fields.Count ? fields[h] : string.Empty;
                    values[table.Headers[h]] = value.Trim();
                }

                var row = new CsvRow(rowStartLine, rowIndex++, values);
                if (!row.IsEmpty)
                {
                    table.AddRow(row);
                }
            }
        }
    }
}
