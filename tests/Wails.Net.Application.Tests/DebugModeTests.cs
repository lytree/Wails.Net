using TUnit.Core;
using Wails.Net.Application;

namespace Wails.Net.Application.Tests;

/// <summary>
/// DebugMode 模式判定的单元测试（TUnit）。
/// 覆盖 WAILS_DEBUG 环境变量 > --debug/-d 命令行参数 > DOTNET_ENVIRONMENT 的优先级链，
/// 以及 IsEnvironmentEnabled 的"仅环境变量"语义。
/// </summary>
[NotInParallel]
public sealed class DebugModeTests
{
    // ---------- IsEnabled：WAILS_DEBUG 环境变量 ----------

    /// <summary>
    /// WAILS_DEBUG=true 时判定为 Debug 模式。
    /// </summary>
    [Test]
    public async Task IsEnabled_EnvTrue_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "true");
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled()).IsTrue();
    }

    /// <summary>
    /// WAILS_DEBUG 不区分大小写（TRUE 同样生效）。
    /// </summary>
    [Test]
    public async Task IsEnabled_EnvValueCaseInsensitive_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "TRUE");
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled()).IsTrue();
    }

    /// <summary>
    /// WAILS_DEBUG=false 时，即使传入 --debug 参数也返回 false（环境变量优先级最高）。
    /// </summary>
    [Test]
    public async Task IsEnabled_EnvFalse_OverridesDebugArg()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "false");
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled(["--debug"])).IsFalse();
    }

    /// <summary>
    /// WAILS_DEBUG 为空字符串时视为未设置，继续按命令行参数判定。
    /// </summary>
    [Test]
    public async Task IsEnabled_EnvEmpty_FallsThroughToArgs()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, string.Empty);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled(["--debug"])).IsTrue();
    }

    // ---------- IsEnabled：命令行参数 ----------

    /// <summary>
    /// 环境变量未设置时，--debug 参数判定为 Debug 模式。
    /// </summary>
    [Test]
    public async Task IsEnabled_DebugArg_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled(["--debug"])).IsTrue();
    }

    /// <summary>
    /// 环境变量未设置时，-d 短参数同样判定为 Debug 模式。
    /// </summary>
    [Test]
    public async Task IsEnabled_ShortDebugArg_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled(["-d"])).IsTrue();
    }

    /// <summary>
    /// 环境变量不可解析（如 "1"）时，不阻断后续命令行参数判定。
    /// </summary>
    [Test]
    public async Task IsEnabled_EnvUnparsable_FallsThroughToArgs()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "1");
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled(["--debug"])).IsTrue();
    }

    // ---------- IsEnabled：.NET 环境变量 ----------

    /// <summary>
    /// 环境变量与参数均未设置时，DOTNET_ENVIRONMENT=Development 判定为 Debug 模式。
    /// </summary>
    [Test]
    public async Task IsEnabled_DotnetEnvironmentDevelopment_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", "Development");
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled()).IsTrue();
    }

    /// <summary>
    /// DOTNET_ENVIRONMENT 未设置时，ASPNETCORE_ENVIRONMENT=Development 判定为 Debug 模式。
    /// </summary>
    [Test]
    public async Task IsEnabled_AspNetCoreEnvironmentDevelopment_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", "Development");

        await Assert.That(DebugMode.IsEnabled()).IsTrue();
    }

    /// <summary>
    /// DOTNET_ENVIRONMENT 优先于 ASPNETCORE_ENVIRONMENT（前者非 Development 时判定 false）。
    /// </summary>
    [Test]
    public async Task IsEnabled_DotnetEnvironmentTakesPriority_OverAspNetCore()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", "Production");
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", "Development");

        await Assert.That(DebugMode.IsEnabled()).IsFalse();
    }

    // ---------- IsEnabled：无任何来源 ----------

    /// <summary>
    /// 所有来源均未设置时返回 false。
    /// </summary>
    [Test]
    public async Task IsEnabled_NoSource_ReturnsFalse()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", null);
        using var ___ = new EnvVarScope("ASPNETCORE_ENVIRONMENT", null);

        await Assert.That(DebugMode.IsEnabled()).IsFalse();
        await Assert.That(DebugMode.IsEnabled(["run", "--foo"])).IsFalse();
    }

    // ---------- IsEnvironmentEnabled：仅环境变量 ----------

    /// <summary>
    /// WAILS_DEBUG=true 时返回 true。
    /// </summary>
    [Test]
    public async Task IsEnvironmentEnabled_EnvTrue_ReturnsTrue()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "true");

        await Assert.That(DebugMode.IsEnvironmentEnabled()).IsTrue();
    }

    /// <summary>
    /// WAILS_DEBUG=false 时返回 false。
    /// </summary>
    [Test]
    public async Task IsEnvironmentEnabled_EnvFalse_ReturnsFalse()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, "false");

        await Assert.That(DebugMode.IsEnvironmentEnabled()).IsFalse();
    }

    /// <summary>
    /// IsEnvironmentEnabled 只认环境变量：--debug 参数与 .NET 环境变量均不影响。
    /// </summary>
    [Test]
    public async Task IsEnvironmentEnabled_IgnoresArgsAndDotnetEnv()
    {
        using var _ = new EnvVarScope(DebugMode.DebugEnvVar, null);
        using var __ = new EnvVarScope("DOTNET_ENVIRONMENT", "Development");

        await Assert.That(DebugMode.IsEnvironmentEnabled()).IsFalse();
    }

    // ---------- 常量 ----------

    /// <summary>
    /// DebugEnvVar 常量固定为 WAILS_DEBUG，保证与 CLI（wails dev）设置的变量一致。
    /// </summary>
    [Test]
    public async Task DebugEnvVar_ConstantValue_IsWailsDebug()
    {
        await Assert.That(DebugMode.DebugEnvVar).IsEqualTo("WAILS_DEBUG");
    }

    // ---------- 辅助 ----------

    /// <summary>
    /// 环境变量临时作用域：设置指定变量，Dispose 时恢复原值。
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _key;
        private readonly string? _original;

        public EnvVarScope(string key, string? value)
        {
            _key = key;
            _original = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_key, _original);
    }
}
