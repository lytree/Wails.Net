using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// <see cref="PermissionOptions"/> 启动期校验器（OPT-CONFIG-002）。
/// <para>
/// 校验权限配置的合法性与最小权限姿态：
/// <list type="bullet">
/// <item><c>Permissions</c> 中不得包含空字符串。</item>
/// <item><c>CapabilitiesDirectory</c> 非空时不得包含非法路径字符。</item>
/// <item><c>Scopes</c> 的键（权限标识）不得为空。</item>
/// </list>
/// </para>
/// </summary>
public sealed class PermissionOptionsValidator : IValidateOptions<PermissionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PermissionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        for (var i = 0; i < options.Permissions.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options.Permissions[i]))
            {
                failures.Add($"Wails:Permissions:Permissions:{i} 不得为空字符串。");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.CapabilitiesDirectory) &&
            options.CapabilitiesDirectory!.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            failures.Add($"Wails:Permissions:CapabilitiesDirectory 包含非法路径字符：{options.CapabilitiesDirectory}。");
        }

        foreach (var key in options.Scopes.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                failures.Add("Wails:Permissions:Scopes 的键（权限标识）不得为空。");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
