# Wails.Net Demo - Binding

演示 Wails.Net 绑定系统的各类方法签名，所有方法通过 `[Binding]` 特性标记由源代码生成器暴露给前端。

## 功能

- 同步方法绑定（`Greet`）
- 异步方法绑定（`GetCurrentTimeAsync`）
- 重载方法（`Add(int, int)` 与 `Add(double, double)`）
- 复杂对象返回（`GetUser` 返回 `UserInfo` record）
- 集合返回（`GetItems` 返回 `List<string>`）
- 异常处理（`ThrowError` 抛出异常，前端 catch）
- CancellationToken 异步（`LongTask`）

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.Binding/Wails.Net.Demo.Binding.csproj
```
