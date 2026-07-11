using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Wails.Net.Runtime.Js;

/// <summary>
/// JavaScript 运行时代码生成器。
/// 对应 Wails v3 Go 版本 <c>internal/runtime/runtime.go</c> 中的运行时生成逻辑。
/// 负责生成注入 Webview 的 JavaScript 运行时代码，包括标志对象、API 对象与传输层。
/// </summary>
public static class RuntimeGenerator
{
    /// <summary>
    /// 运行时模板嵌入资源的文件名。
    /// </summary>
    private const string RuntimeTemplateFileName = "runtime.template.js";

    /// <summary>
    /// 传输层模板嵌入资源的文件名。
    /// </summary>
    private const string TransportTemplateFileName = "transport.template.js";

    /// <summary>
    /// 生成完整的运行时 JavaScript 代码。
    /// 根据 <see cref="RuntimeOptions.IsServerMode" /> 选择桌面或 Server 运行时，
    /// 并组合运行时模板、传输层模板与平台运行时代码。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>生成的完整运行时 JavaScript 代码字符串。</returns>
    public static string Generate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var runtime = LoadTemplate(RuntimeTemplateFileName, options);
        var transport = LoadTemplate(TransportTemplateFileName, options);
        var platformRuntime = options.IsServerMode
            ? ServerRuntime.Generate(options)
            : DesktopRuntime.Generate(options);

        return $"{runtime}\n{transport}\n{platformRuntime}";
    }

    /// <summary>
    /// 生成 <c>window._wails</c> 标志对象。
    /// 包含平台、调试模式、Server 模式等运行时标志。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>包含 <c>window._wails</c> 标志对象的 JavaScript 代码字符串。</returns>
    public static string GenerateFlags(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var platform = JsonSerializer.Serialize(options.Platform);
        var isDebug = FormatBool(options.IsDebug);
        var isServerMode = FormatBool(options.IsServerMode);

        return $$"""
        // Wails.NET Runtime Flags - 自动生成，请勿手动修改
        window._wails = {
          platform: {{platform}},
          isDebug: {{isDebug}},
          isServerMode: {{isServerMode}}
        };
        """;
    }

    /// <summary>
    /// 生成 <c>window.wails</c> API 对象。
    /// 包含绑定调用、事件订阅/发布、窗口管理、屏幕、剪贴板、对话框、菜单、应用命名空间。
    /// 对应 Wails v3 Go 版本 internal/runtime/desktop.ts。
    /// </summary>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>包含 <c>window.wails</c> API 对象的 JavaScript 代码字符串。</returns>
    public static string GenerateApi(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return """
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
          },
          window: {
            setTitle: function(title) {
              return window._wailsInvoke("window.setTitle", { title: title });
            },
            setSize: function(width, height) {
              return window._wailsInvoke("window.setSize", { width: width, height: height });
            },
            setMinSize: function(width, height) {
              return window._wailsInvoke("window.setMinSize", { width: width, height: height });
            },
            setMaxSize: function(width, height) {
              return window._wailsInvoke("window.setMaxSize", { width: width, height: height });
            },
            setPosition: function(x, y) {
              return window._wailsInvoke("window.setPosition", { x: x, y: y });
            },
            close: function() {
              return window._wailsInvoke("window.close", {});
            },
            minimize: function() {
              return window._wailsInvoke("window.minimize", {});
            },
            maximize: function() {
              return window._wailsInvoke("window.maximize", {});
            },
            unminimize: function() {
              return window._wailsInvoke("window.unminimize", {});
            },
            unmaximize: function() {
              return window._wailsInvoke("window.unmaximize", {});
            },
            show: function() {
              return window._wailsInvoke("window.show", {});
            },
            hide: function() {
              return window._wailsInvoke("window.hide", {});
            },
            centre: function() {
              return window._wailsInvoke("window.centre", {});
            },
            setAlwaysOnTop: function(onTop) {
              return window._wailsInvoke("window.setAlwaysOnTop", { onTop: onTop });
            },
            setFullscreen: function(fullscreen) {
              return window._wailsInvoke("window.setFullscreen", { fullscreen: fullscreen });
            },
            execJS: function(js) {
              return window._wailsInvoke("window.execJS", { js: js });
            },
            // 窗口状态查询方法（对应 Tauri v2 / Wails v3 的窗口读取 API）
            getSize: function() {
              return window._wailsInvoke("window.getSize", {});
            },
            getPosition: function() {
              return window._wailsInvoke("window.getPosition", {});
            },
            getURL: function() {
              return window._wailsInvoke("window.getURL", {});
            },
            getZoom: function() {
              return window._wailsInvoke("window.getZoom", {});
            },
            isFullscreen: function() {
              return window._wailsInvoke("window.isFullscreen", {});
            },
            isMaximised: function() {
              return window._wailsInvoke("window.isMaximised", {});
            },
            isMinimised: function() {
              return window._wailsInvoke("window.isMinimised", {});
            },
            isVisible: function() {
              return window._wailsInvoke("window.isVisible", {});
            },
            isFocused: function() {
              return window._wailsInvoke("window.isFocused", {});
            },
            // 额外窗口动作方法
            focus: function() {
              return window._wailsInvoke("window.focus", {});
            },
            restore: function() {
              return window._wailsInvoke("window.restore", {});
            },
            unFullscreen: function() {
              return window._wailsInvoke("window.unFullscreen", {});
            },
            openDevTools: function() {
              return window._wailsInvoke("window.openDevTools", {});
            },
            closeDevTools: function() {
              return window._wailsInvoke("window.closeDevTools", {});
            },
            setZoom: function(zoom) {
              return window._wailsInvoke("window.setZoom", { zoom: zoom });
            },
            goBack: function() {
              return window._wailsInvoke("window.goBack", {});
            },
            goForward: function() {
              return window._wailsInvoke("window.goForward", {});
            },
            reload: function() {
              return window._wailsInvoke("window.reload", {});
            },
            setURL: function(url) {
              return window._wailsInvoke("window.setURL", { url: url });
            },
            setHTML: function(html) {
              return window._wailsInvoke("window.setHTML", { html: html });
            },
            print: function() {
              return window._wailsInvoke("window.print", {});
            },
            printToPDF: function(path, options) {
              return window._wailsInvoke("window.printToPDF", { path: path, options: options || null });
            },
            capturePreview: function() {
              return window._wailsInvoke("window.capturePreview", {});
            },
            registerCustomScheme: function(scheme) {
              return window._wailsInvoke("window.registerCustomScheme", { scheme: scheme });
            },
            setResizable: function(resizable) {
              return window._wailsInvoke("window.setResizable", { resizable: resizable });
            },
            setFrameless: function(frameless) {
              return window._wailsInvoke("window.setFrameless", { frameless: frameless });
            },
            injectCSS: function(css) {
              return window._wailsInvoke("window.injectCSS", { css: css });
            },
            zoomIn: function() {
              return window._wailsInvoke("window.zoomIn", {});
            },
            zoomOut: function() {
              return window._wailsInvoke("window.zoomOut", {});
            },
            zoomReset: function() {
              return window._wailsInvoke("window.zoomReset", {});
            },
            setOpacity: function(opacity) {
              return window._wailsInvoke("window.setOpacity", { opacity: opacity });
            },
            getOpacity: function() {
              return window._wailsInvoke("window.getOpacity", {});
            }
          },
          // 系统托盘 API（对应 Tauri v2 的 tray API 和 Wails v3 的 SystemTray）
          tray: {
            setIcon: function(iconData) {
              return window._wailsInvoke("tray.setIcon", { iconData: iconData });
            },
            setLabel: function(label) {
              return window._wailsInvoke("tray.setLabel", { label: label });
            },
            setMenu: function(menu) {
              return window._wailsInvoke("tray.setMenu", { menu: menu });
            },
            setTooltip: function(tooltip) {
              return window._wailsInvoke("tray.setTooltip", { tooltip: tooltip });
            },
            destroy: function() {
              return window._wailsInvoke("tray.destroy", {});
            },
            isVisible: function() {
              return window._wailsInvoke("tray.isVisible", {});
            },
            show: function() {
              return window._wailsInvoke("tray.show", {});
            },
            hide: function() {
              return window._wailsInvoke("tray.hide", {});
            }
          },
          // 应用级窗口管理 API（对应 Tauri v2 的 getCurrentWindow / getAllWindows）
          windows: {
            getCurrent: function() {
              return window._wailsInvoke("windows.getCurrent", {});
            },
            getAll: function() {
              return window._wailsInvoke("windows.getAll", {});
            },
            getByName: function(name) {
              return window._wailsInvoke("windows.getByName", { name: name });
            },
            getById: function(id) {
              return window._wailsInvoke("windows.getById", { id: id });
            },
            emit: function(eventName, data, targetWindowId) {
              return window._wailsInvoke("windows.emit", { name: eventName, data: data, targetWindowId: targetWindowId || null });
            }
          },
          screen: {
            getAll: function() {
              return window._wailsInvoke("screen.getAll", {});
            }
          },
          clipboard: {
            setText: function(text) {
              return window._wailsInvoke("clipboard.setText", { text: text });
            },
            getText: function() {
              return window._wailsInvoke("clipboard.getText", {});
            },
            setHTML: function(html, fallbackText) {
              return window._wailsInvoke("clipboard.setHTML", { html: html, fallbackText: fallbackText });
            },
            getHTML: function() {
              return window._wailsInvoke("clipboard.getHTML", {});
            },
            setFiles: function(files) {
              return window._wailsInvoke("clipboard.setFiles", { files: files });
            },
            getFiles: function() {
              return window._wailsInvoke("clipboard.getFiles", {});
            }
          },
          dialog: {
            openFile: function(options) {
              return window._wailsInvoke("dialog.openFile", { options: options || {} });
            },
            saveFile: function(options) {
              return window._wailsInvoke("dialog.saveFile", { options: options || {} });
            },
            message: function(title, message, type) {
              return window._wailsInvoke("dialog.message", { title: title, message: message, type: type || "info" });
            },
            question: function(title, message, buttons) {
              return window._wailsInvoke("dialog.question", { title: title, message: message, buttons: buttons || ["Yes", "No"] });
            }
          },
          menu: {
            setApplicationMenu: function(menu) {
              return window._wailsInvoke("menu.setApplicationMenu", { menu: menu });
            },
            setContextMenu: function(menu) {
              return window._wailsInvoke("menu.setContextMenu", { menu: menu });
            },
            updateMenuItem: function(id, properties) {
              return window._wailsInvoke("menu.updateMenuItem", { id: id, properties: properties || {} });
            },
            popup: function(menu, x, y) {
              return window._wailsInvoke("menu.popup", { menu: menu, x: x || 0, y: y || 0 });
            }
          },
          application: {
            quit: function() {
              return window._wailsInvoke("application.quit", {});
            },
            hide: function() {
              return window._wailsInvoke("application.hide", {});
            },
            show: function() {
              return window._wailsInvoke("application.show", {});
            },
            getName: function() {
              return window._wailsInvoke("application.getName", {});
            },
            setIcon: function(iconData) {
              return window._wailsInvoke("application.setIcon", { iconData: iconData });
            },
            isDarkMode: function() {
              return window._wailsInvoke("application.isDarkMode", {});
            },
            getAccentColor: function() {
              return window._wailsInvoke("application.getAccentColor", {});
            },
            setTheme: function(theme) {
              return window._wailsInvoke("application.setTheme", { theme: theme });
            },
            onThemeChanged: function(callback) {
              return window._wailsInvoke("application.onThemeChanged", { callback: callback });
            }
          },
          // 加密安全存储 API（对应 Tauri v2 的 @tauri-apps/plugin-stronghold）
          stronghold: {
            unlock: function(password, vaultPath) {
              return window._wailsInvoke("stronghold.unlock", { password: password, vaultPath: vaultPath || null });
            },
            lock: function(vaultPath) {
              return window._wailsInvoke("stronghold.lock", { vaultPath: vaultPath || null });
            },
            saveSecret: function(key, value, vaultPath) {
              return window._wailsInvoke("stronghold.saveSecret", { key: key, value: value, vaultPath: vaultPath || null });
            },
            getSecret: function(key, vaultPath) {
              return window._wailsInvoke("stronghold.getSecret", { key: key, vaultPath: vaultPath || null });
            },
            deleteSecret: function(key, vaultPath) {
              return window._wailsInvoke("stronghold.deleteSecret", { key: key, vaultPath: vaultPath || null });
            },
            listKeys: function(vaultPath) {
              return window._wailsInvoke("stronghold.listKeys", { vaultPath: vaultPath || null });
            },
            isUnlocked: function(vaultPath) {
              return window._wailsInvoke("stronghold.isUnlocked", { vaultPath: vaultPath || null });
            },
            changePassword: function(oldPassword, newPassword, vaultPath) {
              return window._wailsInvoke("stronghold.changePassword", { oldPassword: oldPassword, newPassword: newPassword, vaultPath: vaultPath || null });
            }
          },
          // 文件系统范围持久化 API（对应 Tauri v2 的 @tauri-apps/plugin-persisted-scope）
          scope: {
            addPath: function(path, scopePath) {
              return window._wailsInvoke("scope.addPath", { path: path, scopePath: scopePath || null });
            },
            removePath: function(path, scopePath) {
              return window._wailsInvoke("scope.removePath", { path: path, scopePath: scopePath || null });
            },
            listPaths: function(scopePath) {
              return window._wailsInvoke("scope.listPaths", { scopePath: scopePath || null });
            },
            clear: function(scopePath) {
              return window._wailsInvoke("scope.clear", { scopePath: scopePath || null });
            },
            isAllowed: function(path, scopePath) {
              return window._wailsInvoke("scope.isAllowed", { path: path, scopePath: scopePath || null });
            },
            save: function(scopePath) {
              return window._wailsInvoke("scope.save", { scopePath: scopePath || null });
            },
            load: function(scopePath) {
              return window._wailsInvoke("scope.load", { scopePath: scopePath || null });
            }
          },
          // 嵌入式本地 HTTP 服务器 API（对应 Tauri v2 的 @tauri-apps/plugin-localhost）
          localhost: {
            start: function(port, rootDir) {
              return window._wailsInvoke("localhost.start", { port: port, rootDir: rootDir || null });
            },
            stop: function(port) {
              return window._wailsInvoke("localhost.stop", { port: port });
            },
            getUrl: function(port) {
              return window._wailsInvoke("localhost.getUrl", { port: port });
            },
            isRunning: function(port) {
              return window._wailsInvoke("localhost.isRunning", { port: port });
            },
            setRoot: function(port, rootDir) {
              return window._wailsInvoke("localhost.setRoot", { port: port, rootDir: rootDir });
            },
            addRoute: function(port, route, method) {
              return window._wailsInvoke("localhost.addRoute", { port: port, route: route, method: method });
            },
            removeRoute: function(port, route) {
              return window._wailsInvoke("localhost.removeRoute", { port: port, route: route });
            },
            listRoutes: function(port) {
              return window._wailsInvoke("localhost.listRoutes", { port: port });
            }
          },
          // 文件系统监听 API（对应 Tauri v2 的 @tauri-apps/plugin-fs-watch）
          fswatch: {
            watch: function(path, recursive, extensions) {
              return window._wailsInvoke("fswatch.watch", { path: path, recursive: recursive || false, extensions: extensions || null });
            },
            unwatch: function(id) {
              return window._wailsInvoke("fswatch.unwatch", { id: id });
            },
            unwatchAll: function() {
              return window._wailsInvoke("fswatch.unwatchAll", {});
            },
            listWatches: function() {
              return window._wailsInvoke("fswatch.listWatches", {});
            },
            isWatching: function(id) {
              return window._wailsInvoke("fswatch.isWatching", { id: id });
            }
          },
          // 系统信息 API（对应 Tauri v2 的 @tauri-apps/plugin-os）
          system: {
            platform: function() {
              return window._wailsInvoke("system.platform", {});
            },
            arch: function() {
              return window._wailsInvoke("system.arch", {});
            },
            hostname: function() {
              return window._wailsInvoke("system.hostname", {});
            },
            version: function() {
              return window._wailsInvoke("system.version", {});
            },
            type: function() {
              return window._wailsInvoke("system.type", {});
            },
            locale: function() {
              return window._wailsInvoke("system.locale", {});
            },
            timezone: function() {
              return window._wailsInvoke("system.timezone", {});
            }
          },
          // 电源管理 API（对应 Tauri v2 的 @tauri-apps/plugin-os 电源部分）
          power: {
            requestWakeLock: function() {
              return window._wailsInvoke("power.requestWakeLock", {});
            },
            releaseWakeLock: function() {
              return window._wailsInvoke("power.releaseWakeLock", {});
            },
            isWakeLockHeld: function() {
              return window._wailsInvoke("power.isWakeLockHeld", {});
            }
          },
          // 进程管理 API
          process: {
            exit: function(code) {
              return window._wailsInvoke("process.exit", { code: code || 0 });
            },
            restart: function() {
              return window._wailsInvoke("process.restart", {});
            },
            getPid: function() {
              return window._wailsInvoke("process.getPid", {});
            }
          },
          // 文件系统 API（对应 Tauri v2 的 @tauri-apps/plugin-fs）
          fs: {
            readTextFile: function(path) {
              return window._wailsInvoke("fs.readTextFile", { path: path });
            },
            writeTextFile: function(path, content) {
              return window._wailsInvoke("fs.writeTextFile", { path: path, content: content });
            },
            readBinaryFile: function(path) {
              return window._wailsInvoke("fs.readBinaryFile", { path: path });
            },
            writeBinaryFile: function(path, data) {
              return window._wailsInvoke("fs.writeBinaryFile", { path: path, data: data });
            },
            exists: function(path) {
              return window._wailsInvoke("fs.exists", { path: path });
            },
            mkdir: function(path, recursive) {
              return window._wailsInvoke("fs.mkdir", { path: path, recursive: recursive || false });
            },
            remove: function(path) {
              return window._wailsInvoke("fs.remove", { path: path });
            },
            rename: function(oldPath, newPath) {
              return window._wailsInvoke("fs.rename", { oldPath: oldPath, newPath: newPath });
            },
            copy: function(src, dst) {
              return window._wailsInvoke("fs.copy", { src: src, dst: dst });
            },
            readDir: function(path) {
              return window._wailsInvoke("fs.readDir", { path: path });
            }
          },
          // Shell API（对应 Tauri v2 的 @tauri-apps/plugin-shell）
          shell: {
            execute: function(command, args, cwd) {
              return window._wailsInvoke("shell.execute", { command: command, args: args || [], cwd: cwd || null });
            },
            open: function(path) {
              return window._wailsInvoke("shell.open", { path: path });
            },
            openUrl: function(url) {
              return window._wailsInvoke("shell.openUrl", { url: url });
            }
          },
          // 通知 API（对应 Tauri v2 的 @tauri-apps/plugin-notification）
          notification: {
            show: function(title, body) {
              return window._wailsInvoke("notification.show", { title: title, body: body });
            },
            requestPermission: function() {
              return window._wailsInvoke("notification.requestPermission", {});
            },
            hasPermission: function() {
              return window._wailsInvoke("notification.hasPermission", {});
            }
          },
          // 存储插件 API（对应 Tauri v2 的 @tauri-apps/plugin-store）
          store: {
            get: function(key) {
              return window._wailsInvoke("store.get", { key: key });
            },
            set: function(key, value) {
              return window._wailsInvoke("store.set", { key: key, value: value });
            },
            delete: function(key) {
              return window._wailsInvoke("store.delete", { key: key });
            },
            keys: function() {
              return window._wailsInvoke("store.keys", {});
            },
            clear: function() {
              return window._wailsInvoke("store.clear", {});
            },
            has: function(key) {
              return window._wailsInvoke("store.has", { key: key });
            }
          },
          // 日志 API（对应 Tauri v2 的 @tauri-apps/plugin-log）
          log: {
            debug: function(message) {
              return window._wailsInvoke("log.debug", { message: message });
            },
            info: function(message) {
              return window._wailsInvoke("log.info", { message: message });
            },
            warn: function(message) {
              return window._wailsInvoke("log.warn", { message: message });
            },
            error: function(message) {
              return window._wailsInvoke("log.error", { message: message });
            },
            trace: function(message) {
              return window._wailsInvoke("log.trace", { message: message });
            }
          }
        };
        """;
    }

    /// <summary>
    /// 从程序集嵌入资源中加载指定模板并替换占位符。
    /// </summary>
    /// <param name="templateFileName">模板文件名（用于在嵌入资源中按后缀匹配查找）。</param>
    /// <param name="options">运行时生成选项，用于占位符替换。</param>
    /// <returns>替换占位符后的模板内容。</returns>
    /// <exception cref="InvalidOperationException">指定的嵌入资源未找到或无法读取。</exception>
    internal static string LoadTemplate(string templateFileName, RuntimeOptions options)
    {
        var assembly = typeof(RuntimeGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(templateFileName, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"嵌入资源 '{templateFileName}' 未找到。");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"无法读取嵌入资源 '{resourceName}'。");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var template = reader.ReadToEnd();
        return ReplacePlaceholders(template, options);
    }

    /// <summary>
    /// 替换模板中的占位符为实际运行时选项值。
    /// 支持的占位符：<c>{PLATFORM}</c>、<c>{IS_DEBUG}</c>、<c>{IS_SERVER_MODE}</c>、
    /// <c>{ASSET_SERVER_URL}</c>、<c>{WEBSOCKET_URL}</c>。
    /// </summary>
    /// <param name="template">包含占位符的模板字符串。</param>
    /// <param name="options">运行时生成选项。</param>
    /// <returns>占位符替换后的字符串。</returns>
    private static string ReplacePlaceholders(string template, RuntimeOptions options)
    {
        return template
            .Replace("{PLATFORM}", options.Platform)
            .Replace("{IS_DEBUG}", FormatBool(options.IsDebug))
            .Replace("{IS_SERVER_MODE}", FormatBool(options.IsServerMode))
            .Replace("{ASSET_SERVER_URL}", options.AssetServerUrl)
            .Replace("{WEBSOCKET_URL}", options.WebSocketUrl);
    }

    /// <summary>
    /// 将布尔值格式化为 JavaScript 布尔字面量（小写 true/false）。
    /// </summary>
    /// <param name="value">要格式化的布尔值。</param>
    /// <returns>JavaScript 布尔字面量字符串。</returns>
    private static string FormatBool(bool value) => value ? "true" : "false";
}
