using System.Net;
using System.Text;
using System.Text.Json;
using Wails.Net.AssetServer;
using Wails.Net.Demo.Services.Services;

namespace Wails.Net.Demo.Services.Handlers;

/// <summary>
/// 用户 API HTTP 处理器，挂载到 /api/users 路由。
/// 支持 GET（列出全部）与 POST（创建新用户）。
/// </summary>
internal sealed class UserApiHandler : IHttpServiceHandler
{
    /// <summary>用户服务引用。</summary>
    private readonly UserService _userService;

    /// <summary>
    /// 构造处理器实例。
    /// </summary>
    /// <param name="userService">用户服务。</param>
    public UserApiHandler(UserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// 处理 HTTP 请求：GET 列出用户，POST 创建用户。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod;
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (method == "GET" && path == "/api/users")
        {
            var users = _userService.GetAll();
            await WriteJsonAsync(context.Response, users, cancellationToken);
            return;
        }

        if (method == "POST" && path == "/api/users")
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var payload = JsonSerializer.Deserialize<CreateUserPayload>(body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.Email))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context.Response, new { error = "name 与 email 必填" }, cancellationToken);
                return;
            }
            var user = _userService.Create(payload.Name, payload.Email);
            context.Response.StatusCode = 201;
            await WriteJsonAsync(context.Response, user, cancellationToken);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context.Response, new { error = "Not Found" }, cancellationToken);
    }

    /// <summary>
    /// 写入 JSON 响应并关闭输出流。
    /// </summary>
    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    /// <summary>
    /// 创建用户请求载荷。
    /// </summary>
    private sealed class CreateUserPayload
    {
        /// <summary>用户名。</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>邮箱。</summary>
        public string Email { get; set; } = string.Empty;
    }
}

/// <summary>
/// 产品 API HTTP 处理器，挂载到 /api/products 路由。
/// 支持 GET（列出全部）与 POST（创建新产品）。
/// </summary>
internal sealed class ProductApiHandler : IHttpServiceHandler
{
    /// <summary>产品服务引用。</summary>
    private readonly ProductService _productService;

    /// <summary>
    /// 构造处理器实例。
    /// </summary>
    /// <param name="productService">产品服务。</param>
    public ProductApiHandler(ProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// 处理 HTTP 请求。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod;
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (method == "GET" && path == "/api/products")
        {
            var products = _productService.GetAll();
            await WriteJsonAsync(context.Response, products, cancellationToken);
            return;
        }

        if (method == "POST" && path == "/api/products")
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var payload = JsonSerializer.Deserialize<CreateProductPayload>(body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context.Response, new { error = "name 必填" }, cancellationToken);
                return;
            }
            var product = _productService.Create(payload.Name, payload.Price);
            context.Response.StatusCode = 201;
            await WriteJsonAsync(context.Response, product, cancellationToken);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context.Response, new { error = "Not Found" }, cancellationToken);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private sealed class CreateProductPayload
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}

/// <summary>
/// 订单 API HTTP 处理器，挂载到 /api/orders 路由。
/// 支持 GET（列出全部）与 POST（创建新订单）。
/// </summary>
internal sealed class OrderApiHandler : IHttpServiceHandler
{
    /// <summary>订单服务引用。</summary>
    private readonly OrderService _orderService;

    /// <summary>
    /// 构造处理器实例。
    /// </summary>
    /// <param name="orderService">订单服务。</param>
    public OrderApiHandler(OrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// 处理 HTTP 请求。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod;
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (method == "GET" && path == "/api/orders")
        {
            var orders = _orderService.GetAll();
            await WriteJsonAsync(context.Response, orders, cancellationToken);
            return;
        }

        if (method == "POST" && path == "/api/orders")
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var payload = JsonSerializer.Deserialize<CreateOrderPayload>(body);
            if (payload is null || payload.UserId <= 0 || payload.ProductId <= 0)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context.Response, new { error = "userId 与 productId 必填且为正数" }, cancellationToken);
                return;
            }
            var order = _orderService.Create(payload.UserId, payload.ProductId, payload.Quantity);
            context.Response.StatusCode = 201;
            await WriteJsonAsync(context.Response, order, cancellationToken);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context.Response, new { error = "Not Found" }, cancellationToken);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private sealed class CreateOrderPayload
    {
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }
}
