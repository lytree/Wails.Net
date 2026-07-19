using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Core;
using Wails.Net.Application.Android.Mobile;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Android.Tests.Mobile;

/// <summary>
/// AndroidRuntimePlugin 的单元测试（TUnit）。
/// 对应 Wails v3 messageprocessor_android.go 中的 device.info / toast.show 方法。
/// 验证插件名称、命令注册、GetDeviceInfo 在非 Android 环境下的降级（仅返回 platform 字段）、
/// ShowToast 在非 Android 环境下的 no-op 行为、ToastShowOptions 属性。
/// </summary>
[NotInParallel]
public sealed class AndroidRuntimePluginTests
{
    // ---------------------------------------------------------------------
    // 基础属性 / 构造
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsAndroidRuntime()
    {
        var plugin = new AndroidRuntimePlugin();
        await Assert.That(plugin.Name).IsEqualTo("android-runtime");
    }

    [Test]
    public async Task ConfigureServices_DoesNotThrow_WithValidServices()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var services = new ServiceCollection();

        // 操作与断言：ConfigureServices 为 no-op，不应抛异常
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task ConfigureServices_DoesNotRegisterAnyService()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var services = new ServiceCollection();

        // 操作
        plugin.ConfigureServices(services);

        // 断言：AndroidRuntimePlugin 无需注册额外服务
        await Assert.That(services.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Configure_ThrowsArgumentNullException_WhenContextIsNull()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();

        // 操作与断言：Configure 内部调用 ArgumentNullException.ThrowIfNull
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ---------------------------------------------------------------------
    // 命令注册测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Configure_RegistersDeviceInfoCommand()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();

        // 操作
        plugin.Configure(context);

        // 断言：device.info 命令已注册
        await Assert.That(context.Commands.Find("device.info")).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersToastShowCommand()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();

        // 操作
        plugin.Configure(context);

        // 断言：toast.show 命令已注册
        await Assert.That(context.Commands.Find("toast.show")).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersExactlyTwoCommands()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();

        // 操作
        plugin.Configure(context);

        // 断言：仅注册 device.info 和 toast.show 两个命令
        await Assert.That(context.Commands.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Configure_DeclaresPermissions()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();
        var registrar = (FakePermissionRegistrar)context.Permissions;

        // 操作
        plugin.Configure(context);

        // 断言：声明了 2 个细粒度权限
        await Assert.That(registrar.DeclaredPermissions.Count).IsEqualTo(2);
        await Assert.That(registrar.DeclaredPermissions).Contains("android-runtime:allow-device-info");
        await Assert.That(registrar.DeclaredPermissions).Contains("android-runtime:allow-toast");
    }

    [Test]
    public async Task Configure_RegistersDefaultPermissionSet()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();
        var registrar = (FakePermissionRegistrar)context.Permissions;

        // 操作
        plugin.Configure(context);

        // 断言：注册了 1 个权限集
        await Assert.That(registrar.PermissionSets.Count).IsEqualTo(1);
        await Assert.That(registrar.PermissionSets).ContainsKey("android-runtime:default");
    }

    // ---------------------------------------------------------------------
    // GetDeviceInfo 静态方法测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetDeviceInfo_ReturnsPlatformAndroid()
    {
        // 操作
        var info = AndroidRuntimePlugin.GetDeviceInfo();

        // 断言：platform 字段始终为 "android"
        await Assert.That(info["platform"]?.ToString()).IsEqualTo("android");
    }

    [Test]
    public async Task GetDeviceInfo_ContainsOnlyPlatformField_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Build.* 访问会抛 TypeLoadException，
        // 实现捕获后保留已填充字段（仅 platform）。
        // 操作
        var info = AndroidRuntimePlugin.GetDeviceInfo();

        // 断言：非 Android 环境下仅包含 platform 字段
        await Assert.That(info.Count).IsEqualTo(1);
        await Assert.That(info).ContainsKey("platform");
    }

    [Test]
    public async Task GetDeviceInfo_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下不应抛异常
        await Assert.That(() => AndroidRuntimePlugin.GetDeviceInfo()).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // GetDeviceInfoJson 静态方法测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetDeviceInfoJson_ReturnsNonEmptyString()
    {
        // 操作
        var json = AndroidRuntimePlugin.GetDeviceInfoJson();

        // 断言：返回的 JSON 不为空
        await Assert.That(json).IsNotNull();
        await Assert.That(json.Length > 0).IsTrue();
    }

    [Test]
    public async Task GetDeviceInfoJson_ContainsPlatformField()
    {
        // 操作
        var json = AndroidRuntimePlugin.GetDeviceInfoJson();

        // 断言：JSON 包含 platform 字段
        await Assert.That(json.Contains("platform")).IsTrue();
        await Assert.That(json.Contains("android")).IsTrue();
    }

    [Test]
    public async Task GetDeviceInfoJson_ReturnsValidJson()
    {
        // 操作
        var json = AndroidRuntimePlugin.GetDeviceInfoJson();

        // 断言：可以反序列化为 Dictionary
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.TryGetProperty("platform", out var platformProp)).IsTrue();
        await Assert.That(platformProp.GetString()).IsEqualTo("android");
    }

    [Test]
    public async Task GetDeviceInfoJson_UsesCamelCaseNaming()
    {
        // 操作
        var json = AndroidRuntimePlugin.GetDeviceInfoJson();

        // 断言：JSON 使用 camelCase（JsonOptions.DefaultSerializerOptions 配置）
        // platform 字段应全小写（已是 camelCase 形式）
        await Assert.That(json.Contains("\"platform\"")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // ShowToast 静态方法测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task ShowToast_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Application.Context 为 null
        // 操作与断言：Context 为 null 时提前返回，不抛异常
        await Assert.That(() => AndroidRuntimePlugin.ShowToast("hello")).ThrowsNothing();
    }

    [Test]
    public async Task ShowToast_DoesNotThrow_WithNullMessage()
    {
        // 操作与断言：null 消息会被替换为 string.Empty，不抛异常
        await Assert.That(() => AndroidRuntimePlugin.ShowToast(null!)).ThrowsNothing();
    }

    [Test]
    public async Task ShowToast_DoesNotThrow_WithEmptyMessage()
    {
        // 操作与断言：空字符串消息不应抛异常
        await Assert.That(() => AndroidRuntimePlugin.ShowToast(string.Empty)).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // ToastShowOptions 测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task ToastShowOptions_Message_DefaultsToEmptyString()
    {
        // 安排
        var opts = new ToastShowOptions();

        // 断言：默认值为空字符串（非 null）
        await Assert.That(opts.Message).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ToastShowOptions_Message_CanBeSet()
    {
        // 安排
        var opts = new ToastShowOptions { Message = "Hello Toast" };

        // 断言
        await Assert.That(opts.Message).IsEqualTo("Hello Toast");
    }

    // ---------------------------------------------------------------------
    // 命令调用测试（通过反射调用注册的委托）
    // ---------------------------------------------------------------------

    [Test]
    public async Task DeviceInfoCommand_ReturnsJsonString_WhenInvoked()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();
        plugin.Configure(context);

        // 操作：通过反射调用 device.info 命令
        var entry = context.Commands.Find("device.info")!;
        var result = InvokeCommand(entry, CreateCommandContext());

        // 断言：返回 JSON 字符串，包含 platform 字段
        await Assert.That(result).IsNotNull();
        var json = result as string;
        await Assert.That(json).IsNotNull();
        await Assert.That(json!.Contains("android")).IsTrue();
    }

    [Test]
    public async Task ToastShowCommand_DoesNotThrow_WhenInvoked()
    {
        // 安排
        var plugin = new AndroidRuntimePlugin();
        var (context, _) = CreatePluginContext();
        plugin.Configure(context);

        // 操作与断言：通过反射调用 toast.show 命令（非 Android 环境 no-op）
        var entry = context.Commands.Find("toast.show")!;
        await Assert.That(() =>
            InvokeCommand(entry, CreateCommandContext(), new ToastShowOptions { Message = "test" }))
            .ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // 辅助方法
    // ---------------------------------------------------------------------

    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，使用真实的 <see cref="CommandRegistry"/> 和 fake 权限注册器。
    /// </summary>
    private static (IPluginContext context, ServiceCollection services) CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });
        var permissions = new FakePermissionRegistrar();

        var context = new FakePluginContext(services, commands, config, loggerFactory, permissions);
        return (context, services);
    }

    /// <summary>
    /// 创建模拟的 <see cref="ICommandContext"/>。
    /// </summary>
    private static ICommandContext CreateCommandContext()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new FakeCommandContext(provider);
    }

    /// <summary>
    /// 调用编译期构建的强类型调用器（遵循 AGENTS.md §3.4 禁令，零反射）。
    /// 自动从 args 中提取 <see cref="ICommandContext"/>（若存在），剩余参数包装为 JsonElement 后传给 Invoker。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry.CommandEntry entry, params object?[] args)
    {
        if (entry.Invoker is null)
        {
            throw new InvalidOperationException($"命令 '{entry.Name}' 未注册调用器");
        }

        // 自动从 args 中提取 ICommandContext（若存在），剩余参数作为业务参数
        ICommandContext? ctx = null;
        var remainingArgs = new List<object?>();
        foreach (var arg in args)
        {
            if (ctx is null && arg is ICommandContext c)
            {
                ctx = c;
            }
            else
            {
                remainingArgs.Add(arg);
            }
        }

        var parameters = ArgsToJsonElement(remainingArgs);
        return entry.Invoker(entry.Instance, parameters, ctx).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 将参数列表序列化为 JsonElement。
    /// 无参数时返回 default；单参数整体序列化；多参数序列化为 JSON 数组。
    /// </summary>
    private static JsonElement ArgsToJsonElement(IReadOnlyList<object?> args)
    {
        if (args is null || args.Count == 0) return default;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (args.Count == 1) return JsonSerializer.SerializeToElement(args[0], options);
        return JsonSerializer.SerializeToElement(args, options);
    }

    // ---------------------------------------------------------------------
    // Fake 实现
    // ---------------------------------------------------------------------

    /// <summary>
    /// 简单的 <see cref="IPluginContext"/> 实现，用于测试。
    /// </summary>
    private sealed class FakePluginContext : IPluginContext
    {
        public FakePluginContext(
            IServiceCollection services,
            CommandRegistry commands,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IPermissionRegistrar permissions)
        {
            Services = services;
            Commands = commands;
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Permissions = permissions;
        }

        public IServiceCollection Services { get; }
        public CommandRegistry Commands { get; }
        public IConfiguration Configuration { get; }
        public ILoggerFactory LoggerFactory { get; }
        public IPermissionRegistrar Permissions { get; }
    }

    /// <summary>
    /// 简单的 <see cref="IPermissionRegistrar"/> 实现，记录所有声明。
    /// </summary>
    private sealed class FakePermissionRegistrar : IPermissionRegistrar
    {
        public Dictionary<string, string> PermissionSets { get; } = new();
        public List<string> DeclaredPermissions { get; } = new();

        public void RegisterPermissionSet(string identifier, string description, params string[] permissions)
        {
            PermissionSets[identifier] = description;
        }

        public void DeclarePermission(string identifier, string description)
        {
            DeclaredPermissions.Add(identifier);
        }

        public void BindScope(string permissionId, IScope scope)
        {
            // 测试中无需验证 BindScope
        }
    }

    /// <summary>
    /// 简单的 <see cref="ICommandContext"/> 实现，用于测试。
    /// </summary>
    private sealed class FakeCommandContext : ICommandContext
    {
        public FakeCommandContext(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
        }

        public IServiceProvider Services { get; }
        public uint? WindowId => null;
        public string? WindowName => null;
        public string? Origin => null;
        public CancellationToken CancellationToken => CancellationToken.None;
    }
}
