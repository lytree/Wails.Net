// sync-runtime.fsx — 同步 @wails-net/runtime 构建产物到所有静态 demo 与模板
// -------------------------------------------------------------------------
// 背景：前端运行时迁往 npm 包 @wails-net/runtime 后，无构建链的静态 demo / 模板
//       通过 ES module 相对路径引用运行时产物（import { wails } from "./wails-runtime/index.js"）。
//       本脚本把 packages/wails-net-runtime/dist/ 下全部 .js 文件（保持 core/api/internal
//       相对结构，跳过 .d.ts / .map）同步到每个目标 frontend/wails-runtime/。
// 用法：dotnet fsi scripts/sync-runtime.fsx
// 注意：每次修改 packages/wails-net-runtime/src 并重新 build 后，重跑本脚本。

open System
open System.IO

let repoRoot =
    Path.GetFullPath(__SOURCE_DIRECTORY__ + "/..")

let sourceDir = Path.Combine(repoRoot, "packages", "wails-net-runtime", "dist")

// 静态 demo（无构建链、前端仅 app.js/index.html/styles.css）+ dotnet new 模板。
// 注意：React/Vue 走 pnpm workspace import，Android 走 WailsBridge，Server 无前端 JS，均不在此列。
let targets =
    [
        "examples/Wails.Net.Demo"
        "examples/Wails.Net.Demo.Binding"
        "examples/Wails.Net.Demo.CancelAsync"
        "examples/Wails.Net.Demo.Clipboard"
        "examples/Wails.Net.Demo.ContextMenus"
        "examples/Wails.Net.Demo.DevRelease"
        "examples/Wails.Net.Demo.Dialogs"
        "examples/Wails.Net.Demo.DragAndDrop"
        "examples/Wails.Net.Demo.Environment"
        "examples/Wails.Net.Demo.Events"
        "examples/Wails.Net.Demo.Frameless"
        "examples/Wails.Net.Demo.Keybindings"
        "examples/Wails.Net.Demo.Menu"
        "examples/Wails.Net.Demo.MultiWindow"
        "examples/Wails.Net.Demo.Notifications"
        "examples/Wails.Net.Demo.Screen"
        "examples/Wails.Net.Demo.Services"
        "examples/Wails.Net.Demo.SingleInstance"
        "examples/Wails.Net.Demo.Store"
        "examples/Wails.Net.Demo.SystemTray"
        "examples/Wails.Net.Demo.Updater"
        "src/Wails.Net.Templates/content/App"
    ]

if not (Directory.Exists sourceDir) then
    failwithf "dist 目录不存在: %s（请先执行 pnpm --filter @wails-net/runtime build）" sourceDir

// 源目录下全部 .js 文件（含子目录，相对路径）
let jsFiles =
    Directory.GetFiles(sourceDir, "*.js", SearchOption.AllDirectories)
    |> Array.map (fun f -> Path.GetRelativePath(sourceDir, f))
    |> Array.sort

if jsFiles.Length = 0 then
    failwith "dist 下未找到 .js 文件"

printfn "源目录: %s" sourceDir
printfn "待同步 .js 文件: %d 个" jsFiles.Length

for rel in targets do
    let destRoot = Path.Combine(repoRoot, rel, "frontend", "wails-runtime")
    if Directory.Exists(Path.Combine(repoRoot, rel, "frontend")) |> not then
        failwithf "目标 frontend 目录不存在: %s/%s" rel "frontend"

    // 1. 清空旧产物（同步语义，避免残留过期文件）
    if Directory.Exists destRoot then
        Directory.Delete(destRoot, true)

    // 2. 拷贝全部 .js
    let mutable copied = 0
    for relPath in jsFiles do
        let src = Path.Combine(sourceDir, relPath)
        let dst = Path.Combine(destRoot, relPath)
        Directory.CreateDirectory(Path.GetDirectoryName(dst)) |> ignore
        File.Copy(src, dst, true)
        copied <- copied + 1

    printfn "  [OK] %-45s -> %d 个文件" (rel + "/frontend/wails-runtime/") copied

printfn ""
printfn "同步完成：%d 个目标，共 %d 个文件。" targets.Length (targets.Length * jsFiles.Length)
