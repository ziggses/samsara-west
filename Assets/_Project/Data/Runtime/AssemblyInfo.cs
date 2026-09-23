using System.Runtime.CompilerServices;

// 数据层的「写入口」全部是 internal：SetIdentity / SetIcon / CsvRow 构造。
// 只有编辑器导入管线与测试程序集可以拿到它们，运行时代码无法绕过校验直接改数据。
[assembly: InternalsVisibleTo("SamsaraWest.Editor")]
[assembly: InternalsVisibleTo("SamsaraWest.Tests.EditMode")]
[assembly: InternalsVisibleTo("SamsaraWest.Tests.PlayMode")]
