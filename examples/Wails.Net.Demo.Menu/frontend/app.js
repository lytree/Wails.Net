/**
 * Wails.Net Demo - Menu 前端脚本
 * 演示应用菜单点击事件的接收与历史记录。
 * 后端菜单项 Callback 通过 app.Events.Emit("menu:clicked", ...) 广播事件，
 * 前端通过 wails.events.on("menu:clicked", ...) 订阅并实时显示。
 */

document.addEventListener('DOMContentLoaded', () => {
    initReceivers();
    initHistoryControls();
    refreshHistory();
});

// 订阅 menu:clicked 事件
function initReceivers() {
    wails.events.on('menu:clicked', (data) => {
        const id = data?.id ?? '(未知)';
        const time = data?.time ?? new Date().toLocaleTimeString();
        document.getElementById('latestClick').textContent = id;
        document.getElementById('clickResult').textContent = `菜单点击: ${id}\n时间: ${time}`;
    });
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('MenuLogService.ClearHistory', []);
            await refreshHistory();
            document.getElementById('latestClick').textContent = '（暂无）';
            document.getElementById('clickResult').textContent = '';
        } catch (err) {
            console.error('清空失败:', err);
        }
    });
}

// 刷新历史列表
async function refreshHistory() {
    try {
        const history = await wails.call('MenuLogService.GetClickHistory', []);
        const list = document.getElementById('historyList');
        if (!list) return;
        if (!history || history.length === 0) {
            list.innerHTML = '<li class="empty">（暂无记录）</li>';
            return;
        }
        // 倒序显示（最新在前）
        list.innerHTML = history.slice().reverse().map((item) =>
            `<li>${escapeHtml(item)}</li>`
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
