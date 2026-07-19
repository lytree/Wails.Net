# 事件系统实现

> 本文描述 Wails.Net 事件子系统的设计与实现，对应 Wails v3 Go 版本 `events.go` 中的 `EventProcessor` 及其周边类型。

## 1. 概述

Wails.Net 事件系统采用**事件总线（Event Bus）架构**，在前端 Webview 与后端 .NET 之间建立**双向事件通信**通道：

```
┌────────────────────────────────────────────────────────────────┐
│                        前端 (WebView / JS)                      │
│  wails.events.on(name, cb)        wails.events.emit(name, data) │
│         │                                  │                     │
│  _wailsOnEvent 注册回调            _wailsInvoke("event.emit",…)   │
└─────────┼──────────────────────────────────┼─────────────────────┘
          │ ↑ 推送: _wailsEmitEvent          │ ↓ HTTP/WebSocket
          │ │                                │
┌─────────┼──────────────────────────────────┼─────────────────────┐
│         │                                  ▼                     │
│  IWailsEventListener          MessageProcessor.ProcessEvent       │
│  NotifyEvent(name, data)      ← EventPayload                     │
│         │                                  │                     │
│         ▼                                  │                     │
│  ┌─────────────────────────────────────────┐                     │
│  │           EventProcessor               │                     │
│  │  - _listeners (ConcurrentDictionary)   │                     │
│  │  - _hooks (pre-emit 拦截)              │                     │
│  │  - Emit / On / Once / Off / Clear      │                     │
│  └─────────────────────────────────────────┘                     │
│                          后端 (.NET)                              │
└──────────────────────────────────────────────────────────────────┘
```

- **后端 → 前端**：`EventProcessor.Emit` 通过 `IWailsEventListener.NotifyEvent` 调用 `ExecuteScriptAsync("window._wailsEmitEvent(name, data)")` 推送事件。
- **前端 → 后端**：`wails.events.emit` 通过 HTTP `fetch("/wails/event.emit", …)` 发送 `EventPayload`，由 [MessageProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 解析后调用 `EventProcessor.Emit`。
- **跨窗口广播**：Server 模式下由 [WebSocketBroadcaster.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketBroadcaster.cs) 将事件同步到所有连接的 WebSocket 客户端。

事件处理器实例由 [Application.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 持有，通过 `Application.Events` 暴露给应用代码：

```csharp
private readonly EventProcessor _events = new();
public EventProcessor Events => _events;
```

## 2. EventProcessor — 事件处理器

[EventProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 是事件系统的核心，对应 Wails v3 Go 版本 `events.go` 中的 `EventProcessor`。

### 2.1 核心数据结构

```csharp
public class EventProcessor
{
    // 事件监听器注册表：事件名 → 监听器列表
    private readonly ConcurrentDictionary<string, List<EventListener>> _listeners = new();

    // Pre-emit 钩子列表，可在事件发布前拦截或取消事件
    private readonly List<Func<CustomEvent, bool>> _hooks = new();

    // 用于事件广播到传输层的监听器接口（IPC / WebSocket）
    private IWailsEventListener? _wailsEventListener;

    // 用于生成唯一监听器 ID 的计数器（线程安全递增）
    private int _nextListenerId = 1;
}
```

### 2.2 On / OnMultiple / Once — 订阅方法

三个订阅方法均返回 `int` 监听器 ID，便于后续按 ID 取消订阅：

```csharp
// 无限制订阅
public int On(string name, Action<CustomEvent> callback)
    => OnMultiple(name, callback, -1);

// 订阅事件，最多触发 maxCalls 次后自动取消（-1 表示无限制）
public int OnMultiple(string name, Action<CustomEvent> callback, int maxCalls)
{
    var id = Interlocked.Increment(ref _nextListenerId);
    var listener = new EventListener(id, name, callback, maxCalls);

    _listeners.AddOrUpdate(name, [listener], (_, list) =>
    {
        lock (list) { list.Add(listener); }
        return list;
    });

    return id;
}

// 一次性订阅：触发后自动取消
public int Once(string name, Action<CustomEvent> callback)
    => OnMultiple(name, callback, 1);
```

注册流程使用 `ConcurrentDictionary.AddOrUpdate` 保证并发安全，监听器列表内部使用 `lock` 串行化写操作。`EventListener` 内部通过 `Interlocked.Increment(ref _callCount)` 记录调用次数，通过 `IsExpired` 属性判断是否达到 `maxCalls` 上限。

### 2.3 Emit — 发布方法

```csharp
public void Emit(string name, object? data = null, uint? senderWindowID = null)
{
    var evt = new CustomEvent(name, data, senderWindowID);

    // 1. 执行 pre-emit 钩子（返回 false 取消事件）
    foreach (var hook in _hooks)
    {
        if (!hook(evt)) return;
    }

    // 2. 若事件通过 Cancel() 被取消则直接返回
    if (evt.IsCancelled) return;

    // 3. 通知传输层监听器（广播到前端）
    _wailsEventListener?.NotifyEvent(name, data);

    // 4. 通知本地订阅者，并清理已过期的监听器
    if (_listeners.TryGetValue(name, out var listeners))
    {
        List<EventListener>? toRemove = null;
        lock (listeners)
        {
            foreach (var listener in listeners)
            {
                if (evt.IsCancelled) break;
                listener.Invoke(evt);
                if (listener.IsExpired) (toRemove ??= []).Add(listener);
            }
            if (toRemove is not null)
                foreach (var item in toRemove) listeners.Remove(item);
        }
    }
}
```

Emit 的关键设计：
- **pre-emit 钩子优先**：钩子在广播和本地派发之前执行，可拦截或取消事件。
- **传输层广播在前**：通过 `NotifyEvent` 将事件推送到前端（即使本地无订阅者也会广播）。
- **支持运行时取消**：通过 `evt.Cancel()` 在监听器链中阻止后续监听器接收。

### 2.4 Off / Clear — 取消订阅

```csharp
// 按 ID 取消（遍历所有事件名查找）
public void Off(int listenerId)
{
    foreach (var kvp in _listeners)
    {
        lock (kvp.Value)
        {
            var index = kvp.Value.FindIndex(l => l.ID == listenerId);
            if (index >= 0) { kvp.Value.RemoveAt(index); return; }
        }
    }
}

// 按事件名取消所有订阅
public void Off(string name) => _listeners.TryRemove(name, out _);

// 清除所有事件订阅
public void Clear() => _listeners.Clear();
```

### 2.5 事件钩子（Hooks）

钩子允许插件或安全层在事件被派发前进行拦截，例如过滤敏感数据、应用权限策略。钩子通过 `RegisterHook` 注册：

```csharp
public void RegisterHook(Func<CustomEvent, bool> hook) => _hooks.Add(hook);
```

[TypedEvent.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/TypedEvent.cs) 同时提供 `EventHook` 辅助类，支持按事件名匹配的条件钩子：

```csharp
// 仅拦截特定事件名
EventHook.ForEvent(processor, "wails:window:closed", evt =>
{
    return ShouldAllowClose(evt.SenderWindowID); // false 取消
});
```

### 2.6 线程安全策略

- **注册表并发安全**：`ConcurrentDictionary<string, List<EventListener>>` 保证不同事件名的读写无锁。
- **监听器列表串行化**：同一事件名下的列表通过 `lock(list)` 保护，避免并发修改异常。
- **ID 生成原子化**：`Interlocked.Increment(ref _nextListenerId)` 确保监听器 ID 唯一。
- **调用计数原子化**：`Interlocked.Increment(ref _callCount)` 保证 `IsExpired` 判定准确。

## 3. 事件类型

事件类型分布在 `Wails.Net.Events` 与 `Wails.Net.Application.Events` 两个命名空间。

### 3.1 ApplicationEventType — 应用级事件

[ApplicationEventType.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/ApplicationEventType.cs) 定义 30 个应用级事件（取值 `0..29`），覆盖应用生命周期、系统状态、URL 加载、移动端平台事件等：

| 事件 | 值 | 名称（KnownEvents） |
|------|----|----|
| Started | 0 | `wails:startup` |
| Shutdown | 1 | `wails:shutdown` |
| ThemeChanged | 2 | `wails:theme:changed` |
| FileDropped | 3 | `wails:file:dropped` |
| WindowClosing / WindowClosed | 4 / 5 | `wails:window:closing` / `wails:window:closed` |
| WindowFocus / WindowFocusLost | 6 / 7 | `wails:window:focus` / `wails:window:focuslost` |
| DPIChanged | 8 | `wails:dpi:changed` |
| BatteryChanged / NetworkChanged | 9 / 10 | `wails:battery:changed` / `wails:network:changed` |
| Resume / Suspend | 11 / 12 | `wails:resume` / `wails:suspend` |
| DisplayChanged | 13 | `wails:display:changed` |
| ClipboardChanged | 14 | `wails:clipboard:changed` |
| SystemTrayClicked / SystemTrayMenuOpened | 15 / 16 | `wails:tray:click` / `wails:tray:menu:open` |
| WindowRuntimeReady | 17 | `wails:window:runtime:ready` |
| WindowEnterFullScreen / WindowExitFullScreen | 18 / 19 | `wails:window:enter:fullscreen` / `wails:window:exit:fullscreen` |
| URLStartsLoading / URLFinishedLoading / URLLoadFailed | 20 / 21 / 23 | `wails:url:*` |
| WindowBeforeUnload | 22 | `wails:window:before:unload` |
| DeepLinkReceived | 24 | `wails:deeplink:received` |
| ApplicationActive / ApplicationInactive | 25 / 26 | `wails:application:active` / `wails:application:inactive` |
| LowMemory | 27 | `wails:low:memory` |
| ScreenLocked | 28 | `wails:screen:locked` |
| ScreenUnlocked | 29 | `wails:screen:unlocked` |

> **P2 新增**：`LowMemory`、`ScreenLocked`、`ScreenUnlocked` 三个事件对应 Wails v3 Go 版本 `Common.LowMemory = 1290`、`Common.ScreenLocked = 1288`、`Common.ScreenUnlocked = 1289`，由 Android 平台事件（见 [§3.7 Android 平台事件映射](#37-android-平台事件映射)）转发而来，使移动端与桌面端可以订阅同一组公共事件名。

### 3.2 WindowEventType — 窗口事件

[WindowEventType.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/WindowEventType.cs) 定义 28 个窗口事件（取值 `1000..1027`），用于窗口创建、移动、调整大小、拖放、全屏等场景。窗口事件取值从 1000 开始，与 `ApplicationEventType` 区分。

```csharp
public enum WindowEventType : uint
{
    WindowCreated = 1000,
    WindowClosing = 1001,
    WindowClosed = 1002,
    WindowFocus = 1003,
    WindowMoved = 1005,
    WindowResized = 1006,
    WindowMinimised = 1007,
    WindowMaximised = 1008,
    WindowFullscreen = 1011,
    WindowDPIChanged = 1013,
    WindowFileDropped = 1014,
    WindowDragEnter = 1015,
    WindowDragDrop = 1017,
    WindowDevToolsOpened = 1019,
    WindowShow = 1021,
    WindowRuntimeReady = 1023,
    WindowEnterFullScreen = 1025,
    WindowTitleChanged = 1027,
    // ...
}
```

### 3.3 KnownEvents — 已知事件名常量

[KnownEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/KnownEvents.cs) 集中管理所有系统保留事件名称常量（带 `wails:` 前缀），并提供三个 `GetEventName` 重载将枚举值映射为字符串：

```csharp
public static string GetEventName(WindowEventType type)   => type switch { … };
public static string GetEventName(ApplicationEventType type) => type switch { … };

// 自动区分窗口事件（>= 1000）与应用程序事件
public static string GetEventName(uint eventType)
{
    if (eventType >= (uint)WindowEventType.WindowCreated)
    {
        if (Enum.IsDefined(typeof(WindowEventType), eventType))
            return GetEventName((WindowEventType)eventType);
    }
    else if (Enum.IsDefined(typeof(ApplicationEventType), eventType))
    {
        return GetEventName((ApplicationEventType)eventType);
    }
    return $"wails:custom:{eventType}";
}
```

未识别的 `uint` 值回退为 `wails:custom:{eventType}`，保证向前兼容。

### 3.4 CommonEvents — 通用事件检查

[CommonEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/CommonEvents.cs) 通过 `KnownEventNames` 集合与 `IsKnownEvent` 方法，防止自定义事件名与系统保留事件名冲突：

```csharp
public static bool IsKnownEvent(string name) => KnownEventNames.Contains(name);
```

### 3.5 CustomEvent — 自定义事件

[CustomEvent.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/CustomEvent.cs) 是事件在处理器内部的标准表示：

```csharp
public class CustomEvent
{
    private readonly CancellationTokenSource _cts = new();

    public string Name { get; }
    public object? Data { get; }
    public uint? SenderWindowID { get; }
    public CancellationToken CancellationToken => _cts.Token;
    public bool IsCancelled => _cts.IsCancellationRequested;

    public void Cancel() => _cts.Cancel();

    public Dictionary<string, object?> ToJson() => new()
    {
        ["name"] = Name,
        ["data"] = Data,
        ["senderWindowId"] = SenderWindowID
    };
}
```

- **SenderWindowID**：标记事件来源窗口，用于跨窗口广播时识别来源、避免回环。
- **Cancel / IsCancelled**：允许监听器中断事件传播链；与 pre-emit 钩子配合形成两层拦截机制。
- **ToJson**：用于序列化为 `EventPayload` 推送到前端。

### 3.6 TypedEvent — 强类型事件

[TypedEvent.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/TypedEvent.cs) 提供编译时类型安全的事件 API，避免运行时类型转换错误：

```csharp
public class TypedEvent<TData>
{
    public string Name { get; }
    private readonly EventProcessor _processor;

    public int On(Action<TData?> callback)  => _processor.On(Name,  evt => callback(ConvertData(evt.Data)));
    public int Once(Action<TData?> callback) => _processor.Once(Name, evt => callback(ConvertData(evt.Data)));
    public void Emit(TData? data, uint? senderWindowID = null) => _processor.Emit(Name, data, senderWindowID);
    public void Off() => _processor.Off(Name);

    // 通过 JSON 中转实现类型转换，兼容 JsonElement 与匿名对象
    private static TData? ConvertData(object? data)
    {
        if (data is null) return default;
        if (data is TData typed) return typed;
        var json = JsonSerializer.Serialize(data, JsonOptions.DefaultSerializerOptions);
        return JsonSerializer.Deserialize<TData>(json, JsonOptions.DefaultSerializerOptions);
    }
}
```

使用示例：

```csharp
var loginEvent = new TypedEvent<UserInfo>("app:user:login", app.Events);
loginEvent.On(user => Console.WriteLine($"用户登录: {user.Name}"));
loginEvent.Emit(new UserInfo { Name = "alice" }, senderWindowID: 1);
```

### 3.7 Android 平台事件映射

[AndroidPlatformEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 对应 Wails v3 Go 版本 `events_common_android.go` 中的 `commonApplicationEventMap`，将 Android 平台专属事件 ID 映射到上表的公共 `ApplicationEventType`，使应用层代码可以跨平台订阅统一事件名。

**Android 平台事件 ID（12 个，对应 Wails v3 `events.Android.*` 常量）**：

| 常量 | 值 | 说明 |
|------|----|------|
| `ActivityCreated` | 1267 | Activity onCreate |
| `ActivityStarted` | 1268 | Activity onStart |
| `ActivityResumed` | 1269 | Activity onResume |
| `ActivityPaused` | 1270 | Activity onPause |
| `ActivityStopped` | 1271 | Activity onStop |
| `ActivityDestroyed` | 1272 | Activity onDestroy |
| `ApplicationLowMemory` | 1273 | 系统内存不足（onLowMemory） |
| `BatteryChanged` | 1281 | 电池状态广播 |
| `NetworkChanged` | 1282 | 网络状态广播 |
| `ThemeChanged` | 1283 | 系统主题变化广播 |
| `ScreenLocked` | 1284 | 屏幕锁定广播 |
| `ScreenUnlocked` | 1285 | 屏幕解锁广播 |

**Android → Common 事件映射（7 个，仅这些被转发为公共事件）**：

| Android 平台事件 | 公共 ApplicationEventType | 公共事件名 |
|------------------|--------------------------|-----------|
| `ActivityCreated` (1267) | `Started` (0) | `wails:startup` |
| `ApplicationLowMemory` (1273) | `LowMemory` (27) | `wails:low:memory` |
| `BatteryChanged` (1281) | `BatteryChanged` (9) | `wails:battery:changed` |
| `NetworkChanged` (1282) | `NetworkChanged` (10) | `wails:network:changed` |
| `ThemeChanged` (1283) | `ThemeChanged` (2) | `wails:theme:changed` |
| `ScreenLocked` (1284) | `ScreenLocked` (28) | `wails:screen:locked` |
| `ScreenUnlocked` (1285) | `ScreenUnlocked` (29) | `wails:screen:unlocked` |

> 其余 5 个 Activity 生命周期事件（`ActivityStarted` / `ActivityResumed` / `ActivityPaused` / `ActivityStopped` / `ActivityDestroyed`）保留为 Android 专属事件，未映射到公共事件，对应 Wails v3 中"仅 Android 平台可订阅"的语义。

**API**：

```csharp
// 将 Android 平台事件 ID 映射到公共事件；未映射返回 null
public static ApplicationEventType? MapToCommonEvent(uint androidEventId);

// 判断 Android 事件是否已映射到公共事件
public static bool HasCommonMapping(uint androidEventId);
```

**`AndroidPlatformApp` 分发流程**：

```
Activity 生命周期回调 / BroadcastReceiver
        │
        ▼
AndroidPlatformApp.On(uint androidEventId)
        │
        ├─ 若 HasCommonMapping(androidEventId):
        │     └─ Application.HandlePlatformEvent((uint)MapToCommonEvent(androidEventId))
        │           └─ EventProcessor.Emit(KnownEvents.GetEventName(commonEvent), data, null)
        │                 └─ 前端 wails.events.on("wails:screen:locked", ...) 被触发
        │
        └─ 否则: 仅作为 Android 专属事件分发（订阅者通过原始 ID 监听）
```

`MapToCommonEvent` 设计的关键点：
- **跨平台事件名统一**：用户代码订阅 `wails:screen:locked` 即可在 Android（来自广播）与未来 iOS（若实现）上同时工作。
- **零侵入式转发**：未映射的 Android 事件不会被丢弃，仍可由 Android 专属监听器处理，对应 Wails v3 的双轨事件策略。
- **映射表不可变**：`_commonEventMap` 为 `private static readonly Dictionary<uint, ApplicationEventType>`，构造期确定，运行期只读，无需加锁。

## 4. 前端事件 API

前端运行时由 [RuntimeGenerator.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 生成并注入到 Webview，事件相关 API 暴露在 `window.wails.events` 命名空间。

### 4.1 wails.events.on — 订阅事件

```javascript
events: {
  // 注册本地事件回调。
  // 事件由后端通过 ExecuteScriptAsync 调用 _wailsEmitEvent(name, data) 推送。
  // 返回取消订阅函数。
  on: function(eventName, callback) {
    return window._wailsOnEvent(eventName, callback);
  },
  // 向后端发布事件，由 EventProcessor 广播到所有窗口。
  emit: function(eventName, data) {
    return window._wailsInvoke("event.emit", { name: eventName, data: data });
  }
}
```

### 4.2 _wailsOnEvent / _wailsEmitEvent 内部机制

实际的回调注册表由 [transport.template.js](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/Resources/transport.template.js) 维护：

```javascript
var _eventCallbacks = {};

// 注册本地事件回调，返回取消订阅函数
window._wailsOnEvent = function(eventName, callback) {
  if (!_eventCallbacks[eventName]) _eventCallbacks[eventName] = [];
  _eventCallbacks[eventName].push(callback);
  return function() {
    var arr = _eventCallbacks[eventName];
    if (arr) {
      var idx = arr.indexOf(callback);
      if (idx >= 0) arr.splice(idx, 1);
    }
  };
};

// 触发本地事件回调（由后端 ExecuteScriptAsync 推送时调用）
window._wailsEmitEvent = function(eventName, data) {
  var arr = _eventCallbacks[eventName];
  if (arr) {
    for (var i = 0; i < arr.length; i++) {
      try { arr[i](data); }
      catch (e) { console.error("[Wails] 事件回调异常:", e); }
    }
  }
};
```

关键设计：
- **回调数组隔离**：每个事件名独立数组，回调异常被 `try/catch` 包裹，不影响后续回调。
- **取消订阅函数**：`on` 返回一个闭包，调用即从数组中移除对应回调，符合 React/DOM 事件 API 习惯。

### 4.3 wails.events.emit — 发布事件

发布事件通过 `_wailsInvoke` 走标准 HTTP 通道，后端 `MessageProcessor` 解析后调用 `EventProcessor.Emit`：

```javascript
window._wailsInvoke = function(method, params) {
  return new Promise(function(resolve, reject) {
    var id = ++_callCounter;
    _pending[id] = { resolve: resolve, reject: reject };

    var message = { id: String(id), type: method, payload: params };

    fetch("/wails/" + method, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(message)
    }).then(/* 解包 result.result / result.error */);
  });
};
```

前端调用示例：

```javascript
// 订阅后端事件
const unsubscribe = wails.events.on("app:user:login", (user) => {
  console.log("用户登录:", user.name);
});

// 向后端发布事件
await wails.events.emit("frontend:ready", { page: "dashboard" });

// 取消订阅
unsubscribe();
```

## 5. 后端事件 API

### 5.1 Application.Events — 标准订阅/发布

应用代码通过 [Application.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 暴露的 `Events` 属性访问 `EventProcessor`：

```csharp
// 订阅事件
int id = app.Events.On("app:user:login", evt =>
{
    var user = (UserInfo)evt.Data!;
    Console.WriteLine($"用户登录: {user.Name} (来自窗口 {evt.SenderWindowID})");
});

// 一次性订阅
app.Events.Once(KnownEvents.Startup, _ => InitializeDatabase());

// 发布事件（指定来源窗口）
app.Events.Emit("app:user:login", new UserInfo { Name = "alice" }, senderWindowID: 1);

// 按事件名取消
app.Events.Off("app:user:login");

// 按 ID 取消
app.Events.Off(id);
```

### 5.2 Application.DispatchWindowEvent — 分发窗口事件

平台实现层在收到原生窗口事件（如 GTK4 信号、Win32 消息）时，通过 `DispatchWindowEvent` 将事件转发到事件总线：

```csharp
public void DispatchWindowEvent(uint windowId, uint eventType)
{
    var eventName = KnownEvents.GetEventName(eventType);
    _events.Emit(eventName, null, windowId);
}
```

调用示例（Windows 平台在窗口过程接收到 `WM_SIZE` 时）：

```csharp
app.DispatchWindowEvent(windowId, (uint)WindowEventType.WindowResized);
```

### 5.3 Application.HandlePlatformEvent — 处理平台级事件

`HandlePlatformEvent` 处理应用级（非窗口特定）事件，例如系统主题变化、电源状态变化：

```csharp
public void HandlePlatformEvent(uint eventId)
{
    var eventName = KnownEvents.GetEventName(eventId);
    _events.Emit(eventName, null, null);
}

public void HandlePlatformEvent(uint eventId, object? data)
{
    var eventName = KnownEvents.GetEventName(eventId);
    _events.Emit(eventName, data, null);
}
```

### 5.4 Application.InitializeFromServiceProvider — 注入传输层监听器

传输层监听器通过 DI 注入到事件处理器，在启动时桥接 `EventProcessor` 与传输层：

```csharp
var listener = serviceProvider.GetService<IWailsEventListener>();
if (listener is not null)
{
    _events.SetWailsEventListener(listener);
}
```

`IWailsEventListener` 接口由 [HttpTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs) 与 [WebSocketTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketTransport.cs) 实现：

```csharp
public interface IWailsEventListener
{
    void NotifyEvent(string eventName, object? data);
}

// HttpTransport / WebSocketTransport 实现：
public void NotifyEvent(string eventName, object? data)
{
    _broadcaster.BroadcastEvent(eventName, data);
}
```

## 6. 跨窗口事件广播

### 6.1 WebSocketBroadcaster — 多窗口同步

[WebSocketBroadcaster.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketBroadcaster.cs) 维护所有已连接 WebSocket 客户端，并提供三种广播模式：

```csharp
// 全量广播：所有连接的客户端
public async Task BroadcastEventAsync(string eventName, object? data);

// 排除指定客户端（避免回环）
public async Task BroadcastEventExceptAsync(string exceptClientId, string eventName, object? data);

// 定向发送
public async Task SendToClientAsync(string clientId, string message);
```

广播消息格式：

```csharp
var message = new
{
    type = "event",
    name = eventName,
    data
};
var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
```

客户端连接管理通过 `ConcurrentDictionary<string, WebSocket>` 与 `Interlocked.Increment(ref _nextClientId)` 保证线程安全，发送失败被静默忽略（客户端可能已断开）。

### 6.2 事件来源标记 — 避免回环

`CustomEvent.SenderWindowID` 标记事件来源窗口，在多窗口场景下可用于：

- **前端过滤**：忽略自身发起的事件，避免 UI 抖动。
- **后端定向广播**：调用 `BroadcastEventExceptAsync` 排除来源客户端。

```csharp
// 平台/插件可基于 senderWindowID 做定向广播
var senderClientId = GetClientIdByWindowId(evt.SenderWindowID);
await _broadcaster.BroadcastEventExceptAsync(senderClientId, evt.Name, evt.Data);
```

`EventProcessor.Emit` 自身在调用 `NotifyEvent` 时仅传递 `name` 与 `data`，来源窗口信息通过 `EventPayload.senderWindowId` 字段在 JSON 层传递给前端。

## 7. 事件消息格式 — EventPayload

前端 → 后端的事件消息通过 `EventPayload` 反序列化，定义于 [MessageProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs)：

```csharp
public class EventPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("senderWindowId")]
    public uint? SenderWindowID { get; set; }
}
```

完整的 JSON 消息结构（外层 `Message` 包裹 `EventPayload`）：

```json
{
  "id": "1",
  "type": "event.emit",
  "payload": {
    "name": "app:user:login",
    "data": { "name": "alice", "role": "admin" },
    "senderWindowId": 1
  }
}
```

`MessageProcessor.ProcessEvent` 解析后将事件转发到事件总线：

```csharp
private ResponseMessage? ProcessEvent(Message message)
{
    var eventPayload = message.Payload.Deserialize<EventPayload>(JsonOptions.DefaultSerializerOptions);
    if (eventPayload is null) return null;

    _events.Emit(eventPayload.Name, eventPayload.Data, eventPayload.SenderWindowID);
    return null; // 事件无需响应
}
```

事件消息类型路由：消息 `type` 字段为 `"event.emit"`，`ProcessAsync` 通过 `IndexOf('.')` 提取基础命名空间 `"event"` 路由到 `ProcessEvent`，与 `MessageTypes.Event` 常量匹配。事件返回 `null`，前端 `fetch` 收到空响应后正常 resolve。

## 8. 事件生命周期 — 注册/触发/取消

完整流程示例（多窗口 + 插件钩子场景）：

```
注册阶段：
  后端:  app.Events.On("app:doc:saved", evt => UpdateRecentList(evt.Data));
  前端:  const off = wails.events.on("app:doc:saved", doc => refreshUI(doc));
  插件:  processor.RegisterHook(evt => AuditLog(evt.Name, evt.Data)); // pre-emit

触发阶段（前端发起）：
  1. 前端 wails.events.emit("app:doc:saved", { id: 42 })
  2. fetch POST /wails/event.emit → Message.ProcessAsync
  3. ProcessEvent 反序列化 EventPayload
  4. EventProcessor.Emit("app:doc:saved", { id: 42 }, senderWindowID: null)
     ├─ 4.1  pre-emit 钩子链（每个钩子可返回 false 取消）
     ├─ 4.2  IsCancelled 检查
     ├─ 4.3  IWailsEventListener.NotifyEvent → WebSocketBroadcaster.BroadcastEventAsync
     │        └─ 推送 {type:"event", name, data} 到所有 WebSocket 客户端
     │        └─ 客户端收到后调用 _wailsEmitEvent(name, data)
     │        └─ 触发 _eventCallbacks[name] 数组中的所有回调
     └─ 4.4  本地监听器循环：
              ├─ listener.Invoke(evt)   // UpdateRecentList 被调用
              ├─ Interlocked.Increment(_callCount)
              └─ if IsExpired → 加入 toRemove 列表，循环结束后清理

取消阶段：
  后端:  app.Events.Off(listenerId)  // 按 ID
         app.Events.Off("app:doc:saved")  // 按事件名
  前端:  off()  // 调用返回的取消订阅函数
  全局:  app.Events.Clear()  // 清除所有订阅
  关闭:  Application.Shutdown 时事件处理器随 Application 实例回收
```

### 关键时序约定

- **钩子先于广播**：pre-emit 钩子在任何监听器（包括传输层）之前执行，可彻底取消事件。
- **广播先于本地派发**：`NotifyEvent` 在本地监听器循环之前调用，确保前端能在后端处理完成前收到事件。
- **取消即时生效**：监听器调用 `evt.Cancel()` 后，循环立即 break，未触发的监听器不再接收。
- **过期清理时机**：`Once` / `OnMultiple` 监听器达到上限后，在当前 `Emit` 调用结束时统一移除，避免循环中修改集合。

## 9. 设计要点总结

| 关注点 | 实现方式 | 位置 |
|--------|---------|------|
| 事件总线 | `EventProcessor` + `ConcurrentDictionary` | [EventProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) |
| 前后端双向通信 | `IWailsEventListener` + HTTP `fetch` + WebSocket 推送 | [ITransport.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/ITransport.cs) |
| 类型安全 | `TypedEvent<TData>` + JSON 中转转换 | [TypedEvent.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/TypedEvent.cs) |
| 事件拦截 | `RegisterHook` + `CustomEvent.Cancel()` | [EventProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) |
| 跨窗口同步 | `WebSocketBroadcaster.BroadcastEventExceptAsync` | [WebSocketBroadcaster.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/WebSocketBroadcaster.cs) |
| 来源识别 | `CustomEvent.SenderWindowID` + `EventPayload.senderWindowId` | [CustomEvent.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/CustomEvent.cs) |
| 系统事件命名 | `KnownEvents` 常量 + `GetEventName` 映射 | [KnownEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/KnownEvents.cs) |
| 名称冲突防护 | `CommonEvents.IsKnownEvent` | [CommonEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/CommonEvents.cs) |
| 一次性订阅 | `OnMultiple(maxCalls:1)` + `IsExpired` 自动清理 | [EventProcessor.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) |
| Android 平台事件转发 | `AndroidPlatformEvents.MapToCommonEvent` + 7 事件映射表 | [AndroidPlatformEvents.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) |

事件系统通过"统一处理器 + 传输层监听器 + 跨窗口广播器"三层结构，实现了与 Wails v3 Go 版本对等的事件模型，同时借助 .NET 的 `ConcurrentDictionary`、`Interlocked` 与 `CancellationTokenSource` 提供了原生的并发安全与取消语义。
