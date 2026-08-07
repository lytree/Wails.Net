using System.Text;

namespace Wails.Net.Cli.Scaffolding;

/// <summary>
/// 项目脚手架结果。
/// </summary>
public sealed class ScaffoldResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>创建的文件相对路径列表。</summary>
    public List<string> CreatedFiles { get; set; } = new();
}

/// <summary>
/// 项目脚手架器，根据模板生成 Wails.Net 项目骨架。
/// 对应 Wails v3 Go 版本 internal/project/project.go。
/// <para>
/// 前端约定：vite + pnpm + <c>@wails-net/runtime</c>。
/// <list type="bullet">
/// <item>依赖安装 / 构建由 <c>wails3</c> CLI 自动探测包管理器（pnpm 优先，npm 回退），
/// 因此 <c>wails.json</c> 中的 <c>installCommand</c> / <c>buildCommand</c> 默认留空。</item>
/// <item>TypeScript 采用宽松配置（<c>strict: false</c>），降低模板上手门槛。</item>
/// </list>
/// </para>
/// </summary>
public sealed class ProjectScaffolder
{
    /// <summary>
    /// 脚手架生成的 <c>@wails-net/runtime</c> 依赖版本范围。
    /// </summary>
    private const string RuntimePackageVersion = "^0.1.0";

    /// <summary>
    /// 支持的前端模板名称。
    /// </summary>
    private static readonly string[] SupportedTemplates =
    [
        "vanilla-ts",
        "vue-ts",
        "react-ts",
        "svelte-ts",
    ];

    /// <summary>
    /// 获取所有支持的前端模板。
    /// </summary>
    /// <returns>模板名称数组。</returns>
    public static IReadOnlyList<string> GetSupportedTemplates() => SupportedTemplates;

    /// <summary>
    /// 判断模板名称是否受支持。
    /// </summary>
    /// <param name="name">模板名称。</param>
    /// <returns>是否受支持。</returns>
    public static bool IsValidTemplateName(string name) =>
        Array.Exists(SupportedTemplates, t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 在指定目录中生成项目脚手架。
    /// </summary>
    /// <param name="projectName">项目名称。</param>
    /// <param name="template">前端模板名称。</param>
    /// <param name="targetDirectory">目标根目录。</param>
    /// <returns>脚手架结果。</returns>
    public Task<ScaffoldResult> ScaffoldAsync(
        string projectName,
        string template,
        DirectoryInfo targetDirectory)
    {
        var result = new ScaffoldResult();
        try
        {
            var normalizedTemplate = NormalizeTemplate(template);
            var projectDir = Path.Combine(targetDirectory.FullName, projectName);
            Directory.CreateDirectory(projectDir);

            var createdFiles = new List<string>();

            // 解决方案文件
            createdFiles.Add(WriteFile(
                projectDir,
                $"{projectName}.slnx",
                GenerateSolutionContent(projectName)));

            // 主项目文件
            var srcDir = Path.Combine(projectDir, "src", projectName);
            Directory.CreateDirectory(srcDir);
            createdFiles.Add(WriteFile(
                srcDir,
                $"{projectName}.csproj",
                GenerateCsprojContent()));

            // Program.cs
            createdFiles.Add(WriteFile(
                srcDir,
                "Program.cs",
                GenerateProgramCsContent(projectName)));

            // 绑定服务示例
            createdFiles.Add(WriteFile(
                srcDir,
                "Bindings.cs",
                GenerateBindingsContent(projectName)));

            // wails.json 配置
            createdFiles.Add(WriteFile(
                projectDir,
                "wails.json",
                GenerateWailsJsonContent(projectName, template)));

            // .gitignore（忽略前端产物与依赖，保留 pnpm-lock.yaml）
            createdFiles.Add(WriteFile(
                projectDir,
                ".gitignore",
                GenerateGitIgnoreContent()));

            // README（快速上手：pnpm + wails3 CLI）
            createdFiles.Add(WriteFile(
                projectDir,
                "README.md",
                GenerateReadmeContent(projectName, normalizedTemplate)));

            // 前端目录
            var frontendDir = Path.Combine(projectDir, "frontend");
            Directory.CreateDirectory(frontendDir);
            createdFiles.Add(WriteFile(
                frontendDir,
                "package.json",
                GeneratePackageJsonContent(projectName, normalizedTemplate)));

            createdFiles.Add(WriteFile(
                frontendDir,
                "index.html",
                GenerateIndexHtmlContent(projectName, normalizedTemplate)));

            createdFiles.Add(WriteFile(
                frontendDir,
                "vite.config.ts",
                GenerateViteConfigContent(normalizedTemplate)));

            createdFiles.Add(WriteFile(
                frontendDir,
                "tsconfig.json",
                GenerateTsConfigContent(normalizedTemplate)));

            // 前端 src 目录
            var frontendSrcDir = Path.Combine(frontendDir, "src");
            Directory.CreateDirectory(frontendSrcDir);
            createdFiles.Add(WriteFile(
                frontendSrcDir,
                GetEntryFileName(normalizedTemplate),
                GenerateFrontendEntryContent(normalizedTemplate)));

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
    /// 将模板名称归一化为小写形式（模板匹配大小写不敏感）。
    /// </summary>
    /// <param name="template">用户输入的模板名称。</param>
    /// <returns>小写模板名称。</returns>
    private static string NormalizeTemplate(string template) => template.ToLowerInvariant();

    /// <summary>
    /// 获取模板对应的前端入口文件名。React 使用 JSX，需 <c>.tsx</c> 扩展名。
    /// </summary>
    /// <param name="template">归一化后的模板名称。</param>
    /// <returns>入口文件名。</returns>
    private static string GetEntryFileName(string template) =>
        template == "react-ts" ? "main.tsx" : "main.ts";

    private static string WriteFile(string dir, string fileName, string content)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return Path.GetRelativePath(Directory.GetParent(dir)?.FullName ?? dir, path);
    }

    private static string GenerateSolutionContent(string projectName) => $$"""
        <Solution>
          <Folder Name="/src/">
            <Project Path="src/{{projectName}}/{{projectName}}.csproj" />
          </Folder>
        </Solution>
        """;

    private static string GenerateCsprojContent() => """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Wails.Net.Application" Version="*" />
            <PackageReference Include="Wails.Net.Application.Windows" Version="*" />
          </ItemGroup>

        </Project>
        """;

    private static string GenerateProgramCsContent(string projectName) => $$"""
        using Wails.Net.Application;
        using Wails.Net.Application.Options;

        namespace {{projectName}};

        public static class Program
        {
            public static void Main(string[] args)
            {
                var app = new Application(new ApplicationOptions
                {
                    Name = "{{projectName}}",
                    Services = { new GreetingService() },
                });

                app.UseWindows();
                app.CreateWebviewWindow(new WebviewWindowOptions
                {
                    Title = "{{projectName}}",
                    Width = 1024,
                    Height = 768,
                    URL = "http://localhost:5173",
                });

                app.Run();
            }
        }
        """;

    private static string GenerateBindingsContent(string projectName) => $$"""
        namespace {{projectName}};

        public class GreetingService
        {
            public string Hello(string name) => $"Hello, {name}! Welcome to {{projectName}}.";
        }
        """;

    private static string GenerateWailsJsonContent(string projectName, string template) => $$"""
        {
          "name": "{{projectName}}",
          "version": "0.1.0",
          "template": "{{template}}",
          "assetDir": "frontend/dist",
          "outputFilename": "{{projectName}}",
          "wailsJsDir": "frontend/src/wails",
          "beforeBuildCommand": "",
          "afterBuildCommand": "",
          "beforeDevCommand": "",
          "afterDevCommand": "",
          "frontend": {
            "dir": "frontend",
            "devServerUrl": "http://localhost:5173",
            "installCommand": "",
            "buildCommand": "",
            "outputDir": "frontend/dist"
          },
          "bindings": {
            "outputDir": "frontend/src/wails"
          }
        }
        """;

    private static string GenerateGitIgnoreContent() => """
        # .NET
        bin/
        obj/
        *.user

        # 前端
        node_modules/
        frontend/dist/
        frontend/.vite/
        *.tsbuildinfo

        # 构建产物
        build/
        dist/

        # 说明：pnpm-lock.yaml 应提交，保证依赖可复现
        """;

    private static string GenerateReadmeContent(string projectName, string template) => $$"""
        # {{projectName}}

        基于 [Wails.Net](https://github.com/wailsapp/wails) 的桌面应用（模板：`{{template}}`）。

        - 后端：.NET 10 + `Wails.Net.Application`
        - 前端：Vite + TypeScript + [`@wails-net/runtime`](https://www.npmjs.com/package/@wails-net/runtime)
        - 包管理：pnpm（CLI 会自动探测，缺失时回退 npm）

        ## 快速开始

        ```bash
        # 安装前端依赖（也可由 wails3 自动执行）
        pnpm -C frontend install

        # 开发模式：并行启动 vite dev server 与 dotnet watch
        wails3 dev

        # 构建：先构建前端，再编译 .NET
        wails3 build

        # 打包为可分发应用
        wails3 pack
        ```

        ## 前端调用后端

        后端 `GreetingService.Hello` 已在 `src/{{projectName}}/Bindings.cs` 中注册，
        前端通过 `@wails-net/runtime` 调用：

        ```ts
        import { wails } from '@wails-net/runtime';

        const message = await wails.call('GreetingService.Hello', ['Wails.Net']);
        ```

        ## 目录结构

        ```
        {{projectName}}/
        ├─ src/{{projectName}}/    .NET 后端（Program.cs、Bindings.cs）
        ├─ frontend/            Vite 前端（index.html、src/、vite.config.ts）
        └─ wails.json           Wails.Net 项目配置
        ```
        """;

    private static string GeneratePackageJsonContent(string projectName, string template)
    {
        // 运行时依赖：Wails.Net 前端 SDK + 框架运行时
        var dependencies = new List<(string Name, string Version)>
        {
            ("@wails-net/runtime", RuntimePackageVersion),
        };

        // 开发依赖：TypeScript + Vite + 框架插件
        var devDependencies = new List<(string Name, string Version)>
        {
            ("typescript", "^5.6.0"),
            ("vite", "^6.0.0"),
        };

        switch (template)
        {
            case "vue-ts":
                dependencies.Add(("vue", "^3.5.0"));
                devDependencies.Add(("@vitejs/plugin-vue", "^5.2.0"));
                devDependencies.Add(("vue-tsc", "^2.1.0"));
                break;
            case "react-ts":
                dependencies.Add(("react", "^18.3.1"));
                dependencies.Add(("react-dom", "^18.3.1"));
                devDependencies.Add(("@vitejs/plugin-react", "^4.3.4"));
                devDependencies.Add(("@types/react", "^18.3.12"));
                devDependencies.Add(("@types/react-dom", "^18.3.1"));
                break;
            case "svelte-ts":
                dependencies.Add(("svelte", "^4.2.19"));
                devDependencies.Add(("@sveltejs/vite-plugin-svelte", "^3.1.2"));
                devDependencies.Add(("svelte-check", "^4.0.0"));
                break;
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{ToPackageName(projectName)}\",");
        sb.AppendLine("  \"version\": \"0.1.0\",");
        sb.AppendLine("  \"private\": true,");
        sb.AppendLine("  \"type\": \"module\",");
        sb.AppendLine("  \"scripts\": {");
        sb.AppendLine("    \"dev\": \"vite\",");
        sb.AppendLine("    \"build\": \"vite build\",");
        sb.AppendLine("    \"preview\": \"vite preview\",");
        sb.AppendLine("    \"typecheck\": \"tsc --noEmit\"");
        sb.AppendLine("  },");
        AppendDependencyBlock(sb, "dependencies", dependencies, trailingComma: true);
        AppendDependencyBlock(sb, "devDependencies", devDependencies, trailingComma: false);
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 追加一个 JSON 依赖块，自动处理逗号分隔。
    /// </summary>
    /// <param name="sb">目标缓冲区。</param>
    /// <param name="blockName">块名称（dependencies / devDependencies）。</param>
    /// <param name="entries">依赖条目。</param>
    /// <param name="trailingComma">块结束后是否追加逗号。</param>
    private static void AppendDependencyBlock(
        StringBuilder sb,
        string blockName,
        IReadOnlyList<(string Name, string Version)> entries,
        bool trailingComma)
    {
        sb.AppendLine($"  \"{blockName}\": {{");
        for (var i = 0; i < entries.Count; i++)
        {
            var (name, version) = entries[i];
            var comma = i == entries.Count - 1 ? string.Empty : ",";
            sb.AppendLine($"    \"{name}\": \"{version}\"{comma}");
        }

        sb.AppendLine(trailingComma ? "  }," : "  }");
    }

    /// <summary>
    /// 将项目名转换为合法的 npm 包名（小写、非法字符替换为连字符）。
    /// </summary>
    /// <param name="projectName">项目名称。</param>
    /// <returns>npm 包名。</returns>
    private static string ToPackageName(string projectName)
    {
        var sb = new StringBuilder(projectName.Length + 9);
        foreach (var ch in projectName)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append('-');
            }
        }

        var name = sb.ToString().Trim('-', '.', '_');
        return string.IsNullOrEmpty(name) ? "app-frontend" : $"{name}-frontend";
    }

    private static string GenerateIndexHtmlContent(string projectName, string template) => $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>{{projectName}}</title>
        </head>
        <body>
          <div id="app"></div>
          <script type="module" src="/src/{{GetEntryFileName(template)}}"></script>
        </body>
        </html>
        """;

    /// <summary>
    /// 生成 vite.config.ts。产物固定输出到 <c>dist</c>（与 wails.json 的 assetDir 对应），
    /// dev server 固定 5173 端口（与后端 WebviewWindowOptions.URL 对应）。
    /// </summary>
    /// <param name="template">归一化后的模板名称。</param>
    /// <returns>vite 配置文件内容。</returns>
    private static string GenerateViteConfigContent(string template)
    {
        var (import, plugin) = template switch
        {
            "vue-ts" => ("import vue from '@vitejs/plugin-vue';", "vue()"),
            "react-ts" => ("import react from '@vitejs/plugin-react';", "react()"),
            "svelte-ts" => ("import { svelte } from '@sveltejs/vite-plugin-svelte';", "svelte()"),
            _ => (string.Empty, string.Empty),
        };

        var importLine = string.IsNullOrEmpty(import) ? string.Empty : import + "\n";
        var pluginLine = string.IsNullOrEmpty(plugin) ? "  plugins: []," : $"  plugins: [{plugin}],";

        return $$"""
            import { defineConfig } from 'vite';
            {{importLine}}
            // Wails.Net 前端构建配置
            // - build.outDir 必须与 wails.json 的 frontend.outputDir 一致
            // - server.port 必须与后端 WebviewWindowOptions.URL 一致
            export default defineConfig({
            {{pluginLine}}
              build: {
                outDir: 'dist',
                emptyOutDir: true,
                target: 'es2022',
              },
              server: {
                port: 5173,
                strictPort: true,
              },
            });

            """;
    }

    /// <summary>
    /// 生成宽松（非 strict）的 tsconfig.json，降低模板上手门槛。
    /// </summary>
    /// <param name="template">归一化后的模板名称。</param>
    /// <returns>tsconfig 内容。</returns>
    private static string GenerateTsConfigContent(string template)
    {
        var jsxLine = template == "react-ts" ? "\n    \"jsx\": \"react-jsx\"," : string.Empty;

        return $$"""
            {
              "compilerOptions": {
                "target": "ES2022",
                "module": "ESNext",
                "moduleResolution": "bundler",
                "lib": ["ES2022", "DOM", "DOM.Iterable"],{{jsxLine}}
                "strict": false,
                "noImplicitAny": false,
                "skipLibCheck": true,
                "allowJs": true,
                "esModuleInterop": true,
                "allowSyntheticDefaultImports": true,
                "resolveJsonModule": true,
                "isolatedModules": true,
                "verbatimModuleSyntax": false,
                "noEmit": true
              },
              "include": ["src", "vite.config.ts"]
            }
            """;
    }

    private static string GenerateFrontendEntryContent(string template) => template switch
    {
        "vue-ts" => """
        import { createApp } from 'vue';
        import { wails } from '@wails-net/runtime';

        const app = createApp({
          template: '<div><h1>Wails.Net + Vue</h1><p>{{ message }}</p></div>',
          data() {
            return { message: '正在调用后端…' };
          },
          async mounted() {
            try {
              this.message = await wails.call('GreetingService.Hello', ['Vue']);
            } catch (err) {
              this.message = `调用失败：${err}`;
            }
          },
        });

        app.mount('#app');
        """,
        "react-ts" => """
        import React, { useEffect, useState } from 'react';
        import { createRoot } from 'react-dom/client';
        import { wails } from '@wails-net/runtime';

        function App() {
          const [message, setMessage] = useState('正在调用后端…');

          useEffect(() => {
            wails
              .call('GreetingService.Hello', ['React'])
              .then((res) => setMessage(String(res)))
              .catch((err) => setMessage(`调用失败：${err}`));
          }, []);

          return (
            <div>
              <h1>Wails.Net + React</h1>
              <p>{message}</p>
            </div>
          );
        }

        const root = createRoot(document.getElementById('app')!);
        root.render(<App />);
        """,
        "svelte-ts" => """
        // Wails.Net + Svelte 应用入口
        // 提示：需要 .svelte 组件时，新建 src/App.svelte 后在此处 import 并挂载
        import { wails } from '@wails-net/runtime';

        const app = document.getElementById('app');
        if (app) {
          app.innerHTML = '<h1>Wails.Net + Svelte</h1><p id="msg">正在调用后端…</p>';
          const msg = document.getElementById('msg');
          wails
            .call('GreetingService.Hello', ['Svelte'])
            .then((res) => { if (msg) msg.textContent = String(res); })
            .catch((err) => { if (msg) msg.textContent = `调用失败：${err}`; });
        }
        """,
        _ => """
        // Wails.Net 前端入口（vanilla + TypeScript）
        import { wails } from '@wails-net/runtime';

        const app = document.getElementById('app');
        if (app) {
          app.innerHTML = '<h1>Wails.Net</h1><p id="msg">正在调用后端…</p>';
          const msg = document.getElementById('msg');
          wails
            .call('GreetingService.Hello', ['Wails.Net'])
            .then((res) => { if (msg) msg.textContent = String(res); })
            .catch((err) => { if (msg) msg.textContent = `调用失败：${err}`; });
        }
        """,
    };
}
