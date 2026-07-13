// BuildAndroid.fsx — Android APK 构建脚本
// 对应 AGENTS.md §7.5：所有脚本任务必须使用 F# (.fsx)，严禁使用 Python
//
// 功能：
//   - dotnet publish 生成自包含 Android .apk
//   - 支持签名（通过环境变量配置密钥）
//   - 可选生成 .apks（需 bundletool）
//
// 前提：
//   - 需安装 .NET Android 工作负载：dotnet workload install android
//   - 需存在 Android Demo 项目（如 examples/Wails.Net.Demo.Android）
//
// 用法：
//   dotnet fsi BuildAndroid.fsx -- --configuration Release --rid android-arm64
//   dotnet fsi BuildAndroid.fsx -- --dry-run
//   dotnet fsi BuildAndroid.fsx -- --help

#load "Common.fsx"

open System
open System.IO
open Common

/// 打印帮助信息
let printHelp () : unit =
    let helpText = """
BuildAndroid.fsx — Android APK 构建脚本

用法: dotnet fsi BuildAndroid.fsx -- [选项]

选项:
  --configuration <Debug|Release>  构建配置（默认 Release）
  --rid <RID>                      目标运行时标识符（默认 android-arm64）
                                   可选: android-arm64, android-x64, android-arm
  --output <path>                  输出根目录（默认 artifacts/dist）
  --version <version>              覆盖版本号（默认读取 Directory.Build.props）
  --project <path>                 指定项目文件
                                   （默认 examples/Wails.Net.Demo.Android）
  --framework <TFM>                覆盖目标框架（默认由项目决定）
  --dry-run                        仅打印命令不实际执行
  --help                           显示此帮助信息

环境变量（签名配置）:
  ANDROID_KEYSTORE_PATH            签名密钥库路径（.keystore 或 .jks）
  ANDROID_KEY_ALIAS                密钥别名
  ANDROID_KEY_PASS                 密钥密码
  ANDROID_STORE_PASS               密钥库密码
  若未设置以上变量，将使用 debug 签名并打印警告。

输出:
  <output>/android/<version>/<RID>/
    └── Wails.Net.Demo-<version>-<RID>.apk
  <output>/android/<version>/Wails.Net.Demo-<version>.apks （需 bundletool）

注意:
  需安装 .NET Android 工作负载: dotnet workload install android
  若不存在 Android Demo 项目，脚本将打印错误并退出。
"""
    printfn "%s" helpText

/// 检查 .NET Android 工作负载是否已安装
let checkAndroidWorkload () : bool =
    if DryRun then
        logInfo "[DRY-RUN] 跳过工作负载检查"
        true
    else
        logInfo "检查 .NET Android 工作负载..."
        match runCommandCapture "dotnet" [|"workload"; "list"|] with
        | Some output ->
            if output.Contains("android", StringComparison.OrdinalIgnoreCase) then
                logSuccess "已安装 .NET Android 工作负载"
                true
            else
                logError "未安装 .NET Android 工作负载！"
                logError "请运行: dotnet workload install android"
                false
        | None ->
            logError "无法执行 'dotnet workload list'，请确认 .NET SDK 已安装"
            false

/// 检查环境变量签名配置，返回是否使用正式签名
let getSigningConfig () : (string * string * string * string) option =
    let keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH")
    let keyAlias = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS")
    let keyPass = Environment.GetEnvironmentVariable("ANDROID_KEY_PASS")
    let storePass = Environment.GetEnvironmentVariable("ANDROID_STORE_PASS")
    let nullToEmpty (s: string) = if isNull s then "" else s
    if String.IsNullOrEmpty(keystorePath) || String.IsNullOrEmpty(keyAlias) then
        None
    else
        Some(keystorePath, keyAlias, nullToEmpty keyPass, nullToEmpty storePass)

/// 验证参数，返回错误码（Some code）或通过（None）
let validate (rid: string) (projectPath: string) : int option =
    let supportedRIDs = ["android-arm64"; "android-x64"; "android-arm"]
    if not (List.contains rid supportedRIDs) then
        let rids = String.Join(", ", supportedRIDs)
        logError $"不支持的 Android RID: {rid}。支持的 RID: {rids}"
        Some 2
    elif not (File.Exists(projectPath)) then
        logError $"Android 项目文件不存在: {projectPath}"
        logError "请创建 Android Demo 项目，或通过 --project 指定现有 Android 项目。"
        logError "示例: dotnet new android -o examples/Wails.Net.Demo.Android"
        Some 2
    else
        None

/// 执行构建主流程，返回退出码
let doBuild (configuration: string) (rid: string) (outputRoot: string)
            (version: string) (projectPath: string) (framework: string option) : int =
    logInfo "========== Android APK 构建 =========="
    logInfo $"版本:     {version}"
    logInfo $"配置:     {configuration}"
    logInfo $"RID:      {rid}"
    logInfo $"项目:     {projectPath}"
    logInfo $"输出根:   {outputRoot}"
    match framework with
    | Some f -> logInfo $"框架:     {f}"
    | None -> logInfo "框架:     （由项目决定）"

    // 输出目录
    let outputDir = Path.Combine(outputRoot, "android", version, rid)
    let apkName = $"Wails.Net.Demo-{version}-{rid}.apk"
    let apkPath = Path.Combine(outputDir, apkName)
    let apksPath = Path.Combine(outputRoot, "android", version, $"Wails.Net.Demo-{version}.apks")

    if DryRun then
        logInfo $"[DRY-RUN] 将输出到: {outputDir}"
        logInfo $"[DRY-RUN] 将生成 APK: {apkPath}"
    else
        ensureDirectory outputDir

    // 构建 dotnet publish 参数
    let publishArgs =
        let baseArgs = ResizeArray<string>()
        baseArgs.Add("publish")
        baseArgs.Add(projectPath)
        baseArgs.Add("-c")
        baseArgs.Add(configuration)
        baseArgs.Add("-r")
        baseArgs.Add(rid)
        match framework with
        | Some f -> baseArgs.Add("-f"); baseArgs.Add(f)
        | None -> ()
        baseArgs.Add("-p:AndroidPackageFormat=apk")
        baseArgs.Add("--self-contained")
        baseArgs.Add("true")
        baseArgs.ToArray()

    // 签名配置
    let signingArgs =
        match getSigningConfig () with
        | Some(keystorePath, keyAlias, keyPass, storePass) ->
            logInfo "使用正式签名（环境变量配置）"
            [|
                $"-p:AndroidKeyStore=true"
                $"-p:AndroidSigningKeyStore={keystorePath}"
                $"-p:AndroidSigningKeyAlias={keyAlias}"
                $"-p:AndroidSigningKeyPass={keyPass}"
                $"-p:AndroidSigningStorePass={storePass}"
            |]
        | None ->
            logWarn "未设置 ANDROID_KEYSTORE_PATH 等环境变量，使用 debug 签名"
            logWarn "正式发布请设置: ANDROID_KEYSTORE_PATH, ANDROID_KEY_ALIAS, ANDROID_KEY_PASS, ANDROID_STORE_PASS"
            [|"-p:AndroidKeyStore=false"|]

    let allArgs = Array.append publishArgs signingArgs

    logInfo "正在执行 dotnet publish（Android APK 构建）..."
    let exitCode = runCommand "dotnet" allArgs
    if exitCode <> 0 then
        logError "dotnet publish 失败"
        exitCode
    else
        logSuccess "dotnet publish 完成"

        // 查找并复制 APK 到输出目录
        logInfo "正在查找生成的 APK 文件..."
        if DryRun then
            logInfo $"[DRY-RUN] 将复制 APK 到: {apkPath}"
        else
            // dotnet publish 的 APK 通常在项目的 bin/<configuration>/<TFM>/ 下
            let projectDir = Path.GetDirectoryName(projectPath)
            let possibleDirs =
                Directory.GetDirectories(Path.Combine(projectDir, "bin"), "*", SearchOption.AllDirectories)
                |> Array.filter (fun d ->
                    let files = Directory.GetFiles(d, "*.apk")
                    files.Length > 0)
            let apkFound =
                possibleDirs
                |> Array.collect (fun d -> Directory.GetFiles(d, "*.apk"))
                |> Array.tryHead
            match apkFound with
            | Some srcApk ->
                ensureDirectory outputDir
                File.Copy(srcApk, apkPath, overwrite = true)
                logSuccess $"已复制 APK: {apkPath}"
            | None ->
                logWarn "未找到生成的 APK 文件。请检查构建输出。"

        // 可选：生成 .apks（需 bundletool）
        logInfo "检查 bundletool..."
        if commandExists "bundletool" then
            logInfo "检测到 bundletool，尝试生成 .apks..."
            if DryRun then
                logInfo $"[DRY-RUN] 将生成 apks: {apksPath}"
            else
                // bundletool build-apks --mode=universal --bundle=<.aab> --output=<.apks>
                // 注意：.apks 需要从 .aab（App Bundle）生成，而非 .apk
                // 此处仅当存在 .aab 时尝试
                let projectDir = Path.GetDirectoryName(projectPath)
                let aabFiles =
                    Directory.GetDirectories(Path.Combine(projectDir, "bin"), "*", SearchOption.AllDirectories)
                    |> Array.collect (fun d -> Directory.GetFiles(d, "*.aab"))
                match Array.tryHead aabFiles with
                | Some aabPath ->
                    let apksExit =
                        runCommand "bundletool" [|"build-apks"; "--mode=universal"; $"--bundle={aabPath}"; $"--output={apksPath}"|]
                    if apksExit = 0 then
                        logSuccess $"已生成 apks: {apksPath}"
                    else
                        logWarn $"bundletool 失败（退出码 {apksExit}），跳过 apks 生成"
                | None ->
                    logWarn "未找到 .aab 文件，跳过 apks 生成。需先构建 App Bundle。"
        else
            logWarn "未检测到 bundletool，跳过 apks 生成。"

        logSuccess "========== Android 构建完成 =========="
        logInfo $"APK:  {apkPath}"
        0

/// 构建入口，返回退出码
let build (args: Map<string, string>) : int =
    let configuration = argOr "configuration" "Release" args
    let rid = argOr "rid" "android-arm64" args
    let outputRoot = argOr "output" "artifacts/dist" args
    let customVersion = args.TryFind "version"
    let customProject = args.TryFind "project"
    let customFramework = args.TryFind "framework"

    let version =
        match customVersion with
        | Some v -> v
        | None -> getVersion ()

    let projectPath =
        match customProject with
        | Some p -> p
        | None -> Path.Combine(projectRoot (), "examples", "Wails.Net.Demo.Android", "Wails.Net.Demo.Android.csproj")

    // 验证 RID 和项目文件
    match validate rid projectPath with
    | Some code -> code
    | None ->
        // 检查工作负载
        if not (checkAndroidWorkload ()) then
            3
        else
            doBuild configuration rid outputRoot version projectPath customFramework

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
