// BuildAll.fsx — 三平台统一构建脚本（主入口）
// 对应 AGENTS.md §7.5：所有脚本任务必须使用 F# (.fsx)，严禁使用 Python
//
// 功能：
//   - 统一调度 Windows、Linux、Android 三平台构建
//   - 通过子进程调用各平台构建脚本（保持独立性）
//   - 支持多 RID 一键编译（--rid all 或 --rid win-x64,linux-x64）
//   - 输出构建摘要
//
// 用法：
//   dotnet fsi BuildAll.fsx -- --platform all
//   dotnet fsi BuildAll.fsx -- --platform windows --configuration Release
//   dotnet fsi BuildAll.fsx -- --platform all --rid all
//   dotnet fsi BuildAll.fsx -- --dry-run
//   dotnet fsi BuildAll.fsx -- --help

#load "Common.fsx"

open System
open System.IO
open Common

/// 打印帮助信息
let printHelp () : unit =
    let helpText = """
BuildAll.fsx — 三平台统一构建脚本

用法: dotnet fsi BuildAll.fsx -- [选项]

选项:
  --platform <windows|linux|android|all>  构建平台（默认 all）
  --rid <RID|RID,RID,...|all>             目标运行时标识符（默认使用各平台默认 RID）
                                          all = 构建该平台全部支持的 RID
                                          逗号分隔 = 多 RID 串行构建
  --configuration <Debug|Release>         构建配置（默认 Release）
  --output <path>                         输出根目录（默认 artifacts/dist）
  --skip-frontend                         跳过前端构建
  --version <version>                     覆盖版本号（默认读取 Directory.Build.props）
  --dry-run                               仅打印命令不实际执行
  --help                                  显示此帮助信息

说明:
  本脚本通过子进程调用各平台构建脚本：
    - Windows: BuildWindows.fsx → .exe + .zip（+可选 .msi）
    - Linux:   BuildLinux.fsx   → 二进制 + .tar.gz（+可选 .deb）
    - Android: BuildAndroid.fsx → .apk（+可选 .apks）

  --rid 参数支持三种模式：
    1. 省略：使用各平台默认 RID（win-x64 / linux-x64 / android-arm64）
    2. 单个 RID：--rid win-x64
    3. 逗号分隔多个 RID：--rid win-x64,win-arm64
    4. all：构建该平台全部支持的 RID

  通用参数（--configuration, --output, --skip-frontend, --version, --dry-run）
  会传递给各平台脚本。

示例:
  # 构建全部平台（默认 RID）
  dotnet fsi BuildAll.fsx -- --platform all

  # 仅构建 Windows（指定 RID）
  dotnet fsi BuildAll.fsx -- --platform windows --rid win-x64

  # 构建 Windows 全部 RID
  dotnet fsi BuildAll.fsx -- --platform windows --rid all

  # 构建全部平台全部 RID（多平台多架构编译）
  dotnet fsi BuildAll.fsx -- --platform all --rid all

  # Dry-run 模式（仅打印命令）
  dotnet fsi BuildAll.fsx -- --platform all --dry-run

  # 指定版本号
  dotnet fsi BuildAll.fsx -- --version 1.0.0
"""
    printfn "%s" helpText

/// 构建平台列表
let allPlatforms = ["windows"; "linux"; "android"]

/// 各平台支持的 RID 列表
let platformRIDs : Map<string, string list> =
    Map.ofList [
        ("windows", ["win-x64"; "win-x86"; "win-arm64"])
        ("linux", ["linux-x64"; "linux-arm64"])
        ("android", ["android-arm64"; "android-x64"; "android-arm"])
    ]

/// 各平台默认 RID
let defaultRID : Map<string, string> =
    Map.ofList [
        ("windows", "win-x64")
        ("linux", "linux-x64")
        ("android", "android-arm64")
    ]

/// 解析 --rid 参数，返回指定平台要构建的 RID 列表。
/// ridArg 可能是 "all"、逗号分隔的多个 RID、或单个 RID。
/// 若 ridArg 为 None 或空，返回该平台的默认 RID。
let resolveRIDs (platform: string) (ridArg: string option) : string list =
    let supported = platformRIDs.TryFind platform |> Option.defaultValue []
    let defaultRid = defaultRID |> Map.find platform
    match ridArg with
    | None -> [defaultRid]
    | Some "" -> [defaultRid]
    | Some "all" -> supported
    | Some rids ->
        let ridList = rids.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.filter (fun s -> s <> "")
        if ridList.Length = 0 then
            [defaultRid]
        else
            ridList
            |> Array.filter (fun rid ->
                if List.contains rid supported then
                    true
                else
                    let supportedStr = String.Join(", ", supported)
                    logWarn $"平台 {platform} 不支持 RID: {rid}（支持的: {supportedStr}）"
                    false)
            |> Array.toList

/// 为指定平台构建传递通用参数，返回参数数组
let buildPassThroughArgs (configuration: string)
                          (outputRoot: string) (skipFrontend: bool)
                          (version: string option) (dryRun: bool)
                          (ridOpt: string option) : string[] =
    let args = ResizeArray<string>()
    args.Add("--configuration"); args.Add(configuration)
    args.Add("--output"); args.Add(outputRoot)
    // 传递 RID（如果指定）
    match ridOpt with
    | Some rid -> args.Add("--rid"); args.Add(rid)
    | None -> ()
    if skipFrontend then args.Add("--skip-frontend")
    match version with
    | Some v -> args.Add("--version"); args.Add(v)
    | None -> ()
    if dryRun then args.Add("--dry-run")
    args.ToArray()

/// 调用平台构建脚本
let invokePlatformBuild (platform: string) (passArgs: string[]) : int =
    let scriptName = $"Build{Char.ToUpper(platform.[0])}{platform.[1..]}.fsx"
    let scriptPath = Path.Combine(__SOURCE_DIRECTORY__, scriptName)
    if not (File.Exists(scriptPath)) then
        logError $"平台构建脚本不存在: {scriptPath}"
        2
    else
        logInfo $"========== 调用 {platform} 构建 =========="
        // 构建 dotnet fsi script.fsx -- args 参数
        let cmdArgs = ResizeArray<string>()
        cmdArgs.Add("fsi")
        cmdArgs.Add(scriptPath)
        cmdArgs.Add("--")
        for a in passArgs do
            cmdArgs.Add(a)
        let exitCode = runCommand "dotnet" (cmdArgs.ToArray())
        if exitCode = 0 then
            logSuccess $"{platform} 构建完成"
        else
            logError $"{platform} 构建失败（退出码 {exitCode}）"
        exitCode

/// 构建入口
let build (args: Map<string, string>) : int =
    let platform = argOr "platform" "all" args
    let configuration = argOr "configuration" "Release" args
    let outputRoot = argOr "output" "artifacts/dist" args
    let skipFrontend = hasFlag "skip-frontend" args
    let customVersion = args.TryFind "version"
    let ridArg = args.TryFind "rid"

    // 读取版本号（用于摘要显示）
    let version =
        match customVersion with
        | Some v -> v
        | None ->
            try getVersion () with _ -> "unknown"

    // 确定要构建的平台列表
    let platforms =
        if platform = "all" then allPlatforms
        elif List.contains platform allPlatforms then [platform]
        else
            logError $"未知平台: {platform}。支持: windows, linux, android, all"
            []
    if platforms.IsEmpty then
        2
    else
        logInfo "========== 三平台统一构建 =========="
        logInfo $"版本:      {version}"
        logInfo $"配置:      {configuration}"
        let platformStr = String.Join(", ", platforms)
        logInfo $"平台:      {platformStr}"
        logInfo $"输出根:    {outputRoot}"
        logInfo $"跳过前端:  {skipFrontend}"
        logInfo $"Dry-run:   {DryRun}"

        // 多平台多 RID 串行构建
        let results = ResizeArray<string * int>()
        for p in platforms do
            let rids = resolveRIDs p ridArg
            let ridsStr = String.Join(", ", rids)
            logInfo $"平台 {p} 将构建 {rids.Length} 个 RID: {ridsStr}"
            for rid in rids do
                let ridLabel = if rids.Length > 1 then $"{p}/{rid}" else p
                logInfo $"----- 构建 {ridLabel} -----"
                let passArgs = buildPassThroughArgs configuration outputRoot skipFrontend customVersion DryRun (Some rid)
                let code = invokePlatformBuild p passArgs
                results.Add((ridLabel, code))

        // 打印构建摘要
        logInfo "========== 构建摘要 =========="
        let mutable allSuccess = true
        for (label, code) in results do
            let status = if code = 0 then "✓ 成功" else $"✗ 失败 (退出码 {code})"
            if code = 0 then
                logSuccess $"{label,-20} {status}"
            else
                logError $"{label,-20} {status}"
                allSuccess <- false

        if allSuccess then
            logSuccess "所有平台构建成功！"
            0
        else
            logError "部分平台构建失败，请检查上方日志。"
            1

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
