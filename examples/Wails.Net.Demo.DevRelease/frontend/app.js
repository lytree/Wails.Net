import { wails } from "./wails-runtime/index.js";

// Wails.Net 前端：DevRelease 演示
// -------------------------------------------------------------------------
// wails 对象由 @wails-net/runtime 提供（从本地 wails-runtime 目录导入）。
// 绑定方法通过 wails.call('Service.Method', args) 调用；
// 命令（wails.window.* 等）由 wails-net/runtime 命名空间提供。
// -------------------------------------------------------------------------

const $ = (id) => document.getElementById(id);

const banner = $("modeBanner");
const kvMode = $("kvMode");
const kvPid = $("kvPid");
const kvRuntime = $("kvRuntime");
const kvStartedAt = $("kvStartedAt");
const kvUpTime = $("kvUpTime");
const kvCallCount = $("kvCallCount");
const kvOs = $("kvOs");

// ---- 1. 模式信息 ----
async function refreshModeInfo() {
    try {
        const info = await wails.call('DevReleaseService.GetModeInfo', []);
        kvMode.textContent = info.mode;
        kvPid.textContent = String(info.processId);
        kvRuntime.textContent = info.runtimeVersion;
        kvStartedAt.textContent = info.startedAt;
        kvUpTime.textContent = `${info.upTimeSeconds.toFixed(1)} s`;
        kvCallCount.textContent = String(info.callCount);
        kvOs.textContent = info.osDescription;

        banner.className = info.isDebug ? "banner banner-debug" : "banner banner-release";
        banner.textContent = info.isDebug
            ? `🐞 Debug 模式 — 已启用 DevTools / 详细日志 / 窗口标题 (Debug)`
            : `🚀 Release 模式 — 生产构建 / 性能优化 / 关闭 DevTools`;
    } catch (e) {
        banner.className = "banner banner-error";
        banner.textContent = `错误：${e.message || e}`;
    }
}

$("refreshBtn").addEventListener("click", refreshModeInfo);

// ---- 2. 调用计数 ----
$("incBtn").addEventListener("click", async () => {
    try {
        const value = await wails.call('DevReleaseService.IncrementCall', []);
        kvCallCount.textContent = String(value);
    } catch (e) {
        console.error('Increment 失败：', e);
    }
});

$("resetBtn").addEventListener("click", async () => {
    try {
        await wails.call('DevReleaseService.Reset', []);
        await refreshModeInfo();
    } catch (e) {
        console.error('Reset 失败：', e);
    }
});

// ---- 3. 异步 AddAsync ----
$("addBtn").addEventListener("click", async () => {
    const a = parseInt($("aInput").value, 10) || 0;
    const b = parseInt($("bInput").value, 10) || 0;
    const result = $("addResult");
    result.textContent = "计算中…";
    try {
        const sum = await wails.call('DevReleaseService.AddAsync', [a, b]);
        result.textContent = `${a} + ${b} = ${sum}`;
        result.className = "result";
    } catch (e) {
        result.textContent = `错误：${e.message || e}`;
        result.className = "result result-error";
    }
});

// ---- 4. 错误处理演示 ----
$("throwBtn").addEventListener("click", async () => {
    const result = $("throwResult");
    result.textContent = "调用中…";
    result.className = "result result-muted";
    try {
        const v = await wails.call('DevReleaseService.ThrowError', []);
        result.textContent = `意外成功：${v}`;
    } catch (e) {
        // 后端会通过 CallError 协议返回结构化错误
        result.textContent = `捕获错误：${e.message || e}`;
        result.className = "result result-error";
    }
});

// ---- 5. 窗口操作（通过命令而不是绑定） ----
$("minBtn").addEventListener("click", () => wails.window.minimize());
$("maxBtn").addEventListener("click", () => wails.window.toggleMaximise());
$("closeBtn").addEventListener("click", () => wails.window.close());
$("devtoolsBtn").addEventListener("click", () => wails.window.openDevTools());

// ---- 6. 初始化 ----
refreshModeInfo();

// 自动每 2 秒刷新一次运行时长
setInterval(refreshModeInfo, 2000);
