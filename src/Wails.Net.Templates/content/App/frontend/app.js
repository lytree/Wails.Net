import { wails } from "./wails-runtime/index.js";

// Wails.Net 前端运行时：从本地 wails-runtime 目录导入（@wails-net/runtime 构建产物）。
// 调用绑定方法：wails.call(method, args)
// 触发命令：wails.call('window.minimize', []) —— 命令名即 `<命名空间>.<动作>`，
//          窗口命令由后端 WindowPlugin 提供（本模板 Program.cs 已注册）
// 监听事件：wails.events.on(name, callback)
//
// 注意：运行时自 M3 起只保留核心 API（call / bindings / events / query / invoke / cancel），
// 窗口、剪贴板等命名空间已拆分为 @wails-net/plugin-* 包，不再挂载到 wails 对象上。

const nameInput = document.getElementById('nameInput');
const greetBtn = document.getElementById('greetBtn');
const greetResult = document.getElementById('greetResult');

const counterValue = document.getElementById('counterValue');
const incBtn = document.getElementById('incBtn');
const decBtn = document.getElementById('decBtn');

const minBtn = document.getElementById('minBtn');
const maxBtn = document.getElementById('maxBtn');
const closeBtn = document.getElementById('closeBtn');

// 问候
greetBtn.addEventListener('click', async () => {
    const name = nameInput.value || 'World';
    try {
        const result = await wails.call('GreetingService.Greet', [name]);
        greetResult.textContent = result;
    } catch (e) {
        greetResult.textContent = `错误：${e.message || e}`;
    }
});

// 计数器
incBtn.addEventListener('click', async () => {
    const value = await wails.call('GreetingService.Increment', []);
    counterValue.textContent = String(value);
});

decBtn.addEventListener('click', async () => {
    const value = await wails.call('GreetingService.Decrement', []);
    counterValue.textContent = String(value);
});

// 窗口操作：统一走命令调用，参数为「选项对象数组」（如 [{ title: '新标题' }]）
const windowResult = document.getElementById('windowResult');

const runCommand = async (name, args = []) => {
    try {
        await wails.call(name, args);
        windowResult.textContent = `已执行 ${name}`;
    } catch (e) {
        windowResult.textContent = `命令 ${name} 调用失败：${e.message || e}`;
        console.error(`命令 ${name} 调用失败：`, e);
    }
};

minBtn.addEventListener('click', () => runCommand('window.minimize'));

maxBtn.addEventListener('click', async () => {
    // 没有 toggleMaximise 命令，需要先查询状态再切换
    const maximised = await wails.call('window.isMaximised', []).catch(() => false);
    await runCommand(maximised ? 'window.unmaximize' : 'window.maximize');
});

closeBtn.addEventListener('click', () => runCommand('window.close'));

// 初始化时同步计数器值
(async () => {
    const value = await wails.call('GreetingService.GetCount', []);
    counterValue.textContent = String(value);
})();
