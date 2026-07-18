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
    /// <para>
    /// <b>P0-A2 修复</b>：与桌面模式（<c>transport.template.js</c>）的 API 表面对齐：
    /// <list type="bullet">
    /// <item>消息格式统一为 <c>{ id, type, payload }</c>（与 <see cref="Wails.Net.Application.Transport.MessageProcessor"/> 兼容）。</item>
    /// <item>响应格式统一解包嵌套 <c>{ id, type: "response", result: { result, error } }</c>。</item>
    /// <item>事件订阅/派发 API 统一为 <c>window._wailsOnEvent</c> / <c>window._wailsEmitEvent</c>，
    /// 与桌面模式共用同一套前端事件回调注册表。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="options">运行时生成选项，<see cref="RuntimeOptions.WebSocketUrl" /> 指定连接地址。</param>
    /// <returns>生成的 Server 模式运行时 JavaScript 代码字符串。</returns>
    public static string Generate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var webSocketUrl = JsonSerializer.Serialize(options.WebSocketUrl);
        var assetServerUrl = JsonSerializer.Serialize(options.AssetServerUrl);

        return $$"""
        // WailsNET Server Runtime - 自动生成，请勿手动修改
        // P0-A2: 与桌面模式 transport.template.js 的 API 表面、消息格式、响应解包逻辑对齐。
        (function() {
          var wsUrl = {{webSocketUrl}};
          var assetUrl = {{assetServerUrl}};
          var socket = null;
          var callId = 0;
          // 调用计数器与挂起的 Promise resolver，结构与桌面模式 transport.template.js 一致
          var pendingCalls = {};
          // 本地事件回调注册表：eventName → callback 数组
          var eventCallbacks = {};

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
              var msg;
              try {
                msg = JSON.parse(event.data);
              } catch (e) {
                console.error("[Wails] 无法解析 WebSocket 消息:", e);
                return;
              }

              // 1. 响应消息：{ id, type: "response", result: { result, error } }
              //    与桌面模式 transport.template.js 的解包逻辑保持一致。
              if (msg.type === "response" && pendingCalls[msg.id]) {
                var callback = pendingCalls[msg.id];
                delete pendingCalls[msg.id];
                // P0-B2：若调用已被取消，丢弃响应不触发 resolve/reject。
                if (callback.cancelled) {
                  return;
                }
                var outer = msg.result;
                if (outer && typeof outer === "object" && "result" in outer && "error" in outer) {
                  if (outer.error) {
                    callback.reject(new Error(outer.error.message || outer.error));
                  } else {
                    callback.resolve(outer.result);
                  }
                } else {
                  callback.resolve(outer);
                }
                return;
              }

              // 2. 事件消息：{ type: "event", name, data }
              //    对应 WebSocketBroadcaster.BroadcastEvent 的消息格式，
              //    调用统一的 _wailsEmitEvent 触发已注册的本地回调。
              if (msg.type === "event" && msg.name) {
                window._wailsEmitEvent(msg.name, msg.data);
                return;
              }
            };
          }

          // 通过 WebSocket 发送调用消息。
          // 消息格式与桌面模式 fetch 一致：{ id, type, payload }
          // 返回值：Promise，附带 .cancel() 方法和 .callId 属性（CancellablePromise，P0-B2）。
          window._wailsInvoke = function(method, params) {
            callId++;
            var id = String(callId);
            var promise = new Promise(function(resolve, reject) {
              pendingCalls[id] = { resolve: resolve, reject: reject, cancelled: false };

              var message = {
                id: id,
                type: method,
                payload: params
              };

              if (socket && socket.readyState === WebSocket.OPEN) {
                socket.send(JSON.stringify(message));
              } else {
                // WebSocket 未就绪，延迟重试一次（最多 1 秒）
                setTimeout(function() {
                  // 若已取消则不重试
                  if (pendingCalls[id] && pendingCalls[id].cancelled) {
                    delete pendingCalls[id];
                    return;
                  }
                  if (socket && socket.readyState === WebSocket.OPEN) {
                    socket.send(JSON.stringify(message));
                  } else {
                    delete pendingCalls[id];
                    reject(new Error("WebSocket 未连接"));
                  }
                }, 1000);
              }
            });

            // P0-B2：附加 callId 和 cancel 方法到 Promise 上（与桌面模式 transport.template.js 一致）。
            promise.callId = id;
            promise.cancel = function() {
              window._wailsCancelCall(id);
            };
            return promise;
          };

          // 取消运行中调用（P0-B2 CancellablePromise 配套）。
          // callId: 要取消的调用 ID（即 _wailsInvoke 返回 Promise 的 .callId 属性）
          // 对应 Wails v3 js-src/calls.ts 中的 cancelCall(CancelMethod, {"call-id": id})。
          // 通过 WebSocket 发送 cancel 消息，避免影响原 Promise 的状态机。
          window._wailsCancelCall = function(callId) {
            // 标记本地 pending 为已取消，使后续响应被忽略
            if (pendingCalls[callId]) {
              pendingCalls[callId].cancelled = true;
            }

            var message = {
              id: "cancel-" + callId + "-" + Date.now(),
              type: "cancel",
              payload: { callId: String(callId) }
            };

            if (socket && socket.readyState === WebSocket.OPEN) {
              try {
                socket.send(JSON.stringify(message));
              } catch (err) {
                console.error("[Wails] 取消调用请求失败:", err);
              }
            }
          };

          // 注册本地事件回调（与桌面模式 transport.template.js 完全一致）。
          // eventName: 事件名称
          // callback: 回调函数，接收事件数据
          // 返回取消订阅函数
          window._wailsOnEvent = function(eventName, callback) {
            if (!eventCallbacks[eventName]) {
              eventCallbacks[eventName] = [];
            }
            eventCallbacks[eventName].push(callback);
            return function() {
              var arr = eventCallbacks[eventName];
              if (arr) {
                var idx = arr.indexOf(callback);
                if (idx >= 0) {
                  arr.splice(idx, 1);
                }
              }
            };
          };

          // 触发本地事件回调（与桌面模式 transport.template.js 完全一致）。
          // 由后端 EventIPCTransport 通过 ExecJS 注入，
          // 或由本文件 onmessage 中收到 { type: "event", name, data } 时调用。
          // eventName: 事件名称
          // data: 事件数据
          window._wailsEmitEvent = function(eventName, data) {
            var arr = eventCallbacks[eventName];
            if (arr) {
              for (var i = 0; i < arr.length; i++) {
                try {
                  arr[i](data);
                } catch (e) {
                  console.error("[Wails] 事件回调异常:", e);
                }
              }
            }
          };

          connect();
        })();
        """;
    }
}
