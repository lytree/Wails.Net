// WailsNET Transport - 自动生成，请勿手动修改
(function() {
  // 调用计数器，用于匹配请求与响应
  var _callCounter = 0;
  // 挂起的 Promise resolver，键为调用 ID
  var _pending = {};
  // 本地事件回调注册表，键为事件名，值为回调函数数组
  var _eventCallbacks = {};

  // 通过 HTTP fetch 调用后端方法。
  // method: 消息类型（call、event.emit、window.setTitle 等）
  // params: 调用载荷
  window._wailsInvoke = function(method, params) {
    return new Promise(function(resolve, reject) {
      var id = ++_callCounter;
      _pending[id] = { resolve: resolve, reject: reject };

      // 构造与后端 MessageProcessor 兼容的消息格式：
      // { id, type, payload }
      var message = {
        id: String(id),
        type: method,
        payload: params
      };

      fetch("/wails/" + method, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(message)
      }).then(function(response) {
        if (!response.ok) {
          delete _pending[id];
          reject(new Error("HTTP " + response.status + ": " + response.statusText));
          return;
        }
        return response.json();
      }).then(function(data) {
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
      }).catch(function(err) {
        delete _pending[id];
        console.error("[Wails] 调用失败:", err);
        reject(err);
      });
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
})();
