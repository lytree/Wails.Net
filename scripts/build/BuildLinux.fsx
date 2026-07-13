// BuildLinux.fsx — Linux 自包含构建脚本
// 对应 AGENTS.md §7.5：所有脚本任务必须使用 F# (.fsx)，严禁使用 Python
//
// 功能：
//   - dotnet publish 生成自包含 Linux 二进制（包含 .NET 运行时）
//   - 压缩输出目录为 .tar.gz
//   - 可选生成 .deb 包（需 dpkg-deb，仅 Linux 环境）
//
// 注意：
//   当前 examples/Wails.Net.Demo 的 TFM 为 net10.0-windows10.0.19041.0，
//   无法直接为 Linux 构建。脚本会检测 TFM 并在不适配时打印明确错误。
//   可通过 --project 指定其他跨平台项目。
//
// 用法：
//   dotnet fsi BuildLinux.fsx -- --configuration Release --rid linux-x64
//   dotnet fsi BuildLinux.fsx -- --dry-run
//   dotnet fsi BuildLinux.fsx -- --help

#load "Common.fsx"

open System
open System.IO
open System.Xml.Linq
open Common

/// 打印帮助信息
let printHelp () : unit =
    let helpText = """
BuildLinux.fsx — Linux 自包含构建脚本

用法: dotnet fsi BuildLinux.fsx -- [选项]

选项:
  --configuration <Debug|Release>  构建配置（默认 Release）
  --rid <RID>                      目标运行时标识符（默认 linux-x64）
                                   可选: linux-x64, linux-arm64,
                                         linux-musl-x64, linux-musl-arm64
  --output <path>                  输出根目录（默认 artifacts/dist）
  --skip-frontend                  跳过前端构建
  --version <version>              覆盖版本号（默认读取 Directory.Build.props）
  --project <path>                 指定项目文件（默认 examples/Wails.Net.Demo）
  --dry-run                        仅打印命令不实际执行
  --help                           显示此帮助信息

输出:
  <output>/linux/<version>/<RID>/
    ├── Wails.Net.Demo             自包含可执行（无扩展名）
    ├── *.dll                      依赖程序集
    ├── *.json                     配置文件
    └── ...
  <output>/linux/<version>/Wails.Net.Demo-<version>-<RID>.tar.gz
  <output>/linux/<version>/Wails.Net.Demo-<version>-<RID>.deb （仅 Linux + dpkg-deb）

注意:
  当前 Demo 项目 TFM 为 net10.0-windows10.0.19041.0，无法为 Linux 构建。
  请通过 --project 指定跨平台项目，或等待 Linux Demo 项目就绪。
"""
    printfn "%s" helpText

/// 从 .csproj 文件读取 TargetFramework
let readTargetFramework (projectPath: string) : string =
    let doc = XDocument.Load(projectPath)
    let root = doc.Root
    if isNull root then ""
    else
        let ns = root.GetDefaultNamespace()
        let propsGroup = root.Element(XName.Get("PropertyGroup", ns.NamespaceName))
        if isNull propsGroup then ""
        else
            let tfmElem = propsGroup.Element(XName.Get("TargetFramework", ns.NamespaceName))
            if isNull tfmElem then ""
            else tfmElem.Value.Trim()

/// 检查 TFM 是否兼容 Linux
let isLinuxCompatible (tfm: string) : bool =
    // TFM 包含 "windows" 则不兼容 Linux
    not (tfm.Contains("windows", StringComparison.OrdinalIgnoreCase))

/// 验证参数，返回错误码（Some code）或通过（None）
let validate (rid: string) (projectPath: string) : int option =
    let supportedRIDs = ["linux-x64"; "linux-arm64"; "linux-musl-x64"; "linux-musl-arm64"]
    if not (List.contains rid supportedRIDs) then
        let rids = String.Join(", ", supportedRIDs)
        logError $"不支持的 Linux RID: {rid}。支持的 RID: {rids}"
        Some 2
    elif not (File.Exists(projectPath)) then
        logError $"项目文件不存在: {projectPath}"
        Some 2
    else
        // 检查 TFM 兼容性
        let tfm = readTargetFramework projectPath
        if not (isLinuxCompatible tfm) then
            logError $"项目 TFM '{tfm}' 不兼容 Linux。"
            logError "TFM 包含 'windows' 标记，无法为 Linux 构建。"
            logError "请通过 --project 指定跨平台项目（TFM 应为 net10.0 或 net10.0-linux）。"
            Some 3
        else
            None

/// 创建 .deb 包的控制文件内容
let createDebControl (packageName: string) (version: string) (arch: string) : string =
    let debArch =
        match arch with
        | "linux-x64" -> "amd64"
        | "linux-arm64" -> "arm64"
        | "linux-musl-x64" -> "amd64"
        | "linux-musl-arm64" -> "arm64"
        | _ -> "all"
    let control = [
        "Package: " + packageName.ToLower()
        "Version: " + version
        "Architecture: " + debArch
        "Maintainer: Wails.Net <noreply@wails.net>"
        "Description: Wails.Net Demo Application"
        " Wails.Net is a .NET 10 port of Wails v3."
        " This package contains the self-contained binary."
        "Section: utils"
        "Priority: optional"
        ""
    ]
    String.Join("\n", control)

/// 尝试生成 .deb 包
let tryBuildDeb (outputDir: string) (packageName: string) (version: string) (rid: string)
                (debPath: string) : unit =
    if DryRun then
        logInfo $"[DRY-RUN] 将创建 deb: {debPath}"
    elif isLinux () then
        if commandExists "dpkg-deb" then
            logInfo "检测到 dpkg-deb，正在生成 .deb 包..."
            // 创建临时目录结构
            let tempDir = Path.Combine(Path.GetTempPath(), $"wails-deb-{Guid.NewGuid():N}")
            try
                let debRoot = Path.Combine(tempDir, "debroot")
                let usrLocal = Path.Combine(debRoot, "usr", "local", "share", packageName.ToLower())
                let binDir = Path.Combine(debRoot, "usr", "local", "bin")
                let debControlDir = Path.Combine(debRoot, "DEBIAN")
                ensureDirectory usrLocal
                ensureDirectory binDir
                ensureDirectory debControlDir

                // 复制构建产物到 /usr/local/share/<package>/
                copyDirectory outputDir usrLocal

                // 创建启动脚本
                let scriptPath = Path.Combine(binDir, packageName.ToLower())
                let scriptContent = "#!/bin/sh\nexec dotnet /usr/local/share/" + packageName.ToLower() + "/" + packageName + ".dll \"$@\"\n"
                File.WriteAllText(scriptPath, scriptContent)
                // 设置可执行权限（Unix only）
                try
                    let _ = runCommand "chmod" [|"+x"; scriptPath|]
                    ()
                with _ -> ()

                // 创建 control 文件
                let controlContent = createDebControl packageName version rid
                File.WriteAllText(Path.Combine(debControlDir, "control"), controlContent)

                // 打包
                let debExit = runCommand "dpkg-deb" [|"--build"; "-Zgzip"; debRoot; debPath|]
                if debExit = 0 then
                    logSuccess $"已生成 deb: {debPath}"
                else
                    logWarn $"dpkg-deb 失败（退出码 {debExit}），跳过 deb 生成"
            finally
                if Directory.Exists(tempDir) then
                    try Directory.Delete(tempDir, recursive = true) with _ -> ()
        else
            logWarn "未检测到 dpkg-deb，跳过 deb 生成。安装: sudo apt install dpkg-dev"
    else
        logWarn "当前非 Linux 环境，无法生成 .deb 包。请在 Linux 或 WSL 中运行。"

/// 执行构建主流程，返回退出码
let doBuild (configuration: string) (rid: string) (outputRoot: string)
            (skipFrontend: bool) (version: string) (projectPath: string) : int =
    logInfo "========== Linux 自包含构建 =========="
    logInfo $"版本:     {version}"
    logInfo $"配置:     {configuration}"
    logInfo $"RID:      {rid}"
    logInfo $"项目:     {projectPath}"
    logInfo $"跳过前端: {skipFrontend}"
    logInfo $"输出根:   {outputRoot}"

    // 输出目录
    let outputDir = Path.Combine(outputRoot, "linux", version, rid)
    let tarGzPath = Path.Combine(outputRoot, "linux", version, $"Wails.Net.Demo-{version}-{rid}.tar.gz")
    let debPath = Path.Combine(outputRoot, "linux", version, $"Wails.Net.Demo-{version}-{rid}.deb")

    if DryRun then
        logInfo $"[DRY-RUN] 将输出到: {outputDir}"
        logInfo $"[DRY-RUN] 将创建 tar.gz: {tarGzPath}"
    else
        ensureDirectory outputDir
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

        // 压缩输出目录为 tar.gz
        logInfo "正在压缩输出目录..."
        compressTarGz outputDir tarGzPath

        // 可选：生成 .deb 包
        logInfo "检查 deb 打包环境..."
        tryBuildDeb outputDir "Wails.Net.Demo" version rid debPath

        logSuccess "========== Linux 构建完成 =========="
        logInfo $"输出目录: {outputDir}"
        logInfo $"tar.gz:   {tarGzPath}"
        0

/// 构建入口，返回退出码
let build (args: Map<string, string>) : int =
    let configuration = argOr "configuration" "Release" args
    let rid = argOr "rid" "linux-x64" args
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

if hasFlag "dry-run" parsedArgs then
    DryRun <- true

if hasFlag "help" parsedArgs then
    printHelp ()
    0
else
    let code = build parsedArgs
    if code <> 0 then exit code
    0
