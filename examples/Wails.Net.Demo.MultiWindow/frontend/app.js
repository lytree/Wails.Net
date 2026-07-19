/**
 * Wails.Net Demo - MultiWindow 前端脚本
 * 演示多窗口管理：创建子窗口、列表查询、聚焦、关闭、窗口事件订阅。
 * 通过 WindowManagerService 绑定方法创建/查询/聚焦/关闭窗口，
 * 通过 wails.events.on 订阅 wails:window:* 事件接收跨窗口通知。
 */

let childCounter = 0;

document.addEventListener('DOMContentLoaded', () => {
    initCreateButton();
    initRefreshButton();
    initEventReceivers();
    refreshWindowList();
});

// 创建子窗口
function initCreateButton() {
    document.getElementById('createBtn')?.addEventListener('click', async () => {
        const nameInput = document.getElementById('winName');
        const titleInput = document.getElementById('winTitle');
        let name = nameInput.value.trim();
        let title = titleInput.value.trim();

        // 自动生成默认值
        if (!name) {
            childCounter++;
            name = `child-${childCounter}`;
        }
        if (!title) {
            title = `子窗口 ${name}`;
        }

        try {
            const id = await wails.call('WindowManagerService.CreateChildWindow', [name, title]);
            document.getElementById('createResult').textContent = `已创建窗口: id=${id}, name="${name}", title="${title}"`;
            nameInput.value = '';
            titleInput.value = '';
            await refreshWindowList();

            // 通过 windows.emit 广播一个窗口创建事件到所有窗口
            try {
                await wails.call('windows.emit', [{ name: 'created', data: { id, name, title } }]);
            } catch (err) {
                console.warn('windows.emit 失败:', err);
            }
        } catch (err) {
            document.getElementById('createResult').textContent = `创建失败: ${err}`;
        }
    });
}

// 刷新窗口列表
function initRefreshButton() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshWindowList);
}

// 订阅窗口事件
function initEventReceivers() {
    wails.events.on('wails:window:created', (data) => {
        logEvent(`窗口创建: ${JSON.stringify(data)}`);
    });
    wails.events.on('wails:window:closed', (data) => {
        logEvent(`窗口关闭: ${JSON.stringify(data)}`);
        refreshWindowList();
    });
    wails.events.on('wails:window:focused', (data) => {
        logEvent(`窗口聚焦: ${JSON.stringify(data)}`);
    });
}

// 刷新窗口列表表格
async function refreshWindowList() {
    try {
        const windows = await wails.call('WindowManagerService.GetAllWindows', []);
        const body = document.getElementById('windowBody');
        if (!body) return;
        if (!windows || windows.length === 0) {
            body.innerHTML = '<tr><td colspan="5" class="empty">（暂无窗口）</td></tr>';
            return;
        }
        body.innerHTML = windows.map(w => `
            <tr>
                <td>${w.id}</td>
                <td>${escapeHtml(w.name)}</td>
                <td>${escapeHtml(w.title)}</td>
                <td>${w.width} x ${w.height}</td>
                <td class="actions">
                    <button class="small" data-action="focus" data-id="${w.id}">聚焦</button>
                    <button class="small danger" data-action="close" data-id="${w.id}">关闭</button>
                </td>
            </tr>
        `).join('');

        // 绑定操作按钮
        body.querySelectorAll('button[data-action]').forEach(btn => {
            btn.addEventListener('click', async () => {
                const action = btn.getAttribute('data-action');
                const id = parseInt(btn.getAttribute('data-id'), 10);
                try {
                    if (action === 'focus') {
                        await wails.call('WindowManagerService.FocusWindow', [id]);
                        logEvent(`已请求聚焦窗口 id=${id}`);
                    } else if (action === 'close') {
                        await wails.call('WindowManagerService.CloseWindow', [id]);
                        logEvent(`已请求关闭窗口 id=${id}`);
                        await refreshWindowList();
                    }
                } catch (err) {
                    logEvent(`操作失败: ${err}`);
                }
            });
        });
    } catch (err) {
        console.error('刷新窗口列表失败:', err);
    }
}

// 写入事件日志
function logEvent(text) {
    const logEl = document.getElementById('eventLog');
    if (!logEl) return;
    const line = document.createElement('div');
    line.className = 'log-line event';
    line.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
    logEl.appendChild(line);
    while (logEl.children.length > 50) {
        logEl.removeChild(logEl.firstChild);
    }
    logEl.scrollTop = logEl.scrollHeight;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
