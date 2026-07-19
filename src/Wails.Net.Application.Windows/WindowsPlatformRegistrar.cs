using System.Runtime.CompilerServices;
using Wails.Net.Application.Clipboard;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application;

/// <summary>
/// Windows 平台注册器，通过 <c>[ModuleInitializer]</c> 在模块加载时自动向
/// <see cref="PlatformFactory"/> 注册 Windows 平台的创建委托。
/// </summary>
/// <remarks>
/// 此机制替代了原本的 <c>Assembly.Load</c> + <c>Activator.CreateInstance</c> 反射路径，
/// 遵循 AGENTS.md §3.4 "禁止使用反射获取对应方法" 的约束，运行时零反射，AOT 友好。
/// <para>
/// 当用户应用引用了 <c>Wails.Net.Application.Windows</c> 程序集时，编译器会自动生成
/// 调用 <see cref="Register"/> 的模块初始化代码，无需用户手动调用。
/// </para>
/// </remarks>
internal static class WindowsPlatformRegistrar
{
    /// <summary>
    /// 模块初始化器，注册 Windows 平台应用和剪贴板的创建委托。
    /// 由 .NET 运行时在加载此程序集时自动调用一次。
    /// </summary>
    [ModuleInitializer]
    public static void Register()
    {
        PlatformFactory.RegisterPlatformApp("windows", static options => new WindowsPlatformApp(options));
        PlatformFactory.RegisterClipboard("windows", static () => new WindowsClipboard());
    }
}
