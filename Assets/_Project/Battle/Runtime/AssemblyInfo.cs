using System.Runtime.CompilerServices;

// 测试程序集需要直接构造 BattleUnit 并驱动 ApplyStatus / ApplyBreakDamage 这类内部方法，
// 才能把「状态叠加规则」「护体值削减与重置」这些纯规则用例写到单点，而不必绕一整场战斗。
// 这条可见性与 SamsaraWest.Data 的 AssemblyInfo.cs 保持一致；运行时代码不因此多一个反射调用。
[assembly: InternalsVisibleTo("SamsaraWest.Tests.EditMode")]
[assembly: InternalsVisibleTo("SamsaraWest.Tests.PlayMode")]
