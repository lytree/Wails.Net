/**
 * Wails.Net Demo - Updater 前端脚本
 * 演示多 Provider 更新检查、版本切换、下载流程，订阅更新相关事件。
 */

document.addEventListener('DOMContentLoaded', () => {
    initStatusControls();
    initCheckControl();
    initDownloadControl();
    initHistoryControls();
    subscribeUpdaterEvents();
    refreshProviders();
    refreshHistory();
});

// 状态控制：设置版本 / 切换 Provider 链 / 查询 Provider
function initStatusControls() {
    document.getElementById('setVersionBtn')?.addEventListener('click', async () => {
        const version = document.getElementById('currentVersionInput').value.trim();
        if (!version) {
            showStatusResult('请输入版本号');
            return;
        }
        try {
            await wails.call('UpdaterDemoService.SetCurrentVersion', [version]);
            showStatusResult(`当前版本已设置为 ${version}`);
        } catch (err) {
            showStatusResult(`设置失败：${err}`);
        }
    });

    document.getElementById('switchProviderBtn')?.addEventListener('click', async () => {
        const preset = document.getElementById('providerPresetSelect').value;
        try {
            await wails.call('UpdaterDemoService.SwitchProviderChain', [preset]);
            showStatusResult(`已切换到 ${preset} 预设`);
            await refreshProviders();
        } catch (err) {
            showStatusResult(`切换失败：${err}`);
        }
    });

    document.getElementById('refreshProvidersBtn')?.addEventListener('click', refreshProviders);
}

// 检查更新
function initCheckControl() {
    document.getElementById('checkBtn')?.addEventListener('click', async () => {
        document.getElementById('checkResult').textContent = '正在检查...';
        try {
            const result = await wails.call('UpdaterDemoService.CheckForUpdatesAsync', []);
            const lines = [
                `当前版本：${result.currentVersion}`,
                `远端版本：${result.version}`,
                `来源 Provider：${result.provider}`,
                `是否有更新：${result.hasUpdate ? '是' : '否'}`,
                `下载 URL：${result.downloadUrl || '（无）'}`,
                `发行说明：${result.releaseNotes || '（无）'}`,
            ];
            document.getElementById('checkResult').textContent = lines.join('\n');
            await refreshHistory();
        } catch (err) {
            document.getElementById('checkResult').textContent = `检查失败：${err}`;
        }
    });
}

// 下载更新
function initDownloadControl() {
    document.getElementById('downloadBtn')?.addEventListener('click', async () => {
        const url = document.getElementById('downloadUrlInput').value.trim();
        if (!url) {
            document.getElementById('downloadResult').textContent = '请输入 URL';
            return;
        }
        document.getElementById('downloadResult').textContent = '正在下载...';
        try {
            const result = await wails.call('UpdaterDemoService.DownloadUpdateAsync', [url]);
            document.getElementById('downloadResult').textContent = result;
            await refreshHistory();
        } catch (err) {
            document.getElementById('downloadResult').textContent = `下载失败：${err}`;
            await refreshHistory();
        }
    });
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshHistoryBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearHistoryBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('UpdaterDemoService.ClearHistory', []);
            await refreshHistory();
        } catch (err) {
            console.error('清空历史失败:', err);
        }
    });
}

// 订阅 UpdaterService 广播的事件
function subscribeUpdaterEvents() {
    const events = [
        'wails:updater:update-available',
        'wails:updater:no-update',
        'wails:updater:download-started',
        'wails:updater:download-progress',
        'wails:updater:download-complete',
        'wails:updater:download-error',
        'wails:updater:install-started',
        'wails:updater:install-complete',
        'wails:updater:install-error',
        'wails:updater:update-applied',
    ];
    events.forEach(name => {
        wails.events?.on(name, (data) => {
            console.log(`事件 ${name}:`, data);
            refreshHistory();
        });
    });
}

// 查询已注册 Provider 列表
async function refreshProviders() {
    try {
        const providers = await wails.call('UpdaterDemoService.GetProviders', []);
        const text = (providers && providers.length > 0)
            ? providers.join(', ')
            : '（无）';
        document.getElementById('providersList').textContent = text;
    } catch (err) {
        document.getElementById('providersList').textContent = `查询失败：${err}`;
    }
}

// 刷新历史
async function refreshHistory() {
    try {
        const history = await wails.call('UpdaterDemoService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.stage)}</td>
                <td>${escapeHtml(item.message)}</td>
                <td>${item.time ? new Date(item.time).toLocaleTimeString() : ''}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showStatusResult(text) {
    document.getElementById('statusResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
