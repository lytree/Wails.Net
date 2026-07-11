using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Security;

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

        var ctx = context ?? new CommandContext(_services, null, effectiveToken);
        // 若传入 context 的取消令牌不是 effectiveToken，需要重建上下文以传播超时。
        if (context is not null && context.CancellationToken != effectiveToken)
        {
            ctx = new CommandContext(context.Services, context.WindowId, effectiveToken);
        }

        try
        {
            var entry = _registry.Find(request.Method);
            if (entry == null)
            {
                _logger?.LogWarning("未找到命令: {Method}", request.Method);
                return new InvokeResponse(request.Id, false, null, $"Command not found: {request.Method}");
            }

            // 权限校验
            if (_permissionManager is not null && !_permissionManager.ValidateCommand(entry.Method))
            {
                _logger?.LogWarning("权限拒绝: 命令 {Method}", request.Method);
                return new InvokeResponse(request.Id, false, null, $"Permission denied: {request.Method}");
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
            // 反射调用，参数绑定
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

            var result = entry.Method.Invoke(entry.Instance, args);

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
