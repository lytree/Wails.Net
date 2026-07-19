/**
 * Wails.Net Demo - Dialogs 前端脚本
 * 演示 dialog.* 插件命令调用各类原生对话框，并通过 DialogHistoryService 记录操作历史。
 */

document.addEventListener('DOMContentLoaded', () => {
    initMessageDialogs();
    initFileDialogs();
    initHistoryControls();
    refreshHistory();
});

// 消息类对话框
function initMessageDialogs() {
    document.getElementById('messageBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.message', ['提示', '这是一条信息对话框']);
            await recordAction('dialog.message', `按钮索引: ${result}`);
            showResult('messageResult', `dialog.message 返回: ${result}`);
        } catch (err) {
            showResult('messageResult', `失败: ${err}`);
        }
    });

    document.getElementById('warningBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.warning', ['警告', '这是一条警告对话框']);
            await recordAction('dialog.warning', `按钮索引: ${result}`);
            showResult('messageResult', `dialog.warning 返回: ${result}`);
        } catch (err) {
            showResult('messageResult', `失败: ${err}`);
        }
    });

    document.getElementById('errorBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.error', ['错误', '这是一条错误对话框']);
            await recordAction('dialog.error', `按钮索引: ${result}`);
            showResult('messageResult', `dialog.error 返回: ${result}`);
        } catch (err) {
            showResult('messageResult', `失败: ${err}`);
        }
    });

    document.getElementById('questionBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.question', ['询问', '是否继续？']);
            const text = result === 1 ? 'Yes (1)' : 'No (0)';
            await recordAction('dialog.question', text);
            showResult('messageResult', `dialog.question 返回: ${text}`);
        } catch (err) {
            showResult('messageResult', `失败: ${err}`);
        }
    });
}

// 文件类对话框
function initFileDialogs() {
    document.getElementById('openFileBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.openFile', [
                '选择文件', null, ['文本文件 (*.txt)', '所有文件 (*.*)']
            ]);
            const text = result || '(未选择)';
            await recordAction('dialog.openFile', text);
            showResult('fileResult', `选中: ${text}`);
        } catch (err) {
            showResult('fileResult', `失败: ${err}`);
        }
    });

    document.getElementById('openMultipleBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.openMultipleFiles', [
                '选择多个文件', null, ['所有文件 (*.*)']
            ]);
            const text = (result && result.length > 0) ? result.join('\n') : '(未选择)';
            await recordAction('dialog.openMultipleFiles', `${result?.length || 0} 个文件`);
            showResult('fileResult', `选中文件:\n${text}`);
        } catch (err) {
            showResult('fileResult', `失败: ${err}`);
        }
    });

    document.getElementById('saveFileBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('dialog.saveFile', [
                '保存文件', null, 'untitled.txt', ['文本文件 (*.txt)']
            ]);
            const text = result || '(未选择)';
            await recordAction('dialog.saveFile', text);
            showResult('fileResult', `保存到: ${text}`);
        } catch (err) {
            showResult('fileResult', `失败: ${err}`);
        }
    });
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshHistoryBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearHistoryBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('DialogHistoryService.ClearHistory', []);
            await refreshHistory();
        } catch (err) {
            console.error('清空失败:', err);
        }
    });
}

// 记录一次操作
async function recordAction(action, result) {
    try {
        await wails.call('DialogHistoryService.RecordAction', [action, result]);
        await refreshHistory();
    } catch (err) {
        console.error('记录失败:', err);
    }
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('DialogHistoryService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.action)}</td>
                <td>${escapeHtml(item.result)}</td>
                <td>${new Date(item.timestamp).toLocaleString()}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showResult(elementId, text) {
    document.getElementById(elementId).textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
