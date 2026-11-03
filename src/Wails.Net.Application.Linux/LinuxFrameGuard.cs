namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux（WebKitGTK）帧守卫脚本（§11.2 第 3 项 / GHSA-2p5f-2gx8-mh8m 缓解）。
/// <para>
/// 对应 Wails v3 <c>v3.0.0</c> 的修复思路：GTK3/GTK4 通过
/// <c>webkit_user_content_manager_add_script</c>（<c>AllFrames</c>）向所有帧注入
/// <b>guard 脚本</b>，使运行时只对可信帧（顶层帧或与顶层帧同源的子帧）生效。
/// </para>
/// <para>
/// 判定与处置：
/// <list type="bullet">
/// <item>可信帧：<c>window.top === window.self</c>，或 <c>window.top.location.origin</c>
/// 与 <c>window.location.origin</c> 相同（同源子帧，本就可访问主帧，无需额外隔离）；</item>
/// <item>其它帧（跨源 / 不透明源）：设置 <c>window.__wailsFrameTrusted = false</c>，
/// 阻断 <c>fetch</c> / <c>XMLHttpRequest</c> / <c>sendBeacon</c> 对 <c>wails:</c> 端点的请求，
/// 并拦截运行时标志 <c>window._wails</c> 的写入；</item>
/// <item>运行时标志脚本通过 <see cref="WrapRuntimeScript"/> 包裹，
/// 在不可信帧中直接跳过注入（不暴露后端平台/端点信息）。</item>
/// </list>
/// </para>
/// <para>
/// <b>已知平台限制</b>：WebKitGTK 的 <c>URISchemeRequest</c> 不暴露发起帧信息，
/// 因此帧来源只能由脚本判定；同源子帧无法（也不需要）区分——与上游 Wails v3 结论一致。
/// 原生侧的 <see cref="LinuxIpcOriginGuard"/> 提供 <c>Origin</c> 头校验作为第二道防线。
/// </para>
/// </summary>
internal static class LinuxFrameGuard
{
    /// <summary>
    /// 帧可信标记的全局变量名（由守卫脚本写入，供运行时标志脚本读取）。
    /// </summary>
    internal const string TrustedFlagName = "__wailsFrameTrusted";

    /// <summary>
    /// 生成注入到所有帧（文档开始）的守卫脚本。
    /// </summary>
    /// <returns>守卫脚本源码。</returns>
    internal static string GetGuardScript()
    {
        return """
            (function () {
              // Wails.Net Linux 帧守卫：只有顶层帧或与顶层帧同源的子帧允许使用后端 IPC。
              var trusted = true;
              try {
                if (window.top && window.top !== window.self) {
                  try {
                    trusted = window.top.location.origin === window.location.origin;
                  } catch (e) {
                    // 读取跨源窗口的 location 会抛异常，视为不可信。
                    trusted = false;
                  }
                }
              } catch (e) {
                trusted = false;
              }

              try {
                window.__wailsFrameTrusted = trusted;
              } catch (e) {
              }

              if (trusted) {
                return;
              }

              // 不可信帧：拦截运行时标志的写入，避免暴露平台信息与端点。
              try {
                Object.defineProperty(window, '_wails', {
                  configurable: true,
                  get: function () { return undefined; },
                  set: function () { }
                });
              } catch (e) {
              }

              // 不可信帧：阻断 fetch 对 wails: 自定义协议的访问。
              try {
                if (typeof window.fetch === 'function') {
                  var originalFetch = window.fetch;
                  window.fetch = function (input) {
                    var url = typeof input === 'string' ? input : (input && input.url) || '';
                    if (typeof url === 'string' && url.indexOf('wails:') === 0) {
                      return Promise.reject(new TypeError('Wails: cross-origin subframe cannot access IPC'));
                    }
                    return originalFetch.apply(window, arguments);
                  };
                }
              } catch (e) {
              }

              // 不可信帧：阻断 XMLHttpRequest 对 wails: 自定义协议的访问。
              try {
                if (typeof XMLHttpRequest === 'function') {
                  var originalOpen = XMLHttpRequest.prototype.open;
                  XMLHttpRequest.prototype.open = function (method, url) {
                    if (typeof url === 'string' && url.indexOf('wails:') === 0) {
                      throw new TypeError('Wails: cross-origin subframe cannot access IPC');
                    }
                    return originalOpen.apply(this, arguments);
                  };
                }
              } catch (e) {
              }

              // 不可信帧：阻断 sendBeacon 对 wails: 自定义协议的访问。
              try {
                if (navigator && typeof navigator.sendBeacon === 'function') {
                  var originalBeacon = navigator.sendBeacon.bind(navigator);
                  navigator.sendBeacon = function (url, data) {
                    if (typeof url === 'string' && url.indexOf('wails:') === 0) {
                      return false;
                    }
                    return originalBeacon(url, data);
                  };
                }
              } catch (e) {
              }
            })();
            """;
    }

    /// <summary>
    /// 将运行时标志脚本包裹为「仅可信帧执行」。
    /// 守卫脚本必须先于本脚本注册（WebKit 按注册顺序在同一注入时机执行）。
    /// </summary>
    /// <param name="runtimeJs">由 <c>Application.GenerateRuntimeJs</c> 生成的运行时标志脚本。</param>
    /// <returns>包裹后的脚本源码。</returns>
    internal static string WrapRuntimeScript(string runtimeJs)
    {
        ArgumentNullException.ThrowIfNull(runtimeJs);

        // 守卫未执行（标记为 undefined）时按可信处理，保持与非守卫注入路径一致。
        return "if (window." + TrustedFlagName + " !== false) {\n" + runtimeJs + "\n}\n";
    }
}
