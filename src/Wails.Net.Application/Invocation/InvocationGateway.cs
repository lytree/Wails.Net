using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;
using Wails.Net.Application.Windows;
using Wails.Net.Errors;

namespace Wails.Net.Application.Invocation;

/// <summary>
/// 统一调用网关：Binding 与 Plugin Command 共用同一入口、上下文、错误模型。
/// 对应融合审计 §10.3 / R2-1。
/// <para>
/// 调用顺序：先尝试 <see cref="BindingManager"/>（按方法全名/ID），未命中时回退到
/// <see cref="CommandDispatcher"/>（按命令名）。Binding 路径返回 ReferenceError
/// 表示"未找到"，网关据此判定是否需要回退；命令路径返回 RuntimeError 表示真实错误。
/// </para>
/// <para>
/// 后续 R3 阶段将在 <see cref="Authorize"/> 处接入 Scope / Capability 统一校验，
/// 当前为占位实现（默认 Allow），以保持 R2-1 的纯重构语义。
/// </para>
/// </summary>
public sealed class InvocationGateway
{
    private readonly BindingManager _bindings;
    private readonly CommandDispatcher? _commands;
    private readonly PermissionManager? _permissions;
    private readonly IServiceProvider? _services;
    private readonly ILogger<InvocationGateway>? _logger;

    /// <summary>
    /// 窗口查找委托，由宿主（<see cref="Application"/>）在平台就绪后注入。
    /// 用于将 <see cref="uint"/> 窗口 ID 解析为窗口名（窗口级 Capability 运行时隔离）。
    /// </summary>
    private Func<uint, WebviewWindow?>? _windowLookup;

    /// <summary>
    /// 构造调用网关。
    /// </summary>
    /// <param name="bindings">绑定管理器，必填。</param>
    /// <param name="commands">命令调度器，可为 null（无命令路径时不回退）。</param>
    /// <param name="permissions">权限管理器，可为 null（R3 将填充统一授权）。</param>
    /// <param name="services">DI 服务容器，用于创建 <see cref="CommandContext"/>。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public InvocationGateway(
        BindingManager bindings,
        CommandDispatcher? commands,
        PermissionManager? permissions,
        IServiceProvider? services,
        ILogger<InvocationGateway>? logger = null)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _commands = commands;
        _permissions = permissions;
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// 设置窗口查找委托。
    /// 由 <see cref="Application"/> 在 <see cref="Application.SetPlatformApp"/> 之后注入，
    /// 使网关能将调用来源的窗口 ID 解析为窗口名（窗口级 Capability 运行时隔离）。
    /// </summary>
    /// <param name="windowLookup">窗口查找委托，可为 null（清除）。</param>
    public void SetWindowLookup(Func<uint, WebviewWindow?>? windowLookup)
    {
        _windowLookup = windowLookup;
    }

    /// <summary>
    /// 统一调用入口（按方法/命令全限定名）。
    /// 优先尝试 <see cref="BindingManager"/>，未命中且已配置 <see cref="CommandDispatcher"/> 时回退到命令路径。
    /// </summary>
    /// <param name="fullName">方法或命令全限定名（如 <c>"MyApp.Services.GreetService.Greet"</c> 或 <c>"notification.show"</c>）。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="windowId">调用来源窗口 ID，可为 null。</param>
    /// <param name="origin">调用来源 URL（前端页面 origin），可为 null；用于 Capability.remote 远程 URL 校验。</param>
    /// <param name="cancellationToken">调用级取消令牌。</param>
    /// <returns>调用结果字典 <c>{ result, error }</c>。未命中任何路径时返回 Binding 的 ReferenceError 结果。</returns>
    public async Task<Dictionary<string, object?>> InvokeAsync(
        string fullName,
        JsonElement[] args,
        uint? windowId,
        string? origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(args);

        // R2-1 阶段：先尝试 Binding，未命中时回退到 Command。
        // 后续 R3 将在此调用 Authorize(...) 接入统一授权。
        var bindingResult = await _bindings.Call(fullName, args, cancellationToken).ConfigureAwait(false);
        if (!IsNotFoundResult(bindingResult, fullName))
        {
            return bindingResult;
        }

        // Binding 未命中且未配置 CommandDispatcher：保留 Binding 的 ReferenceError。
        if (_commands is null)
        {
            return bindingResult;
        }

        var commandResult = await TryDispatchCommandAsync(fullName, args, windowId, origin, cancellationToken).ConfigureAwait(false);
        if (commandResult is null)
        {
            // Command 路径也未找到：返回 Binding 的 ReferenceError，保持"未找到绑定方法"语义。
            return bindingResult;
        }

        return commandResult;
    }

    /// <summary>
    /// 统一调用入口（按 FNV-1a 哈希 ID）。
    /// 仅 <see cref="BindingManager"/> 支持 ID 查找；未命中 ID 时返回 ReferenceError。
    /// </summary>
    /// <param name="id">方法 FNV-1a 32 位哈希 ID。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="windowId">调用来源窗口 ID，可为 null。</param>
    /// <param name="origin">调用来源 URL，可为 null。</param>
    /// <param name="cancellationToken">调用级取消令牌。</param>
    /// <returns>调用结果字典。</returns>
    public async Task<Dictionary<string, object?>> InvokeByIdAsync(
        uint id,
        JsonElement[] args,
        uint? windowId,
        string? origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        // ID → 全名映射来自编译期生成的 GeneratedBindingsMetadata.Methods，运行时零反射。
        var methodName = TryResolveMethodNameById(id);
        if (methodName is null)
        {
            var error = new CallError($"未找到 ID 为 '{id}' 的绑定方法", null, CallErrorKind.ReferenceError);
            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = error.ToJson()
            };
        }

        return await InvokeAsync(methodName, args, windowId, origin, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 统一授权检查（R2-1 占位，R3 将接入完整 Scope/Capability 校验）。
    /// 当前实现：返回 <see cref="AuthorizationResult.Allow"/>，不修改现有行为。
    /// </summary>
    /// <param name="fullName">方法或命令全限定名。</param>
    /// <param name="context">调用上下文。</param>
    /// <returns>授权结果。</returns>
    public AuthorizationResult Authorize(string fullName, ICommandContext context)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(context);
        _ = context; // R2-1 阶段未使用，预留 R3 接入点。
        return AuthorizationResult.Allow;
    }

    /// <summary>
    /// 通过 FNV-1a 哈希 ID 查找对应的方法全名。
    /// 在 <c>GeneratedBindingsMetadata.Methods</c> 中线性扫描，匹配 <c>BoundMethodInfo.Id</c>。
    /// 数据由源生成器在编译期填充，运行时零反射（遵循 AGENTS.md §3.4）。
    /// </summary>
    /// <param name="id">FNV-1a 32 位哈希 ID。</param>
    /// <returns>匹配的方法全名；未找到返回 null。</returns>
    private static string? TryResolveMethodNameById(uint id)
    {
        foreach (var method in GeneratedBindingsMetadata.Methods)
        {
            if (method.Id == id)
            {
                return method.FullName;
            }
        }
        return null;
    }

    /// <summary>
    /// 判断 Binding 结果是否为"未找到"语义（用于决定是否回退到 Command 路径）。
    /// </summary>
    private static bool IsNotFoundResult(Dictionary<string, object?> result, string name)
    {
        if (result.TryGetValue("error", out var errorObj) && errorObj is not null)
        {
            if (errorObj is Dictionary<string, object?> errorDict
                && errorDict.TryGetValue("message", out var msgObj)
                && msgObj is string msg)
            {
                return msg.Contains("未找到") && msg.Contains(name);
            }
        }
        return false;
    }

    /// <summary>
    /// 尝试通过 <see cref="CommandDispatcher"/> 派发命令。
    /// 将 JSON 参数数组转换为 <see cref="CommandDispatcher"/> 期望的 JsonElement 参数形式。
    /// </summary>
    /// <param name="commandName">命令名称。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="windowId">窗口 ID（可为 null）。</param>
    /// <param name="origin">调用来源 URL（可为 null），用于 Capability.remote 远程 URL 校验。</param>
    /// <param name="cancellationToken">调用级取消令牌（P0-B1：支持取消运行中命令调用）。</param>
    /// <returns>调用结果字典；若命令未找到则返回 null（保留 Binding ReferenceError）。</returns>
    private async Task<Dictionary<string, object?>?> TryDispatchCommandAsync(
        string commandName,
        JsonElement[] args,
        uint? windowId,
        string? origin,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonElement parametersElement;
            if (args.Length == 1)
            {
                parametersElement = args[0];
            }
            else if (args.Length > 1)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(args, JsonOptions.DefaultSerializerOptions);
                parametersElement = JsonDocument.Parse(bytes).RootElement.Clone();
            }
            else
            {
                parametersElement = default;
            }

            var request = new InvokeRequest(Guid.NewGuid(), commandName, parametersElement);

            var ctx = _services is not null
                ? new CommandContext(_services, windowId, cancellationToken, ResolveWindowName(windowId), origin)
                : null;
            var response = await _commands!.DispatchAsync(request, ctx).ConfigureAwait(false);

            if (response.Success)
            {
                return new Dictionary<string, object?>
                {
                    ["result"] = response.Result,
                    ["error"] = null
                };
            }

            // 命令未找到：返回 null，保留 BindingManager 返回的 ReferenceError，
            // 使"未知绑定"语义（ReferenceError）不被命令回退的 RuntimeError 覆盖。
            if (response.Error is not null &&
                response.Error.Contains("Command not found", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = new CallError(response.Error ?? "命令调用失败", null, CallErrorKind.RuntimeError).ToJson()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "命令 {Command} 派发失败", commandName);
            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = new CallError(ex.Message, null, CallErrorKind.RuntimeError).ToJson()
            };
        }
    }

    /// <summary>
    /// 根据窗口 ID 解析窗口名称，用于窗口级 Capability 运行时隔离。
    /// </summary>
    /// <param name="windowId">窗口 ID，可为 null。</param>
    /// <returns>窗口名称；若 <paramref name="windowId"/> 为 null 或查找失败则返回 null。</returns>
    private string? ResolveWindowName(uint? windowId)
    {
        if (windowId is null || _windowLookup is null)
        {
            return null;
        }

        try
        {
            return _windowLookup(windowId.Value)?.Name;
        }
        catch
        {
            return null;
        }
    }
}