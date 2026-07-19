using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Services.Services;

/// <summary>
/// 用户数据记录。
/// </summary>
/// <param name="Id">用户 ID。</param>
/// <param name="Name">用户名。</param>
/// <param name="Email">邮箱地址。</param>
public sealed record User(int Id, string Name, string Email);

/// <summary>
/// 用户服务，提供 [Binding] 方法供前端调用，
/// 同时供 UserApiHandler 在 HTTP 路径下复用同一份数据。
/// </summary>
public sealed class UserService
{
    /// <summary>
    /// 用户列表（线程安全）。
    /// </summary>
    private readonly List<User> _users = new()
    {
        new(1, "Alice", "alice@example.com"),
        new(2, "Bob", "bob@example.com"),
        new(3, "Charlie", "charlie@example.com"),
    };

    /// <summary>
    /// ID 生成器（线程安全）。
    /// </summary>
    private int _nextId = 4;

    /// <summary>
    /// 同步锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取所有用户。
    /// </summary>
    /// <returns>用户列表副本。</returns>
    [Binding]
    public List<User> GetAll()
    {
        lock (_lock)
        {
            return new List<User>(_users);
        }
    }

    /// <summary>
    /// 按 ID 查询用户。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <returns>用户实例；不存在时返回 null。</returns>
    [Binding]
    public User? GetById(int id)
    {
        lock (_lock)
        {
            return _users.FirstOrDefault(u => u.Id == id);
        }
    }

    /// <summary>
    /// 创建新用户。
    /// </summary>
    /// <param name="name">用户名。</param>
    /// <param name="email">邮箱。</param>
    /// <returns>新创建的用户实例。</returns>
    [Binding]
    public User Create(string name, string email)
    {
        lock (_lock)
        {
            var user = new User(Interlocked.Increment(ref _nextId) - 1, name, email);
            _users.Add(user);
            return user;
        }
    }
}

/// <summary>
/// 产品数据记录。
/// </summary>
/// <param name="Id">产品 ID。</param>
/// <param name="Name">产品名称。</param>
/// <param name="Price">价格。</param>
public sealed record Product(int Id, string Name, decimal Price);

/// <summary>
/// 产品服务，提供 [Binding] 方法供前端调用，
/// 同时供 ProductApiHandler 在 HTTP 路径下复用。
/// </summary>
public sealed class ProductService
{
    /// <summary>
    /// 产品列表（线程安全）。
    /// </summary>
    private readonly List<Product> _products = new()
    {
        new(1, "Wails.Net T-Shirt", 99.00m),
        new(2, ".NET Sticker Pack", 15.50m),
        new(3, "USB-C Cable", 12.90m),
    };

    /// <summary>
    /// ID 生成器。
    /// </summary>
    private int _nextId = 4;

    /// <summary>
    /// 同步锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取所有产品。
    /// </summary>
    [Binding]
    public List<Product> GetAll()
    {
        lock (_lock)
        {
            return new List<Product>(_products);
        }
    }

    /// <summary>
    /// 创建新产品。
    /// </summary>
    /// <param name="name">产品名。</param>
    /// <param name="price">价格。</param>
    [Binding]
    public Product Create(string name, decimal price)
    {
        lock (_lock)
        {
            var product = new Product(Interlocked.Increment(ref _nextId) - 1, name, price);
            _products.Add(product);
            return product;
        }
    }
}

/// <summary>
/// 订单数据记录。
/// </summary>
/// <param name="Id">订单 ID。</param>
/// <param name="UserId">下单用户 ID。</param>
/// <param name="ProductId">所购产品 ID。</param>
/// <param name="Quantity">数量。</param>
public sealed record Order(int Id, int UserId, int ProductId, int Quantity);

/// <summary>
/// 订单服务，提供 [Binding] 方法供前端调用，
/// 同时供 OrderApiHandler 在 HTTP 路径下复用。
/// </summary>
public sealed class OrderService
{
    /// <summary>
    /// 订单列表。
    /// </summary>
    private readonly List<Order> _orders = new()
    {
        new(1001, 1, 2, 3),
        new(1002, 2, 1, 1),
    };

    /// <summary>
    /// ID 生成器。
    /// </summary>
    private int _nextId = 1003;

    /// <summary>
    /// 同步锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取所有订单。
    /// </summary>
    [Binding]
    public List<Order> GetAll()
    {
        lock (_lock)
        {
            return new List<Order>(_orders);
        }
    }

    /// <summary>
    /// 创建新订单。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="productId">产品 ID。</param>
    /// <param name="quantity">数量。</param>
    [Binding]
    public Order Create(int userId, int productId, int quantity)
    {
        lock (_lock)
        {
            var order = new Order(Interlocked.Increment(ref _nextId) - 1, userId, productId, quantity);
            _orders.Add(order);
            return order;
        }
    }
}
