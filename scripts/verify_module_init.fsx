// 验证 ModuleInitializer 何时运行
open System
open System.Reflection
open System.IO

let dllDir = @"f:\Code\Dotnet\Wails.Net\examples\Wails.Net.Demo.Vue\bin\Debug\net10.0-windows10.0.19041.0"

AppDomain.CurrentDomain.add_AssemblyResolve(
    ResolveEventHandler(fun _ args ->
        let name = args.Name.Split(',').[0]
        let path = Path.Combine(dllDir, name + ".dll")
        if File.Exists(path) then
            try Assembly.LoadFrom(path) with _ -> null
        else null
    )
)

let appAsm = Assembly.LoadFrom(Path.Combine(dllDir, "Wails.Net.Application.dll"))
let registryType = appAsm.GetType("Wails.Net.Application.Bindings.GeneratedBindingRegistry")
let countProp = registryType.GetProperty("Count")
let getCount () = countProp.GetValue(null)

Console.WriteLine("加载 Wails.Net.Application 后:")
Console.WriteLine("  Count = {0}", getCount())

let demoAsm = Assembly.LoadFrom(Path.Combine(dllDir, "Wails.Net.Demo.Vue.dll"))
Console.WriteLine("加载 Demo 后（仅 LoadFrom）:")
Console.WriteLine("  Count = {0}", getCount())

// 尝试访问 Demo 中的类型，强制运行时初始化
let counterType = demoAsm.GetType("Wails.Net.Demo.Vue.Plugins.CounterService")
Console.WriteLine("调用 GetType('CounterService') 后:")
Console.WriteLine("  Count = {0}", getCount())

// 尝试创建实例，强制 JIT 编译
let counterInstance = Activator.CreateInstance(counterType)
Console.WriteLine("创建 CounterService 实例后:")
Console.WriteLine("  Count = {0}", getCount())

// 验证 counter.increment
let tryGetInvoker = registryType.GetMethod("TryGetInvoker")
let parameters = [| box "counter.increment"; null |]
let found = tryGetInvoker.Invoke(null, parameters) :?> bool
Console.WriteLine("TryGetInvoker('counter.increment') = {0}", found)

// 检查 GeneratedBindingsRegistration 类型，强制 JIT
let generatedType = demoAsm.GetType("Wails.Net.Generated.GeneratedBindingsRegistration")
Console.WriteLine("访问 GeneratedBindingsRegistration 类型后:")
Console.WriteLine("  Count = {0}", getCount())

// 手动调用 Register
let registerMethod = generatedType.GetMethod("Register", BindingFlags.Public ||| BindingFlags.Static)
registerMethod.Invoke(null, null) |> ignore
Console.WriteLine("手动调用 Register() 后:")
Console.WriteLine("  Count = {0}", getCount())
