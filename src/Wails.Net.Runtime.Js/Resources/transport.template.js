// Wails.NET Transport - 自动生成，请勿手动修改
(function() {
  var assetServerUrl = "{ASSET_SERVER_URL}";

  // 通过 HTTP fetch 调用后端绑定方法
  window._wailsInvoke = function(method, params) {
    var url = assetServerUrl + "/wails/" + method;
    return fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(params)
    }).then(function(response) {
      return response.json();
    }).then(function(data) {
      return data.result;
    }).catch(function(err) {
      console.error("[Wails] 调用失败:", err);
      throw err;
    });
  };
})();
