// WailsNET Transport - 自动生成，请勿手动修改
(function() {
  // 调用计数器，用于匹配请求与响应
  var _callCounter = 0;
  // 挂起的 Promise resolver，键为调用 ID
  var _pending = {};

  // 通过 HTTP fetch 调用后端方法
  // method: 消息类型（call、event）
  // params: 调用载荷（{name/id, args} 或 {name, data}）
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
          resolve(data ? data.result : undefined);
        }
      }).catch(function(err) {
        delete _pending[id];
        console.error("[Wails] 调用失败:", err);
        reject(err);
      });
    });
  };
})();
