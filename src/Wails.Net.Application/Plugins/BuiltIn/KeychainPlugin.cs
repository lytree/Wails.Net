using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 系统钥匙串插件，提供原生安全存储能力。
/// 对应 Wails v3 Go 版本 <c>internal/keychain</c> 包，桥接 Windows Credential Manager / Linux libsecret / macOS Keychain。
/// </summary>
/// <remarks>
/// 与 <see cref="StrongholdPlugin"/> 的区别：
/// <list type="bullet">
/// <item><see cref="StrongholdPlugin"/>：基于 AES-GCM 文件加密，跨平台但需要主密码。</item>
/// <item><see cref="KeychainPlugin"/>：调用系统原生钥匙串，凭据由 OS 加密保护，无需用户输入主密码。</item>
/// </list>
/// </remarks>
public class KeychainPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "keychain";

    /// <summary>
    /// 平台钥匙串实例，由平台扩展方法注入；为 null 时表示当前平台无原生实现。
    /// </summary>
    public IPlatformKeychain? Keychain { get; set; }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件将自身注册为单例，
    /// 便于平台扩展方法通过 DI 注入 <see cref="IPlatformKeychain"/> 实例。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<KeychainPlugin>(this);
    }

    /// <summary>
    /// 配置插件，注册 <c>keychain.*</c> 命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 设置密码：keychain.setPassword(service, account, password)
        context.Commands.MapCommand("keychain.setPassword",
            (Func<string, string, string, bool>)((service, account, password) =>
            {
                if (Keychain is null) return false;
                return Keychain.SetPassword(service, account, password);
            }));

        // 读取密码：keychain.getPassword(service, account) -> string?
        context.Commands.MapCommand("keychain.getPassword",
            (Func<string, string, string?>)((service, account) =>
            {
                if (Keychain is null) return null;
                return Keychain.GetPassword(service, account);
            }));

        // 删除密码：keychain.deletePassword(service, account) -> bool
        context.Commands.MapCommand("keychain.deletePassword",
            (Func<string, string, bool>)((service, account) =>
            {
                if (Keychain is null) return false;
                return Keychain.DeletePassword(service, account);
            }));
    }
}
