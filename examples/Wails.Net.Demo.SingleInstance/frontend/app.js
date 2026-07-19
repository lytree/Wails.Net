/**
 * Wails.Net Demo - SingleInstance 前端脚本
 * 订阅 wails:second-instance:launched 事件，实时显示二次启动尝试。
 */

document.addEventListener('DOMContentLoaded', () => {
    initControls();
    subscribeSecondInstanceEvent();
    refreshProcessInfo();
    refreshStats();
    refreshHistory();
});

function initControls() {
    document.getElementById('refreshProcessBtn')?.addEventListener('click', refreshProcessInfo);
    document.getElementById('refreshStatsBtn')?.addEventListener('click', refreshStats);
    document.getElementById('refreshHistoryBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearHistoryBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('SingleInstanceLogService.ClearHistory', []);
            await refreshHistory();
            await refreshStats();
        } catch (err) {
            console.error('清空历史失败:', err);
        }
    });
}

// 订阅二次启动事件
function subscribeSecondInstanceEvent() {
    wails.events?.on('wails:second-instance:launched', (args) => {
        console.log('收到二次启动事件，参数：', args);
        refreshHistory();
        refreshStats();
    });
}

// 刷新当前进程信息
async function refreshProcessInfo() {
    try {
        const info = await wails.call('SingleInstanceLogService.GetCurrentProcessInfo', []);
        const lines = [
            `进程 ID：${info.processId}`,
            `机器名：${info.machineName}`,
            `用户名：${info.userName}`,
            `操作系统：${info.osVersion}`,
            `主模块：${info.mainModuleFileName}`,
            `启动时间：${info.startTime ? new Date(info.startTime).toLocaleString() : '（未知）'}`,
            `命令行：${info.commandLine}`,
            `参数列表：`,
            ...(Array.isArray(info.args) ? info.args.map((a, i) => `  [${i}] ${a}`) : ['  （无）']),
        ];
        document.getElementById('processInfo').textContent = lines.join('\n');
    } catch (err) {
        document.getElementById('processInfo').textContent = `查询失败：${err}`;
    }
}

// 刷新统计
async function refreshStats() {
    try {
        const stats = await wails.call('SingleInstanceLogService.GetStats', []);
        document.getElementById('statsResult').textContent =
            `总启动次数：${stats.total}\n首实例：${stats.first}\n二次实例：${stats.second}`;
    } catch (err) {
        document.getElementById('statsResult').textContent = `查询失败：${err}`;
    }
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('SingleInstanceLogService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="5" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.kind)}</td>
                <td>${item.processId}</td>
                <td>${escapeHtml((item.args || []).join(' '))}</td>
                <td>${item.time ? new Date(item.time).toLocaleString() : ''}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
