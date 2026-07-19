/**
 * Wails.Net Demo - Binding 前端脚本
 * 演示 wails.call 调用各类后端绑定方法。
 */

document.addEventListener('DOMContentLoaded', () => {
    initGreet();
    initTime();
    initAddInt();
    initAddDouble();
    initGetUser();
    initGetItems();
    initThrow();
    initLongTask();
});

// 全局取消控制器，用于长任务取消
let currentCancellationToken = null;

// 同步方法
function initGreet() {
    document.getElementById('greetBtn')?.addEventListener('click', async () => {
        const name = document.getElementById('nameInput').value;
        try {
            const result = await wails.call('BindingService.Greet', [name]);
            document.getElementById('greetResult').textContent = result;
        } catch (err) {
            document.getElementById('greetResult').textContent = `错误: ${err}`;
        }
    });
}

// 异步方法
function initTime() {
    document.getElementById('timeBtn')?.addEventListener('click', async () => {
        try {
            const result = await wails.call('BindingService.GetCurrentTimeAsync', []);
            document.getElementById('timeResult').textContent = `当前时间: ${result}`;
        } catch (err) {
            document.getElementById('timeResult').textContent = `错误: ${err}`;
        }
    });
}

// 整数加法重载
function initAddInt() {
    document.getElementById('addIntBtn')?.addEventListener('click', async () => {
        const a = parseInt(document.getElementById('intA').value, 10);
        const b = parseInt(document.getElementById('intB').value, 10);
        try {
            const result = await wails.call('BindingService.Add', [a, b]);
            document.getElementById('addIntResult').textContent = `int Add: ${a} + ${b} = ${result}`;
        } catch (err) {
            document.getElementById('addIntResult').textContent = `错误: ${err}`;
        }
    });
}

// 浮点数加法重载
function initAddDouble() {
    document.getElementById('addDoubleBtn')?.addEventListener('click', async () => {
        const a = parseFloat(document.getElementById('doubleA').value);
        const b = parseFloat(document.getElementById('doubleB').value);
        try {
            const result = await wails.call('BindingService.Add', [a, b]);
            document.getElementById('addDoubleResult').textContent = `double Add: ${a} + ${b} = ${result}`;
        } catch (err) {
            document.getElementById('addDoubleResult').textContent = `错误: ${err}`;
        }
    });
}

// 复杂对象返回
function initGetUser() {
    document.getElementById('getUserBtn')?.addEventListener('click', async () => {
        const id = parseInt(document.getElementById('userId').value, 10);
        try {
            const user = await wails.call('BindingService.GetUser', [id]);
            document.getElementById('userResult').innerHTML = `
                <div><strong>Id:</strong> ${user.id}</div>
                <div><strong>Name:</strong> ${user.name}</div>
                <div><strong>Email:</strong> ${user.email}</div>
                <div><strong>CreatedAt:</strong> ${new Date(user.createdAt).toLocaleString()}</div>
            `;
        } catch (err) {
            document.getElementById('userResult').textContent = `错误: ${err}`;
        }
    });
}

// 集合返回
function initGetItems() {
    document.getElementById('getItemsBtn')?.addEventListener('click', async () => {
        try {
            const items = await wails.call('BindingService.GetItems', []);
            document.getElementById('itemsResult').innerHTML =
                `<ul>${items.map(i => `<li>${i}</li>`).join('')}</ul>`;
        } catch (err) {
            document.getElementById('itemsResult').textContent = `错误: ${err}`;
        }
    });
}

// 异常处理
function initThrow() {
    document.getElementById('throwBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('BindingService.ThrowError', []);
            document.getElementById('throwResult').textContent = '未抛出异常';
        } catch (err) {
            document.getElementById('throwResult').textContent = `已捕获异常: ${err}`;
        }
    });
}

// CancellationToken 异步
function initLongTask() {
    document.getElementById('longTaskBtn')?.addEventListener('click', async () => {
        document.getElementById('longTaskResult').textContent = '长任务运行中...';
        currentCancellationToken = { cancelled: false };
        const localToken = currentCancellationToken;
        try {
            // 通过轮询检查前端取消标志（实际项目可通过 wails 的取消 API）
            const promise = wails.call('BindingService.LongTask', []);
            const cancelCheck = setInterval(() => {
                if (localToken.cancelled) {
                    // 注意：此处仅演示前端取消逻辑，实际取消需后端 CancellationToken 支持
                    clearInterval(cancelCheck);
                }
            }, 200);
            const result = await promise;
            clearInterval(cancelCheck);
            document.getElementById('longTaskResult').textContent = result;
        } catch (err) {
            document.getElementById('longTaskResult').textContent = `长任务失败: ${err}`;
        } finally {
            currentCancellationToken = null;
        }
    });

    document.getElementById('cancelBtn')?.addEventListener('click', () => {
        if (currentCancellationToken) {
            currentCancellationToken.cancelled = true;
            document.getElementById('longTaskResult').textContent = '已请求取消（前端标志已设置）';
        }
    });
}
