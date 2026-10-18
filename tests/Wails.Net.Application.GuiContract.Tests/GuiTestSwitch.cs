using TUnit.Core;

namespace Wails.Net.Application.GuiContract.Tests;

/// <summary>
/// GUI 相关测试的运行时开关。
/// <para>
/// 项目默认禁用需要真实 GUI 环境（原生窗口、对话框、消息循环）的单元测试，
/// 以避免在无桌面会话的环境（CI 容器、远程会话）中执行原生窗口操作。
/// 设置环境变量 <c>WAILS_RUN_GUI_TESTS=1</c>（或 <c>true</c>）后启用这些测试。
/// </para>
/// </summary>
public static class GuiTestSwitch
{
    /// <summary>
    /// 启用 GUI 相关测试的环境变量名。
    /// </summary>
    public const string EnvironmentVariableName = "WAILS_RUN_GUI_TESTS";

    /// <summary>
    /// 是否启用 GUI 相关测试。默认返回 <c>false</c>（禁用）。
    /// </summary>
    public static bool Enabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            return !string.IsNullOrEmpty(value)
                && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>
/// 标记测试需要真实 GUI 环境（原生窗口 / 对话框 / 消息循环）。
/// <para>
/// 默认跳过：仅当环境变量 <see cref="GuiTestSwitch.EnvironmentVariableName"/>
/// 设置为 <c>1</c> 或 <c>true</c> 时才执行。可标注在测试类或单个测试方法上。
/// </para>
/// </summary>
public sealed class RequiresGuiEnvironmentAttribute : SkipAttribute
{
    /// <summary>
    /// 初始化跳过原因说明。
    /// </summary>
    public RequiresGuiEnvironmentAttribute()
        : base($"GUI 相关测试默认禁用；设置 {GuiTestSwitch.EnvironmentVariableName}=1 后启用。")
    {
    }

    /// <inheritdoc />
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(!GuiTestSwitch.Enabled);
    }
}
