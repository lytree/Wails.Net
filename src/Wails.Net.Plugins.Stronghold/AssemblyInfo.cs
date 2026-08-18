using System.Runtime.CompilerServices;

// 暴露 internal 成员给测试程序集，便于白盒测试 StrongholdVault nonce 旋转等内部行为。
// 使用源代码 AssemblyInfo 而非 MSBuild 的 <InternalsVisibleTo> 项，可避免部分 SDK
// 在属性类型未知警告下漏发特性导致编译期 internal 检查失败的问题。
[assembly: InternalsVisibleTo("Wails.Net.Plugins.Stronghold.Tests")]
