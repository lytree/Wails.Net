using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件系统插件，提供文件读写命令。
/// </summary>
public class FileSystemPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "filesystem";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件系统相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 同步命令
        context.Commands.MapCommand("fs.read", (Func<string, string>)(path => File.ReadAllText(path)));
        context.Commands.MapCommand("fs.write", (Action<string, string>)((path, content) => File.WriteAllText(path, content)));
        context.Commands.MapCommand("fs.exists", (Func<string, bool>)(path => File.Exists(path)));
        context.Commands.MapCommand("fs.delete", (Action<string>)(path => File.Delete(path)));

        // 异步命令
        context.Commands.MapCommand("fs.readAsync", (Func<string, Task<string>>)(async path => await File.ReadAllTextAsync(path)));
        context.Commands.MapCommand("fs.writeAsync", (Func<string, string, Task>)(async (path, content) => await File.WriteAllTextAsync(path, content)));
    }
}
