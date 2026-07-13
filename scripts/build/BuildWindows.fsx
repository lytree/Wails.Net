// BuildWindows.fsx — Windows 自包含构建脚本
// 对应 AGENTS.md §7.5：所有脚本任务必须使用 F# (.fsx)，严禁使用 Python
//
// 功能：
//   - dotnet publish 生成自包含单文件 .exe（包含 .NET 运行时）
//   - 压缩输出目录为 .zip
//   - 可选生成 .msi（需安装 WiX 工具）
//
// 用法：
//   dotnet fsi BuildWindows.fsx -- --configuration Release --rid win-x64
//   dotnet fsi BuildWindows.fsx -- --dry-run
//   dotnet fsi BuildWindows.fsx -- --help

#load "Common.fsx"

open System
open System.IO
open Common

/// 打印帮助信息
let printHelp () : unit =
    let helpText = """
BuildWindows.fsx — Windows 自包含构建脚本

用法: dotnet fsi BuildWindows.fsx -- [选项]

选项:
  --configuration <Debug|Release>  构建配置（默认 Release）
  --rid <RID>                      目标运行时标识符（默认 win-x64）
                                   可选: win-x64, win-x86, win-arm64
  --output <path>                  输出根目录（默认 artifacts/dist）
  --skip-frontend                  跳过前端构建
  --version <version>              覆盖版本号（默认读取 Directory.Build.props）
  --project <path>                 指定项目文件（默认 examples/Wails.Net.Demo）
  --dry-run                        仅打印命令不实际执行
  --help                           显示此帮助信息

输出:
  <output>/windows/<version>/<RID>/
    ├── Wails.Net.Demo.exe         自包含可执行
    ├── *.dll                      依赖程序集
    ├── appsettings.json
    ├── frontend/                  前端资源
    └── ...
  <output>/windows/<version>/Wails.Net.Demo-<version>-<RID>.zip
"""
    printfn "%s" helpText

/// 验证参数，返回错误码（Some code）或通过（None）
let validate (rid: string) (projectPath: string) : int option =
    let supportedRIDs = ["win-x64"; "win-x86"; "win-arm64"]
    if not (List.contains rid supportedRIDs) then
        let rids = String.Join(", ", supportedRIDs)
        logError $"不支持的 Windows RID: {rid}。支持的 RID: {rids}"
        Some 2
    elif not (File.Exists(projectPath)) then
        logError $"项目文件不存在: {projectPath}"
        Some 2
    else
        None

/// 执行构建主流程，返回退出码
let doBuild (configuration: string) (rid: string) (outputRoot: string)
            (skipFrontend: bool) (version: string) (projectPath: string) : int =
    logInfo "========== Windows 自包含构建 =========="
    logInfo $"版本:     {version}"
    logInfo $"配置:     {configuration}"
    logInfo $"RID:      {rid}"
    logInfo $"项目:     {projectPath}"
    logInfo $"跳过前端: {skipFrontend}"
    logInfo $"输出根:   {outputRoot}"

    // 输出目录
    let outputDir = Path.Combine(outputRoot, "windows", version, rid)
    let zipPath = Path.Combine(outputRoot, "windows", version, $"Wails.Net.Demo-{version}-{rid}.zip")

    if DryRun then
        logInfo $"[DRY-RUN] 将输出到: {outputDir}"
        logInfo $"[DRY-RUN] 将创建 zip: {zipPath}"
    else
        ensureDirectory outputDir
        // 清理旧的输出（保证幂等）
        if Directory.Exists(outputDir) then
            Directory.Delete(outputDir, recursive = true)
        ensureDirectory outputDir

    // 构建 dotnet publish 参数
    let publishArgs =
        let baseArgs =
            [|
                "publish"
                projectPath
                "-c"; configuration
                "-r"; rid
                "--self-contained"; "true"
                "-p:PublishSingleFile=true"
                "-p:PublishTrimmed=false"
                "-p:IncludeNativeLibrariesForSelfExtract=true"
                "-o"; outputDir
            |]
        if skipFrontend then
            Array.append baseArgs [|"-p:SkipFrontendBuild=true"|]
        else
            baseArgs

    logInfo "正在执行 dotnet publish（自包含构建）..."
    let exitCode = runCommand "dotnet" publishArgs
    if exitCode <> 0 then
        logError "dotnet publish 失败"
        exitCode
    else
        logSuccess "dotnet publish 完成"

        // 压缩输出目录为 zip
        logInfo "正在压缩输出目录..."
        compressZip outputDir zipPath

        // 可选：生成 MSI（需 WiX 工具）
        logInfo "检查 WiX 工具..."
        let wixAvailable = commandExists "wix"
        if wixAvailable then
            logInfo "检测到 WiX 工具，尝试生成 MSI..."
            let msiPath = Path.Combine(outputRoot, "windows", version, $"Wails.Net.Demo-{version}-{rid}.msi")
            let wxsTemplate = Path.Combine(projectRoot (), "packaging", "windows", "Wails.Net.Demo.wxs")
            if File.Exists(wxsTemplate) then
                let wixArgs = [|"build"; wxsTemplate; "-o"; msiPath|]
                let wixExit = runCommand "wix" wixArgs
                if wixExit = 0 then
                    logSuccess $"已生成 MSI: {msiPath}"
                else
                    logWarn $"WiX 构建失败（退出码 {wixExit}），跳过 MSI 生成"
            else
                logWarn $"未找到 WiX 模板文件: {wxsTemplate}，跳过 MSI 生成"
        else
            logWarn "未检测到 WiX 工具，跳过 MSI 生成。如需 MSI，请安装: dotnet tool install -g wix"

        logSuccess "========== Windows 构建完成 =========="
        logInfo $"输出目录: {outputDir}"
        logInfo $"ZIP 包:   {zipPath}"
        0

/// 构建入口，返回退出码
let build (args: Map<string, string>) : int =
    // 解析参数
    let configuration = argOr "configuration" "Release" args
    let rid = argOr "rid" "win-x64" args
    let outputRoot = argOr "output" "artifacts/dist" args
    let skipFrontend = hasFlag "skip-frontend" args
    let customVersion = args.TryFind "version"
    let customProject = args.TryFind "project"

    let version =
        match customVersion with
        | Some v -> v
        | None -> getVersion ()

    let projectPath =
        match customProject with
        | Some p -> p
        | None -> Path.Combine(projectRoot (), "examples", "Wails.Net.Demo", "Wails.Net.Demo.csproj")

    // 验证参数
    match validate rid projectPath with
    | Some code -> code
    | None -> doBuild configuration rid outputRoot skipFrontend version projectPath

// ===== 脚本入口 =====
let mainArgs =
    if fsi.CommandLineArgs.Length > 1 then
        fsi.CommandLineArgs.[1..]
    else
        [||]

let parsedArgs = parseArgs mainArgs

// 设置 DryRun 全局标志
if hasFlag "dry-run" parsedArgs then
    DryRun <- true

// 处理 --help
if hasFlag "help" parsedArgs then
    printHelp ()
    0
else
    let code = build parsedArgs
    if code <> 0 then exit code
    0
