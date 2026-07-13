// Common.fsx — 共享函数库
// 对应 AGENTS.md §7.5：所有脚本任务必须使用 F# (.fsx)，严禁使用 Python
//
// 提供构建脚本共享的工具函数：参数解析、版本读取、日志、命令执行、
// 目录复制、zip/tar.gz 压缩等。仅依赖 .NET 标准库，不引入外部 NuGet 包。

#r "System.Xml.Linq"
#r "System.IO.Compression"
#r "System.IO.Compression.ZipFile"
#r "System.Formats.Tar"

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Formats.Tar
open System.Xml.Linq

// ===== 全局可变状态 =====

/// Dry-run 模式：仅打印将要执行的命令，不实际执行。
/// 各脚本在解析 --dry-run 参数后设置此标志。
let mutable DryRun = false

// ===== 日志函数 =====

let private timestamp () = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")

/// 打印信息日志（带时间戳，青色）
let logInfo (msg: string) =
    let prev = Console.ForegroundColor
    Console.ForegroundColor <- ConsoleColor.Cyan
    Console.WriteLine($"[{timestamp()}] [INFO]  {msg}")
    Console.ForegroundColor <- prev

/// 打印警告日志（带时间戳，黄色）
let logWarn (msg: string) =
    let prev = Console.ForegroundColor
    Console.ForegroundColor <- ConsoleColor.Yellow
    Console.WriteLine($"[{timestamp()}] [WARN]  {msg}")
    Console.ForegroundColor <- prev

/// 打印错误日志到 stderr（带时间戳，红色）
let logError (msg: string) =
    let prev = Console.ForegroundColor
    Console.ForegroundColor <- ConsoleColor.Red
    Console.Error.WriteLine($"[{timestamp()}] [ERROR] {msg}")
    Console.ForegroundColor <- prev

/// 打印成功日志（带时间戳，绿色）
let logSuccess (msg: string) =
    let prev = Console.ForegroundColor
    Console.ForegroundColor <- ConsoleColor.Green
    Console.WriteLine($"[{timestamp()}] [OK]    {msg}")
    Console.ForegroundColor <- prev

// ===== 路径工具 =====

/// 获取脚本所在目录（scripts/build/）
let scriptDirectory () : string = __SOURCE_DIRECTORY__

/// 获取项目根目录（脚本目录的上两级）
let projectRoot () : string =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

// ===== 参数解析 =====

/// 解析命令行参数为 Map<string, string>。
/// 支持两种格式：
///   --key value     → Map["key", "value"]
///   --key=value     → Map["key", "value"]
///   --key           → Map["key", "true"]（无值参数）
/// 重复键以最后出现为准。
let parseArgs (argv: string[]) : Map<string, string> =
    let rec parse (args: string list) acc =
        match args with
        | [] -> acc
        | key :: rest when key.StartsWith("--") ->
            let keyPart = key.TrimStart('-')
            // --key=value 形式
            let eqIdx = keyPart.IndexOf('=')
            if eqIdx >= 0 then
                let k = keyPart.Substring(0, eqIdx)
                let v = keyPart.Substring(eqIdx + 1)
                parse rest (Map.add k v acc)
            else
                // --key value 形式（若下一个不是 -- 开头则视为值）
                match rest with
                | value :: rest' when not (value.StartsWith("--")) ->
                    parse rest' (Map.add keyPart value acc)
                | _ ->
                    parse rest (Map.add keyPart "true" acc)
        | _ :: rest -> parse rest acc
    parse (Array.toList argv) Map.empty

/// 从参数 Map 中获取指定键的值，不存在则返回默认值。
let argOr (key: string) (defaultValue: string) (args: Map<string, string>) : string =
    match args.TryFind key with
    | Some v -> v
    | None -> defaultValue

/// 检查参数 Map 中是否包含指定键（布尔标志）。
let hasFlag (key: string) (args: Map<string, string>) : bool =
    args.TryFind key |> Option.exists (fun v -> v = "true" || v = "1" || v = "yes")

// ===== 版本号读取 =====

/// 从指定的 Directory.Build.props 文件读取 WailsNetVersion 属性。
let getVersionFrom (propsPath: string) : string =
    if not (File.Exists(propsPath)) then
        failwith $"Directory.Build.props 不存在: {propsPath}"
    let doc = XDocument.Load(propsPath)
    let root = doc.Root
    if isNull root then
        failwith "Directory.Build.props 为空"
    let ns = root.GetDefaultNamespace()
    let propsGroup = root.Element(XName.Get("PropertyGroup", ns.NamespaceName))
    if isNull propsGroup then
        failwith "Directory.Build.props 中未找到 <PropertyGroup>"
    let versionElem = propsGroup.Element(XName.Get("WailsNetVersion", ns.NamespaceName))
    if isNull versionElem then
        failwith "Directory.Build.props 中未找到 <WailsNetVersion> 属性"
    versionElem.Value.Trim()

/// 从项目根目录的 Directory.Build.props 读取 WailsNetVersion 属性。
let getVersion () : string =
    getVersionFrom (Path.Combine(projectRoot (), "Directory.Build.props"))

// ===== 文件系统工具 =====

/// 确保目录存在，不存在则创建（含父目录）。
let ensureDirectory (path: string) : unit =
    if not (Directory.Exists(path)) then
        Directory.CreateDirectory(path) |> ignore

/// 递归复制目录。目标目录不存在则创建。
let rec copyDirectory (src: string) (dst: string) : unit =
    if not (Directory.Exists(src)) then
        failwith $"源目录不存在: {src}"
    ensureDirectory dst
    for file in Directory.GetFiles(src) do
        let name = Path.GetFileName(file)
        File.Copy(file, Path.Combine(dst, name), overwrite = true)
    for sub in Directory.GetDirectories(src) do
        let name = Path.GetFileName(sub)
        copyDirectory sub (Path.Combine(dst, name))

// ===== 命令执行 =====

/// 检查命令是否在 PATH 中可用。
/// Windows 上检查 .exe/.cmd/.bat 扩展名。
let commandExists (cmd: string) : bool =
    if DryRun then true
    else
        let extensions =
            if OperatingSystem.IsWindows() then [".exe"; ".cmd"; ".bat"; ""] else [""]
        let pathVar =
            match Environment.GetEnvironmentVariable("PATH") with
            | null -> ""
            | s -> s
        let separators = if OperatingSystem.IsWindows() then ';' else ':'
        let pathDirs = pathVar.Split(separators, StringSplitOptions.RemoveEmptyEntries)
        pathDirs
        |> Array.exists (fun dir ->
            extensions |> List.exists (fun ext ->
                let fullPath = Path.Combine(dir, cmd + ext)
                File.Exists(fullPath)))

/// 运行命令并等待完成，返回退出码。
/// 输出直接继承到当前控制台（实时显示）。
/// DryRun 模式下仅打印命令不执行，返回 0。
let runCommand (cmd: string) (args: string[]) : int =
    let argStr =
        String.Join(" ",
            args |> Array.map (fun a ->
                if a.Contains(" ") || a.Contains("\"") then
                    "\"" + a.Replace("\"", "\\\"") + "\""
                else a))
    if DryRun then
        logInfo $"[DRY-RUN] {cmd} {argStr}"
        0
    else
        logInfo $"$ {cmd} {argStr}"
        use proc = new Process()
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.FileName <- cmd
        for a in args do
            proc.StartInfo.ArgumentList.Add(a)
        proc.Start() |> ignore
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            logError $"命令失败（退出码 {proc.ExitCode}）: {cmd} {argStr}"
        proc.ExitCode

/// 运行命令并捕获 stdout（单行或少量输出场景）。
/// 返回 Some(输出字符串) 若成功，None 若失败。
/// DryRun 模式下返回 None。
let runCommandCapture (cmd: string) (args: string[]) : string option =
    let argStr = String.Join(" ", args)
    if DryRun then
        logInfo $"[DRY-RUN] {cmd} {argStr}"
        None
    else
        logInfo $"$ {cmd} {argStr}"
        use proc = new Process()
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.FileName <- cmd
        proc.StartInfo.RedirectStandardOutput <- true
        proc.StartInfo.RedirectStandardError <- true
        for a in args do
            proc.StartInfo.ArgumentList.Add(a)
        proc.Start() |> ignore
        let stdout = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode = 0 then Some(stdout.Trim())
        else None

// ===== 压缩工具 =====

/// 将目录压缩为 zip 文件。
/// src 为源目录，dst 为目标 .zip 文件路径。
let compressZip (src: string) (dst: string) : unit =
    if DryRun then
        logInfo $"[DRY-RUN] 创建 zip: {dst} (源: {src})"
    else
        let parent = Path.GetDirectoryName(dst)
        if not (isNull parent) && parent <> "" then ensureDirectory parent
        if File.Exists(dst) then File.Delete(dst)
        ZipFile.CreateFromDirectory(src, dst, CompressionLevel.Optimal, includeBaseDirectory = false)
        logSuccess $"已创建 zip: {dst}"

/// 将目录压缩为 tar.gz 文件。
/// 先用 TarFile.CreateFromDirectory 生成 .tar，再用 GZipStream 压缩为 .tar.gz。
/// srcDir 为源目录，dstFile 为目标 .tar.gz 文件路径。
let compressTarGz (srcDir: string) (dstFile: string) : unit =
    if DryRun then
        logInfo $"[DRY-RUN] 创建 tar.gz: {dstFile} (源: {srcDir})"
    else
        let parent = Path.GetDirectoryName(dstFile)
        if not (isNull parent) && parent <> "" then ensureDirectory parent
        if File.Exists(dstFile) then File.Delete(dstFile)
        // 先创建临时 .tar 文件，再 gzip 压缩
        let tempTar = Path.GetTempFileName()
        try
            File.Delete(tempTar) // GetTempFileName 会创建空文件，需先删除
            TarFile.CreateFromDirectory(srcDir, tempTar, includeBaseDirectory = false)
            use srcStream = File.OpenRead(tempTar)
            use dstStream = File.Create(dstFile)
            use gzip = new GZipStream(dstStream, CompressionLevel.Optimal)
            srcStream.CopyTo(gzip)
        finally
            if File.Exists(tempTar) then File.Delete(tempTar)
        logSuccess $"已创建 tar.gz: {dstFile}"

// ===== 平台检测 =====

/// 当前操作系统是否为 Windows
let isWindows () : bool = OperatingSystem.IsWindows()

/// 当前操作系统是否为 Linux
let isLinux () : bool = OperatingSystem.IsLinux()
