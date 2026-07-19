/**
 * Wails.Net Demo - Notifications 前端脚本
 * 演示通过 NotificationService 绑定方法发送立即/延迟通知，并维护历史记录。
 */

document.addEventListener('DOMContentLoaded', () => {
    initSendNow();
    initSchedule();
    initHistoryControls();
    refreshHistory();
});

// 立即发送
function initSendNow() {
    document.getElementById('sendNowBtn')?.addEventListener('click', async () => {
        const title = document.getElementById('titleInput').value;
        const body = document.getElementById('bodyInput').value;
        if (!title || !body) {
            showResult('标题和内容不能为空');
            return;
        }
        try {
            await wails.call('NotificationService.SendNotification', [title, body]);
            showResult(`已立即发送通知: ${title}`);
            await refreshHistory();
        } catch (err) {
            showResult(`发送失败: ${err}`);
        }
    });
}

// 延迟发送
function initSchedule() {
    document.getElementById('scheduleBtn')?.addEventListener('click', async () => {
        const title = document.getElementById('titleInput').value;
        const body = document.getElementById('bodyInput').value;
        const delay = parseInt(document.getElementById('delayInput').value, 10);
        if (!title || !body) {
            showResult('标题和内容不能为空');
            return;
        }
        if (!delay || delay < 1) {
            showResult('延迟秒数必须为正整数');
            return;
        }
        try {
            showResult(`已调度通知，将在 ${delay} 秒后发送...`);
            // 后端 Task.Delay 后发送，前端 await 等待完成
            await wails.call('NotificationService.ScheduleNotification', [title, body, delay]);
            showResult(`延迟通知已发送: ${title}`);
            await refreshHistory();
        } catch (err) {
            showResult(`调度失败: ${err}`);
        }
    });
}

// 历史控制
function initHistoryControls() {
    document.getElementById('refreshBtn')?.addEventListener('click', refreshHistory);

    document.getElementById('clearBtn')?.addEventListener('click', async () => {
        try {
            await wails.call('NotificationService.ClearHistory', []);
            await refreshHistory();
            showResult('历史已清空');
        } catch (err) {
            showResult(`清空失败: ${err}`);
        }
    });
}

// 刷新历史表格
async function refreshHistory() {
    try {
        const history = await wails.call('NotificationService.GetHistory', []);
        const body = document.getElementById('historyBody');
        if (!body) return;
        if (!history || history.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="empty">（暂无记录）</td></tr>';
            return;
        }
        body.innerHTML = history.map((item, idx) =>
            `<tr>
                <td>${idx + 1}</td>
                <td>${escapeHtml(item.title)}</td>
                <td>${escapeHtml(item.body)}</td>
                <td>${new Date(item.sentAt).toLocaleString()}</td>
            </tr>`
        ).join('');
    } catch (err) {
        console.error('刷新历史失败:', err);
    }
}

function showResult(text) {
    document.getElementById('sendResult').textContent = text;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}
