// Wails.NET Desktop Runtime - 自动生成，请勿手动修改
(function() {
  // 桌面平台通过原生 IPC 通道与后端通信
  window._wailsInvoke = function(method, params) {
    // 通过 Webview 原生消息通道发送请求
    // Windows: WebView2 postMessage；Linux: WebKitGTK evaluate_javascript
    var request = JSON.stringify({ method: method, params: params });
    if (window.chrome && window.chrome.webview) {
      // WebView2 IPC
      window.chrome.webview.postMessage(request);
    } else if (window.webkit && window.webkit.messageHandlers) {
      // WebKitGTK IPC
      window.webkit.messageHandlers.wails.postMessage(request);
    } else {
      console.error("[Wails] 未找到可用的原生 IPC 通道");
    }
    return new Promise(function(resolve) {
      // 响应通过 window._wailsResolve(id, result) 回调
      window._wailsPending = window._wailsPending || {};
      var id = Date.now() + Math.random();
      window._wailsPending[id] = resolve;
    });
  };

  // 后端调用此方法解析挂起的请求
  window._wailsResolve = function(id, result) {
    if (window._wailsPending && window._wailsPending[id]) {
      var callback = window._wailsPending[id];
      delete window._wailsPending[id];
      callback(result);
    }
  };
})();
