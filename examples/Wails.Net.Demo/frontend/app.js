/**
 * Wails.Net Demo 前端 JavaScript
 *
 * 演示如何调用后端绑定的 C# 方法和命令。
 */

// 等待 DOM 加载完成
document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    initGreetingPanel();
    initTodoPanel();
    initCounterPanel();
    initSystemPanel();
    initP1FeaturesPanel();
});

// =========================================================================
// 标签页切换
// =========================================================================
function initTabs() {
    const tabs = document.querySelectorAll('.tab');
    const panels = document.querySelectorAll('.panel');

    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const targetTab = tab.dataset.tab;

            tabs.forEach(t => t.classList.remove('active'));
            panels.forEach(p => p.classList.remove('active'));

            tab.classList.add('active');
            document.getElementById(targetTab)?.classList.add('active');
        });
    });
}

// =========================================================================
// 问候服务面板
// =========================================================================
function initGreetingPanel() {
    // 打招呼
    document.getElementById('greetBtn')?.addEventListener('click', async () => {
        const name = document.getElementById('nameInput').value;
        try {
            // 调用后端绑定的 GreetingService.Greet 方法
            const result = await wails.call('GreetingService.Greet', [name]);
            document.getElementById('greetResult').textContent = result;
        } catch (err) {
            console.error('调用失败:', err);
        }
    });

    // 获取当前时间
    document.getElementById('timeBtn')?.addEventListener('click', async () => {
        try {
            // 调用异步方法
            const result = await wails.call('GreetingService.GetCurrentTimeAsync', []);
            document.getElementById('timeResult').textContent = `当前时间: ${result}`;
        } catch (err) {
            console.error('调用失败:', err);
        }
    });

    // 加法计算
    document.getElementById('addBtn')?.addEventListener('click', async () => {
        const a = parseInt(document.getElementById('numA').value);
        const b = parseInt(document.getElementById('numB').value);
        try {
            const result = await wails.call('GreetingService.Add', [a, b]);
            document.getElementById('addResult').textContent = `结果: ${a} + ${b} = ${result}`;
        } catch (err) {
            console.error('调用失败:', err);
        }
    });
}

// =========================================================================
// 待办事项面板
// =========================================================================
let todos = [];

async function initTodoPanel() {
    await refreshTodos();

    // 添加待办
    document.getElementById('addTodoBtn')?.addEventListener('click', async () => {
        const input = document.getElementById('todoInput');
        const title = input.value.trim();
        if (!title) return;

        try {
            await wails.call('TodoService.Add', [title]);
            input.value = '';
            await refreshTodos();
        } catch (err) {
            console.error('添加失败:', err);
        }
    });

    // 清除已完成
    document.getElementById('clearCompletedBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('TodoService.ClearCompleted', []);
            await refreshTodos();
        } catch (err) {
            console.error('清除失败:', err);
        }
    });
}

async function refreshTodos() {
    try {
        todos = await wails.call('TodoService.GetAll', []);
        const stats = await wails.call('TodoService.GetStats', []);

        document.getElementById('totalCount').textContent = stats.total;
        document.getElementById('completedCount').textContent = stats.completed;
        document.getElementById('pendingCount').textContent = stats.pending;

        renderTodoList();
    } catch (err) {
        console.error('获取待办失败:', err);
    }
}

function renderTodoList() {
    const list = document.getElementById('todoList');
    if (!list) return;

    list.innerHTML = todos.map(todo => `
        <li class="todo-item ${todo.completed ? 'completed' : ''}">
            <input type="checkbox" ${todo.completed ? 'checked' : ''}
                   onchange="toggleTodo(${todo.id})">
            <span class="todo-title">${escapeHtml(todo.title)}</span>
            <span class="todo-date">${new Date(todo.createdAt).toLocaleString()}</span>
            <button class="btn btn-small btn-danger" onclick="deleteTodo(${todo.id})">删除</button>
        </li>
    `).join('');
}

async function toggleTodo(id) {
    try {
        await wails.call('TodoService.Toggle', [id]);
        await refreshTodos();
    } catch (err) {
        console.error('切换失败:', err);
    }
}

async function deleteTodo(id) {
    try {
        await wails.call('TodoService.Delete', [id]);
        await refreshTodos();
    } catch (err) {
        console.error('删除失败:', err);
    }
}

// 将函数暴露到全局，供内联事件处理使用
window.toggleTodo = toggleTodo;
window.deleteTodo = deleteTodo;

// =========================================================================
// 计数器插件面板
// =========================================================================
function initCounterPanel() {
    // 获取初始值
    refreshCounter();

    document.getElementById('incrementBtn')?.addEventListener('click', async () => {
        try {
            // 调用自定义插件注册的命令
            const result = await wails.call('counter.increment', []);
            document.getElementById('counterValue').textContent = result;
        } catch (err) {
            console.error('增加失败:', err);
        }
    });

    document.getElementById('decrementBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('counter.decrement', []);
            document.getElementById('counterValue').textContent = result;
        } catch (err) {
            console.error('减少失败:', err);
        }
    });

    document.getElementById('resetBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('counter.reset', []);
            document.getElementById('counterValue').textContent = '0';
        } catch (err) {
            console.error('重置失败:', err);
        }
    });
}

async function refreshCounter() {
    try {
        const value = await wails.call('counter.getValue', []);
        document.getElementById('counterValue').textContent = value;
    } catch (err) {
        // 插件可能未加载，使用默认值
        document.getElementById('counterValue').textContent = '0';
    }
}

// =========================================================================
// 系统信息面板
// =========================================================================
function initSystemPanel() {
    // 获取服务器信息
    document.getElementById('getServerInfoBtn')?.addEventListener('click', async () => {
        try {
            const info = await wails.call('GreetingService.GetServerInfo', []);
            const resultDiv = document.getElementById('serverInfoResult');
            resultDiv.innerHTML = Object.entries(info)
                .map(([key, value]) => `<div class="info-row"><strong>${key}:</strong> ${value}</div>`)
                .join('');
        } catch (err) {
            console.error('获取信息失败:', err);
        }
    });

    // 复制到剪贴板
    document.getElementById('copyBtn')?.addEventListener('click', async () => {
        const text = document.getElementById('clipboardInput').value;
        if (!text) return;
        try {
            await wails.call('clipboard.setText', [text]);
            document.getElementById('clipboardResult').textContent = '已复制到剪贴板';
        } catch (err) {
            console.error('复制失败:', err);
        }
    });

    // 从剪贴板粘贴
    document.getElementById('pasteBtn')?.addEventListener('click', async () => {
        try {
            const text = await wails.call('clipboard.getText', []);
            document.getElementById('clipboardResult').textContent = `剪贴板内容: ${text}`;
        } catch (err) {
            console.error('粘贴失败:', err);
        }
    });

    // 发送通知
    document.getElementById('notifyBtn')?.addEventListener('click', async () => {
        const text = document.getElementById('notificationInput').value;
        if (!text) return;
        try {
            await wails.call('notification.show', [{ title: 'Wails.Net Demo', body: text }]);
        } catch (err) {
            console.error('通知失败:', err);
        }
    });
}

// =========================================================================
// P1 新能力面板
// =========================================================================
function initP1FeaturesPanel() {
    // P1-1：BrowserManager 打开外部 URL
    document.getElementById('openUrlBtn')?.addEventListener('click', async () => {
        const url = document.getElementById('browserUrlInput').value;
        if (!url) return;
        try {
            const result = await wails.call('P1FeaturesService.OpenExternalUrl', [url]);
            document.getElementById('browserResult').textContent = result;
        } catch (err) {
            console.error('打开 URL 失败:', err);
            document.getElementById('browserResult').textContent = `错误: ${err}`;
        }
    });

    // P1-3：后端写日志（Logger 双向桥接）
    document.getElementById('logFromBackendBtn')?.addEventListener('click', async () => {
        const level = document.getElementById('logLevelSelect').value;
        const message = document.getElementById('logMessageInput').value;
        if (!message) return;
        try {
            const result = await wails.call('P1FeaturesService.LogFromBackend', [level, message]);
            document.getElementById('logResult').textContent = result;
            // 同时在前端 console 打印，对比双向桥接效果
            console.log(`[前端 console] 后端已写入 ${level} 日志: ${message}`);
        } catch (err) {
            console.error('写日志失败:', err);
        }
    });

    // P1-6：Service Route 挂载（fetch 自定义路由）
    document.getElementById('fetchHealthBtn')?.addEventListener('click', async () => {
        try {
            const response = await fetch('/api/health');
            const text = await response.text();
            document.getElementById('serviceRouteResult').textContent =
                `GET /api/health → ${response.status}\n${text}`;
        } catch (err) {
            document.getElementById('serviceRouteResult').textContent = `请求失败: ${err}`;
        }
    });

    document.getElementById('fetchVersionBtn')?.addEventListener('click', async () => {
        try {
            const response = await fetch('/api/version');
            const text = await response.text();
            document.getElementById('serviceRouteResult').textContent =
                `GET /api/version → ${response.status}\n${text}`;
        } catch (err) {
            document.getElementById('serviceRouteResult').textContent = `请求失败: ${err}`;
        }
    });

    // P1-7：Event Hooks（获取应用状态）
    document.getElementById('getAppStatusBtn')?.addEventListener('click', async () => {
        try {
            const json = await wails.call('P1FeaturesService.GetApplicationStatus', []);
            const status = JSON.parse(json);
            const resultDiv = document.getElementById('appStatusResult');
            resultDiv.innerHTML = `
                <div class="info-row"><strong>isRunning:</strong> ${status.isRunning}</div>
                <div class="info-row"><strong>shouldQuit:</strong> ${status.shouldQuit}</div>
                <div class="info-row"><strong>hasPostShutdownHook:</strong> ${status.hasPostShutdownHook}</div>
                <div class="info-row"><strong>hasShouldQuitHook:</strong> ${status.hasShouldQuitHook}</div>
            `;
        } catch (err) {
            console.error('获取状态失败:', err);
        }
    });

    // P1-7：触发 Shutdown（会关闭应用，谨慎使用）
    document.getElementById('triggerShutdownBtn')?.addEventListener('click', async () => {
        if (!confirm('确认要触发 Shutdown 吗？应用将退出并执行 PostShutdown 回调。')) return;
        try {
            await wails.call('P1FeaturesService.TriggerShutdown', []);
        } catch (err) {
            // 应用关闭时连接会断开，错误可忽略
            console.log('应用正在关闭');
        }
    });

    // P1-8：多 Provider Updater
    document.getElementById('getProvidersBtn')?.addEventListener('click', async () => {
        try {
            const json = await wails.call('P1FeaturesService.GetRegisteredProviders', []);
            const providers = JSON.parse(json);
            document.getElementById('updaterResult').textContent =
                providers.length > 0
                    ? `已注册 Provider: ${providers.join(', ')}`
                    : '未注册任何 Provider';
        } catch (err) {
            console.error('获取 Provider 失败:', err);
        }
    });

    document.getElementById('checkUpdatesBtn')?.addEventListener('click', async () => {
        try {
            const json = await wails.call('P1FeaturesService.CheckForUpdatesAsync', []);
            const result = JSON.parse(json);
            const resultDiv = document.getElementById('updaterResult');
            if (result.error) {
                resultDiv.textContent = `检查失败: ${result.error}`;
            } else {
                resultDiv.innerHTML = `
                    <div class="info-row"><strong>version:</strong> ${result.version}</div>
                    <div class="info-row"><strong>provider:</strong> ${result.provider}</div>
                    <div class="info-row"><strong>isNewer:</strong> ${result.isNewer}</div>
                    <div class="info-row"><strong>releaseNotes:</strong> ${result.releaseNotes || '(无)'}</div>
                `;
            }
        } catch (err) {
            console.error('检查更新失败:', err);
        }
    });
}

// =========================================================================
// 工具函数
// =========================================================================
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
