/**
 * Wails.Net Demo - Store 前端脚本
 * 演示通过 StorePlugin 的 store.* 命令操作键值存储，
 * 并通过 StoreLogService 绑定方法记录操作日志。
 */

document.addEventListener('DOMContentLoaded', () => {
    refreshLog();
    document.getElementById('setBtn')?.addEventListener('click', onSet);
    document.getElementById('getBtn')?.addEventListener('click', onGet);
    document.getElementById('deleteBtn')?.addEventListener('click', onDelete);
    document.getElementById('listBtn')?.addEventListener('click', onList);
    document.getElementById('refreshLogBtn')?.addEventListener('click', refreshLog);
    document.getElementById('clearLogBtn')?.addEventListener('click', onClearLog);
});

// 设置值：store.set(key, value)
async function onSet() {
    const key = document.getElementById('keyInput').value.trim();
    const value = document.getElementById('valueInput').value;
    if (!key) {
        showOp('请输入键名');
        return;
    }
    try {
        await wails.call('store.set', [key, value]);
        await logOperation(`set key="${key}" value="${value}"`);
        showOp(`已设置：${key} = ${value}`);
    } catch (err) {
        showOp(`set 失败: ${err}`);
    }
}

// 获取值：store.get(key)
async function onGet() {
    const key = document.getElementById('keyInput').value.trim();
    if (!key) {
        showOp('请输入键名');
        return;
    }
    try {
        const value = await wails.call('store.get', [key]);
        showOp(value === null || value === undefined
            ? `键 "${key}" 不存在`
            : `get "${key}" = ${value}`);
        await logOperation(`get key="${key}" → ${value}`);
    } catch (err) {
        showOp(`get 失败: ${err}`);
    }
}

// 删除值：store.delete(key)
async function onDelete() {
    const key = document.getElementById('keyInput').value.trim();
    if (!key) {
        showOp('请输入键名');
        return;
    }
    try {
        const result = await wails.call('store.delete', [key]);
        await logOperation(`delete key="${key}" → ${result ? '已删除' : '不存在'}`);
        showOp(result ? `已删除：${key}` : `键 "${key}" 不存在`);
    } catch (err) {
        showOp(`delete 失败: ${err}`);
    }
}

// 列出所有键：store.keys()
async function onList() {
    try {
        const keys = await wails.call('store.keys', []);
        await logOperation(`keys → [${(keys || []).join(', ')}]`);
        showOp(`所有键：\n${(keys && keys.length > 0) ? keys.join('\n') : '(空)'}`);
    } catch (err) {
        showOp(`keys 失败: ${err}`);
    }
}

// 清空日志（仅清前端日志，不影响 store 数据）
async function onClearLog() {
    try {
        await wails.call('StoreLogService.ClearLog', []);
        await refreshLog();
        showOp('日志已清空');
    } catch (err) {
        showOp(`清空日志失败: ${err}`);
    }
}

// 记录一条操作日志
async function logOperation(op) {
    try {
        await wails.call('StoreLogService.LogOperation', [op]);
        await refreshLog();
    } catch (err) {
        console.error('记录日志失败:', err);
    }
}

// 刷新日志显示
async function refreshLog() {
    try {
        const [count, recent] = await Promise.all([
            wails.call('StoreLogService.GetOperationCount', []),
            wails.call('StoreLogService.GetRecentOperations', []),
        ]);
        document.getElementById('opCount').textContent = count;
        const list = document.getElementById('logList');
        if (!recent || recent.length === 0) {
            list.innerHTML = '<li class="empty">（暂无操作）</li>';
        } else {
            list.innerHTML = recent.map(item => `<li>${escapeHtml(item)}</li>`).join('');
        }
    } catch (err) {
        console.error('刷新日志失败:', err);
    }
}

function showOp(text) {
    document.getElementById('opResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
