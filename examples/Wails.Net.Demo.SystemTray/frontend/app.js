/**
 * Wails.Net Demo - SystemTray 前端脚本
 * 演示通过 tray.* 插件命令操作托盘，通过 TrayLogService 绑定方法查询历史。
 * 同时订阅 tray:clicked 和 tray:event 事件以实时显示托盘活动。
 */

document.addEventListener('DOMContentLoaded', () => {
    initTrayControls();
    initNotify();
    initHistoryControls();
    subscribeTrayEvents();
    refreshHistory();
});

// 托盘属性与可见性操作
function initTrayControls() {
    document.getElementById('setTooltipBtn')?.addEventListener('click', async () => {
        const tooltip = document.getElementById('tooltipInput').value;
        try {
            await wails.call('tray.setTooltip', [{ tooltip }]);
            showResult(`已设置提示：${tooltip}`);
        } catch (err) {
            showResult(`设置失败：${err}`);
        }
    });

    document.getElementById('setLabelBtn')?.addEventListener('click', async () => {
        const label = document.getElementById('labelInput').value;
        try {
            await wails.call('tray.setLabel', [{ label }]);
            showResult(`已设置标签：${label}`);
        } catch (err) {
            showResult(`设置失败：${err}`);
        }
    });

    document.getElementById('showBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('tray.show', []);
            showResult('已显示托盘');
        } catch (err) {
            showResult(`显示失败：${err}`);
        }
    });

    document.getElementById('hideBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('tray.hide', []);
            showResult('已隐藏托盘');
        } catch (err) {
            showResult(`隐藏失败：${err}`);
        }
    });

    document.getElementById('isVisibleBtn')?.addEventListener('click', async () => {
        try {
            const visible = await wails.call('tray.isVisible', []);
            showResult(`托盘当前是否可见：${visible}`);
        } catch (err) {
            showResult(`查询失败：${err}`);
        }
    });
}

// 发送通知
function initNotify() {
    document.getElementById('notifyBtn')?.addEventListener('click', async () => {
        const title = document.getElementById('titleInput').value;
        const body = document.getElementById('bodyInput').value;
        if (!title || !body) {
            showResult('标题和内容不能为空');
            return;
        }
        try {
            await wails.call('TrayLogService.SendTrayNotification', [title, body]);
            showResult(`已发送通知：${title}`);
            await refreshHistory();
        } catch (err) {
            showResult(`发送失败：${err}`);
        }
    });
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('TrayLogService.ClearEvents', []);
            await refreshHistory();
            showResult('历史已清空');
        } catch (err) {
            showResult(`清空失败：${err}`);
        }
    });
}

// 订阅托盘事件
function subscribeTrayEvents() {
    // tray:clicked 由 Program.cs 在托盘左键点击时广播
    wails.events?.on('tray:clicked', (data) => {
        console.log('托盘点击事件：', data);
        refreshHistory();
    });

    // tray:event 由 TrayLogService.RecordEvent 广播
    wails.events?.on('tray:event', (data) => {
        console.log('托盘事件：', data);
        refreshHistory();
    });
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const events = await wails.call('TrayLogService.GetTrayEvents', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!events || events.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = events.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.kind)}</td>
                <td>${escapeHtml(item.message)}</td>
                <td>${item.time ? new Date(item.time).toLocaleTimeString() : ''}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showResult(text) {
    document.getElementById('trayResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
