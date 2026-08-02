using System.Runtime.CompilerServices;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Testing.Recording;

namespace Wails.Net.Testing.Platform;

/// <summary>
/// Mock 平台注册器：通过 <see cref="ModuleInitializerAttribute"/> 在程序集加载时
/// 向 <see cref="PlatformFactory"/> 注册 <c>mock</c> 平台的创建委托。
/// <para>
/// 遵循 AGENTS.md §3.4：注册走强类型委托，运行时零反射，与
/// <c>WindowsPlatformRegistrar</c> / <c>LinuxPlatformRegistrar</c> / <c>AndroidPlatformRegistrar</c>
/// 完全同构。测试只需引用 <c>Wails.Net.Testing</c> 并设置
/// <c>WAILS_PLATFORM=mock</c>（或调用 <see cref="UseMockPlatform"/>），
/// 即可让 <see cref="PlatformFactory.CreatePlatformApp"/> 返回 <see cref="MockPlatformApp"/>。
/// </para>
/// </summary>
public static class MockPlatformRegistrar
{
    /// <summary>
    /// Mock 平台名称。
    /// </summary>
    public const string PlatformName = "mock";

    /// <summary>
    /// 用于强制指定平台的环境变量名称（与 <see cref="PlatformFactory"/> 中的常量一致）。
    /// </summary>
    private const string PlatformEnvVar = "WAILS_PLATFORM";

    /// <summary>
    /// 最近一次通过工厂委托创建的平台应用实例，便于测试直接取到 Mock 断言入口。
    /// </summary>
    private static MockPlatformApp? _lastPlatformApp;

    /// <summary>
    /// 最近一次通过工厂委托创建的剪贴板实例。
    /// </summary>
    private static MockClipboard? _lastClipboard;

    /// <summary>
    /// 供工厂委托共享的调用记录器，null 表示每个实例各自新建。
    /// </summary>
    private static CallRecorder? _sharedRecorder;

    /// <summary>
    /// 获取最近一次由 <see cref="PlatformFactory"/> 创建的 Mock 平台应用；
    /// 尚未创建时返回 null。
    /// </summary>
    public static MockPlatformApp? LastPlatformApp => Volatile.Read(ref _lastPlatformApp);

    /// <summary>
    /// 获取最近一次由 <see cref="PlatformFactory"/> 创建的 Mock 剪贴板；
    /// 尚未创建时返回 null。
    /// </summary>
    public static MockClipboard? LastClipboard => Volatile.Read(ref _lastClipboard);

    /// <summary>
    /// 模块初始化器：程序集加载时自动注册 Mock 平台委托。
    /// <para>
    /// 注意：.NET 运行时对 <c>[ModuleInitializer]</c> 采用 lazy 策略，
    /// 若测试项目未直接触达本程序集中的类型，可显式调用 <see cref="EnsureRegistered"/>。
    /// </para>
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Register();
    }

    /// <summary>
    /// 确保 Mock 平台委托已注册（幂等）。
    /// 用于 <see cref="PlatformFactory.ClearRegistrations"/> 之后重新注册的场景。
    /// </summary>
    public static void EnsureRegistered()
    {
        Register();
    }

    /// <summary>
    /// 设置所有后续创建的 Mock 实例共享的调用记录器。
    /// 传入 null 表示恢复为"每个实例独立记录器"。
    /// </summary>
    /// <param name="recorder">共享的调用记录器，可为 null。</param>
    public static void UseSharedRecorder(CallRecorder? recorder)
    {
        Volatile.Write(ref _sharedRecorder, recorder);
    }

    /// <summary>
    /// 将当前进程切换到 Mock 平台：注册委托并设置 <c>WAILS_PLATFORM=mock</c> 环境变量。
    /// </summary>
    /// <returns>用于恢复原有环境变量的作用域对象，<c>Dispose</c> 时自动还原。</returns>
    public static IDisposable UseMockPlatform()
    {
        Register();
        var previous = Environment.GetEnvironmentVariable(PlatformEnvVar);
        Environment.SetEnvironmentVariable(PlatformEnvVar, PlatformName);
        return new PlatformScope(previous);
    }

    /// <summary>
    /// 重置注册器的状态记录（不影响 <see cref="PlatformFactory"/> 中的委托注册）。
    /// 建议在每个测试的 SetUp 中调用，避免跨测试污染。
    /// </summary>
    public static void Reset()
    {
        Volatile.Write(ref _lastPlatformApp, null);
        Volatile.Write(ref _lastClipboard, null);
        Volatile.Write(ref _sharedRecorder, null);
    }

    /// <summary>
    /// 执行实际的委托注册。<see cref="PlatformFactory"/> 使用字典赋值，重复注册是幂等的。
    /// </summary>
    private static void Register()
    {
        PlatformFactory.RegisterPlatformApp(PlatformName, CreatePlatformApp);
        PlatformFactory.RegisterClipboard(PlatformName, CreateClipboard);
    }

    /// <summary>
    /// Mock 平台应用创建委托。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    /// <returns>Mock 平台应用实例。</returns>
    private static IPlatformApp CreatePlatformApp(ApplicationOptions options)
    {
        var app = new MockPlatformApp(options, Volatile.Read(ref _sharedRecorder));
        Volatile.Write(ref _lastPlatformApp, app);
        return app;
    }

    /// <summary>
    /// Mock 剪贴板创建委托。
    /// </summary>
    /// <returns>Mock 剪贴板实例。</returns>
    private static Wails.Net.Application.Clipboard.IClipboardImpl CreateClipboard()
    {
        var clipboard = new MockClipboard(Volatile.Read(ref _sharedRecorder));
        Volatile.Write(ref _lastClipboard, clipboard);
        return clipboard;
    }

    /// <summary>
    /// 用于恢复 <c>WAILS_PLATFORM</c> 环境变量的作用域对象。
    /// </summary>
    private sealed class PlatformScope : IDisposable
    {
        /// <summary>
        /// 进入作用域前的环境变量值。
        /// </summary>
        private readonly string? _previous;

        /// <summary>
        /// 是否已释放。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 构造作用域对象。
        /// </summary>
        /// <param name="previous">进入作用域前的环境变量值。</param>
        public PlatformScope(string? previous)
        {
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Environment.SetEnvironmentVariable(PlatformEnvVar, _previous);
        }
    }
}
