/**
 * Wails.Net Demo - Screen 前端脚本
 * 演示 ScreenPlugin 提供的屏幕查询命令。
 * 通过 wails.call('screen.getPrimary', []) 获取主屏，
 * 通过 wails.call('screen.getAll', []) 获取所有屏幕，
 * 通过 ScreenLogService 绑定方法记录查询日志。
 */

document.addEventListener('DOMContentLoaded', () => {
    initButtons();
    loadAll();
});

function initButtons() {
    document.getElementById('loadPrimaryBtn')?.addEventListener('click', loadPrimary);
    document.getElementById('loadAllBtn')?.addEventListener('click', loadAll);
    document.getElementById('refreshBtn')?.addEventListener('click', async () => {
        await loadPrimary();
        await loadAll();
        await refreshLog();
    });
}

// 查询主屏幕
async function loadPrimary() {
    try {
        const screen = await wails.call('screen.getPrimary', []);
        await wails.call('ScreenLogService.LogScreenQuery', ['getPrimary']);
        renderPrimary(screen);
        await refreshLog();
    } catch (err) {
        document.getElementById('primaryInfo').innerHTML = `<p class="empty">查询失败: ${escapeHtml(String(err))}</p>`;
    }
}

// 查询所有屏幕
async function loadAll() {
    try {
        const screens = await wails.call('screen.getAll', []);
        await wails.call('ScreenLogService.LogScreenQuery', ['getAll']);
        renderAll(screens || []);
        await refreshLog();
    } catch (err) {
        document.getElementById('allScreens').innerHTML = `<p class="empty">查询失败: ${escapeHtml(String(err))}</p>`;
    }
}

// 渲染主屏信息卡片
function renderPrimary(screen) {
    const el = document.getElementById('primaryInfo');
    if (!screen) {
        el.innerHTML = '<p class="empty">（未获取到主屏信息）</p>';
        return;
    }
    el.innerHTML = `
        <div class="row"><span>名称：</span><strong>${escapeHtml(screen.name || '-')}</strong><span class="badge">主屏</span></div>
        <div class="row"><span>分辨率：</span><strong>${screen.width} x ${screen.height}</strong></div>
        <div class="row"><span>坐标：</span><strong>(${screen.x}, ${screen.y})</strong></div>
        <div class="row"><span>工作区：</span><strong>${screen.workAreaWidth} x ${screen.workAreaHeight} @ (${screen.workAreaX}, ${screen.workAreaY})</strong></div>
        <div class="row"><span>DPI 缩放：</span><strong>${screen.scaleFactor}</strong><span class="badge secondary">ScaleFactor</span></div>
    `;
}

// 渲染所有屏幕列表
function renderAll(screens) {
    const el = document.getElementById('allScreens');
    if (!screens || screens.length === 0) {
        el.innerHTML = '<p class="empty">（未获取到屏幕信息）</p>';
        return;
    }
    el.innerHTML = screens.map((s, idx) => `
        <div class="screen-item ${s.isPrimary ? 'primary-item' : ''}">
            <div class="name">#${idx + 1} ${escapeHtml(s.name || '-')} ${s.isPrimary ? '<span class="badge">主屏</span>' : ''}</div>
            <div class="row"><span>分辨率：</span><strong>${s.width} x ${s.height}</strong> <span>坐标：</span><strong>(${s.x}, ${s.y})</strong></div>
            <div class="row"><span>工作区：</span><strong>${s.workAreaWidth} x ${s.workAreaHeight} @ (${s.workAreaX}, ${s.workAreaY})</strong></div>
            <div class="row"><span>DPI 缩放：</span><strong>${s.scaleFactor}</strong></div>
        </div>
    `).join('');
}

// 刷新查询日志
async function refreshLog() {
    try {
        const log = await wails.call('ScreenLogService.GetQueryLog', []);
        const list = document.getElementById('logList');
        if (!list) return;
        if (!log || log.length === 0) {
            list.innerHTML = '<li class="empty">（暂无查询记录）</li>';
            return;
        }
        list.innerHTML = log.slice().reverse().map(item =>
            `<li>${escapeHtml(item)}</li>`
        ).join('');
    } catch (err) {
        console.error('刷新日志失败:', err);
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
