using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Security;

// 预定义注入类型集合，Scope 校验时跳过这些参数。
// 与 MapCommandExtensions 和 ExecuteCommandAsync 的参数绑定逻辑一致。

namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令调度器，处理前端调用请求。
/// 对应 Wails v3 Go 版本 bindings.go 中的调用分发逻辑。
/// 通过 <see cref="CommandRegistry"/> 查找命令，调用编译期生成的强类型 <see cref="CompiledCommandInvoker"/>，
/// 支持异步方法、依赖注入参数、JSON 参数绑定、
/// <see cref="ICommandMiddleware"/> 中间件管道和命令超时机制。
/// </summary>
public sealed class CommandDispatcher
{
    /// <summary>
    /// 命令注册表。
    /// </summary>
    private readonly CommandRegistry _registry;

    /// <summary>
    /// DI 服务容器。
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    /// 日志记录器（可选）。
    /// </summary>
    private readonly ILogger<CommandDispatcher>? _logger;

    /// <summary>
    /// 权限管理器（可选）。
    /// </summary>
    private readonly PermissionManager? _permissionManager;

    /// <summary>
    /// 命令中间件列表（按注册顺序执行）。
    /// </summary>
    private readonly IReadOnlyList<ICommandMiddleware> _middlewares;

    /// <summary>
    /// 命令默认超时时间（可选）。超过此时间未完成的命令将被取消。
    /// </summary>
    private readonly TimeSpan? _defaultTimeout;

    /// <summary>
    /// 构造命令调度器。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <param name="permissionManager">权限管理器，可为 null；未注入时跳过权限校验。</param>
    /// <param name="middlewares">命令中间件列表，按顺序组成管道；未注入时跳过中间件。</param>
    /// <param name="defaultTimeout">命令默认超时时间；未指定时不启用超时。</param>
    public CommandDispatcher(
        CommandRegistry registry,
        IServiceProvider services,
        ILogger<CommandDispatcher>? logger = null,
        PermissionManager? permissionManager = null,
        IReadOnlyList<ICommandMiddleware>? middlewares = null,
        TimeSpan? defaultTimeout = null)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
        _permissionManager = permissionManager;
        _middlewares = middlewares ?? [];
        _defaultTimeout = defaultTimeout;
    }

    /// <summary>
    /// 处理调用请求。
    /// 查找命令、执行中间件管道、绑定参数（支持 <see cref="ICommandContext"/>、
    /// <see cref="CancellationToken"/>、<see cref="IServiceProvider"/> 特殊参数类型和 JSON 反序列化参数），
    /// 调用编译期生成的强类型调用器并处理异步返回值。
    /// 若设置了 <see cref="_defaultTimeout"/>，通过 <see cref="CancellationTokenSource"/>
    /// 实现自动超时取消。
    /// </summary>
    /// <param name="request">调用请求。</param>
    /// <param name="context">命令上下文，若为 null 则使用默认上下文。</param>
    /// <returns>调用响应。</returns>
    public async Task<InvokeResponse> DispatchAsync(InvokeRequest request, ICommandContext? context = null)
    {
        // 创建带超时的 CancellationTokenSource。
        CancellationTokenSource? timeoutCts = null;
        CancellationToken originalToken;
        if (_defaultTimeout is { } timeout)
        {
            timeoutCts = new CancellationTokenSource(timeout);
            originalToken = context?.CancellationToken ?? CancellationToken.None;
        }
        else
        {
            originalToken = context?.CancellationToken ?? CancellationToken.None;
        }

        // 合并外部取消令牌和超时令牌。
        CancellationTokenSource? linkedCts = null;
        var effectiveToken = originalToken;
        if (timeoutCts is not null)
        {
            if (originalToken == CancellationToken.None)
            {
                effectiveToken = timeoutCts.Token;
            }
            else
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(originalToken, timeoutCts.Token);
                effectiveToken = linkedCts.Token;
            }
        }

        var ctx = context ?? new CommandContext(_services, null, effectiveToken, null);
        // 若传入 context 的取消令牌不是 effectiveToken，需要重建上下文以传播超时。
        // 同时保留 WindowName 和 Origin 以确保权限校验信息完整。
        if (context is not null && context.CancellationToken != effectiveToken)
        {
            ctx = new CommandContext(
                context.Services, context.WindowId, effectiveToken, context.WindowName, context.Origin);
        }

        try
        {
            var entry = _registry.Find(request.Method);
            if (entry == null)
            {
                _logger?.LogWarning("未找到命令: {Method}", request.Method);
                return new InvokeResponse(request.Id, false, null, $"Command not found: {request.Method}");
            }

            // 权限校验：合并 CommandEntry.RequiredCapabilities（注册时显式声明）
            // 和 GeneratedBindingRegistry 中编译期生成的能力列表（来自 [RequireCapability] 特性），
            // 统一通过 ValidateCapabilities 校验。
            // 同时传入 ctx.WindowName 实现窗口级 Capability 运行时隔离（对应 Tauri v2 Capability.Windows）。
            // 同时传入 ctx.Origin 实现 Capability.remote 远程 URL 校验（对应 Tauri v2 Capability.remote）。
            if (_permissionManager is not null)
            {
                // 收集所需能力：entry.RequiredCapabilities + 源生成器编译期生成列表
                var requiredCapabilities = CollectRequiredCapabilities(request.Method, entry.RequiredCapabilities);
                if (requiredCapabilities.Count > 0 && !_permissionManager.ValidateCapabilities(requiredCapabilities, ctx.WindowName, ctx.Origin))
                {
                    _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capabilities}（窗口={Window}，来源={Origin}）",
                        request.Method, string.Join(", ", requiredCapabilities),
                        ctx.WindowName ?? "未知", string.IsNullOrEmpty(ctx.Origin) ? "本地" : ctx.Origin);
                    return new InvokeResponse(request.Id, false, null, $"Permission denied: {request.Method}");
                }

                // Scope 校验：通过源生成器编译期生成的 ScopeExtractor 委托提取参数值，
                // 适配 [ScopeParameter] 标记的 string 参数。
                // 实现 IScopeParameter 的 Options 类的 Scope 提取应由源生成器扩展生成 ScopeExtractor，
                // 不再通过运行时反射枚举参数（遵循 AGENTS.md §3.4）。
                var scopeValues = ExtractScopeValues(request.Method, request.Parameters);
                if (scopeValues.Count > 0 && !_permissionManager.ValidateScopes(scopeValues))
                {
                    _logger?.LogWarning("Scope 拒绝: 命令 {Method}", request.Method);
                    return new InvokeResponse(request.Id, false, null, $"Scope denied: {request.Method}");
                }
            }

            // 构建中间件管道：middlewares[0] → middlewares[1] → ... → 终端处理器（实际命令调用）。
            // 管道从最后一个中间件向前构建，使第一个中间件最先执行。
            Func<Task<InvokeResponse>> pipeline = () => ExecuteCommandAsync(entry, request, ctx);

            for (var i = _middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = _middlewares[i];
                var next = pipeline;
                pipeline = () => middleware.InvokeAsync(ctx, request, next);
            }

            return await pipeline().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            _logger?.LogWarning("命令超时: {Method}（超时 {Timeout}ms）", request.Method, _defaultTimeout!.Value.TotalMilliseconds);
            return new InvokeResponse(request.Id, false, null, $"Command timed out after {_defaultTimeout!.Value.TotalMilliseconds}ms");
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogWarning("命令取消: {Method}", request.Method);
            return new InvokeResponse(request.Id, false, null, $"Command cancelled: {ex.Message}");
        }
        finally
        {
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// Scope 校验用 JSON 序列化选项（与 MapCommandExtensions 保持一致）。
    /// </summary>
    private static readonly JsonSerializerOptions _scopeJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 收集命令所需的能力列表。
    /// 合并两个来源：
    /// <list type="bullet">
    /// <item><see cref="CommandRegistry.CommandEntry.RequiredCapabilities"/>：在 <see cref="CommandRegistry.Register"/> 注册时显式传入的能力列表。</item>
    /// <item><see cref="GeneratedBindingRegistry"/>：源生成器在编译期从 <see cref="RequireCapabilityAttribute"/> 特性提取的能力列表。</item>
    /// </list>
    /// 两来源去重后返回。无任何能力时返回空列表。
    /// </summary>
    /// <param name="methodName">命令名（与 <see cref="GeneratedBindingRegistry"/> 注册时的键一致）。</param>
    /// <param name="declaredCapabilities">注册时显式声明的能力列表。</param>
    /// <returns>去重后的能力列表，可能为空。</returns>
    private static IReadOnlyList<string> CollectRequiredCapabilities(
        string methodName,
        IReadOnlyList<string> declaredCapabilities)
    {
        // 若源生成器未生成能力列表（或生成空列表），直接返回注册时显式声明的能力
        if (!GeneratedBindingRegistry.TryGetCapabilities(methodName, out var generated) ||
            generated is null or { Count: 0 })
        {
            return declaredCapabilities;
        }

        // 合并两来源并去重
        var merged = new List<string>(declaredCapabilities);
        var seen = new HashSet<string>(declaredCapabilities, StringComparer.Ordinal);
        foreach (var cap in generated)
        {
            if (seen.Add(cap))
            {
                merged.Add(cap);
            }
        }
        return merged;
    }

    /// <summary>
    /// 从命令参数中提取 Scope 校验值。
    /// 仅使用源生成器编译期生成的 <see cref="ScopeExtractor"/> 委托，
    /// 由源生成器从标记 <see cref="ScopeParameterAttribute"/> 的参数生成，运行时零反射。
    /// 对应 Tauri v2 的参数级 Scope 校验。
    /// </summary>
    /// <param name="methodName">命令名（与 <see cref="GeneratedBindingRegistry"/> 注册时的键一致）。</param>
    /// <param name="parameters">前端传入的 JSON 参数。</param>
    /// <returns>需要 Scope 校验的 (权限标识, 值) 对列表，可能为空。</returns>
    private static List<(string PermissionId, string Value)> ExtractScopeValues(
        string methodName, JsonElement parameters)
    {
        var result = new List<(string PermissionId, string Value)>();

        // 参数的 ValueKind 为 Undefined 或 Null 时无法反序列化
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return result;
        }

        // 源生成器编译期生成的 ScopeExtractor 委托（来自 [ScopeParameter] 参数）
        if (GeneratedBindingRegistry.TryGetScopeExtractors(methodName, out var extractors) &&
            extractors is { Count: > 0 })
        {
            foreach (var extractor in extractors)
            {
                var extracted = extractor(parameters);
                if (extracted is { } pair && !string.IsNullOrEmpty(pair.Value))
                {
                    result.Add(pair);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 将参数名转换为 camelCase 形式，与 <see cref="JsonSerializerDefaults.Web"/> 的命名策略一致。
    /// </summary>
    /// <param name="s">原始参数名。</param>
    /// <returns>camelCase 形式的名称。</returns>
    private static string ToCamelCase(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>
    /// 执行命令的终端处理器：调用编译期生成的强类型调用器并处理异步返回值。
    /// 此方法作为中间件管道的终端，由最后一个中间件通过 <paramref name="next"/> 委托调用。
    /// </summary>
    /// <param name="entry">命令条目。</param>
    /// <param name="request">调用请求。</param>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>调用响应。</returns>
    private async Task<InvokeResponse> ExecuteCommandAsync(
        CommandRegistry.CommandEntry entry, InvokeRequest request, ICommandContext ctx)
    {
        try
        {
            // 使用编译期构建的强类型调用器（零反射）。
            // 调用器由 MapCommandExtensions 强类型泛型重载在编译期构建闭包，
            // 或由源生成器在编译期生成。
            // Invoker 为 null 表示命令未正确注册，不再回退到反射调用（AGENTS.md §3.4 禁止反射获取方法）。
            if (entry.Invoker is null)
            {
                throw new InvalidOperationException(
                    $"命令 '{request.Method}' 未注册调用器（Invoker 为 null），请检查 MapCommand 注册代码");
            }

            // CompiledCommandInvoker 委托统一返回 Task<object?>：闭包内部已 await 异步方法并装箱结果。
            // 调用方仅需 await，无需运行时反射提取 Task.Result（遵循 AGENTS.md §3.4）。
            var returnValue = await entry.Invoker(entry.Instance, request.Parameters, ctx).ConfigureAwait(false);

            return new InvokeResponse(request.Id, true, returnValue, null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "命令调度异常: {Method}", request.Method);
            return new InvokeResponse(request.Id, false, null, ex.Message);
        }
    }
}
