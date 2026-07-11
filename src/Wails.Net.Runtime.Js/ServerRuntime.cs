using System.Text.Json;

namespace Wails.Net.Runtime.Js;

/// <summary>
/// Server 模式运行时代码生成器。
/// 对应 Wails v3 Go 版本中 Server 模式（无 GUI 容器化部署）的运行时生成逻辑。
/// 生成通过 WebSocket 与后端通信的 JS 运行时代码。
/// </summary>
public static class ServerRuntime
{
    /// <summary>
    /// 生成 Server 模式运行时 JavaScript 代码。
    /// 包含 WebSocket 连接建立、消息收发与断线重连逻辑。
    /// </summary>
    /// <param name="options">运行时生成选项，<see cref="RuntimeOptions.WebSocketUrl" /> 指定连接地址。</param>
    /// <returns>生成的 Server 模式运行时 JavaScript 代码字符串。</returns>
    public static string Generate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var webSocketUrl = JsonSerializer.Serialize(options.WebSocketUrl);
        var assetServerUrl = JsonSerializer.Serialize(options.AssetServerUrl);

        return $$"""
        // Wails.NET Server Runtime - 自动生成，请勿手动修改
        (function() {
          var wsUrl = {{webSocketUrl}};
          var assetUrl = {{assetServerUrl}};
          var socket = null;
          var pendingCalls = {};
          var callId = 0;

          // 建立 WebSocket 连接
          function connect() {
            socket = new WebSocket(wsUrl);
            socket.onopen = function() {
              console.log("[Wails] WebSocket 已连接");
            };
            socket.onclose = function() {
              console.log("[Wails] WebSocket 已断开，3 秒后重连");
              setTimeout(connect, 3000);
            };
            socket.onerror = function(err) {
              console.error("[Wails] WebSocket 错误", err);
            };
            socket.onmessage = function(event) {
              var msg = JSON.parse(event.data);
              if (msg.type === "response" && pendingCalls[msg.id]) {
                var callback = pendingCalls[msg.id];
                delete pendingCalls[msg.id];
                callback(msg.result);
              } else if (msg.type === "event") {
                window.dispatchEvent(new CustomEvent("wails:" + msg.name, { detail: msg.data }));
              }
            };
          }

          // 通过 WebSocket 发送消息
          window._wailsInvoke = function(method, params) {
            var id = ++callId;
            return new Promise(function(resolve) {
              pendingCalls[id] = resolve;
              socket.send(JSON.stringify({ id: id, method: method, params: params }));
            });
          };

          connect();
        })();
        """;
    }
}
