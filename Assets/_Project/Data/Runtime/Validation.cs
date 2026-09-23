using System.Collections.Generic;
using System.Text;

namespace SamsaraWest.Data
{
    public enum ValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>一条校验结论。不可变。</summary>
    public readonly struct ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string code, string message, string definitionId = null, string assetPath = null, string fieldName = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            DefinitionId = definitionId;
            AssetPath = assetPath;
            FieldName = fieldName;
        }

        public ValidationSeverity Severity { get; }

        /// <summary>稳定的机器可读错误码，便于测试断言与文档引用。</summary>
        public string Code { get; }

        public string Message { get; }

        public string DefinitionId { get; }

        public string AssetPath { get; }

        public string FieldName { get; }

        public override string ToString()
        {
            var location = string.IsNullOrEmpty(AssetPath) ? DefinitionId : AssetPath;
            var field = string.IsNullOrEmpty(FieldName) ? string.Empty : $" [{FieldName}]";
            return $"{Severity,-7} {Code,-28} {location}{field} {Message}";
        }
    }

    /// <summary>校验报告。所有校验器共用一个报告对象，最后统一汇总，避免逐条弹窗。</summary>
    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();

        public IReadOnlyList<ValidationIssue> Issues => _issues;

        public int ErrorCount { get; private set; }

        public int WarningCount { get; private set; }

        public bool HasErrors => ErrorCount > 0;

        public bool IsClean => _issues.Count == 0;

        public void Add(ValidationIssue issue)
        {
            _issues.Add(issue);
            switch (issue.Severity)
            {
                case ValidationSeverity.Error:
                    ErrorCount++;
                    break;
                case ValidationSeverity.Warning:
                    WarningCount++;
                    break;
            }
        }

        public void Error(string code, string message, string definitionId = null, string assetPath = null, string fieldName = null) =>
            Add(new ValidationIssue(ValidationSeverity.Error, code, message, definitionId, assetPath, fieldName));

        public void Warn(string code, string message, string definitionId = null, string assetPath = null, string fieldName = null) =>
            Add(new ValidationIssue(ValidationSeverity.Warning, code, message, definitionId, assetPath, fieldName));

        public void Info(string code, string message, string definitionId = null, string assetPath = null, string fieldName = null) =>
            Add(new ValidationIssue(ValidationSeverity.Info, code, message, definitionId, assetPath, fieldName));

        public void Absorb(ValidationReport other)
        {
            if (other == null)
            {
                return;
            }

            for (var i = 0; i < other._issues.Count; i++)
            {
                Add(other._issues[i]);
            }
        }

        public void Clear()
        {
            _issues.Clear();
            ErrorCount = 0;
            WarningCount = 0;
        }

        public IReadOnlyList<ValidationIssue> WithSeverity(ValidationSeverity minimum)
        {
            var filtered = new List<ValidationIssue>();
            for (var i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Severity >= minimum)
                {
                    filtered.Add(_issues[i]);
                }
            }

            return filtered;
        }

        public string Summary() =>
            $"校验完成：{_issues.Count} 条结论（{ErrorCount} 错误 / {WarningCount} 警告）。";

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine(Summary());
            for (var i = 0; i < _issues.Count; i++)
            {
                builder.AppendLine(_issues[i].ToString());
            }

            return builder.ToString();
        }
    }

    /// <summary>可插拔的额外校验规则。定义类自身的通用规则由 <see cref="DefinitionBase.Validate"/> 负责。</summary>
    public interface IDefinitionValidator
    {
        /// <summary>稳定标识，用于日志与测试断言。</summary>
        string Name { get; }

        void Validate(DefinitionBase definition, ValidationReport report);
    }
}
