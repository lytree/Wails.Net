using System.Text;
using System.Xml.Linq;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Scaffolding;

/// <summary>
/// 插件脚手架器，生成「前后端一体双包」插件骨架。
/// 对应 docs/development/plugin-platform-split.md 的插件拆分模型：
/// 后端 <c>src/Wails.Net.Plugins.{Name}/</c>（NuGet 包）+ 前端
/// <c>packages/wails-net-plugin-{name}/</c>（npm 包）+ 测试
/// <c>tests/Wails.Net.Plugins.{Name}.Tests/</c>（TUnit）。
/// <para>
/// 目录命名与 <see cref="PluginBuilder.DiscoverPlugins"/> 的扫描规则一致，
/// 生成后即可被 <c>wails plugin build</c> / <c>wails plugin publish</c> 识别。
/// </para>
/// </summary>
public sealed class PluginScaffolder
{
    /// <summary>
    /// 支持的插件平台类型。
    /// </summary>
    public static readonly string[] SupportedPlatforms = ["desktop", "mobile", "platform-special"];

    /// <summary>
    /// 获取所有支持的平台类型。
    /// </summary>
    /// <returns>平台类型数组。</returns>
    public static IReadOnlyList<string> GetSupportedPlatforms() => SupportedPlatforms;

    /// <summary>
    /// 判断平台类型是否受支持。
    /// </summary>
    /// <param name="platform">平台类型名称。</param>
    /// <returns>是否受支持。</returns>
    public static bool IsValidPlatform(string platform) =>
        Array.Exists(SupportedPlatforms, p => string.Equals(p, platform, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 校验插件名合法性（仅允许字母与数字，首字符不能为数字）。
    /// </summary>
    /// <param name="name">插件名。</param>
    /// <returns>是否合法。</returns>
    public static bool IsValidPluginName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (char.IsDigit(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 在 Wails.Net 仓库中生成插件双包脚手架。
    /// </summary>
    /// <param name="name">插件名（PascalCase 或 kebab-case，如 Updater / file-system）。</param>
    /// <param name="commandPrefix">命令前缀（默认取 kebab-case 插件名）。</param>
    /// <param name="platform">平台类型：desktop / mobile / platform-special。</param>
    /// <param name="repoRoot">仓库根目录（含 Directory.Build.props 与 src/、packages/）。</param>
    /// <param name="force">已存在时是否覆盖。</param>
    /// <returns>脚手架结果。</returns>
    public Task<ScaffoldResult> ScaffoldAsync(
        string name,
        string commandPrefix,
        string platform,
        string repoRoot,
        bool force = false)
    {
        var result = new ScaffoldResult();
        try
        {
            var kebab = PluginBuilder.ToKebabCase(name);
            // pascal 必须由 kebab 推导：ToPascalCase 直接作用于 PascalCase 输入
            // 会把内部大写转小写（OsInfo → Osinfo），先经 kebab 规范化再还原（os-info → OsInfo）
            var pascal = PluginBuilder.ToPascalCase(kebab);
            var normalizedPlatform = platform.ToLowerInvariant();

            var backendDir = Path.Combine(repoRoot, "src", $"{PluginBuilder.BackendPrefix}{pascal}");
            var frontendDir = Path.Combine(repoRoot, "packages", $"{PluginBuilder.FrontendDirPrefix}{kebab}");
            var testsDir = Path.Combine(repoRoot, "tests", $"{PluginBuilder.BackendPrefix}{pascal}.Tests");

            foreach (var dir in new[] { backendDir, frontendDir, testsDir })
            {
                if (Directory.Exists(dir) && !force)
                {
                    result.Success = false;
                    result.ErrorMessage = $"目录已存在：{dir}（如需覆盖请使用 --force）";
                    return Task.FromResult(result);
                }
            }

            Directory.CreateDirectory(backendDir);
            Directory.CreateDirectory(Path.Combine(frontendDir, "src"));
            Directory.CreateDirectory(testsDir);

            var createdFiles = new List<string>();
            var version = ReadWailsNetVersion(repoRoot);

            // ---- 后端 NuGet 包 ----
            createdFiles.Add(WriteFile(backendDir, $"{PluginBuilder.BackendPrefix}{pascal}.csproj",
                GenerateBackendCsproj(pascal, normalizedPlatform), repoRoot));
            createdFiles.Add(WriteFile(backendDir, $"{pascal}Plugin.cs",
                GeneratePluginClass(pascal, kebab, commandPrefix, normalizedPlatform), repoRoot));

            if (normalizedPlatform == "platform-special")
            {
                createdFiles.Add(WriteFile(backendDir, $"{pascal}Extensions.cs",
                    GenerateExtensionsClass(pascal), repoRoot));
            }

            // ---- 前端 npm 包（薄壳） ----
            createdFiles.Add(WriteFile(frontendDir, "package.json",
                GeneratePackageJson(kebab, version), repoRoot));
            createdFiles.Add(WriteFile(frontendDir, "tsconfig.json",
                GenerateTsConfig(), repoRoot));
            createdFiles.Add(WriteFile(Path.Combine(frontendDir, "src"), "index.ts",
                GenerateFrontendIndex(kebab, commandPrefix, normalizedPlatform), repoRoot));

            // ---- 测试项目（TUnit） ----
            createdFiles.Add(WriteFile(testsDir, $"{PluginBuilder.BackendPrefix}{pascal}.Tests.csproj",
                GenerateTestsCsproj(pascal), repoRoot));
            createdFiles.Add(WriteFile(testsDir, $"{pascal}PluginTests.cs",
                GenerateTestsClass(pascal, commandPrefix), repoRoot));

            result.Success = true;
            result.CreatedFiles = createdFiles;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 写入文件并返回相对基准目录（仓库根）的路径。
    /// </summary>
    /// <param name="dir">目标目录。</param>
    /// <param name="fileName">文件名。</param>
    /// <param name="content">文件内容。</param>
    /// <param name="baseDir">相对路径基准目录（仓库根）。</param>
    /// <returns>文件相对路径。</returns>
    private static string WriteFile(string dir, string fileName, string content, string baseDir)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return Path.GetRelativePath(baseDir, path);
    }

    /// <summary>
    /// 从 Directory.Build.props 读取 WailsNetVersion（前端 package.json 版本与后端 NuGet 版本保持一致）。
    /// </summary>
    /// <param name="repoRoot">仓库根目录。</param>
    /// <returns>版本号；读取失败时返回 0.1.0-alpha.1。</returns>
    private static string ReadWailsNetVersion(string repoRoot)
    {
        const string fallback = "0.1.0-alpha.1";
        var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
        if (!File.Exists(propsPath))
        {
            return fallback;
        }

        try
        {
            var doc = XDocument.Load(propsPath);
            var version = doc.Descendants("WailsNetVersion").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(version) ? fallback : version.Trim();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// 生成后端 .csproj。桌面与平台特有插件为 net10.0；
    /// 移动端插件为 net10.0 + net10.0-android36.0 双目标（桌面 TFM 供单元测试，Android TFM 为正式目标）。
    /// </summary>
    /// <param name="pascal">PascalCase 插件名。</param>
    /// <param name="platform">平台类型。</param>
    /// <returns>csproj 内容。</returns>
    private static string GenerateBackendCsproj(string pascal, string platform)
    {
        if (platform == "mobile")
        {
            return $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <PackageId>Wails.Net.Plugins.{{pascal}}</PackageId>
                    <Description>Wails.Net {{pascal}} 移动端插件（仅 Android，前后端一体双包）</Description>
                    <!-- 双 TFM：net10.0 仅供单元测试编译（命令降级 no-op）；net10.0-android36.0 为正式目标 -->
                    <TargetFrameworks>net10.0;net10.0-android36.0</TargetFrameworks>
                  </PropertyGroup>

                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-android36.0'">
                    <ProjectReference Include="..\Wails.Net.Application.Android\Wails.Net.Application.Android.csproj" />
                  </ItemGroup>

                  <ItemGroup>
                    <ProjectReference Include="..\Wails.Net.Application\Wails.Net.Application.csproj" />
                  </ItemGroup>

                </Project>
                """;
        }

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <PackageId>Wails.Net.Plugins.{{pascal}}</PackageId>
                <Description>Wails.Net {{pascal}} 插件（前后端一体双包）</Description>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\Wails.Net.Application\Wails.Net.Application.csproj" />
              </ItemGroup>

            </Project>
            """;
    }

    /// <summary>
    /// 生成插件主类。
    /// </summary>
    /// <param name="pascal">PascalCase 插件名。</param>
    /// <param name="kebab">kebab-case 插件名。</param>
    /// <param name="commandPrefix">命令前缀。</param>
    /// <param name="platform">平台类型。</param>
    /// <returns>插件类内容。</returns>
    private static string GeneratePluginClass(string pascal, string kebab, string commandPrefix, string platform)
    {
        var platformNote = platform switch
        {
            "mobile" => "仅 Android（net10.0-android36.0）可用，其他平台调用返回 PlatformNotSupportedException。",
            "platform-special" => "平台特有插件：通过 Add" + pascal + "&lt;TImpl&gt;() 注入平台实现，未注册实现的平台调用返回 PlatformNotSupportedException。",
            _ => "桌面通用插件：Windows / Linux / macOS。",
        };

        return $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Wails.Net.Application.Commands;
            using Wails.Net.Application.Plugins;

            namespace Wails.Net.Plugins.{{pascal}};

            /// <summary>
            /// {{pascal}} 插件：{{platformNote}}
            /// 对应 docs/development/plugin-packaging.md 的前后端一体双包模型。
            /// </summary>
            public class {{pascal}}Plugin : IPlugin
            {
                /// <summary>插件名称（命令命名空间前缀）。</summary>
                public string Name => "{{commandPrefix}}";

                /// <summary>注册插件 DI 服务（Host 构建前调用）。</summary>
                /// <param name="services">DI 服务集合。</param>
                public void ConfigureServices(IServiceCollection services)
                {
                    // 示例：services.AddSingleton<{{pascal}}Service>();
                }

                /// <summary>注册插件命令（Build 阶段调用）。</summary>
                /// <param name="context">插件配置上下文。</param>
                public void Configure(IPluginContext context)
                {
                    // 示例：无参命令 {{commandPrefix}}.ping
                    context.Commands.MapCommand("{{commandPrefix}}.ping", (Func<ICommandContext, string>)(ctx => "pong"));
                }
            }
            """;
    }

    /// <summary>
    /// 生成平台特有插件的 DI 扩展类（注入平台实现，模式见 KeychainExtensions）。
    /// </summary>
    /// <param name="pascal">PascalCase 插件名。</param>
    /// <returns>扩展类内容。</returns>
    private static string GenerateExtensionsClass(string pascal) => $$"""
        using Microsoft.Extensions.DependencyInjection;
        using Wails.Net.Application.Plugins;

        namespace Wails.Net.Plugins.{{pascal}};

        /// <summary>
        /// {{pascal}} 插件 DI 扩展方法：注入平台实现。
        /// 平台程序集（如 Wails.Net.Application.Windows）在注册平台时调用
        /// <c>services.Add{{pascal}}&lt;WindowsImpl&gt;()</c>；未注册实现的平台
        /// 调用插件命令时将抛出 <see cref="PlatformNotSupportedException"/>。
        /// </summary>
        public static class {{pascal}}Extensions
        {
            /// <summary>
            /// 注册平台实现与插件实例。
            /// </summary>
            /// <typeparam name="TImpl">平台实现类型（无参构造函数，实现 IPlatform{{pascal}}）。</typeparam>
            /// <param name="services">DI 服务集合。</param>
            /// <returns>DI 服务集合，以支持链式调用。</returns>
            public static IServiceCollection Add{{pascal}}<TImpl>(this IServiceCollection services)
                where TImpl : class, IPlatform{{pascal}}, new()
            {
                services.AddSingleton<IPlatform{{pascal}}, TImpl>();
                services.AddSingleton<{{pascal}}Plugin>();
                return services;
            }
        }
        """;

    /// <summary>
    /// 生成前端 package.json。
    /// </summary>
    /// <param name="kebab">kebab-case 插件名。</param>
    /// <param name="version">版本号（与 WailsNetVersion 一致）。</param>
    /// <returns>package.json 内容。</returns>
    private static string GeneratePackageJson(string kebab, string version) => $$"""
        {
          "name": "@wails-net/plugin-{{kebab}}",
          "version": "{{version}}",
          "type": "module",
          "main": "./dist/index.js",
          "types": "./dist/index.d.ts",
          "exports": {
            ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" },
            "./package.json": "./package.json"
          },
          "files": ["dist"],
          "scripts": {
            "build": "tsc -p tsconfig.json",
            "typecheck": "tsc --noEmit"
          },
          "dependencies": {
            "@wails-net/runtime": "workspace:*"
          },
          "devDependencies": {
            "typescript": "^5.6.0"
          }
        }
        """;

    /// <summary>
    /// 生成前端 tsconfig.json。
    /// </summary>
    /// <returns>tsconfig 内容。</returns>
    private static string GenerateTsConfig() => """
        {
          "compilerOptions": {
            "target": "ES2022",
            "module": "ESNext",
            "moduleResolution": "bundler",
            "declaration": true,
            "outDir": "dist",
            "rootDir": "src",
            "strict": true,
            "skipLibCheck": true,
            "esModuleInterop": true,
            "forceConsistentCasingInFileNames": true
          },
          "include": ["src"]
        }
        """;

    /// <summary>
    /// 生成前端强类型封装（defineCommand 薄壳）。
    /// </summary>
    /// <param name="kebab">kebab-case 插件名。</param>
    /// <param name="commandPrefix">命令前缀。</param>
    /// <param name="platform">平台类型。</param>
    /// <returns>index.ts 内容。</returns>
    private static string GenerateFrontendIndex(string kebab, string commandPrefix, string platform)
    {
        var platformDoc = platform switch
        {
            "mobile" => " * @platform android  仅 Android 可用，其他平台后端抛 PlatformNotSupportedException。",
            "platform-special" => " * @platform windows,macos  平台特有插件，未注入实现的平台后端抛 PlatformNotSupportedException。",
            _ => " * @platform windows,linux,macos  桌面通用插件。",
        };

        return $$"""
            /**
             * @wails-net/plugin-{{kebab}} — {{commandPrefix}} 插件前端封装。
             * 命令前缀 `{{commandPrefix}}.*`，后端经 L2 抽象层 `defineCommand` 转发（强类型化）。
            {{platformDoc}}
             */
            import { defineCommand } from "@wails-net/runtime";

            /** 示例：{{commandPrefix}}.ping（无参数，返回字符串）。 */
            export const ping = defineCommand<[], string>("{{commandPrefix}}.ping", "none");
            """;
    }

    /// <summary>
    /// 生成测试项目 csproj（TUnit）。
    /// </summary>
    /// <param name="pascal">PascalCase 插件名。</param>
    /// <returns>csproj 内容。</returns>
    private static string GenerateTestsCsproj(string pascal) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <IsPackable>false</IsPackable>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\src\Wails.Net.Plugins.{{pascal}}\Wails.Net.Plugins.{{pascal}}.csproj" />
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="TUnit" />
          </ItemGroup>

        </Project>
        """;

    /// <summary>
    /// 生成插件测试类（TUnit）。
    /// </summary>
    /// <param name="pascal">PascalCase 插件名。</param>
    /// <param name="commandPrefix">命令前缀。</param>
    /// <returns>测试类内容。</returns>
    private static string GenerateTestsClass(string pascal, string commandPrefix) => $$"""
        using Wails.Net.Plugins.{{pascal}};

        namespace Wails.Net.Plugins.{{pascal}}.Tests;

        public class {{pascal}}PluginTests
        {
            [Test]
            public async Task Plugin_Name_MatchesCommandPrefix()
            {
                var plugin = new {{pascal}}Plugin();
                await Assert.That(plugin.Name).IsEqualTo("{{commandPrefix}}");
            }
        }
        """;
}
