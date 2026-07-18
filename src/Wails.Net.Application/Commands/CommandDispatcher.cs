using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Security;

// 预定义注入类型集合，Scope 校验时跳过这些参数。
// 与 CommandInvokerCompiler 和 ExecuteCommandAsync 的参数绑定逻辑一致。

namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令调度器，处理前端调用请求。
/// 对应 Wails v3 Go 版本 bindings.go 中的调用分发逻辑。
/// 通过 <see cref="CommandRegistry"/> 查找命令并反射调用，
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
    /// 反射调用并处理异步返回值。
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

            // 权限校验：检查 [RequireCapability] 特性和 CommandEntry.RequiredCapabilities
            // 同时传入 ctx.WindowName 实现窗口级 Capability 运行时隔离（对应 Tauri v2 Capability.Windows）
            // 同时传入 ctx.Origin 实现 Capability.remote 远程 URL 校验（对应 Tauri v2 Capability.remote）
            if (_permissionManager is not null)
            {
                if (!_permissionManager.ValidateCommand(entry.Method, ctx.WindowName, ctx.Origin))
                {
                    _logger?.LogWarning("权限拒绝: 命令 {Method}（窗口={Window}，来源={Origin}）",
                        request.Method, ctx.WindowName ?? "未知",
                        string.IsNullOrEmpty(ctx.Origin) ? "本地" : ctx.Origin);
                    return new InvokeResponse(request.Id, false, null, $"Permission denied: {request.Method}");
                }

                if (entry.RequiredCapabilities.Count > 0 && !_permissionManager.ValidateCapabilities(entry.RequiredCapabilities, ctx.WindowName, ctx.Origin))
                {
                    _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capabilities}（窗口={Window}，来源={Origin}）",
                        request.Method, string.Join(", ", entry.RequiredCapabilities),
                        ctx.WindowName ?? "未知", string.IsNullOrEmpty(ctx.Origin) ? "本地" : ctx.Origin);
                    return new InvokeResponse(request.Id, false, null, $"Permission denied: {request.Method}");
                }

                // Scope 校验：检查实现 IScopeParameter 的参数对象
                var scopeValues = ExtractScopeValues(entry.Method, request.Parameters);
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
    /// Scope 校验用 JSON 序列化选项（与 CommandInvokerCompiler 保持一致）。
    /// </summary>
    private static readonly JsonSerializerOptions _scopeJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 从命令参数中提取 Scope 校验值。
    /// 遍历方法参数，支持两种提取方式：
    /// <list type="bullet">
    /// <item>实现 <see cref="IScopeParameter"/> 的 Options 类：反序列化后调用 <see cref="IScopeParameter.GetScopeValues"/></item>
    /// <item>标记 <see cref="ScopeParameterAttribute"/> 的 <c>string</c> 参数：从 JSON 中按参数名提取值</item>
    /// </list>
    /// 对应 Tauri v2 的参数级 Scope 校验。
    /// </summary>
    /// <param name="method">命令方法信息。</param>
    /// <param name="parameters">前端传入的 JSON 参数。</param>
    /// <returns>需要 Scope 校验的 (权限标识, 值) 对列表，可能为空。</returns>
    private static List<(string PermissionId, string Value)> ExtractScopeValues(MethodInfo method, JsonElement parameters)
    {
        var result = new List<(string PermissionId, string Value)>();

        // 参数的 ValueKind 为 Undefined 或 Null 时无法反序列化
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return result;
        }

        var methodParams = method.GetParameters();
        var rawText = parameters.GetRawText();

        foreach (var param in methodParams)
        {
            var paramType = param.ParameterType;

            // 跳过注入类型
            if (paramType == typeof(ICommandContext) ||
                paramType == typeof(CancellationToken) ||
                paramType == typeof(IServiceProvider))
            {
                continue;
            }

            // 分支 1：实现 IScopeParameter 的 Options 类
            if (typeof(IScopeParameter).IsAssignableFrom(paramType))
            {
                try
                {
                    var obj = JsonSerializer.Deserialize(rawText, paramType, _scopeJsonOptions);
                    if (obj is IScopeParameter scopeParam)
                    {
                        result.AddRange(scopeParam.GetScopeValues());
                    }
                }
                catch (JsonException)
                {
                    // 反序列化失败时跳过此参数（让命令执行时的正常反序列化报错）
                }
                continue;
            }

            // 分支 2：标记 [ScopeParameter] 特性的 string 参数
            var scopeAttr = param.GetCustomAttribute<ScopeParameterAttribute>();
            if (scopeAttr is not null && paramType == typeof(string))
            {
                var propName = scopeAttr.JsonPropertyName ?? ToCamelCase(param.Name ?? string.Empty);
                if (!string.IsNullOrEmpty(propName) &&
                    parameters.TryGetProperty(propName, out var propValue) &&
                    propValue.ValueKind == JsonValueKind.String)
                {
                    var value = propValue.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        result.Add((scopeAttr.PermissionId, value));
                    }
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
    /// 执行命令的终端处理器：参数绑定、反射调用和异步返回值处理。
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
            object? result;

            // 优先使用编译后的强类型调用器（零反射）
            if (entry.Invoker is not null)
            {
                result = entry.Invoker(entry.Instance, request.Parameters, ctx);
            }
            else
            {
                // 回退到反射调用（编译失败或特殊场景）
                var parameters = entry.Method.GetParameters();
                var args = new object?[parameters.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType == typeof(ICommandContext))
                    {
                        args[i] = ctx;
                    }
                    else if (parameters[i].ParameterType == typeof(CancellationToken))
                    {
                        args[i] = ctx.CancellationToken;
                    }
                    else if (parameters[i].ParameterType == typeof(IServiceProvider))
                    {
                        args[i] = ctx.Services;
                    }
                    else
                    {
                        // 从 JSON 参数绑定
                        args[i] = request.Parameters.Deserialize(parameters[i].ParameterType);
                    }
                }

                result = entry.Method.Invoke(entry.Instance, args);
            }

            // 处理异步方法
            object? returnValue;
            if (result is Task task)
            {
                await task.ConfigureAwait(false);

                // 如果是 Task<T>，需要获取 Result
                if (task.GetType().IsGenericType)
                {
                    var resultProperty = task.GetType().GetProperty("Result");
                    returnValue = resultProperty?.GetValue(task);
                }
                else
                {
                    returnValue = null;
                }
            }
            else
            {
                returnValue = result;
            }

            return new InvokeResponse(request.Id, true, returnValue, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // 仅反射回退路径可能抛出 TargetInvocationException
            _logger?.LogError(ex.InnerException, "命令执行异常: {Method}", request.Method);
            return new InvokeResponse(request.Id, false, null, ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "命令调度异常: {Method}", request.Method);
            return new InvokeResponse(request.Id, false, null, ex.Message);
        }
    }
}
