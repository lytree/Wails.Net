#!/usr/bin/env dotnet fsi
// ============================================================================
// Wails.Net Android E2E 测试脚本（设备端插桩测试替代方案）
// ----------------------------------------------------------------------------
// 遵循 AGENTS.md §7.5：使用 F# (.fsx) 脚本，通过 `dotnet fsi run-android-e2e.fsx` 运行。
// 避开 §4.1 测试框架约束：本脚本不属于"测试框架"使用，而是通过 adb 驱动 UI 自动化。
//
// 测试目标（端到端验证 Wails.Net Android 平台）：
//   1. Demo APK 构建并安装到设备/模拟器
//   2. MainActivity 启动并触发 Wails.Net 平台事件（Android.ActivityCreated）
//   3. WebView 加载本地资源（uiautomator dump 验证 "Wails.Net Demo" 文字出现）
//   4. IPC 绑定调用（点击"打招呼"按钮，验证 greetResult 出现 "Hello," 前缀）
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

let startApp () =
    info "步骤 3/4：启动 MainActivity 并验证平台事件"
    // 清空 logcat 缓冲
    adb "logcat -c" |> ignore
    // 启动 MainActivity
    let _ = adb "shell am start -n wails.net.demo.android/wails.net.demo.android.MainActivity"
    // 等待应用启动（WebView 加载需要时间）
    Thread.Sleep(8000)
    // 抓取 Wails.Net 相关日志
    let logcat = adb "logcat -d Wails.Net.Application.Android:V Wails.Net.Application:V *:S"
    log (sprintf "logcat 输出:\n%s" logcat)

    // 验证 1：Android.ActivityCreated 事件（OnCreate → 平台事件转发）
    let test1 = logcat.Contains("ActivityCreated") || logcat.Contains("ActivityStarted") || logcat.Contains("ActivityResumed")
    if test1 then
        pass "平台事件转发正常（Android.ActivityCreated/Started/Resumed 至少一个已触发）"
    else
        fail "未检测到平台事件（Android.ActivityCreated/Started/Resumed 均未触发）"

    test1

let verifyWebView () =
    info "步骤 4/4：验证 WebView 加载与 IPC 绑定"
    // 1. 通过 uiautomator dump 验证 WebView 渲染
    let _ = adb "shell uiautomator dump /sdcard/wails_ui.xml"
    let uiXml = adb "shell cat /sdcard/wails_ui.xml"

    let test1 =
        uiXml.Contains("Wails.Net Demo")  // HTML <title>
        || uiXml.Contains("问候服务")      // 卡片标题
        || uiXml.Contains("打招呼")        // 按钮文字
    if test1 then
        pass "WebView 渲染正常（检测到 Wails.Net Demo 页面元素）"
    else
        fail "WebView 渲染失败（uiautomator dump 未检测到 Wails.Net Demo 页面元素）"

    // 2. 通过 input tap 模拟点击"打招呼"按钮，验证 IPC 绑定调用
    //    按钮位置：第一张卡片内的按钮，估算位于屏幕中上部 (540, ~480)
    //    注：实际坐标依赖屏幕分辨率，可通过 --verbose 查看原始 uiXml 调整
    adb "shell input tap 540 480" |> ignore
    Thread.Sleep(2000)

    let _ = adb "shell uiautomator dump /sdcard/wails_ui2.xml"
    let uiXml2 = adb "shell cat /sdcard/wails_ui2.xml"

    // IPC 调用成功后，greetResult 应显示 "Hello, ..." 文字
    let test2 =
        uiXml2.Contains("Hello,")
        || uiXml2.Contains("Hello World")
        || uiXml2.Contains("你好")
    if test2 then
        pass "IPC 绑定调用正常（GreetingService.Greet 返回结果已渲染）"
    else
        fail "IPC 绑定调用未返回预期结果（greetResult 未出现 Hello/你好 前缀）"
        log (sprintf "UI dump 内容:\n%s" uiXml2)

    test1 && test2

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
