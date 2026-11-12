#!/usr/bin/env dotnet fsi
// ============================================================================
// Wails.Net Android E2E 测试脚本（设备端插桩测试替代方案）
// ----------------------------------------------------------------------------
// 遵循 AGENTS.md §7.5：使用 F# (.fsx) 脚本，通过 `dotnet fsi run-android-e2e.fsx` 运行。
// 避开 §4.1 测试框架约束：本脚本不属于"测试框架"使用，而是通过 adb 驱动 UI 自动化。
//
// 测试目标（端到端验证 Wails.Net Android 平台）：
//   1. Demo APK 构建并安装到设备/模拟器
//   2. MainActivity 启动、进程存活并进入前台（mCurrentFocus/mFocusedApp 指向本应用）
//   3. WebView 加载本地资源（uiautomator dump 验证页面文字出现）
//   4. IPC 绑定调用（点击"打招呼"按钮，验证 greetResult 出现 Hello/你好 前缀）
//
// 运行前提：
//   - adb 已加入 PATH（Android SDK platform-tools）
//   - 已连接 Android 设备或运行中的模拟器（adb devices 至少一个 device）
//   - .NET 10 SDK + android 工作负载已安装
//
// 用法：
//   dotnet fsi run-android-e2e.fsx                        # 默认配置
//   dotnet fsi run-android-e2e.fsx -- --verbose           # 详细日志
//   dotnet fsi run-android-e2e.fsx -- --no-build          # 跳过 APK 构建
//   dotnet fsi run-android-e2e.fsx -- --no-install        # 跳过 APK 安装
//   dotnet fsi run-android-e2e.fsx -- --apk-path <path>   # 指定 APK 路径
//
// 退出码：
//   0 = 全部测试通过
//   1 = 测试失败
//   2 = 环境错误（adb 缺失、无设备等）
// ============================================================================

open System
open System.Diagnostics
open System.IO
open System.Threading

// ----------------------------------------------------------------------------
// 命令行参数解析
// ----------------------------------------------------------------------------
let argv = Environment.GetCommandLineArgs() |> Array.toList
let verbose = argv |> List.exists ((=) "--verbose")
let noBuild = argv |> List.exists ((=) "--no-build")
let noInstall = argv |> List.exists ((=) "--no-install")

let customApkPath =
    match argv |> List.tryFindIndex ((=) "--apk-path") with
    | Some i when i + 1 < argv.Length -> Some argv.[i + 1]
    | _ -> None

// 日志函数统一接受预格式化字符串，避免 F# 类型推断与 kprintf 的 unit 返回类型冲突。
let log (msg: string) =
    if verbose then Console.WriteLine("[verbose] " + msg)

let info (msg: string) = Console.WriteLine("[info] " + msg)
let pass (msg: string) = Console.WriteLine("  ✓ " + msg)
let fail (msg: string) = Console.WriteLine("  ✗ " + msg)

// ----------------------------------------------------------------------------
// 进程执行辅助
// ----------------------------------------------------------------------------
let runProcess (fileName: string) (args: string) (timeoutMs: int) =
    // 启动失败时返回特殊退出码 -127，调用方按"可执行文件不可用"处理
    try
        let psi = ProcessStartInfo(fileName, args,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true)
        use p = new Process(StartInfo = psi)
        if not (p.Start()) then
            -127, "", sprintf "无法启动进程: %s %s" fileName args
        else
            let stdout = p.StandardOutput.ReadToEnd()
            let stderr = p.StandardError.ReadToEnd()
            if not (p.WaitForExit(timeoutMs)) then
                p.Kill(entireProcessTree = true)
                failwithf "进程超时（%dms）: %s %s" timeoutMs fileName args
            p.ExitCode, stdout, stderr
    with
    | :? System.ComponentModel.Win32Exception as ex ->
        // Windows/Linux 上可执行文件不在 PATH 时抛 Win32Exception（errorCode=2）
        -127, "", ex.Message
    | ex ->
        -127, "", ex.Message

let adb (args: string) =
    log (sprintf "adb %s" args)
    let code, out, err = runProcess "adb" args 30000
    if code <> 0 then
        log (sprintf "adb exit=%d stderr=%s" code err)
    out

let dotnetCmd (args: string) (timeoutMs: int) =
    log (sprintf "dotnet %s" args)
    let code, out, err = runProcess "dotnet" args timeoutMs
    if code <> 0 then
        Console.WriteLine(sprintf "dotnet %s 失败 (exit=%d)" args code)
        Console.WriteLine("stderr: " + err)
    code, out, err

// ----------------------------------------------------------------------------
// 环境检查
// ----------------------------------------------------------------------------
let checkEnvironment () =
    info "环境检查"
    // 1. adb 是否可用
    let code, adbVersion, _ = runProcess "adb" "version" 5000
    if code = -127 || String.IsNullOrWhiteSpace adbVersion then
        Console.Error.WriteLine("错误：adb 不在 PATH 中。请安装 Android SDK platform-tools。")
        2
    else
        let firstLine = adbVersion.Split('\n').[0].Trim()
        pass (sprintf "adb 可用: %s" firstLine)

        // 2. 设备是否连接
        let devices = adb "devices"
        let deviceLines =
            devices.Split('\n')
            |> Array.skip 1
            |> Array.filter (fun l -> l.Trim().Length > 0 && not (l.Contains("List of devices")))
            |> Array.filter (fun l -> l.Contains("device") || l.Contains("emulator"))
        if Array.isEmpty deviceLines then
            Console.Error.WriteLine("错误：无 Android 设备/模拟器连接。请先启动模拟器或连接设备。")
            2
        else
            pass (sprintf "已连接设备: %d 台" deviceLines.Length)
            0

// ----------------------------------------------------------------------------
// 测试步骤
// ----------------------------------------------------------------------------
let buildApk () =
    info "步骤 1/4：构建 Demo Android APK"
    let code, _, _ = dotnetCmd "build examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj -c Release" 600000
    if code <> 0 then
        fail "APK 构建失败"
        false
    else
        pass "APK 构建成功"
        true

let findApkPath () =
    // .NET Android 默认输出路径：bin/Release/net10.0-android36.0/wails.net.demo.android-Signed.apk
    let candidates = [
        "examples/Wails.Net.Demo.Android/bin/Release/net10.0-android36.0/wails.net.demo.android-Signed.apk"
        "examples/Wails.Net.Demo.Android/bin/Release/net10.0-android36.0/publish/wails.net.demo.android-Signed.apk"
    ]
    candidates |> List.tryFind File.Exists

let installApk apkPath =
    info "步骤 2/4：安装 APK 到设备"
    log (sprintf "APK 路径: %s" apkPath)
    let result = adb (sprintf "install -r -t %s" apkPath)
    if result.Contains("Success") then
        pass "APK 安装成功"
        true
    else
        fail (sprintf "APK 安装失败: %s" (result.Trim()))
        false

let appPackage = "wails.net.demo.android"

/// am start 失败时输出组件解析与清单信息，用于判断是"包没装上/包名不对""没有
/// LAUNCHER 活动"还是"活动未导出"。
let dumpLaunchDiagnostics () =
    // 不按包名过滤，先在全部包里找含 demo 的项——避免"包名与预期不一致"这种情况被过滤掉
    let allPackages = adb "shell pm list packages"
    let demoPackages =
        allPackages.Split('\n')
        |> Array.filter (fun l -> l.Contains("demo"))
        |> String.concat "\n"
    log (sprintf "含 demo 的已安装包:\n%s" demoPackages)

    let resolved =
        adb (sprintf "shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER %s" appPackage)
    log (sprintf "resolve-activity: %s" (resolved.Trim()))

    let dump = adb (sprintf "shell dumpsys package %s" appPackage)
    let interesting =
        dump.Split('\n')
        |> Array.filter (fun l ->
            l.Contains("Activity") || l.Contains("MAIN") || l.Contains("LAUNCHER") || l.Contains("android.intent"))
        |> Array.truncate 40
        |> String.concat "\n"
    log (sprintf "dumpsys package（Activity 相关）:\n%s" interesting)

/// 用 LAUNCHER intent 启动应用。
/// 不能用 `am start -n <pkg>/<pkg>.MainActivity`：.NET Android 生成的 Java 类名是
/// crc64<hash>.MainActivity（C# 类名只存在于托管侧），组件名不匹配会让 am start
/// 直接失败——旧脚本丢弃了该命令的输出，于是表现为"应用没起来、日志为空"。
/// 判定依据用退出码而不是输出文本：am start 的报错走 stderr，只看 stdout 会误判为成功。
let tryLaunchWithIntent () =
    let code, out, err =
        runProcess "adb"
            (sprintf "shell am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p %s" appPackage)
            30000
    log (sprintf "am start exit=%d 输出=%s 错误=%s" code (out.Trim()) (err.Trim()))
    code = 0

/// 兜底：部分设备/清单组合下 am start 解析不到组件，但 monkey 能正常拉起 LAUNCHER 活动。
let tryLaunchWithMonkey () =
    let code, out, err =
        runProcess "adb" (sprintf "shell monkey -p %s -c android.intent.category.LAUNCHER 1" appPackage) 30000
    log (sprintf "monkey exit=%d 输出=%s 错误=%s" code (out.Trim()) (err.Trim()))
    code = 0 && out.Contains("Events injected")

let launchApp () =
    let launched = tryLaunchWithIntent () || tryLaunchWithMonkey ()
    if not launched then
        fail "应用启动失败（am start 与 monkey 均未成功）"
        dumpLaunchDiagnostics ()
    launched

/// 抓取应用相关错误日志（崩溃堆栈、未捕获异常），供失败时排障。
let dumpErrorLogs (title: string) =
    let logs = adb "logcat -d -s AndroidRuntime:E DOTNET:E mono-rt:E monodroid:E libc:F *:S"
    if not (String.IsNullOrWhiteSpace logs) then
        log (sprintf "%s:\n%s" title (logs.Trim()))

let startApp () =
    info "步骤 3/4：启动应用并验证 Activity 进入前台"
    adb "logcat -c" |> ignore

    let launched = launchApp()

    // 等待 WebView 初始化
    Thread.Sleep(8000)

    let pid = (adb (sprintf "shell pidof %s" appPackage)).Trim()
    if String.IsNullOrWhiteSpace pid then
        fail (sprintf "应用进程未存活（pidof %s 为空），应用可能在启动时崩溃" appPackage)
        dumpErrorLogs "错误日志"
        false
    else
        pass (sprintf "应用进程已启动（pid=%s）" pid)

        // 平台事件的转发不产生任何 logcat 文本（事件 ID 走内部通道），因此不能用
        // "日志里出现 ActivityCreated" 作为判据。改为验证窗口焦点落在本应用上。
        let windows = adb "shell dumpsys window"
        let focusLines =
            windows.Split('\n')
            |> Array.filter (fun l -> l.Contains("mCurrentFocus") || l.Contains("mFocusedApp"))
            |> String.concat "\n"
        log (sprintf "窗口焦点: %s" (focusLines.Trim()))

        let inForeground =
            focusLines.Contains(appPackage) || focusLines.Contains("Wails.Net Demo")
        if inForeground then
            pass "MainActivity 已进入前台（mCurrentFocus/mFocusedApp 指向本应用）"
        else
            fail (sprintf "MainActivity 未进入前台（当前焦点: %s）" (focusLines.Trim()))
            dumpErrorLogs "错误日志"

        launched && inForeground

let uiAutomatorDump (name: string) =
    adb (sprintf "shell uiautomator dump /sdcard/%s" name) |> ignore
    adb (sprintf "shell cat /sdcard/%s" name)

/// 从 uiautomator XML 中定位包含指定文字的节点，返回其中心坐标。
let tryFindNodeCenter (xml: string) (text: string) =
    let pattern =
        "text=\"" + Text.RegularExpressions.Regex.Escape(text)
        + "\"[^>]*bounds=\"\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]\""
    let m = Text.RegularExpressions.Regex.Match(xml, pattern)
    if not m.Success then
        None
    else
        let x1 = int m.Groups.[1].Value
        let y1 = int m.Groups.[2].Value
        let x2 = int m.Groups.[3].Value
        let y2 = int m.Groups.[4].Value
        Some((x1 + x2) / 2, (y1 + y2) / 2)

let verifyWebView () =
    info "步骤 4/4：验证 WebView 加载与 IPC 绑定"
    // 1. 通过 uiautomator dump 验证 WebView 渲染
    let uiXml = uiAutomatorDump "wails_ui.xml"

    let pageVisible =
        uiXml.Contains("问候服务")      // 卡片标题
        || uiXml.Contains("打招呼")     // 按钮文字
        || uiXml.Contains("Wails.Net Demo") // HTML <title>

    if not pageVisible then
        fail "WebView 渲染失败（uiautomator dump 未检测到 Wails.Net Demo 页面元素）"
        log (sprintf "UI dump 内容:\n%s" uiXml)
        false
    else
        pass "WebView 渲染正常（检测到 Wails.Net Demo 页面元素）"

        // 2. 点击"打招呼"按钮，验证 IPC 绑定调用。
        //    坐标从 uiautomator 的 bounds 计算，不再硬编码（旧脚本固定 540,480，
        //    而模拟器分辨率为 320x640，点击点根本不在按钮上）。
        match tryFindNodeCenter uiXml "打招呼" with
        | None ->
            fail "未能在 UI 树中定位「打招呼」按钮，无法验证 IPC 绑定"
            log (sprintf "UI dump 内容:\n%s" uiXml)
            false
        | Some(x, y) ->
            log (sprintf "点击「打招呼」按钮 (%d, %d)" x y)
            adb (sprintf "shell input tap %d %d" x y) |> ignore
            Thread.Sleep(2000)

            let uiXml2 = uiAutomatorDump "wails_ui2.xml"
            let invoked =
                uiXml2.Contains("Hello")
                || uiXml2.Contains("你好")

            if invoked then
                pass "IPC 绑定调用正常（greetResult 已渲染 Greet 返回值）"
            else
                fail "IPC 绑定调用未返回预期结果（greetResult 未出现 Hello/你好 前缀）"
                log (sprintf "UI dump 内容:\n%s" uiXml2)

            invoked

let cleanup () =
    info "清理：关闭 Demo 应用"
    adb "shell am force-stop wails.net.demo.android" |> ignore
    adb "shell rm /sdcard/wails_ui.xml /sdcard/wails_ui2.xml" |> ignore

// ----------------------------------------------------------------------------
// 主流程
// ----------------------------------------------------------------------------
let main () =
    Console.WriteLine("==============================================")
    Console.WriteLine("Wails.Net Android E2E 测试")
    Console.WriteLine("==============================================")

    let envCode = checkEnvironment ()
    if envCode <> 0 then envCode
    else
        let allPass =
            let buildOk =
                if noBuild then
                    info "跳过 APK 构建（--no-build）"
                    true
                else
                    buildApk ()

            if not buildOk then
                false
            else
                let apkPath =
                    match customApkPath with
                    | Some p -> p
                    | None ->
                        match findApkPath () with
                        | Some p -> p
                        | None ->
                            fail "未找到 APK 输出文件，请确认构建成功或通过 --apk-path 指定"
                            ""
                if String.IsNullOrEmpty apkPath then
                    false
                else
                    let installOk =
                        if noInstall then
                            info "跳过 APK 安装（--no-install）"
                            true
                        else
                            installApk apkPath

                    if not installOk then
                        false
                    else
                        let startOk = startApp ()
                        let webviewOk = verifyWebView ()
                        startOk && webviewOk

        cleanup ()

        Console.WriteLine("==============================================")
        if allPass then
            Console.WriteLine("结果：全部测试通过 ✓")
            0
        else
            Console.WriteLine("结果：部分测试失败 ✗")
            1

exit (main ())
