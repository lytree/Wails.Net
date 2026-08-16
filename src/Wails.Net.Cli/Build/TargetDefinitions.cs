namespace Wails.Net.Cli.Build;

/// <summary>
/// 目标 RID 定义，供 build/publish/pack 命令的 <c>--all-targets</c> 使用。
/// 对应 benchmark-audit-and-unified-sdk-plan.md 任务 SDK-4。
/// <para>
/// 各 RID 经 Wails.Net.Sdk 的 RID→平台映射（SDK-1）自动切换平台引用：
/// win-* → windows、linux-* → linux、android-* → android、osx-* → macos。
/// </para>
/// </summary>
public static class TargetDefinitions
{
    /// <summary>
    /// <c>--all-targets</c> 默认的跨平台目标 RID 集合。
    /// </summary>
    private static readonly string[] DefaultTargets = { "win-x64", "linux-x64", "android-arm64" };

    /// <summary>
    /// 获取 <c>--all-targets</c> 需构建的所有目标 RID。
    /// macOS 相关 RID（osx-*）仅当构建机为 macOS 时加入（macOS 只能在 macOS 主机上构建/打包）。
    /// </summary>
    /// <returns>目标 RID 数组。</returns>
    public static string[] GetAllTargets()
    {
        var targets = new List<string>(DefaultTargets);
        if (OperatingSystem.IsMacOS())
        {
            targets.Add("osx-arm64");
            targets.Add("osx-x64");
        }

        return targets.ToArray();
    }
}