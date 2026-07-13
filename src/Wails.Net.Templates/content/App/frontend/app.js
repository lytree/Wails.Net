// Wails.Net 前端运行时由后端自动注入到 window.wails。
// 调用绑定方法：wails.Call<T>(method, args)
// 触发命令：wails.Command.<commandName>(options)
// 监听事件：wails.Event.On(name, callback)

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
        const result = await wails.Call<string>('GreetingService.Greet', [name]);
        greetResult.textContent = result;
    } catch (e) {
        greetResult.textContent = `错误：${e.message || e}`;
    }
});

// 计数器
incBtn.addEventListener('click', async () => {
    const value = await wails.Call<number>('GreetingService.Increment', []);
    counterValue.textContent = String(value);
});

decBtn.addEventListener('click', async () => {
    const value = await wails.Call<number>('GreetingService.Decrement', []);
    counterValue.textContent = String(value);
});

// 窗口操作
minBtn.addEventListener('click', () => wails.window.minimize());
maxBtn.addEventListener('click', () => wails.window.toggleMaximise());
closeBtn.addEventListener('click', () => wails.window.close());

// 初始化时同步计数器值
(async () => {
    const value = await wails.Call<number>('GreetingService.GetCount', []);
    counterValue.textContent = String(value);
})();
