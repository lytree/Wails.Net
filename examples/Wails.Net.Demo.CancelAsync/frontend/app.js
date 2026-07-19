/**
 * Wails.Net Demo - CancelAsync 前端脚本
 * 演示通过 wails.call 调用长任务绑定方法，并使用 _wailsCancelCall 取消。
 *
 * 关键 API：
 *   - wails.call('LongRunningService.StartLongTask', [durationSeconds])
 *       返回 Promise，前端可通过 promise.cancel() 或 _wailsCancelCall(callId) 取消。
 *   - wails.call('LongRunningService.GetProgress', [])
 *   - wails.call('LongRunningService.IsRunning', [])
 *   - wails.call('LongRunningService.CancelFromServer', [])  // 后端主动取消
 */

let currentCallId = null;
let pollTimer = null;

document.addEventListener('DOMContentLoaded', () => {
    refreshStatus();
    document.getElementById('startBtn')?.addEventListener('click', onStart);
    document.getElementById('cancelBtn')?.addEventListener('click', onCancelFromFrontend);
    document.getElementById('cancelServerBtn')?.addEventListener('click', onCancelFromServer);
});

// 启动长任务
async function onStart() {
    const duration = parseInt(document.getElementById('durationInput').value, 10) || 10;
    log(`启动任务：duration=${duration}s`);

    try {
        // 调用长任务，wails.call 返回 CancellablePromise
        const promise = wails.call('LongRunningService.StartLongTask', [duration]);
        currentCallId = promise.callId || null;

        // 启动进度轮询
        startPolling();

        // 等待任务完成
        const result = await promise;
        log(`任务完成：${result}`);
    } catch (err) {
        log(`任务被中断：${err}`);
    } finally {
        stopPolling();
        currentCallId = null;
        refreshStatus();
    }
}

// 前端取消：通过 _wailsCancelCall 发送 cancel 消息
async function onCancelFromFrontend() {
    if (!currentCallId) {
        log('当前没有运行中的任务');
        return;
    }
    log(`前端发起取消：callId=${currentCallId}`);
    try {
        // _wailsCancelCall 由 wails runtime 注入到 window 对象
        if (typeof window._wailsCancelCall === 'function') {
            await window._wailsCancelCall(currentCallId);
        } else if (typeof wails.cancelCall === 'function') {
            await wails.cancelCall(currentCallId);
        } else {
            log('当前 runtime 不支持 cancel API，改用服务端取消');
            await wails.call('LongRunningService.CancelFromServer', []);
        }
    } catch (err) {
        log(`取消调用失败：${err}`);
    }
}

// 服务端取消：调用绑定方法 CancelFromServer
async function onCancelFromServer() {
    log('服务端取消：调用 LongRunningService.CancelFromServer');
    try {
        await wails.call('LongRunningService.CancelFromServer', []);
    } catch (err) {
        log(`服务端取消失败：${err}`);
    }
}

// 进度轮询：每 200ms 查询一次进度
function startPolling() {
    stopPolling();
    pollTimer = setInterval(async () => {
        try {
            const progress = await wails.call('LongRunningService.GetProgress', []);
            updateProgress(progress);
        } catch (err) {
            // 忽略查询失败
        }
    }, 200);
}

function stopPolling() {
    if (pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
    }
}

function updateProgress(pct) {
    pct = Math.max(0, Math.min(100, pct));
    document.getElementById('progressFill').style.width = pct + '%';
    document.getElementById('progressText').textContent = pct + '%';
}

async function refreshStatus() {
    try {
        const running = await wails.call('LongRunningService.IsRunning', []);
        document.getElementById('statusText').textContent = running ? '运行中' : '空闲';
    } catch (err) {
        document.getElementById('statusText').textContent = '未知';
    }
}

function log(msg) {
    const time = new Date().toLocaleTimeString();
    const line = `[${time}] ${msg}`;
    const el = document.getElementById('log');
    el.textContent = (el.textContent ? el.textContent + '\n' : '') + line;
    el.scrollTop = el.scrollHeight;
    refreshStatus();
}
