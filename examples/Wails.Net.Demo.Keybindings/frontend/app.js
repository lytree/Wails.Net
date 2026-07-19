/**
 * Wails.Net Demo - Keybindings 前端脚本
 * 演示前端动态注册/注销快捷键，订阅后端与前端快捷键触发事件。
 */

document.addEventListener('DOMContentLoaded', () => {
    initRegisterControls();
    initHistoryControls();
    subscribeKeybindingEvents();
    refreshHistory();
});

// 订阅快捷键触发事件
function subscribeKeybindingEvents() {
    // 后端 KeyBindingManager 触发的事件（由 Program.cs 广播）
    wails.events?.on('keybinding:pressed', (data) => {
        console.log('后端快捷键触发：', data);
        refreshHistory();
    });

    // 前端通过 globalshortcut.register 注册的快捷键触发事件
    //（由 GlobalShortcutPlugin 广播，payload 为 accelerator 字符串）
    wails.events?.on('globalshortcut:pressed', async (accelerator) => {
        console.log('前端快捷键触发：', accelerator);
        // 将前端触发也记录到后端历史
        if (accelerator) {
            try {
                await wails.call('KeybindingLogService.RecordFrontendPress', [accelerator]);
                await refreshHistory();
            } catch (err) {
                console.error('记录前端触发失败:', err);
            }
        }
    });
}

// 注册/注销/查询
function initRegisterControls() {
    document.getElementById('registerBtn')?.addEventListener('click', async () => {
        const acc = document.getElementById('acceleratorInput').value.trim();
        if (!acc) {
            showRegisterResult('请输入加速键');
            return;
        }
        try {
            await wails.call('globalshortcut.register', [acc]);
            showRegisterResult(`已注册：${acc}`);
        } catch (err) {
            showRegisterResult(`注册失败：${err}`);
        }
    });

    document.getElementById('unregisterBtn')?.addEventListener('click', async () => {
        const acc = document.getElementById('acceleratorInput').value.trim();
        if (!acc) {
            showRegisterResult('请输入加速键');
            return;
        }
        try {
            await wails.call('globalshortcut.unregister', [acc]);
            showRegisterResult(`已注销：${acc}`);
        } catch (err) {
            showRegisterResult(`注销失败：${err}`);
        }
    });

    document.getElementById('isRegisteredBtn')?.addEventListener('click', async () => {
        const acc = document.getElementById('acceleratorInput').value.trim();
        if (!acc) {
            showRegisterResult('请输入加速键');
            return;
        }
        try {
            const registered = await wails.call('globalshortcut.isRegistered', [acc]);
            showRegisterResult(`${acc} 注册状态：${registered ? '已注册' : '未注册'}`);
        } catch (err) {
            showRegisterResult(`查询失败：${err}`);
        }
    });
}

// 历史与统计控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('statsBtn')?.addEventListener('click', async () => {
        try {
            const stats = await wails.call('KeybindingLogService.GetCountByAccelerator', []);
            const entries = Object.entries(stats || {});
            if (entries.length === 0) {
                document.getElementById('statsResult').textContent = '（暂无统计数据）';
                return;
            }
            const text = entries
                .sort((a, b) => b[1] - a[1])
                .map(([k, v]) => `${k}: ${v} 次`)
                .join('\n');
            document.getElementById('statsResult').textContent = text;
        } catch (err) {
            document.getElementById('statsResult').textContent = `查询失败：${err}`;
        }
    });

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('KeybindingLogService.ClearHistory', []);
            await refreshHistory();
            showRegisterResult('历史已清空');
        } catch (err) {
            showRegisterResult(`清空失败：${err}`);
        }
    });
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('KeybindingLogService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="5" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.source)}</td>
                <td>${escapeHtml(item.accelerator || '')}</td>
                <td>${escapeHtml(item.message)}</td>
                <td>${item.time ? new Date(item.time).toLocaleTimeString() : ''}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showRegisterResult(text) {
    document.getElementById('registerResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
