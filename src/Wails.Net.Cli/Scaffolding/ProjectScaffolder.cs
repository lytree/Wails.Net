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
            <UseWails>true</UseWails>
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
                });

                app.UseWindows();
                app.Window.NewWebviewWindow(new WebviewWindowOptions
                {
                    Title = "{{projectName}}",
                    Width = 1024,
                    Height = 768,
                    Url = "http://localhost:5173",
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
          "frontend": {
            "dir": "frontend",
            "devServerUrl": "http://localhost:5173",
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
        var frameworkDep = template switch
        {
            "vue-ts" => """
            ,
                "vue": "^3.4.0"
            """,
            "react-ts" => """
            ,
                "react": "^18.3.0",
                "react-dom": "^18.3.0"
            """,
            "svelte-ts" => """
            ,
                "svelte": "^4.2.0"
            """,
            _ => string.Empty,
        };

        return $$"""
        {
          "name": "{{projectName}}-frontend",
          "version": "0.1.0",
          "private": true,
          "type": "module",
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "preview": "vite preview"
          },
          "devDependencies": {
            "typescript": "^5.4.0",
            "vite": "^5.2.0"{{frameworkDep}}
          }
        }
        """;
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

    private static string GenerateFrontendEntryContent(string template) => """
        // 入口文件：模板生成。请在此处导入框架并启动应用。
        console.log('Wails.Net 前端已启动');
        """;
}
