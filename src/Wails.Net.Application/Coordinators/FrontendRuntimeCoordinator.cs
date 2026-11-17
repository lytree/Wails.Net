using System.Text.Json;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Coordinators;

/// <summary>
/// 前端运行时注入协调器（RH-HOST-02）。
/// <para>
/// 负责生成后端 → 前端的最小配置片段（<c>window._wails</c> 标志对象），
/// 从 <see cref="Wails.Net.Application.Application.GenerateRuntimeJs"/> 抽出，使
/// "前端运行时视图"与"应用聚合根"解耦：输入只有平台应用、传输层与 debug 标志，输出是纯字符串。
/// </para>
/// <para>
/// 完整前端运行时（<c>wails</c> 对象、46 个命名空间）已迁至 npm 包 <c>@wailsnet/runtime</c>，
/// 本协调器只注入前端初始化时读取的标志（对应
/// <c>@wailsnet/runtime/src/internal/types.ts#readRuntimeFlags()</c>）。
/// </para>
/// </summary>
public sealed class FrontendRuntimeCoordinator
{
    /// <summary>
    /// 生成 JavaScript 运行时代码（<c>window._wails</c> 标志 + Server 模式 stream factory）。
    /// </summary>
    /// <param name="platformApp">平台应用实例；为 null 表示纯 Server 模式。</param>
    /// <param name="transport">传输层实例；Server 模式下用于注入 URL 与 handshake token。</param>
    /// <param name="isDebug">是否调试模式。</param>
    /// <returns>可直接注入页面的 JS 片段。</returns>
    public string GenerateRuntimeJs(IPlatformApp? platformApp, ITransport? transport, bool isDebug)
    {
        var platform = platformApp switch
        {
            null => "server",
            _ when OperatingSystem.IsWindows() => "windows",
            _ when OperatingSystem.IsLinux() => "linux",
            _ => "unknown",
        };

        var isServerMode = platformApp is null or Platform.ServerMode.ServerPlatformApp;

        // P0-D：Server 模式事件 API 完善 — 将传输层 URL 注入标志对象，
        // 使前端 @wailsnet/runtime 的 Transport.init() 能连接到正确的端点。
        // OPT-IPC-002：同时注入进程级 handshake token（ipcToken），
        // 前端发起 HTTP/WebSocket IPC 请求时回传校验；token 不写入日志。
        string webSocketUrl = string.Empty;
        string assetServerUrl = string.Empty;
        string ipcToken = string.Empty;
        if (isServerMode && transport is WebSocketTransport wsTransport)
        {
            // RH-IPC-04：独立 HTTP 监听传输（原 HttpTransport）已移除，
            // Server 模式唯一的宿主级传输就是 WebSocketTransport，
            // 因此这里不再保留"传输是 HttpTransport 时只注入 assetServerUrl"的分支。
            webSocketUrl = wsTransport.WebSocketUrl;
            assetServerUrl = wsTransport.BaseUrl;
            ipcToken = wsTransport.HandshakeToken ?? string.Empty;
        }

        // JSON 序列化保证字符串安全转义（前端 wails 标志字段须为合法 JSON 值）。
        var platformJson = JsonSerializer.Serialize(platform);
        var isDebugJson = isDebug ? "true" : "false";
        var isServerModeJson = isServerMode ? "true" : "false";
        var webSocketUrlJson = JsonSerializer.Serialize(webSocketUrl);
        var assetServerUrlJson = JsonSerializer.Serialize(assetServerUrl);
        var ipcTokenJson = JsonSerializer.Serialize(ipcToken);

        // P0-1 Streams：server 模式注入真 WebSocket factory，使前端 @wailsnet/runtime 的
        // Stream(name) 直接返回真实 WebSocket（而非走桌面 held-poll）。
        // 对应 Wails v3 Go 版本 stream_prelude_server.go 的 streamPrelude()：在 runtime
        // bundle 之前预置 window._wails.streamFactory，保证模块作用域的 Stream(...) 可用。
        // 端点与桌面 poll 共用同一 asset server 主机，故按 location 推导 URL（与 Go 一致）。
        var streamFactoryJs = isServerMode
            ? "\n          // Streams（P0-1）：server 模式注入真 WebSocket factory（对应 Go stream_prelude_server.go）。\n"
              + "          window._wails.streamFactory = function(name) {\n"
              + "            var p = location.protocol === 'https:' ? 'wss:' : 'ws:';\n"
              + "            var sock = new WebSocket(p + '//' + location.host + '/wails/stream/ws?name=' + encodeURIComponent(name));\n"
              + "            sock.binaryType = 'arraybuffer';\n"
              + "            return sock;\n"
              + "          };"
            : string.Empty;

        return $$"""
        // Wails.NET Runtime Flags - 由 FrontendRuntimeCoordinator 自动生成，请勿手动修改。
        // 前端运行时 JS（wails 对象、传输层、contextmenu 钩子等）已迁至 npm 包 @wailsnet/runtime，
        // 见 https://github.com/wails-net/wails-net/tree/main/packages/wails-net-runtime。
        // 前端必须执行 import { wails } from "@wailsnet/runtime"; 后才能调用后端。
        (function() {
          if (window.__wailsFlagsInjected) return;
          window.__wailsFlagsInjected = true;
          window._wails = {
            platform: {{platformJson}},
            isDebug: {{isDebugJson}},
            isServerMode: {{isServerModeJson}},
            webSocketUrl: {{webSocketUrlJson}},
            assetServerUrl: {{assetServerUrlJson}},
            ipcToken: {{ipcTokenJson}}
          };{{streamFactoryJs}}
          if (!window.__wailsRuntimeSkippedNotice && !window.__wailsRuntimeLoaded) {
            window.__wailsRuntimeSkippedNotice = true;
            console.info("[Wails.NET] window._wails 已注入。前端请 import { wails } from \"@wailsnet/runtime\"; 加载完整运行时。");
          }
        })();
        """;
    }
}
