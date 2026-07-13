// 检查 Demo DLL 是否包含 GeneratedBindingsRegistration 类型
// 使用 fsx 脚本（按 AGENTS.md 规范，禁止使用 Python）

open System
open System.Reflection
open System.IO

let dllDir = @"f:\Code\Dotnet\Wails.Net\examples\Wails.Net.Demo.Vue\bin\Debug\net10.0-windows10.0.19041.0"

// 注册程序集解析器，从 DLL 目录加载依赖
AppDomain.CurrentDomain.add_AssemblyResolve(
    ResolveEventHandler(fun _ args ->
        let name = args.Name.Split(',').[0]
        let path = Path.Combine(dllDir, name + ".dll")
        if File.Exists(path) then
            try
                Assembly.LoadFrom(path)
            with _ -> null
        else
            null
    )
)

let asmPath = Path.Combine(dllDir, "Wails.Net.Demo.Vue.dll")
printfn "加载程序集: %s" asmPath
let asm = Assembly.LoadFrom(asmPath)

printfn "\n=== 所有类型 ==="
for t in asm.GetTypes() do
    printfn "  %s" t.FullName

printfn "\n=== 查找 GeneratedBindingsRegistration ==="
let generatedType = asm.GetTypes() |> Array.tryFind (fun t -> t.Name = "GeneratedBindingsRegistration")
match generatedType with
| Some t ->
    printfn "找到类型: %s" t.FullName
    printfn "方法列表:"
    for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly) do
        printfn "  - %s" m.Name
| None ->
    printfn "❌ 未找到 GeneratedBindingsRegistration 类型！"

printfn "\n=== 查找 CounterService ==="
let counterType = asm.GetTypes() |> Array.tryFind (fun t -> t.Name = "CounterService")
match counterType with
| Some t ->
    printfn "找到类型: %s (Namespace: %s)" t.Name t.Namespace
    printfn "全名: %s" t.FullName
| None ->
    printfn "❌ 未找到 CounterService 类型！"

printfn "\n=== 调用 ModuleInitializer ==="
match generatedType with
| Some t ->
    let registerMethodOpt = t.GetMethod("Register", BindingFlags.Public ||| BindingFlags.Static)
    match registerMethodOpt with
    | null -> printfn "❌ 未找到 Register 静态方法"
    | m ->
        try
            m.Invoke(null, null) |> ignore
            printfn "✓ ModuleInitializer.Register() 调用成功"
        with ex ->
            printfn "❌ 调用失败: %A" ex.InnerException

printfn "\n=== 验证 GeneratedBindingRegistry 是否已注册 'counter.increment' ==="
let loadedAsmOpt = AppDomain.CurrentDomain.GetAssemblies() |> Seq.tryFind (fun a -> a.GetName().Name = "Wails.Net.Application")
match loadedAsmOpt with
| None -> printfn "❌ Wails.Net.Application 程序集未加载"
| Some a ->
    let t = a.GetType("Wails.Net.Application.Bindings.GeneratedBindingRegistry")
    match t with
    | null -> printfn "❌ 未找到 GeneratedBindingRegistry 类型"
    | t ->
        let tryGetInvoker = t.GetMethod("TryGetInvoker")
        let parameters = [| box "counter.increment"; null |]
        let result = tryGetInvoker.Invoke(null, parameters)
        printfn "TryGetInvoker('counter.increment') = %A, invoker=%A" result (parameters.[1])

        // 检查所有已注册的方法
        let countProp = t.GetProperty("Count")
        match countProp with
        | null -> ()
        | p -> printfn "GeneratedBindingRegistry.Count = %A" (p.GetValue(null))
