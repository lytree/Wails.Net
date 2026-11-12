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

/// 抓取应用相关错误日志（崩溃堆栈、未捕获异常），供失败时排障。
let dumpErrorLogs (title: string) =
    let logs = adb "logcat -d -s AndroidRuntime:E DOTNET:E mono-rt:E monodroid:E libc:F *:S"
    if not (String.IsNullOrWhiteSpace logs) then
        log (sprintf "%s:\n%s" title (logs.Trim()))

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

    let apkPath = (adb (sprintf "shell pm path %s" appPackage)).Trim()
    log (sprintf "pm path: %s" apkPath)

    let resolved =
        adb (sprintf "shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p %s" appPackage)
    log (sprintf "resolve-activity: %s" (resolved.Trim()))

    // 列出设备上全部 LAUNCHER 活动，确认本包是否出现在其中——
    // 若这里都没有本包，说明 APK 清单里根本没有 MAIN/LAUNCHER intent-filter。
    let allLaunchers =
        adb "shell cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER"
    log (sprintf "全部 LAUNCHER 活动:\n%s" (allLaunchers.Trim()))

    let dump = adb (sprintf "shell dumpsys package %s" appPackage)
    let interesting =
        dump.Split('\n')
        |> Array.filter (fun l ->
            l.Contains("Activity") || l.Contains("MAIN") || l.Contains("LAUNCHER") || l.Contains("android.intent"))
        |> Array.truncate 40
        |> String.concat "\n"
    log (sprintf "dumpsys package（Activity 相关）:\n%s" interesting)

/// 应用是否在运行：`pidof` 有输出即存活。
/// 这是唯一可靠的"启动成功"判据。既不能用退出码也不能用输出文本判定——
/// 实测 `am start` 解析不到 Intent 时只在 stdout 打印
/// `Error: Activity not started, unable to resolve Intent ...`，退出码仍是 0。
/// 旧实现按退出码判定，于是把"根本没解析到组件"误判为启动成功，
/// `||` 又短路掉 monkey 兜底，最终表现为"启动成功但 pidof 为空"。
let appPid () = (adb (sprintf "shell pidof %s" appPackage)).Trim()

/// 轮询等待进程出现（冷启动 + JIT 预热通常 1~3 秒）。
let waitForPid (timeoutMs: int) =
    let deadline = DateTime.UtcNow.AddMilliseconds(float timeoutMs)
    let mutable pid = appPid ()
    while pid = "" && DateTime.UtcNow < deadline do
        Thread.Sleep 500
        pid <- appPid ()
    pid

/// 用 LAUNCHER intent 启动应用（不指定组件）。
/// 备注：在 API 34 模拟器上该形式实测返回
/// `Error: Activity not started, unable to resolve Intent { ... pkg=<pkg> }`，
/// 而 APK 清单内有 MAIN/LAUNCHER 且 exported=true（已核验），故属于 `-p` 包名
/// 限定解析的行为差异，不是清单问题。保留作为兜底尝试。
let tryLaunchWithIntent () =
    let code, out, err =
        runProcess "adb"
            (sprintf "shell am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p %s" appPackage)
            60000
    log (sprintf "am start exit=%d 输出=%s 错误=%s" code (out.Trim()) (err.Trim()))
    let unresolved =
        out.Contains("unable to resolve") || err.Contains("unable to resolve")
    code = 0 && not unresolved

/// 向设备查询本包真实的 LAUNCHER 组件名，例如
/// `wails.net.demo.android/crc64c664fe95ef5e8873.MainActivity`。
/// 不能硬编码：该 Java 类名是 .NET Android 由 C# 类型名算出的 crc64 前缀，
/// 换类名/命名空间就会变。
let resolveLauncherComponent () =
    let attempts = [
        sprintf "shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p %s" appPackage
        sprintf "shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER %s" appPackage
    ]
    attempts
    |> List.tryPick (fun args ->
        let out = adb args
        out.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.tryFind (fun l -> l.StartsWith(appPackage + "/") && not (l.Contains(" "))))

/// 按解析出的组件名精确启动（最贴近"点桌面图标"的行为）。
let tryLaunchWithComponent () =
    match resolveLauncherComponent () with
    | None ->
        log "未能解析出 LAUNCHER 组件名，跳过 -n 启动"
        false
    | Some launcherComponent ->
        log (sprintf "解析到 LAUNCHER 组件: %s" launcherComponent)
        let code, out, err =
            runProcess "adb" (sprintf "shell am start -W -n %s" launcherComponent) 60000
        log (sprintf "am start -n exit=%d 输出=%s 错误=%s" code (out.Trim()) (err.Trim()))
        let unresolved =
            out.Contains("unable to resolve") || err.Contains("unable to resolve")
        code = 0 && not unresolved

/// 兜底：am start 解析不到组件时，monkey 由 PackageManager 自行解析 LAUNCHER 活动。
let tryLaunchWithMonkey () =
    let code, out, err =
        runProcess "adb" (sprintf "shell monkey -p %s -c android.intent.category.LAUNCHER 1" appPackage) 60000
    log (sprintf "monkey exit=%d 输出=%s 错误=%s" code (out.Trim()) (err.Trim()))
    code = 0 && out.Contains("Events injected")

let launchApp () =
    let existing = appPid ()
    if existing <> "" then
        pass (sprintf "应用已在运行（pid=%s），无需重复启动" existing)
        true
    else
        // 顺序按可靠性排列：monkey 由系统解析活动，对 `-p`/组件名差异最不敏感；
        // 其次是按解析出的组件名精确启动；最后才是不指定组件的 LAUNCHER intent。
        let attempts = [
            ("monkey（LAUNCHER 分类）", tryLaunchWithMonkey)
            ("am start -n <解析组件>", tryLaunchWithComponent)
            ("am start（LAUNCHER intent）", tryLaunchWithIntent)
        ]
        let mutable started = false
        for (name, launch) in attempts do
            if not started then
                launch () |> ignore
                let pid = waitForPid 8000
                if pid <> "" then
                    pass (sprintf "应用已启动（%s，pid=%s）" name pid)
                    started <- true
        if not started then
            fail "应用启动失败（monkey 与两种 am start 均未能拉起进程）"
            dumpLaunchDiagnostics ()
            dumpErrorLogs "错误日志"
        started

let startApp () =
    info "步骤 3/4：启动应用并验证 Activity 进入前台"
    adb "logcat -c" |> ignore

    if not (launchApp ()) then
        false
    else
        // 等待 WebView 初始化
        Thread.Sleep(8000)

        // 进程存活已在 launchApp 里确认过，这里复查一次：能覆盖"拉起后立刻崩溃"的情况
        let pid = appPid ()
        if String.IsNullOrWhiteSpace pid then
            fail (sprintf "应用进程在启动后消失（pidof %s 为空），可能启动时崩溃" appPackage)
            dumpErrorLogs "错误日志"
            false
        else
            pass (sprintf "应用进程存活（pid=%s）" pid)

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

            inForeground

let uiAutomatorDump (name: string) =
    // 先删旧文件：uiautomator 对已存在的目标文件行为不一致（部分版本报错不写入），
    // 且删除后能保证"命令失败 → cat 读不到 → 判定为未命中"，避免读到上一轮的陈旧内容。
    adb (sprintf "shell rm -f /sdcard/%s" name) |> ignore
    // dump 可能较慢（模拟器负载高时实测出现过 uiautomator 被 SIGKILL、exit=137），
    // 用更长的超时，失败由调用方的重试覆盖。
    log (sprintf "adb shell uiautomator dump /sdcard/%s" name)
    let code, _, err = runProcess "adb" (sprintf "shell uiautomator dump /sdcard/%s" name) 60000
    if code <> 0 then
        log (sprintf "uiautomator dump exit=%d stderr=%s" code err)
    adb (sprintf "shell cat /sdcard/%s" name)

/// 轮询 uiautomator dump 直到命中判定条件。
/// WebView 内部的 DOM 文本是**延迟可见**的：Chromium 的无障碍树在首次被查询后才
/// 开始构建，第一次 dump 通常只给出一个空的 android.webkit.WebView 节点，
/// 几秒后重试才能读到页面文字。一次定论会把"加载慢"误判为"渲染失败"。
let uiAutomatorDumpUntil (name: string) (matched: string -> bool) (attempts: int) =
    let mutable xml = ""
    let mutable hit = false
    let mutable i = 0
    while not hit && i < attempts do
        if i > 0 then Thread.Sleep 2000
        xml <- uiAutomatorDump name
        hit <- matched xml
        i <- i + 1
        if not hit then log (sprintf "第 %d 次 dump 未命中目标文本，%s" i (if i < attempts then "2s 后重试" else "放弃"))
    xml, hit

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
    // 1. 通过 uiautomator dump 验证 WebView 渲染（轮询等待无障碍树构建完成）
    //    特征只取只存在于页面 DOM 里的文字。"Wails.Net Demo" 不能用：它同时是
    //    Activity 的 ActionBar 标题，WebView 尚未暴露内容时也会命中，造成
    //    "渲染正常"的假阳性，随后又因找不到按钮而失败（实测踩过）。
    let pageSignatures = [ "问候服务"; "打招呼" ]
    let uiXml, pageVisible =
        uiAutomatorDumpUntil "wails_ui.xml" (fun xml -> pageSignatures |> List.exists xml.Contains) 12

    if not pageVisible then
        fail "WebView 渲染失败（uiautomator dump 未检测到 Wails.Net Demo 页面元素）"
        // 区分两种失败：WebView 压根没挂上视图层级 vs 挂上了但无障碍树没暴露 DOM。
        // 后者属于测试手段的限制，前者才是产品问题，日志里需要能分辨。
        if uiXml.Contains("android.webkit.WebView") then
            log "UI 树里存在 WebView 节点，但读不到 DOM 文本（无障碍树未暴露页面内容）"
        else
            log "UI 树里连 WebView 节点都没有，视图层级可能未附加"
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

            // IPC 往返 + 渲染需要时间，同样用轮询判定，避免把"慢"判成"失败"
            let uiXml2, invoked =
                uiAutomatorDumpUntil "wails_ui2.xml"
                    (fun xml -> xml.Contains("Hello") || xml.Contains("你好")) 5

            if invoked then
                pass "IPC 绑定调用正常（greetResult 已渲染 Greet 返回值）"
            else
                fail "IPC 绑定调用未返回预期结果（greetResult 未出现 Hello/你好 前缀）"
                log (sprintf "UI dump 内容:\n%s" uiXml2)

            invoked

let cleanup () =
    info "清理：关闭 Demo 应用"
    adb "shell am force-stop wails.net.demo.android" |> ignore
    // -f：文件可能不存在（步骤 4 未执行到），不加会输出 "No such file or directory"
    adb "shell rm -f /sdcard/wails_ui.xml /sdcard/wails_ui2.xml" |> ignore

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
                        // 启动失败时继续跑 WebView 校验只会被前台 UI（桌面启动器）
                        // 误导为"页面元素缺失"，掩盖真正原因，故直接返回。
                        let startOk = startApp ()
                        if not startOk then
                            false
                        else
                            verifyWebView ()

        cleanup ()

        Console.WriteLine("==============================================")
        if allPass then
            Console.WriteLine("结果：全部测试通过 ✓")
            0
        else
            Console.WriteLine("结果：部分测试失败 ✗")
            1

exit (main ())
