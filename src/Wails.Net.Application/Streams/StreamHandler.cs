namespace Wails.Net.Application.Streams;

/// <summary>
/// 流连接处理器：每个连接在自己的任务上调用一次。连接在 handler 运行期间存活；
/// 从 handler 返回即关闭连接，因此 handler 应阻塞在 <see cref="StreamConn.ReceiveAsync"/>（或
/// <see cref="StreamConn.Closed"/>）上，直到它想让连接保持打开多久。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c> 中的 <c>StreamHandler</c>。
/// </summary>
/// <param name="connection">连接。</param>
/// <returns>handler 完成的任务。</returns>
public delegate Task StreamHandler(StreamConn connection);
