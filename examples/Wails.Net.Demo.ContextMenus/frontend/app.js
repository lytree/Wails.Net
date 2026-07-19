/**
 * Wails.Net Demo - ContextMenus 前端脚本
 * 演示上下文菜单的注册与点击事件接收。
 * 后端通过 MenuManager.RegisterContextMenu 注册命名菜单，
 * 前端通过 CSS 变量 --custom-contextmenu 引用菜单 ID，
 * 右键元素时由后端 MessageProcessor 自动弹出。
 * 菜单项 Callback 通过 app.Events.Emit("contextmenu:clicked", ...) 广播事件，
 * 前端通过 wails.events.on 订阅并显示。
 */

document.addEventListener('DOMContentLoaded', () => {
    initReceivers();
    initHistoryControls();
    initDemoActions();
    refreshHistory();
});

// 订阅 contextmenu:clicked 事件
function initReceivers() {
    wails.events.on('contextmenu:clicked', (data) => {
        const target = data?.target ?? '(未知)';
        const action = data?.action ?? '(未知)';
        const time = data?.time ?? new Date().toLocaleTimeString();
        document.getElementById('latestClick').textContent = `${target} / ${action}`;
        document.getElementById('clickResult').textContent = `目标: ${target}\n动作: ${action}\n时间: ${time}`;

        // 根据动作执行对应的前端行为
        applyAction(target, action);
    });
}

// 根据菜单动作更新 UI
function applyAction(target, action) {
    if (target === 'input') {
        const input = document.getElementById('demoInput');
        if (!input) return;
        if (action === 'clear') input.value = '';
        if (action === 'copy' && input.select) document.execCommand?.('copy');
        if (action === 'paste' && navigator.clipboard) {
            navigator.clipboard.readText().then(text => { input.value = text; }).catch(() => {});
        }
    } else if (target === 'button') {
        const btn = document.getElementById('demoButton');
        if (!btn) return;
        if (action === 'disable') btn.disabled = true;
        if (action === 'enable') btn.disabled = false;
        if (action === 'reset-color') btn.style.background = '#667eea';
    } else if (target === 'text') {
        const text = document.getElementById('demoText');
        if (!text) return;
        if (action === 'select-all' && window.getSelection) {
            const range = document.createRange();
            range.selectNodeContents(text);
            const sel = window.getSelection();
            sel?.removeAllRanges();
            sel?.addRange(range);
        }
        if (action === 'invert') {
            text.style.background = text.style.background === 'rgb(45, 55, 72)' ? '' : '#2d3748';
            text.style.color = text.style.color === 'rgb(255, 255, 255)' ? '' : '#ffffff';
        }
    }
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('ContextMenuService.ClearHistory', []);
            await refreshHistory();
            document.getElementById('latestClick').textContent = '（暂无）';
            document.getElementById('clickResult').textContent = '';
        } catch (err) {
            console.error('清空失败:', err);
        }
    });
}

// 演示按钮点击
function initDemoActions() {
    document.getElementById('demoButton')?.addEventListener('click', () => {
        document.getElementById('clickResult').textContent = '按钮被点击（这是左键点击，不是右键菜单）';
    });
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('ContextMenuService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="empty">（暂无记录）</td></tr>';
            return;
        }
        // 倒序显示（最新在前）
        body.innerHTML = history.slice().reverse().map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.target)}</td>
                <td>${escapeHtml(item.action)}</td>
                <td>${new Date(item.timestamp).toLocaleTimeString()}</td>
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
