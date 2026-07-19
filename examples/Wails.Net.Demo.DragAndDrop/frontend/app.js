/**
 * Wails.Net Demo - DragAndDrop 前端脚本
 * 订阅 wails:window:file:dropped 事件，将文件路径传给后端 FileDropService 持久化。
 * 演示运行时启用/禁用拖放、查询历史与统计。
 */

let dropEnabled = true;

document.addEventListener('DOMContentLoaded', () => {
    initToggleDrop();
    initHistoryControls();
    subscribeFileDropEvent();
    refreshHistory();
});

// 订阅平台广播的文件拖放事件
function subscribeFileDropEvent() {
    wails.events?.on('wails:window:file:dropped', async (data) => {
        // data 为文件路径字符串数组（或包含 paths 字段的对象，兼容两种 payload）
        const paths = extractPaths(data);
        const indicator = document.getElementById('dropIndicator');
        if (indicator) {
            indicator.textContent = `收到 ${paths.length} 个文件：\n${paths.join('\n')}`;
        }
        renderCurrentDrop(paths);

        // 将路径传给后端持久化
        if (paths.length > 0) {
            try {
                await wails.call('FileDropService.RecordDrop', [paths]);
                await refreshHistory();
            } catch (err) {
                console.error('记录拖放失败:', err);
            }
        }
    });
}

// 兼容两种 payload 形式：纯数组 或 { paths: [...] } 对象
function extractPaths(data) {
    if (!data) return [];
    if (Array.isArray(data)) return data;
    if (Array.isArray(data.paths)) return data.paths;
    if (typeof data === 'string') {
        try {
            const parsed = JSON.parse(data);
            if (Array.isArray(parsed)) return parsed;
            if (Array.isArray(parsed.paths)) return parsed.paths;
        } catch {
            return [data];
        }
    }
    return [];
}

// 渲染本次拖入的文件列表
function renderCurrentDrop(paths) {
    const body = document.getElementById('currentDropBody');
    if (!body) return;
    if (!paths || paths.length === 0) {
        body.innerHTML = '<tr><td colspan="3" class="empty">（暂无）</td></tr>';
        return;
    }
    body.innerHTML = paths.map((p, idx) => {
        const name = p.split(/[\\/]/).pop() || p;
        return `<tr>
            <td>${idx + 1}</td>
            <td>${escapeHtml(name)}</td>
            <td>${escapeHtml(p)}</td>
        </tr>`;
    }).join('');
}

// 启用/禁用拖放
function initToggleDrop() {
    document.getElementById('toggleDropBtn')?.addEventListener('click', async () => {
        dropEnabled = !dropEnabled;
        try {
            await wails.call('window.setFileDropEnabled', [{ enabled: dropEnabled }]);
            const btn = document.getElementById('toggleDropBtn');
            btn.textContent = dropEnabled ? '禁用拖放' : '启用拖放';
            showToggleResult(`已${dropEnabled ? '启用' : '禁用'}拖放`);
        } catch (err) {
            showToggleResult(`操作失败：${err}`);
            // 回滚状态
            dropEnabled = !dropEnabled;
        }
    });
}

// 历史与统计控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('statsBtn')?.addEventListener('click', async () => {
        try {
            const stats = await wails.call('FileDropService.GetStats', []);
            const sizeText = formatBytes(stats.totalSizeBytes);
            const exts = (stats.distinctExtensions || []).join(', ') || '（无）';
            document.getElementById('statsResult').textContent =
                `累计文件数：${stats.totalCount}\n累计大小：${sizeText}\n扩展名：${exts}`;
        } catch (err) {
            document.getElementById('statsResult').textContent = `查询失败：${err}`;
        }
    });

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('FileDropService.ClearHistory', []);
            await refreshHistory();
            showToggleResult('历史已清空');
        } catch (err) {
            showToggleResult(`清空失败：${err}`);
        }
    });
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('FileDropService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="5" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.name)}</td>
                <td>${escapeHtml(item.extension || '')}</td>
                <td>${item.sizeBytes < 0 ? '—' : formatBytes(item.sizeBytes)}</td>
                <td>${item.droppedAt ? new Date(item.droppedAt).toLocaleString() : ''}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showToggleResult(text) {
    document.getElementById('toggleResult').textContent = text;
}

function formatBytes(bytes) {
    if (bytes < 0) return '—';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
