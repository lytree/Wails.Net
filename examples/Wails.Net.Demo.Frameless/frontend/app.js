/**
 * Wails.Net Demo - Frameless 前端脚本
 * 演示无边框窗口的自定义标题栏与窗口控制按钮。
 * 通过 window.minimize / window.maximize / window.close 命令控制窗口，
 * 通过 WindowStateService 绑定方法保存与查询窗口状态。
 */

document.addEventListener('DOMContentLoaded', () => {
    initTitlebarButtons();
    initQueryButtons();
    refreshSavedState();
});

// 标题栏按钮：最小化 / 最大化 / 关闭
function initTitlebarButtons() {
    document.getElementById('minBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('window.minimize', []);
            await wails.call('WindowStateService.SaveState', ['minimized']);
            log('已最小化窗口');
            await refreshSavedState();
        } catch (err) {
            log('最小化失败: ' + err, true);
        }
    });

    document.getElementById('maxBtn')?.addEventListener('click', async () => {
        try {
            // 切换最大化：先查询当前状态，再调用 maximize 或 unmaximize
            const isMax = await wails.call('window.isMaximised', []);
            if (isMax) {
                await wails.call('window.unmaximize', []);
                await wails.call('WindowStateService.SaveState', ['normal']);
                log('已还原窗口');
            } else {
                await wails.call('window.maximize', []);
                await wails.call('WindowStateService.SaveState', ['maximized']);
                log('已最大化窗口');
            }
            await refreshSavedState();
            await refreshActualState();
        } catch (err) {
            log('最大化切换失败: ' + err, true);
        }
    });

    document.getElementById('closeBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('window.close', []);
            log('已请求关闭窗口');
        } catch (err) {
            log('关闭失败: ' + err, true);
        }
    });
}

// 查询按钮
function initQueryButtons() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshSavedState);
    document.getElementById('queryBtn')?.addEventListener('click', refreshActualState);
}

// 刷新后端保存的状态
async function refreshSavedState() {
    try {
        const state = await wails.call('WindowStateService.GetWindowState', []);
        document.getElementById('savedState').textContent = state;
    } catch (err) {
        log('获取保存状态失败: ' + err, true);
    }
}

// 查询窗口实际状态
async function refreshActualState() {
    try {
        const isMax = await wails.call('window.isMaximised', []);
        const isMin = await wails.call('window.isMinimised', []);
        const text = `maximised=${isMax}, minimised=${isMin}`;
        document.getElementById('isMaximised').textContent = isMax ? '是' : '否';
        document.getElementById('stateResult').textContent = text;
        log('查询窗口状态: ' + text);
    } catch (err) {
        log('查询窗口状态失败: ' + err, true);
    }
}

// 写入操作日志
function log(text, isError) {
    const logEl = document.getElementById('log');
    if (!logEl) return;
    const line = document.createElement('div');
    line.className = 'log-line' + (isError ? ' error' : '');
    line.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
    logEl.appendChild(line);
    while (logEl.children.length > 50) {
        logEl.removeChild(logEl.firstChild);
    }
    logEl.scrollTop = logEl.scrollHeight;
}
