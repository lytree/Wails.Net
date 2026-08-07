import { wails } from "./wails-runtime/index.js";

// Wails.Net 前端运行时：从本地 wails-runtime 目录导入（@wails-net/runtime 构建产物）。
// 调用绑定方法：wails.call(method, args)
// 触发命令：wails.window.minimize() 等命名空间 API，或 wails.call('ns.command', args)
// 监听事件：wails.events.on(name, callback)

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

// 窗口操作
minBtn.addEventListener('click', () => wails.window.minimize());
maxBtn.addEventListener('click', () => wails.window.toggleMaximise());
closeBtn.addEventListener('click', () => wails.window.close());

// 初始化时同步计数器值
(async () => {
    const value = await wails.call('GreetingService.GetCount', []);
    counterValue.textContent = String(value);
})();
