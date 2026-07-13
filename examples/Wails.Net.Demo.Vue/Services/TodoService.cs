using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Vue.Services;

/// <summary>
/// 待办事项数据模型。
/// </summary>
public class TodoItem
{
    /// <summary>唯一标识</summary>
    public int Id { get; set; }

    /// <summary>标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>是否已完成</summary>
    public bool Completed { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 待办事项服务，演示 CRUD 操作的绑定。
/// </summary>
public class TodoService
{
    private readonly List<TodoItem> _todos = new();
    private int _nextId = 1;

    /// <summary>
    /// 获取所有待办事项。
    /// </summary>
    /// <returns>待办事项列表。</returns>
    [Binding]
    public List<TodoItem> GetAll()
    {
        return _todos.ToList();
    }

    /// <summary>
    /// 添加新的待办事项。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <returns>新创建的待办事项。</returns>
    [Binding]
    public TodoItem Add(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("标题不能为空", nameof(title));
        }

        var item = new TodoItem
        {
            Id = _nextId++,
            Title = title.Trim(),
            Completed = false,
            CreatedAt = DateTime.Now,
        };

        _todos.Add(item);
        return item;
    }

    /// <summary>
    /// 切换待办事项的完成状态。
    /// </summary>
    /// <param name="id">待办事项 ID。</param>
    /// <returns>更新后的待办事项；若未找到返回 null。</returns>
    [Binding]
    public TodoItem? Toggle(int id)
    {
        var item = _todos.FirstOrDefault(t => t.Id == id);
        if (item is null)
        {
            return null;
        }

        item.Completed = !item.Completed;
        return item;
    }

    /// <summary>
    /// 删除指定待办事项。
    /// </summary>
    /// <param name="id">待办事项 ID。</param>
    /// <returns>是否删除成功。</returns>
    [Binding]
    public bool Delete(int id)
    {
        var item = _todos.FirstOrDefault(t => t.Id == id);
        if (item is null)
        {
            return false;
        }

        return _todos.Remove(item);
    }

    /// <summary>
    /// 清除所有已完成的待办事项。
    /// </summary>
    /// <returns>被清除的数量。</returns>
    [Binding]
    public int ClearCompleted()
    {
        var count = _todos.Count(t => t.Completed);
        _todos.RemoveAll(t => t.Completed);
        return count;
    }

    /// <summary>
    /// 获取统计信息。
    /// </summary>
    /// <returns>包含总数、已完成数和未完成数的字典。</returns>
    [Binding]
    public Dictionary<string, int> GetStats()
    {
        return new Dictionary<string, int>
        {
            ["total"] = _todos.Count,
            ["completed"] = _todos.Count(t => t.Completed),
            ["pending"] = _todos.Count(t => !t.Completed),
        };
    }
}
