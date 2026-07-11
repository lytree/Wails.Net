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
/// 支持异步方法、依赖注入参数和 JSON 参数绑定。
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
    /// 构造命令调度器。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <param name="permissionManager">权限管理器，可为 null；未注入时跳过权限校验。</param>
    public CommandDispatcher(
        CommandRegistry registry,
        IServiceProvider services,
        ILogger<CommandDispatcher>? logger = null,
        PermissionManager? permissionManager = null)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
        _permissionManager = permissionManager;
    }

    /// <summary>
    /// 处理调用请求。
    /// 查找命令、绑定参数（支持 <see cref="ICommandContext"/>、<see cref="CancellationToken"/>、
    /// <see cref="IServiceProvider"/> 特殊参数类型和 JSON 反序列化参数），
    /// 反射调用并处理异步返回值。
    /// </summary>
    /// <param name="request">调用请求。</param>
    /// <param name="context">命令上下文，若为 null 则使用默认上下文。</param>
    /// <returns>调用响应。</returns>
    public async Task<InvokeResponse> DispatchAsync(InvokeRequest request, ICommandContext? context = null)
    {
        var ctx = context ?? new CommandContext(_services, null, CancellationToken.None);

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
