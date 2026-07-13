# CLI 工具实现

## 1. 概述

`Wails.Net.Cli` 是 Wails.Net 项目（阶段 7）的命令行工具链，对应 Wails v3 Go 版本的 `cmd/wails3` 入口。它基于 [`System.CommandLine` 2.0.9](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Wails.Net.Cli.csproj)（项目约束中**禁止**使用 `McMaster.Extensions.CommandLineUtils`），为开发者提供从项目脚手架、绑定生成、构建发布到打包分发的一站式能力。

CLI 通过 [`PackAsTool=true`](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Wails.Net.Cli.csproj) 打包为 .NET 全局工具，工具命令名为 `wails.net`。安装后即可在任意目录调用：

```bash
dotnet tool install --global Wails.Net.Cli
wails.net --help
```

CLI 项目引用了三个核心程序集：`Wails.Net.Generator`（绑定生成器）、`Wails.Net.Application`、`Wails.Net.Events`，并暴露 `InternalsVisibleTo` 给 `Wails.Net.Cli.Tests`，便于单元测试访问内部类型。

## 2. 命令结构

### 2.1 入口与命令树

入口点位于 [Program.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Program.cs)，对应 Go 版 `cmd/wails3/main.go`。其 `Main` 方法仅做三件事：构建根命令、解析参数、异步调用：

```csharp
public static async Task<int> Main(string[] args)
{
    var rootCommand = BuildRootCommand();
    var parseResult = rootCommand.Parse(args);
    return await parseResult.InvokeAsync();
}
```

`BuildRootCommand` 将所有子命令注册到 `RootCommand` 上，形成扁平的子命令树：

- `generate`、`doctor`、`new`、`build`、`dev`、`publish`、`pack`
- `plugin`、`version`、`clean`、`info`、`icon`、`signer`、`platform`、`self-update`

每个命令通过 `Subcommands.Add(XxxCommand.Create())` 注册，遵循"工厂方法 + 命令类"模式。

### 2.2 CliCommandBase 基类

[CliCommandBase](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/CliCommandBase.cs) 是所有命令的抽象基类，仅提供受控的控制台输出辅助方法，不持有状态：

| 方法 | 输出流 | 颜色 | 用途 |
|------|--------|------|------|
| `Info(message)` | stdout | 默认 | 普通信息行 |
| `Success(message)` | stdout | 绿色 | 成功提示 |
| `Warn(message)` | stdout | 黄色 | 警告（带 `[警告]` 前缀） |
| `Error(message)` | stderr | 默认 | 错误（带 `[错误]` 前缀） |

颜色通过 `Console.ForegroundColor` 临时切换，并在 `finally` 中恢复，保证终端状态一致性。

### 2.3 命令模式约定

每个具体命令遵循统一约定：

1. `sealed class XxxCommand : CliCommandBase`，`internal` 可见性。
2. 暴露 `public static Command Create()` 工厂方法，在其中定义 `Argument<T>` / `Option<T>` 并挂载到 `Command` 实例。
3. 通过 `AsyncAction.Create(...)` 绑定异步处理委托，委托内部 `new XxxCommand()` 后调用私有 `ExecuteAsync(...)`。
4. 退出码语义统一：`0` 成功、`1` 参数/前置条件错误、`2` 执行失败、`3` 后续阶段失败（如 `pack` 中打包阶段）。

## 3. 命令清单

下表汇总了 [Program.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Program.cs) 中注册的全部命令（本文重点详述前 8 个核心命令）：

| 命令 | 对应类 | 功能 | 对应 Go 版 |
|------|--------|------|-----------|
| `new` | `NewCommand` | 项目脚手架 | `cmd_new.go` |
| `build` | `BuildCommand` | 编译项目 | `build.go` |
| `dev` | `DevCommand` | 开发模式（热更新） | `dev.go` |
| `generate` | `GenerateCommand` | 生成 TypeScript 绑定 | `generate.go` |
| `pack` | `PackCommand` | 打包分发 | `package.go` |
| `publish` | `PublishCommand` | 发布可分发产物 | `build.go`（发布部分） |
| `doctor` | `DoctorCommand` | 环境诊断 | `internal/doctor/doctor.go` |
| `version` | `VersionCommand` | 显示版本信息 | Tauri `tauri version` |
| `plugin`/`clean`/`info`/`icon`/`signer`/`platform`/`self-update` | — | 辅助命令 | — |

## 4. new 命令 — 项目脚手架

[NewCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/NewCommand.cs) 负责创建新的 Wails.Net 项目，对应 Go 版 `cmd_new.go`。

**参数与选项**：

| 名称 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `name`（参数） | `string` | 必填 | 项目名称，同时作为目录名 |
| `--template` | `string` | `vanilla-ts` | 前端模板 |
| `--directory` | `DirectoryInfo?` | 当前目录 | 项目创建目录 |

**用法**：

```bash
wails.net new MyApp --template vue-ts
wails.net new MyApp --template react-ts --directory D:\Projects
```

**执行流程**（`ExecuteAsync`）：

1. 校验 `name` 非空，否则输出错误并返回 `1`。
2. 通过 `ProjectScaffolder.IsValidTemplateName(template)` 校验模板名；失败时列出 `GetSupportedTemplates()` 返回的可用模板。
3. 若目标目录不存在则创建之。
4. 调用 `ProjectScaffolder.ScaffoldAsync(name, template, directory)` 生成骨架。
5. 成功后打印生成的文件清单及"后续步骤"提示（`dotnet restore` → `wails.net generate` → `dotnet run`）。

### 4.1 ProjectScaffolder 模板系统

[ProjectScaffolder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Scaffolding/ProjectScaffolder.cs) 对应 Go 版 `internal/project/project.go`，内置 4 个前端模板：

```csharp
private static readonly string[] SupportedTemplates =
[
    "vanilla-ts", "vue-ts", "react-ts", "svelte-ts",
];
```

`ScaffoldAsync` 在目标目录下创建完整的项目结构：

```
{projectName}/
├── {projectName}.slnx              # .NET 解决方案（slnx 格式）
├── wails.json                       # Wails.Net 项目配置
└── src/{projectName}/
    ├── {projectName}.csproj         # net10.0-windows 主项目
    ├── Program.cs                    # 入口（Application + UseWindows）
    └── Bindings.cs                   # GreetingService 示例绑定

frontend/
├── package.json                     # 含 vite/typescript/@wails/runtime
├── index.html
└── src/main.ts                      # 模板专属入口
```

模板通过 C# 原始字符串字面量（`"""..."""`）和插值（`$$"""...{{projectName}}..."""`）生成，**不依赖外部模板文件**。`GeneratePackageJsonContent` 根据 `template` 分支写入不同的 devDependencies/dependencies：

- `vue-ts`：`vue` + `@vitejs/plugin-vue`
- `react-ts`：`react`/`react-dom` + `@vitejs/plugin-react` + `@types/*`
- `svelte-ts`：`svelte` + `@sveltejs/vite-plugin-svelte`

生成的 `.csproj` 默认引用 `Wails.Net.Application` 和 `Wails.Net.Application.Windows`（Version="*"），目标是 `net10.0-windows10.0.19041.0`。

## 5. build 命令 — 项目构建

[BuildCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/BuildCommand.cs) 编译 Wails.Net 项目，对应 Go 版 `build.go`。

**选项**：

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--project` | `FileInfo?` | 当前目录唯一 .csproj | 项目文件路径 |
| `--configuration` | `string` | `Release` | Debug/Release |
| `--runtime` | `string?` | 无 | RID（如 `win-x64`、`linux-x64`） |
| `--self-contained` | `bool` | `false` | 自包含发布 |

**用法**：

```bash
wails.net build
wails.net build --project src/MyApp.csproj -c Debug
wails.net build --runtime win-x64 --self-contained
```

`ResolveProjectPath` 实现：若用户未通过 `--project` 指定，则在当前目录扫描 `*.csproj`，**仅在恰好存在 1 个时**自动采用，多个时返回 `null` 并提示用户显式指定，避免歧义。

### 5.1 ProjectBuilder

[ProjectBuilder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Build/ProjectBuilder.cs) 封装 `dotnet build`/`dotnet publish` 子进程调用。`BuildAsync` 拼接参数列表（`build`、`project.FullName`、`-c`、`-r`、`--self-contained`），通过 `RunDotnetAsync` 启动 `dotnet` 进程并捕获 stdout/stderr。

关键设计：**输出目录解析不硬编码 TFM**。`ParseOutputDirectory` 从 `dotnet build` 输出中查找 ` -> ` 箭头后的路径（如 `MyApp -> /path/to/bin/Release/net10.0/`），无法解析时回退到 `bin/{configuration}`。

```csharp
var outputDir = ParseOutputDirectory(output)
    ?? Path.Combine(Path.GetDirectoryName(project.FullName)!, "bin", configuration);
```

`RunDotnetAsync` 异步读取 stdout/stderr 防止缓冲区死锁，并将两者合并返回 `(ExitCode, Output)`。退出码非 0 时填充 `BuildResult.BuildLog` 供上层打印诊断信息。

## 6. dev 命令 — 开发模式

[DevCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/DevCommand.cs) 启动开发服务器与热更新，对应 Go 版 `dev.go`。**不自行实现文件监视**，而是直接转发到 `dotnet watch`。

**选项**：

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--project` | `FileInfo?` | 自动探测 | 项目文件 |
| `--no-hot-reload` | `bool` | `false` | 禁用热更新，每次变更完整重启 |
| `--verbose` | `bool` | `false` | 详细日志 |

**用法**：

```bash
wails.net dev
wails.net dev --no-hot-reload --verbose
```

`ExecuteAsync` 构造 `dotnet watch --project <path>` 参数链，按需附加 `--no-hot-reload`/`--verbose`，通过 `ProcessStartInfo.ArgumentList`（避免命令注入）启动 `dotnet` 进程：

```csharp
var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    UseShellExecute = false,
    CreateNoWindow = false,
};
foreach (var a in args) psi.ArgumentList.Add(a);
using var proc = new Process { StartInfo = psi };
proc.Start();
await proc.WaitForExitAsync(cancellationToken);
```

命令传递 `CancellationToken`，支持 Ctrl+C 优雅退出。捕获 `OperationCanceledException` 后输出 `Warn("开发模式已停止")` 并返回 `0`，将 `dotnet watch` 的退出码透传给上层。

## 7. generate 命令 — 绑定生成

[GenerateCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/GenerateCommand.cs) 从 C# 程序集生成 TypeScript 绑定文件，对应 Go 版 `generate.go`，是前后端类型一致性的核心保证。

**选项**：

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--assembly` | `FileInfo?` | 必填 | 要分析的 .dll |
| `--output` | `DirectoryInfo` | `bindings` | TypeScript 输出目录 |
| `--events-assembly` | `FileInfo?` | 同 `--assembly` | 包含事件枚举的程序集 |

**用法**：

```bash
wails.net generate --assembly bin/Release/net10.0/MyApp.dll \
                   --output frontend/src/wails
```

**执行流程**：

1. 校验 `assembly.Exists`，否则返回 `1`。
2. 通过 `Assembly.LoadFrom(assembly.FullName)` 加载主程序集；若 `--events-assembly` 指定且存在则单独加载，否则复用主程序集。
3. 构造 `BindingGenerationOptions`，开启四类产物：
   ```csharp
   var options = new BindingGenerationOptions
   {
       OutputDirectory = outputDir.FullName,
       GenerateDefinitions = true,   // .d.ts 类型定义
       GenerateCaller = true,        // 调用器（caller）
       GenerateIdMap = true,         // FNV-1a 方法 ID 映射
       GenerateEvents = true,        // 事件枚举
   };
   ```
4. 通过 `BindingGenerationPipeline.GenerateToDisk(loaded, eventsLoaded, options)` 执行生成管线。
5. 成功后打印 `方法数 / 类数` 及所有生成文件名。

`BindingGenerationPipeline` 来自 `Wails.Net.Generator` 项目，CLI 仅作为宿主调用，不参与具体生成逻辑——遵循"前端无关、生成器独立"的分层原则。

## 8. pack 命令 — 平台打包

[PackCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/PackCommand.cs) 将项目打包为可分发应用，对应 Go 版 `package.go`，是**两阶段流水线**：先 `dotnet publish`，再调用 `Packager` 压缩/封装。

**选项**（共 8 个）：

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--project` | `FileInfo?` | 自动探测 | 项目文件 |
| `--configuration` | `string` | `Release` | 构建配置 |
| `--runtime` | `string?` | 无 | RID |
| `--self-contained` | `bool` | `false` | 自包含 |
| `--format` | `string` | Windows:`zip` / 其他:`targz` | 打包格式 |
| `--output` | `DirectoryInfo?` | `bin/packages` | 输出目录 |
| `--app-name` | `string` | `WailsApp` | 应用名称 |
| `--version` | `string` | `1.0.0` | 版本号 |

**用法**：

```bash
# Windows 默认 zip
wails.net pack --app-name MyApp --version 1.2.0

# 生成 NSIS 安装程序
wails.net pack --format nsis --app-name MyApp --version 1.0.0

# Linux AppImage
wails.net pack --format appimage --runtime linux-x64 --self-contained
```

`TryParseFormat` 接受 `zip`/`targz`/`tar.gz`/`nsis`/`appimage`，解析为 `PackageFormat` 枚举。

### 8.1 Packager 打包器

[Packager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Build/Packager.cs) 支持 4 种格式，输出文件名为 `{AppName}-{Version}{ext}`：

| 格式 | 扩展名 | 实现 | 平台限制 |
|------|--------|------|---------|
| `Zip` | `.zip` | `ZipFile.CreateFromDirectory` | 跨平台 |
| `TarGz` | `.tar.gz` | **手写 tar 头** + `GZipStream` | 跨平台 |
| `Nsis` | `.exe` | 生成 `.nsi` 脚本调用 `makensis` | 仅 Windows |
| `AppImage` | `.AppImage` | 构造 AppDir 调用 `appimagetool` | 仅 Linux |

**tar.gz 实现亮点**：`Packager` **不依赖第三方 tar 库**，自行实现 [POSIX ustar 格式](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Build/Packager.cs)。`CreateTarHeader` 按规范写入 512 字节头部：

- `name`(0-99)、`mode`(100-107, 八进制 0644=420)、`uid/gid`、`size`(124-135)、`mtime`(136-147, Unix 时间戳)
- `magic`(257-262) = `"ustar\0"`、`version`(263-264) = `"00"`
- 校验和字段（148-155）先填充空格，对所有字节求和后以八进制回填

文件内容按 512 字节块对齐，末尾写入两个空块作为 EOF。`IsSymbolFile` 过滤 `.pdb`/`.dbg`（除非 `IncludeSymbols=true`）。

**NSIS 实现**：`PackageNsisAsync` 在临时目录生成 `.nsi` 脚本（`GenerateNsisScript`），包含安装/卸载段、桌面/开始菜单快捷方式、注册表卸载条目，然后调用 `makensis`。`FindNsisInCommonLocations` 在 `ProgramFiles`/`ProgramFilesX86` 下兜底查找。

**AppImage 实现**：`PackageAppImageAsync` 构造标准 AppDir 结构（`usr/bin/` + `AppRun` + `.desktop` + 图标），通过 `chmod +x` 设置可执行权限，最后调用 `appimagetool`。`MinimalPngIcon` 内嵌 1x1 透明 PNG 字节数组作为占位图标。

`GenerateChecksum`（默认启用）使用 `SHA256.HashDataAsync` 计算包哈希，输出 `{package}.sha256` 文件，格式为 `<hash>  <filename>`。

## 9. publish 命令 — 发布

[PublishCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/PublishCommand.cs) 调用 `dotnet publish` 生成可分发产物，**不进行打包**（与 `pack` 区分）。

**选项**：`--project`、`--configuration`(默认 `Release`)、`--runtime`、`--self-contained`、`--output`(默认 `bin/Release/publish`)。

```bash
wails.net publish --runtime linux-x64 --self-contained -o ./dist
```

内部委托 `ProjectBuilder.PublishAsync(project, configuration, runtime, selfContained, output?.FullName)`，输出目录解析优先级：解析输出 → 用户指定 → 默认 `bin/{config}/publish`。

## 10. doctor 命令 — 环境诊断

[DoctorCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/DoctorCommand.cs) 对应 Go 版 `internal/doctor/doctor.go`，提供跨平台环境自检。

**无参数**，逐项检查并以 `[OK]`/`[WARN]`/`[FAIL]` 标记输出，退出码 `0`=全通过、`1`=有缺失。

**通用检查项**：

- `dotnet SDK`：通过 `LocateExecutable("dotnet")` 查找 PATH，再 `dotnet --version` 读取版本
- `Node.js`：`node --version`，缺失仅 Warn（前端构建将不可用）
- `Git`：`git --version`，缺失仅 Warn
- `操作系统`：通过 `RuntimeInformation.IsOSPlatform` 报告平台/架构/版本

**Windows 专属检查**：

- `WebView2 Runtime`：优先通过 `reg query` 读取注册表 `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A5E36C}` 的 `pv` 值，与 `WebView2MinVersion = "100.0.0.0"` 比较（`IsVersionAtLeast`）；注册表无值时回退检测 `Microsoft\Edge\Application\msedge.exe` 是否存在。
- `NSIS`：检查 `makensis`，缺失仅 Warn。

**Linux 专属检查**：

- `GTK4`：`pkg-config --modversion gtk4`
- `WebKitGTK-6.0`：`pkg-config --modversion webkitgtk-6.0`
- `Linux 共享库`：执行 `ldconfig -p` 并匹配 5 个核心库（`libgtk-4.so.1`、`libwebkitgtk-6.0.so`、`libglib-2.0.so.0`、`libgio-2.0.so.0`、`libgdk_pixbuf-2.0.so.0`），缺失即 Fail。
- `D-Bus`：检查 `dbus-run-session` 或 `dbus-send`。

`ReadExecutableVersion` 异步读取 stderr 防止死锁，并对 2 秒未退出的进程强制 `Kill()`，避免挂起诊断流程。

## 11. 版本管理 — version 命令

[VersionCommand](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Commands/VersionCommand.cs) 输出 CLI 版本及运行环境信息，对应 Tauri `tauri version`。

```bash
wails.net version
```

输出示例：

```
Wails.Net CLI
=============
版本:        v1.0.0
.NET 运行时: v10.0.0
操作系统:    Windows 10.0.19041.0
架构:        X64
机器名:      DESKTOP-XXX
```

### 11.1 版本号解析策略

`GetCliVersion()` 采用三级回退策略，**完全支持手动设置版本号**：

```csharp
internal static string GetCliVersion()
{
    var asm = typeof(VersionCommand).Assembly;

    // 1. 优先读取 AssemblyInformationalVersionAttribute
    var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
    if (informational is not null && !string.IsNullOrEmpty(informational.InformationalVersion))
    {
        return informational.InformationalVersion.Split('+')[0]; // 去掉源版本后缀
    }

    // 2. 回退到 AssemblyName.Version
    var assemblyVersion = asm.GetName().Version;
    if (assemblyVersion is not null)
    {
        return assemblyVersion.ToString();
    }

    // 3. 最终回退
    return "0.0.0";
}
```

- **优先级 1**：`AssemblyInformationalVersionAttribute`，通常由 MSBuild 在打包时根据 `<Version>`/`<PackageVersion>` 或 `MinVer`/`Nerdbank.GitVersioning` 等工具自动写入，**支持 SemVer 2.0**（如 `1.0.0-beta1+sha.abc`），代码用 `Split('+')[0]` 去掉构建元数据后缀。
- **优先级 2**：`AssemblyName.Version`，对应 `<AssemblyVersion>` 属性。
- **优先级 3**：硬编码 `0.0.0`。

这意味着开发者可以通过在 `.csproj` 中设置 `<Version>1.2.3</Version>` 或 `<AssemblyVersion>1.2.3.0</AssemblyVersion>` 来控制 `wails.net version` 的输出，**无需修改源代码**。`GetOsDescription` 则通过 `RuntimeInformation.IsOSPlatform` 与 `Environment.OSVersion.Version` 组合输出操作系统描述。

## 12. 设计要点总结

1. **System.CommandLine 集成**：所有命令通过 `Create()` 工厂方法注册，`AsyncAction.Create` 绑定异步委托，统一退出码语义。
2. **进程隔离**：`dotnet build`/`publish`/`watch`、`makensis`、`appimagetool`、`reg`、`pkg-config`、`ldconfig` 均通过 `ProcessStartInfo.ArgumentList`（非字符串拼接）启动，杜绝命令注入。
3. **零外部打包依赖**：tar.gz 手写 ustar 头、PNG 图标内嵌字节数组、NSIS 脚本模板字符串生成，避免引入 `SharpZipLib`/`Tarlib` 等第三方库。
4. **跨平台分支**：`Packager`/`DoctorCommand` 通过 `OperatingSystem.IsWindows()`/`IsLinux()` 分支，平台不匹配时抛 `PlatformNotSupportedException`。
5. **测试友好**：CLI 项目通过 `InternalsVisibleTo` 暴露内部类型，关键静态方法（如 `GenerateNsisScript`、`FindMainExecutable`、`IsVersionAtLeast`）标记为 `internal static`，便于 [Wails.Net.Cli.Tests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Cli.Tests) 直接验证。
6. **分层清晰**：CLI 仅作为薄壳编排，业务逻辑下沉到 `ProjectScaffolder`/`ProjectBuilder`/`Packager`/`BindingGenerationPipeline`，符合 AGENTS.md 的"管理器模式"架构原则。
