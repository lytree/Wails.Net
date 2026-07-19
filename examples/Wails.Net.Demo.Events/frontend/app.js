/**
 * Wails.Net Demo - Events 前端脚本
 * 演示 wails.events.on / emit 与后端 EventProcessor 的双向通信。
 */

document.addEventListener('DOMContentLoaded', () => {
    initReceivers();
    initSender();
    initSubscriptionControl();
    initFrontendEventSender();
    autoStartTimer();
});

// 当前订阅句柄
let tickUnsubscribe = null;
let notificationUnsubscribe = null;
let echoUnsubscribe = null;

// 订阅 demo:tick 与 demo:notification，显示统计与通知
function initReceivers() {
    notificationUnsubscribe = wails.events.on('demo:notification', (data) => {
        logEvent(`demo:notification: ${data?.message ?? JSON.stringify(data)}`);
        document.getElementById('lastNotification').textContent = data?.message ?? '-';
    });

    echoUnsubscribe = wails.events.on('demo:echo', (data) => {
        logEvent(`demo:echo: ${JSON.stringify(data)}`);
    });

    // 默认订阅 tick
    subscribeTick();
}

// 发送通知：调用绑定方法，后端会 emit demo:notification 事件
function initSender() {
    document.getElementById('sendBtn')?.addEventListener('click', async () => {
        const message = document.getElementById('messageInput').value;
        if (!message) return;
        try {
            await wails.call('EventService.SendNotification', [message]);
            logEvent(`调用 EventService.SendNotification("${message}")`);
        } catch (err) {
            logEvent(`发送失败: ${err}`);
        }
    });
}

// 订阅控制：订阅 / 取消订阅 / 一次性订阅
function initSubscriptionControl() {
    document.getElementById('subscribeBtn')?.addEventListener('click', () => {
        if (tickUnsubscribe) {
            logEvent('已订阅 demo:tick，无需重复订阅');
            return;
        }
        subscribeTick();
        logEvent('已订阅 demo:tick');
    });

    document.getElementById('unsubscribeBtn')?.addEventListener('click', () => {
        if (tickUnsubscribe) {
            tickUnsubscribe();
            tickUnsubscribe = null;
            document.getElementById('tickCount').textContent = '-';
            logEvent('已取消订阅 demo:tick');
        } else {
            logEvent('未订阅 demo:tick');
        }
    });

    document.getElementById('onceBtn')?.addEventListener('click', () => {
        // 一次性订阅：在回调内立即调用返回的取消订阅函数
        const off = wails.events.on('demo:once', (data) => {
            if (off) { off(); }
            logEvent(`demo:once 触发一次: ${JSON.stringify(data)}`);
        });
        logEvent('已一次性订阅 demo:once');
    });

    document.getElementById('emitOnceBtn')?.addEventListener('click', async () => {
        await wails.events.emit('demo:once', { time: new Date().toISOString() });
        logEvent('emit demo:once');
    });
}

// 前端 emit frontend:event，后端订阅后回发 demo:echo
function initFrontendEventSender() {
    document.getElementById('emitFrontendBtn')?.addEventListener('click', async () => {
        const payload = { from: 'frontend', ts: Date.now() };
        await wails.events.emit('frontend:event', payload);
        logEvent(`emit frontend:event: ${JSON.stringify(payload)}`);
    });
}

// 订阅 demo:tick，更新计数
function subscribeTick() {
    tickUnsubscribe = wails.events.on('demo:tick', (data) => {
        const count = data?.count ?? 0;
        document.getElementById('tickCount').textContent = count;
    });
}

// 自动启动后端定时器
async function autoStartTimer() {
    try {
        await wails.call('EventService.StartTimer', []);
        logEvent('已启动后端定时器（demo:tick 每 1s 一次）');
    } catch (err) {
        logEvent(`启动定时器失败: ${err}`);
    }
}

// 写入事件日志
function logEvent(text) {
    const log = document.getElementById('eventLog');
    if (!log) return;
    const line = document.createElement('div');
    line.className = 'log-line';
    line.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
    log.appendChild(line);
    // 保留最近 50 行
    while (log.children.length > 50) {
        log.removeChild(log.firstChild);
    }
    log.scrollTop = log.scrollHeight;
}
