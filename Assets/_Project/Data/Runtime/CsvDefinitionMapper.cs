using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 特性驱动的 CSV → 定义对象映射。反射只在编辑器导入期使用，不进入运行时热路径。
    /// 新增一个定义类只需给字段加 <see cref="CsvColumnAttribute"/>，无需改导入代码。
    /// </summary>
    public static class CsvDefinitionMapper
    {
        /// <summary>一条字段与列的绑定关系。公开以便编辑器生成数据表结构文档。</summary>
        public sealed class FieldBinding
        {
            public FieldInfo Field;
            public string Column;
            public bool Required;

            public string FieldName => Field == null ? string.Empty : Field.Name;

            public string TypeName => Field == null ? string.Empty : Field.FieldType.Name;
        }

        private static readonly Dictionary<Type, FieldBinding[]> BindingCache = new Dictionary<Type, FieldBinding[]>();

        public static DefinitionBase Map(Type definitionType, CsvRow row, CsvTable table, ValidationReport report)
        {
            if (definitionType == null)
            {
                throw new ArgumentNullException(nameof(definitionType));
            }

            if (!typeof(DefinitionBase).IsAssignableFrom(definitionType))
            {
                throw new ArgumentException($"{definitionType.Name} 不是 DefinitionBase 的子类。", nameof(definitionType));
            }

            var instance = (DefinitionBase)ScriptableObject.CreateInstance(definitionType);
            instance.name = definitionType.Name;
            Apply(instance, row, table, report);
            return instance;
        }

        /// <summary>
        /// 把一行的内容写进<b>已存在</b>的定义实例。增量导入与重导入走这条路径，
        /// 从而保留资产已有的 GUID（场景与存档里的引用不会断）。
        /// </summary>
        public static void Apply(DefinitionBase instance, CsvRow row, CsvTable table, ValidationReport report)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            report ??= new ValidationReport();

            var definitionType = instance.GetType();
            var bindings = GetBindings(definitionType);
            var mappedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                mappedColumns.Add(binding.Column);

                if (!row.TryGet(binding.Column, out var raw))
                {
                    if (binding.Required)
                    {
                        report.Error(
                            "CSV_COLUMN_MISSING",
                            $"表格 '{table.SourceName}' 缺少必需列 '{binding.Column}'，第 {row.LineNumber} 行无法导入。",
                            fieldName: binding.Column);
                    }
                    else
                    {
                        report.Warn(
                            "CSV_COLUMN_ABSENT",
                            $"表格 '{table.SourceName}' 没有列 '{binding.Column}'，字段保持默认值。",
                            fieldName: binding.Column);
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(raw))
                {
                    if (binding.Required)
                    {
                        report.Error(
                            "CSV_VALUE_EMPTY",
                            $"必需列 '{binding.Column}' 在第 {row.LineNumber} 行为空。",
                            fieldName: binding.Column);
                    }

                    // 列存在但值为空 = 作者删掉了原内容，要清空。
                    // 只有「列不存在」才代表「保持默认值」。若这里 continue，资产里的旧值会活下来，
                    // 于是出现「表格里删了、运行时还在」的幽灵数据，且只在跨表比对时才暴露。
                    binding.Field.SetValue(instance, CreateEmptyValue(binding.Field.FieldType));
                    continue;
                }

                if (!TryConvert(raw, binding.Field.FieldType, out var converted, out var error))
                {
                    report.Error(
                        "CSV_VALUE_INVALID",
                        $"列 '{binding.Column}' 的值 '{raw}'（第 {row.LineNumber} 行）无法转换为 {binding.Field.FieldType.Name}：{error}",
                        fieldName: binding.Column);
                    continue;
                }

                binding.Field.SetValue(instance, converted);
            }

            foreach (var header in table.Headers)
            {
                if (!mappedColumns.Contains(header))
                {
                    report.Warn(
                        "CSV_COLUMN_UNMAPPED",
                        $"表格 '{table.SourceName}' 的列 '{header}' 没有任何字段绑定，将被忽略。",
                        fieldName: header);
                }
            }

            instance.name = string.IsNullOrEmpty(instance.Id) ? $"{definitionType.Name}_未命名" : instance.Id;
        }

        /// <summary>
        /// 空单元格清空成什么：字符串为 null、数组为空数组、其余值类型为零值。
        /// 数组不能给 null——各定义类的校验逻辑按「空数组」而非「null」写的，给 null 会在校验时炸掉。
        /// </summary>
        private static object CreateEmptyValue(Type type)
        {
            if (type == typeof(string))
            {
                return null;
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                return elementType == null ? Array.Empty<string>() : Array.CreateInstance(elementType, 0);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public static FieldBinding[] GetBindings(Type definitionType)
        {
            if (BindingCache.TryGetValue(definitionType, out var cached))
            {
                return cached;
            }

            var bindings = new List<FieldBinding>();
            var current = definitionType;
            while (current != null && current != typeof(ScriptableObject) && current != typeof(UnityEngine.Object))
            {
                var fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    var attribute = fields[i].GetCustomAttribute<CsvColumnAttribute>();
                    if (attribute == null)
                    {
                        continue;
                    }

                    bindings.Add(new FieldBinding
                    {
                        Field = fields[i],
                        Column = CsvRow.Normalize(attribute.Column),
                        Required = attribute.Required,
                    });
                }

                current = current.BaseType;
            }

            var result = bindings.ToArray();
            BindingCache[definitionType] = result;
            return result;
        }

        private static bool TryConvert(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            var text = raw.Trim();

            if (targetType == typeof(string))
            {
                value = text;
                return true;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    value = intValue;
                    return true;
                }

                error = "不是整数";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    value = floatValue;
                    return true;
                }

                error = "不是浮点数";
                return false;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                error = "不是浮点数";
                return false;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(text, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                if (text == "1")
                {
                    value = true;
                    return true;
                }

                if (text == "0")
                {
                    value = false;
                    return true;
                }

                error = "不是布尔值（true/false/1/0）";
                return false;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    value = Enum.Parse(targetType, text, ignoreCase: true);
                    return true;
                }
                catch (ArgumentException)
                {
                    error = $"不是 {targetType.Name} 的合法取值（{string.Join("/", Enum.GetNames(targetType))}）";
                    return false;
                }
            }

            if (targetType == typeof(string[]))
            {
                value = text.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < ((string[])value).Length; i++)
                {
                    ((string[])value)[i] = ((string[])value)[i].Trim();
                }

                return true;
            }

            if (targetType == typeof(int[]) || targetType == typeof(float[]))
            {
                // 并列数组（材料数量、掉落权重）用 ';' 分隔，与 itemIds 的位置一一对应。
                var parts = text.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (targetType == typeof(int[]))
                {
                    var numbers = new int[parts.Length];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                        {
                            error = $"第 {i + 1} 项 '{parts[i].Trim()}' 不是整数";
                            return false;
                        }
                    }

                    value = numbers;
                }
                else
                {
                    var numbers = new float[parts.Length];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                        {
                            error = $"第 {i + 1} 项 '{parts[i].Trim()}' 不是浮点数";
                            return false;
                        }
                    }

                    value = numbers;
                }

                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                error = "资源引用不能来自 CSV，请在编辑器内指定";
                return false;
            }

            error = $"不支持的字段类型 {targetType.Name}";
            return false;
        }
    }
}
