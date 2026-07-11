// Wails.NET Runtime - 自动生成，请勿手动修改
window._wails = {
  platform: "{PLATFORM}",
  isDebug: {IS_DEBUG},
  isServerMode: {IS_SERVER_MODE}
};

window.wails = {
  bindings: {
    call: function(bindingId, args) {
      return window._wailsInvoke("binding.call", { id: bindingId, args: args });
    }
  },
  events: {
    on: function(eventName, callback) {
      return window._wailsInvoke("event.on", { name: eventName, callback: callback });
    },
    emit: function(eventName, data) {
      return window._wailsInvoke("event.emit", { name: eventName, data: data });
    }
  }
};
