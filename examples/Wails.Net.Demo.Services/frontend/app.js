/**
 * Wails.Net Demo - Services 前端脚本
 * 演示通过 [Binding] 方法调用后端 UserService/ProductService/OrderService，
 * 同时后端通过 IHttpServiceHandler 挂载相同的 /api/* HTTP 路由供外部调用。
 */

document.addEventListener('DOMContentLoaded', () => {
    initUsers();
    initProducts();
    initOrders();
});

// ============================================================
// 用户
// ============================================================
function initUsers() {
    document.getElementById('listUsersBtn')?.addEventListener('click', async () => {
        try {
            const users = await wails.call('UserService.GetAll', []);
            document.getElementById('userResult').textContent = JSON.stringify(users, null, 2);
        } catch (err) {
            showErr('userResult', err);
        }
    });

    document.getElementById('createUserBtn')?.addEventListener('click', async () => {
        const name = document.getElementById('userName').value.trim();
        const email = document.getElementById('userEmail').value.trim();
        if (!name || !email) {
            document.getElementById('userResult').textContent = '请填写用户名和邮箱';
            return;
        }
        try {
            const user = await wails.call('UserService.Create', [name, email]);
            document.getElementById('userResult').textContent = `已创建: ${JSON.stringify(user)}`;
        } catch (err) {
            showErr('userResult', err);
        }
    });
}

// ============================================================
// 产品
// ============================================================
function initProducts() {
    document.getElementById('listProductsBtn')?.addEventListener('click', async () => {
        try {
            const products = await wails.call('ProductService.GetAll', []);
            document.getElementById('productResult').textContent = JSON.stringify(products, null, 2);
        } catch (err) {
            showErr('productResult', err);
        }
    });

    document.getElementById('createProductBtn')?.addEventListener('click', async () => {
        const name = document.getElementById('productName').value.trim();
        const price = parseFloat(document.getElementById('productPrice').value);
        if (!name || isNaN(price)) {
            document.getElementById('productResult').textContent = '请填写产品名和价格';
            return;
        }
        try {
            const product = await wails.call('ProductService.Create', [name, price]);
            document.getElementById('productResult').textContent = `已创建: ${JSON.stringify(product)}`;
        } catch (err) {
            showErr('productResult', err);
        }
    });
}

// ============================================================
// 订单
// ============================================================
function initOrders() {
    document.getElementById('listOrdersBtn')?.addEventListener('click', async () => {
        try {
            const orders = await wails.call('OrderService.GetAll', []);
            document.getElementById('orderResult').textContent = JSON.stringify(orders, null, 2);
        } catch (err) {
            showErr('orderResult', err);
        }
    });

    document.getElementById('createOrderBtn')?.addEventListener('click', async () => {
        const userId = parseInt(document.getElementById('orderUserId').value, 10);
        const productId = parseInt(document.getElementById('orderProductId').value, 10);
        const qty = parseInt(document.getElementById('orderQty').value, 10) || 1;
        if (!userId || !productId) {
            document.getElementById('orderResult').textContent = '请填写有效的用户 ID 和产品 ID';
            return;
        }
        try {
            const order = await wails.call('OrderService.Create', [userId, productId, qty]);
            document.getElementById('orderResult').textContent = `已创建: ${JSON.stringify(order)}`;
        } catch (err) {
            showErr('orderResult', err);
        }
    });
}

function showErr(id, err) {
    document.getElementById(id).textContent = `失败: ${err}`;
}
