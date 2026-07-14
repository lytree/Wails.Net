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
/// </summary>
public sealed class ProjectScaffolder
{
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
                GenerateCsprojContent(projectName)));

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

            // 前端目录
            var frontendDir = Path.Combine(projectDir, "frontend");
            Directory.CreateDirectory(frontendDir);
            createdFiles.Add(WriteFile(
                frontendDir,
                "package.json",
                GeneratePackageJsonContent(projectName, template)));

            createdFiles.Add(WriteFile(
                frontendDir,
                "index.html",
                GenerateIndexHtmlContent(projectName)));

            // 前端 src 目录
            var frontendSrcDir = Path.Combine(frontendDir, "src");
            Directory.CreateDirectory(frontendSrcDir);
            createdFiles.Add(WriteFile(
                frontendSrcDir,
                "main.ts",
                GenerateFrontendEntryContent(template)));

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

    private static string GenerateCsprojContent(string projectName) => $$"""
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
            "installCommand": "npm install",
            "buildCommand": "npm run build",
            "outputDir": "frontend/dist"
          },
          "bindings": {
            "outputDir": "frontend/src/wails"
          }
        }
        """;

    private static string GeneratePackageJsonContent(string projectName, string template)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{projectName}-frontend\",");
        sb.AppendLine("  \"version\": \"0.1.0\",");
        sb.AppendLine("  \"private\": true,");
        sb.AppendLine("  \"type\": \"module\",");
        sb.AppendLine("  \"scripts\": {");
        sb.AppendLine("    \"dev\": \"vite\",");
        sb.AppendLine("    \"build\": \"vite build\",");
        sb.AppendLine("    \"preview\": \"vite preview\"");
        sb.AppendLine("  },");

        // devDependencies：typescript、vite、框架插件
        sb.AppendLine("  \"devDependencies\": {");
        sb.AppendLine("    \"typescript\": \"^5.4.0\",");
        sb.AppendLine("    \"vite\": \"^5.2.0\",");
        sb.AppendLine("    \"@wails/runtime\": \"*\"");

        switch (template)
        {
            case "vue-ts":
                sb.AppendLine("    ,\"@vitejs/plugin-vue\": \"^5.0.0\"");
                break;
            case "react-ts":
                sb.AppendLine("    ,\"@vitejs/plugin-react\": \"^4.3.0\"");
                sb.AppendLine("    ,\"@types/react\": \"^18.3.0\"");
                sb.AppendLine("    ,\"@types/react-dom\": \"^18.3.0\"");
                break;
            case "svelte-ts":
                sb.AppendLine("    ,\"@sveltejs/vite-plugin-svelte\": \"^3.1.0\"");
                sb.AppendLine("    ,\"svelte-check\": \"^3.8.0\"");
                break;
        }

        sb.AppendLine("  }");

        // dependencies：框架运行时
        var hasFrameworkDep = template is "vue-ts" or "react-ts" or "svelte-ts";
        if (hasFrameworkDep)
        {
            sb.AppendLine("  ,\"dependencies\": {");
            switch (template)
            {
                case "vue-ts":
                    sb.AppendLine("    \"vue\": \"^3.4.0\"");
                    break;
                case "react-ts":
                    sb.AppendLine("    \"react\": \"^18.3.0\"");
                    sb.AppendLine("    ,\"react-dom\": \"^18.3.0\"");
                    break;
                case "svelte-ts":
                    sb.AppendLine("    \"svelte\": \"^4.2.0\"");
                    break;
            }
            sb.AppendLine("  }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateIndexHtmlContent(string projectName) => $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>{{projectName}}</title>
        </head>
        <body>
          <div id="app"></div>
          <script type="module" src="/src/main.ts"></script>
        </body>
        </html>
        """;

    private static string GenerateFrontendEntryContent(string template) => template switch
    {
        "vue-ts" => """
        import { createApp } from 'vue';

        const app = createApp({
          template: '<div>{{ message }}</div>',
          data() {
            return { message: 'Wails.Net + Vue 应用已启动' };
          },
        });

        app.mount('#app');
        """,
        "react-ts" => """
        import { createRoot } from 'react-dom/client';
        import React from 'react';

        function App() {
          return <div>Wails.Net + React 应用已启动</div>;
        }

        const root = createRoot(document.getElementById('app')!);
        root.render(<App />);
        """,
        "svelte-ts" => """
        // Wails.Net + Svelte 应用入口
        // 请在此处导入并挂载 Svelte 组件
        const app = document.getElementById('app');
        if (app) {
          app.textContent = 'Wails.Net + Svelte 应用已启动';
        }
        """,
        _ => """
        // Wails.Net 前端入口
        const app = document.getElementById('app');
        if (app) {
          app.textContent = 'Wails.Net 应用已启动';
        }
        """,
    };
}
