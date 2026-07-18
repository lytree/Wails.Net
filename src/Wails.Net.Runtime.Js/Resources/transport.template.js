// WailsNET Transport - 自动生成，请勿手动修改
// P0-C1：新增分块上传支持（body > 512KB 时自动切分串行发送）。
(function() {
  // 调用计数器，用于匹配请求与响应
  var _callCounter = 0;
  // 挂起的 Promise resolver，键为调用 ID
  var _pending = {};
  // 本地事件回调注册表，键为事件名，值为回调函数数组
  var _eventCallbacks = {};

  // P0-C1：分块上传阈值（512KB），与 Wails v3 js-src/runtime.ts CHUNK_THRESHOLD 一致。
  // 超过此阈值的请求体将被切分为多个 chunk 串行发送。
  var CHUNK_THRESHOLD = 512 * 1024;

  // P0-C1：所有 IPC 消息都通过 /wails/message 端点收发（与后端 MessageEndpoint 一致）。
  // 消息类型由 body 中的 type 字段区分，而非 URL 路径。
  var MESSAGE_URL = "/wails/message";

  // P0-C1：简易 nanoid 实现，生成分块会话唯一 ID。
  // 不依赖外部库，使用 crypto.getRandomValues（WebView2/现代浏览器均支持）。
  function _wailsNanoid() {
    var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
    var bytes = new Uint8Array(21);
    if (typeof crypto !== "undefined" && crypto.getRandomValues) {
      crypto.getRandomValues(bytes);
    } else {
      // 回退到 Math.random（不安全但功能可用，仅在 crypto 不可用时）
      for (var i = 0; i < bytes.length; i++) {
        bytes[i] = Math.floor(Math.random() * 256);
      }
    }
    var id = "";
    for (var i = 0; i < bytes.length; i++) {
      id += chars[bytes[i] & 63];
    }
    return id;
  }

  // 通过 HTTP fetch 调用后端方法。
  // method: 消息类型（call、event.emit、window.setTitle 等）
  // params: 调用载荷
  // 返回值：Promise，附带 .cancel() 方法和 .callId 属性（CancellablePromise，P0-B2）。
  //   - 调用 .cancel() 会向后端发送 "cancel" 消息，触发对应 callId 的 CancellationTokenSource.Cancel()。
  //   - 同时本地移除 pending resolver，使后端响应（若已发出）被忽略。
  //   - 对应 Wails v3 js-src/calls.ts 中 CancellablePromise 的 cancel 行为。
  // P0-C1：若序列化后的 body 字符串长度 > CHUNK_THRESHOLD，自动启用分块上传路径。
  window._wailsInvoke = function(method, params) {
    var id = ++_callCounter;
    var promise = new Promise(function(resolve, reject) {
      _pending[id] = { resolve: resolve, reject: reject, cancelled: false };

      // 构造与后端 MessageProcessor 兼容的消息格式：
      // { id, type, payload }
      var message = {
        id: String(id),
        type: method,
        payload: params
      };
      var bodyStr = JSON.stringify(message);

      // 处理响应的共用函数（普通路径与分块路径共享）。
      function handleResponse(response) {
        if (!response.ok) {
          if (_pending[id] && _pending[id].cancelled) {
            delete _pending[id];
            return;
          }
          delete _pending[id];
          // 尝试读取错误 body（422 错误携带 JSON 错误体）
          return response.text().then(function(text) {
            reject(new Error("HTTP " + response.status + ": " + text));
          }, function() {
            reject(new Error("HTTP " + response.status + ": " + response.statusText));
          });
        }
        return response.json();
      }

      function processResponseData(data) {
        if (_pending[id] && _pending[id].cancelled) {
          delete _pending[id];
          return;
        }
        delete _pending[id];
        if (data && data.error) {
          reject(new Error(data.error.message || data.error));
        } else {
          // 后端响应格式: { id, type, result: { result: <value>, error: null } }
          // 解包嵌套的 result 结构，将实际值传给前端
          var outer = data ? data.result : undefined;
          if (outer && typeof outer === "object" && "result" in outer && "error" in outer) {
            if (outer.error) {
              reject(new Error(outer.error.message || outer.error));
            } else {
              resolve(outer.result);
            }
          } else {
            resolve(outer);
          }
        }
      }

      function handleError(err) {
        if (_pending[id] && _pending[id].cancelled) {
          delete _pending[id];
          return;
        }
        delete _pending[id];
        console.error("[Wails] 调用失败:", err);
        reject(err);
      }

      // P0-C1：根据 body 大小选择普通路径或分块路径。
      // 阈值按字符串长度比较（与 Wails v3 runtime.ts 一致），
      // 切分时按 UTF-8 字节切片，避免在字符串索引处切分破坏非 BMP 字符（surrogate pairs）。
      var fetchPromise;
      if (bodyStr.length > CHUNK_THRESHOLD) {
        // 分块路径：_wailsSendChunked 返回已解析的 JSON data，需要再走 processResponseData
        fetchPromise = _wailsSendChunked(bodyStr).then(processResponseData);
      } else {
        fetchPromise = fetch(MESSAGE_URL, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: bodyStr
        }).then(handleResponse).then(processResponseData);
      }

      fetchPromise.catch(handleError);
    });

    // P0-B2：附加 callId 和 cancel 方法到 Promise 上，使其成为 CancellablePromise。
    // 对应 Wails v3 js-src/calls.ts 中 CancellablePromise.prototype.cancel。
    promise.callId = String(id);
    promise.cancel = function() {
      window._wailsCancelCall(String(id));
    };
    return promise;
  };

  // P0-C1：分块上传实现。
  // 将序列化后的 body 字符串转为 UTF-8 字节，按 CHUNK_THRESHOLD 切分为多个 chunk，
  // 串行 POST 同一端点，通过 x-wails-chunk-* HTTP 头标识会话。
  // 前 n-1 个 chunk 只检查 resp.ok，最后一个 chunk 的响应携带 RPC 结果。
  // 对应 Wails v3 js-src/runtime.ts sendChunked 函数。
  function _wailsSendChunked(bodyStr) {
    var chunkId = _wailsNanoid();
    var bodyBytes = new TextEncoder().encode(bodyStr);
    var totalChunks = Math.ceil(bodyBytes.length / CHUNK_THRESHOLD);

    var chain = Promise.resolve();

    // 前 n-1 个 chunk：发送后只检查 resp.ok，丢弃 body
    for (var i = 0; i < totalChunks - 1; i++) {
      (function(i) {
        chain = chain.then(function() {
          var chunk = bodyBytes.subarray(i * CHUNK_THRESHOLD, (i + 1) * CHUNK_THRESHOLD);
          return fetch(MESSAGE_URL, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "x-wails-chunk-id": chunkId,
              "x-wails-chunk-index": String(i),
              "x-wails-chunk-total": String(totalChunks)
            },
            body: chunk
          }).then(function(resp) {
            if (!resp.ok) {
              return resp.text().then(function(text) {
                throw new Error("分块 " + i + " 上传失败: HTTP " + resp.status + ": " + text);
              });
            }
          });
        });
      })(i);
    }

    // 最后一个 chunk：响应携带 RPC 结果
    chain = chain.then(function() {
      var lastChunk = bodyBytes.subarray((totalChunks - 1) * CHUNK_THRESHOLD);
      return fetch(MESSAGE_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "x-wails-chunk-id": chunkId,
          "x-wails-chunk-index": String(totalChunks - 1),
          "x-wails-chunk-total": String(totalChunks)
        },
        body: lastChunk
      });
    });

    return chain.then(function(response) {
      // 最后一个 chunk 的响应走标准响应处理流程
      if (!response.ok) {
        return response.text().then(function(text) {
          throw new Error("HTTP " + response.status + ": " + text);
        }, function() {
          throw new Error("HTTP " + response.status + ": " + response.statusText);
        });
      }
      return response.json();
    }).then(function(data) {
      // 与 _wailsInvoke 中的 processResponseData 逻辑一致
      // 但因为已在外面 _pending[id] 检查，这里直接返回 data 给调用方
      // 实际处理在 _wailsInvoke 内部的 .then(processResponseData) 中完成
      return data;
    });
  }

  // 取消运行中调用（P0-B2 CancellablePromise 配套）。
  // callId: 要取消的调用 ID（即 _wailsInvoke 返回 Promise 的 .callId 属性）
  // 对应 Wails v3 js-src/calls.ts 中的 cancelCall(CancelMethod, {"call-id": id})。
  // 内部使用独立的 fetch 发送 cancel 消息，避免影响原 Promise 的状态机。
  // 取消失败（如网络错误）忽略，仅在 console 打印错误。
  window._wailsCancelCall = function(callId) {
    // 标记本地 pending 为已取消，使后续响应被忽略
    if (_pending[callId]) {
      _pending[callId].cancelled = true;
    }

    var message = {
      id: "cancel-" + callId + "-" + Date.now(),
      type: "cancel",
      payload: { callId: String(callId) }
    };

    fetch(MESSAGE_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(message)
    }).catch(function(err) {
      console.error("[Wails] 取消调用请求失败:", err);
    });
  };

  // 注册本地事件回调。
  // eventName: 事件名称
  // callback: 回调函数，接收事件数据
  // 返回取消订阅函数
  window._wailsOnEvent = function(eventName, callback) {
    if (!_eventCallbacks[eventName]) {
      _eventCallbacks[eventName] = [];
    }
    _eventCallbacks[eventName].push(callback);
    return function() {
      var arr = _eventCallbacks[eventName];
      if (arr) {
        var idx = arr.indexOf(callback);
        if (idx >= 0) {
          arr.splice(idx, 1);
        }
      }
    };
  };

  // 触发本地事件回调（由后端推送事件时调用）。
  // eventName: 事件名称
  // data: 事件数据
  window._wailsEmitEvent = function(eventName, data) {
    var arr = _eventCallbacks[eventName];
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

  // ====================================================================
  // P0-C2：原生 IPC postMessage 通道（低延迟副通道）
  // ====================================================================
  //
  // 设计目标：
  //   - 主通道（HTTP fetch /wails/message）保留不变，覆盖大 payload、二进制、Server 模式。
  //   - 新增原生 postMessage 副通道，承载小消息（< CHUNK_THRESHOLD），降低延迟。
  //   - 对应 Wails v3 runtime_windows.go / runtime_linux.go / runtime_android.go
  //     中 `window._wails.invoke = window.chrome.webview.postMessage` 等的等价实现。
  //
  // 平台检测（与 Wails v3 js-src/system.ts 平台 invoke() 检测一致）：
  //   - Windows：window.chrome.webview.postMessage
  //   - Linux：window.webkit.messageHandlers.external.postMessage
  //   - Android：window.wails.invoke（同步阻塞，由 JavascriptInterface 实现）
  //
  // 消息协议：
  //   - 上行：直接发送 JSON 字符串（与 HTTP body 格式完全一致：{ id, type, payload }）
  //   - 下行：后端通过 PostNativeMessageAsync 推送两种消息：
  //     1. RPC 响应：{ id, type, result: { result, error } } — 与 HTTP 响应体格式一致
  //     2. 事件推送：{ type: "event", name, data } — 由 NativeIpcTransport.NotifyEvent 发出
  //
  // 通道选择：
  //   - body 字符串长度 > CHUNK_THRESHOLD → HTTP 分块上传（保留原 _wailsInvoke 逻辑）
  //   - 否则 → 原生 postMessage（低延迟）
  //   - 若原生通道不可用（_wailsNativePost undefined）→ 回退 HTTP
  //
  // 注意：取消调用（_wailsCancelCall）仍走 HTTP，因为 cancel 消息需要可靠投递。
  // ====================================================================

  // 检测原生 postMessage 通道。
  // 返回值：函数 (jsonString) => void，若平台无原生通道则返回 undefined。
  function _detectNativePost() {
    try {
      // Windows：WebView2 的 chrome.webview.postMessage
      if (typeof window.chrome !== "undefined" && window.chrome.webview
          && typeof window.chrome.webview.postMessage === "function") {
        return function(s) { window.chrome.webview.postMessage(s); };
      }
      // Linux：WebKitGTK 的 webkit.messageHandlers.external.postMessage
      if (typeof window.webkit !== "undefined" && window.webkit.messageHandlers
          && window.webkit.messageHandlers.external
          && typeof window.webkit.messageHandlers.external.postMessage === "function") {
        return function(s) { window.webkit.messageHandlers.external.postMessage(s); };
      }
      // Android：window.wails.invoke（JavascriptInterface 暴露的同步方法）
      // 注意：Android 的 invoke 为同步阻塞调用，无下行消息推送，
      // 仍依赖现有 HTTP 轮询或 EventIPCTransport 兜底。此处仅作为上行通道。
      if (typeof window.wails !== "undefined" && typeof window.wails.invoke === "function") {
        return function(s) { window.wails.invoke(s); };
      }
    } catch (e) {
      // 检测失败回退 HTTP
    }
    return undefined;
  }

  var _wailsNativePost = _detectNativePost();

  // 保存 HTTP 版本的 _wailsInvoke 作为回退路径
  var _wailsInvokeHttp = window._wailsInvoke;

  // 原生消息分发器：后端通过 PostNativeMessageAsync 推送的 JSON 字符串进入此函数。
  // 协议：
  //   - 若 data.type === "event"：解包 { name, data } 并触发本地事件回调
  //   - 否则视为 RPC 响应：按 data.id 查找 pending resolver 并 resolve/reject
  // 与 HTTP 响应处理逻辑保持一致，避免后端为两种通道维护两套序列化格式。
  window.__wailsNative = {
    onMessage: function(jsonString) {
      var data;
      try {
        data = JSON.parse(jsonString);
      } catch (e) {
        console.error("[Wails] 原生 IPC 消息解析失败:", e);
        return;
      }
      if (!data) return;

      // 事件推送分支
      if (data.type === "event" && typeof data.name === "string") {
        window._wailsEmitEvent(data.name, data.data);
        return;
      }

      // RPC 响应分支：复用 _wailsInvokeHttp 内部的 processResponseData 逻辑。
      // 由于该函数在 _wailsInvokeHttp 闭包内私有，这里复制其解包逻辑。
      // 后端响应格式: { id, type, result: { result: <value>, error: null } }
      var id = data.id;
      if (id && _pending[id]) {
        var entry = _pending[id];
        if (entry.cancelled) {
          delete _pending[id];
          return;
        }
        delete _pending[id];
        var outer = data ? data.result : undefined;
        if (outer && typeof outer === "object" && "result" in outer && "error" in outer) {
          if (outer.error) {
            entry.reject(new Error(outer.error.message || outer.error));
          } else {
            entry.resolve(outer.result);
          }
        } else {
          entry.resolve(outer);
        }
      }
    }
  };

  // 重写 _wailsInvoke：根据 body 大小自动选择原生通道或 HTTP 通道。
  // 签名与返回值与原 _wailsInvoke 完全一致（含 CancellablePromise）。
  // 取消调用（cancel）仍通过 HTTP 发送以保证可靠投递，因此 _wailsCancelCall 不变。
  window._wailsInvoke = function(method, params) {
    var id = ++_callCounter;
    var message = {
      id: String(id),
      type: method,
      payload: params
    };
    var bodyStr = JSON.stringify(message);

    // 大消息走 HTTP 分块路径（保留原逻辑）
    if (bodyStr.length > CHUNK_THRESHOLD) {
      return _wailsInvokeHttp(method, params);
    }

    // 原生通道不可用时回退 HTTP
    if (!_wailsNativePost) {
      return _wailsInvokeHttp(method, params);
    }

    // 小消息走原生 postMessage 通道
    var promise = new Promise(function(resolve, reject) {
      _pending[id] = { resolve: resolve, reject: reject, cancelled: false };
      try {
        _wailsNativePost(bodyStr);
      } catch (e) {
        delete _pending[id];
        reject(e);
      }
    });

    promise.callId = String(id);
    promise.cancel = function() {
      window._wailsCancelCall(String(id));
    };
    return promise;
  };
})();
