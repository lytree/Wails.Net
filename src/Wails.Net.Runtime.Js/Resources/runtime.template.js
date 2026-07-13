// WailsNET Runtime - 自动生成，请勿手动修改
window._wails = {
  platform: "{PLATFORM}",
  isDebug: {IS_DEBUG},
  isServerMode: {IS_SERVER_MODE}
};

window.wails = {
  // 便捷调用方法：通过绑定名称调用后端方法
  // 用法：const result = await wails.call("GreetingService.Greet", ["张三"]);
  call: function(name, args) {
    return window._wailsInvoke("call", { name: name, args: args || [] });
  },
  // 通过绑定 ID 调用后端方法
  bindings: {
    call: function(bindingId, args) {
      return window._wailsInvoke("call", { id: bindingId, args: args || [] });
    }
  },
  events: {
    on: function(eventName, callback) {
      return window._wailsInvoke("event", { name: eventName, callback: callback });
    },
    emit: function(eventName, data) {
      return window._wailsInvoke("event", { name: eventName, data: data });
    }
  }
};
