/**
 * Wails.Net Demo - Environment 前端脚本
 * 演示通过 OsInfoPlugin / PathPlugin / AppInfoPlugin 命令查询环境信息，
 * 并通过 EnvironmentLogService 绑定方法记录查询历史。
 *
 * 注意：OsInfoPlugin / PathPlugin / AppInfoPlugin 不直接提供"环境变量读写"命令，
 * 因此环境变量操作在前端使用浏览器 API（无法持久化），仅作演示。
 * 真正的环境变量读取应在后端绑定方法中实现，此处保持简单演示。
 */

document.addEventListener('DOMContentLoaded', () => {
    refreshOs();
    refreshPath();
    refreshApp();
    refreshLog();
    document.getElementById('refreshOsBtn')?.addEventListener('click', refreshOs);
    document.getElementById('refreshPathBtn')?.addEventListener('click', refreshPath);
    document.getElementById('refreshAppBtn')?.addEventListener('click', refreshApp);
    document.getElementById('getEnvBtn')?.addEventListener('click', onGetEnv);
    document.getElementById('setEnvBtn')?.addEventListener('click', onSetEnv);
    document.getElementById('refreshLogBtn')?.addEventListener('click', refreshLog);
    document.getElementById('clearLogBtn')?.addEventListener('click', onClearLog);
});

// ============================================================
// 操作系统信息（os.* / system.* 命令）
// ============================================================
async function refreshOs() {
    const items = [
        ['os.platform', 'osPlatform'],
        ['os.hostname', 'osHostname'],
        ['os.arch', 'osArch'],
        ['os.locale', 'osLocale'],
        ['os.version', 'osVersion'],
        ['os.type', 'osType'],
        ['system.timezone', 'systemTimezone'],
    ];
    for (const [cmd, elId] of items) {
        try {
            const value = await wails.call(cmd, []);
            document.getElementById(elId).textContent = value;
            await logQuery(cmd, value);
        } catch (err) {
            document.getElementById(elId).textContent = `失败: ${err}`;
        }
    }
}

// ============================================================
// 路径信息（path.* 命令）
// ============================================================
async function refreshPath() {
    const items = [
        ['path.tempDir', 'pathTemp'],
        ['path.appDataDir', 'pathAppData'],
        ['path.appConfigDir', 'pathAppConfig'],
        ['path.runtimeDir', 'pathRuntime'],
        ['path.homeDir', 'pathHome'],
        ['path.downloadDir', 'pathDownload'],
        ['path.documentDir', 'pathDocument'],
    ];
    for (const [cmd, elId] of items) {
        try {
            const value = await wails.call(cmd, []);
            document.getElementById(elId).textContent = value;
            await logQuery(cmd, value);
        } catch (err) {
            document.getElementById(elId).textContent = `失败: ${err}`;
        }
    }
}

// ============================================================
// 应用信息（app.* 命令）
// ============================================================
async function refreshApp() {
    const items = [
        ['app.getName', 'appName'],
        ['app.getVersion', 'appVersion'],
        ['app.getDescription', 'appDescription'],
        ['app.getTauriVersion', 'appTauriVer'],
    ];
    for (const [cmd, elId] of items) {
        try {
            const value = await wails.call(cmd, []);
            document.getElementById(elId).textContent = value;
            await logQuery(cmd, value);
        } catch (err) {
            document.getElementById(elId).textContent = `失败: ${err}`;
        }
    }
}

// ============================================================
// 环境变量（浏览器端演示，实际应用应在后端绑定方法中实现）
// ============================================================
function onGetEnv() {
    const key = document.getElementById('envKeyInput').value.trim();
    if (!key) {
        showEnv('请输入变量名');
        return;
    }
    // 浏览器无法直接读取系统环境变量，此处仅展示无权限提示
    showEnv(`浏览器沙箱无法直接读取系统环境变量 "${key}"。\n如需读取，请在后端绑定方法中使用 Environment.GetEnvironmentVariable("${key}")。`);
}

function onSetEnv() {
    const key = document.getElementById('envKeyInput').value.trim();
    if (!key) {
        showEnv('请输入变量名');
        return;
    }
    showEnv(`浏览器沙箱无法设置系统环境变量。\n请在后端绑定方法中使用 Environment.SetEnvironmentVariable("${key}", value)。`);
}

// ============================================================
// 查询日志
// ============================================================
async function logQuery(type, result) {
    try {
        await wails.call('EnvironmentLogService.LogQuery', [type, String(result)]);
    } catch (err) {
        // 忽略日志记录失败
    }
}

async function refreshLog() {
    try {
        const queries = await wails.call('EnvironmentLogService.GetQueries', []);
        const list = document.getElementById('logList');
        if (!queries || queries.length === 0) {
            list.innerHTML = '<li class="empty">（暂无查询）</li>';
            return;
        }
        list.innerHTML = queries.map(item => `<li>${escapeHtml(item)}</li>`).join('');
    } catch (err) {
        console.error('刷新日志失败:', err);
    }
}

async function onClearLog() {
    try {
        await wails.call('EnvironmentLogService.ClearLog', []);
        await refreshLog();
    } catch (err) {
        console.error('清空日志失败:', err);
    }
}

function showEnv(text) {
    document.getElementById('envResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
