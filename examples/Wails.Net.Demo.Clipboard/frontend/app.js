/**
 * Wails.Net Demo - Clipboard 前端脚本
 * 演示通过 clipboard.setText / clipboard.getText 命令操作剪贴板，
 * 并通过 ClipboardStatsService 绑定方法维护复制计数与历史记录。
 */

const history = [];

document.addEventListener('DOMContentLoaded', () => {
    refreshCount();
    initCopy();
    initPaste();
    initReset();
});

// 复制：先调插件命令 clipboard.setText，再调绑定方法 IncrementCount 累加计数
function initCopy() {
    document.getElementById('copyBtn')?.addEventListener('click', async () => {
        const text = document.getElementById('copyInput').value;
        if (!text) return;
        try {
            await wails.call('clipboard.setText', [text]);
            await wails.call('ClipboardStatsService.IncrementCount', []);
            history.unshift({ text, time: new Date().toLocaleTimeString() });
            if (history.length > 5) history.length = 5;
            renderHistory();
            document.getElementById('copyResult').textContent = `已复制: ${text}`;
            await refreshCount();
        } catch (err) {
            document.getElementById('copyResult').textContent = `复制失败: ${err}`;
        }
    });
}

// 粘贴：调用 clipboard.getText 读取剪贴板内容
function initPaste() {
    document.getElementById('pasteBtn')?.addEventListener('click', async () => {
        try {
            const text = await wails.call('clipboard.getText', []);
            document.getElementById('pasteResult').textContent = text || '(剪贴板为空)';
        } catch (err) {
            document.getElementById('pasteResult').textContent = `粘贴失败: ${err}`;
        }
    });
}

// 重置计数
function initReset() {
    document.getElementById('resetBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('ClipboardStatsService.ResetCount', []);
            await refreshCount();
            document.getElementById('copyResult').textContent = '计数已重置';
        } catch (err) {
            console.error('重置失败:', err);
        }
    });
}

// 刷新计数显示
async function refreshCount() {
    try {
        const count = await wails.call('ClipboardStatsService.GetCopyCount', []);
        document.getElementById('copyCount').textContent = count;
    } catch (err) {
        console.error('获取计数失败:', err);
    }
}

// 渲染历史记录列表
function renderHistory() {
    const list = document.getElementById('historyList');
    if (!list) return;
    if (history.length === 0) {
        list.innerHTML = '<li class="empty">（暂无记录）</li>';
        return;
    }
    list.innerHTML = history.map((item, idx) =>
        `<li><span class="idx">#${idx + 1}</span> <span class="time">${item.time}</span> <span class="text">${escapeHtml(item.text)}</span></li>`
    ).join('');
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
