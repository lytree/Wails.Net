using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 绑定分析器单元测试。
/// 验证源代码生成器填充的元数据是否正确驱动 BindingAnalyzer：
/// - 仅标记 [Binding] 的方法出现在元数据中
/// - 静态方法、泛型方法被源生成器过滤
/// - 异步、CancellationToken、可变参数正确识别
/// - FNV-1a ID 与运行时 BindingManager 一致
/// </summary>
[NotInParallel]
public sealed class BindingAnalyzerTests
{
    [Test]
    public async Task AnalyzeType_IncludesMarkedMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).Contains("Greet");
        await Assert.That(names).Contains("Add");
    }

    [Test]
    public async Task AnalyzeType_ExcludesMethodsWithoutBindingAttribute()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithLifecycle));

        var names = methods.Select(m => m.MethodName).ToList();
        // 未标记 [Binding] 的方法（包括服务内部方法）不会出现在元数据中
        await Assert.That(names).DoesNotContain("ServiceName");
        await Assert.That(names).DoesNotContain("ServiceStartup");
        await Assert.That(names).DoesNotContain("ServiceShutdown");
        // DoWork 标记了 [Binding]，应被包含
        await Assert.That(names).Contains("DoWork");
    }

    [Test]
    public async Task AnalyzeType_ExcludesPropertyAccessorsWithoutBindingAttribute()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithProperty));

        var names = methods.Select(m => m.MethodName).ToList();
        // 属性 get/set 访问器未标记 [Binding]，不在元数据中
        await Assert.That(names).DoesNotContain("get_Name");
        await Assert.That(names).DoesNotContain("set_Name");
        // 显式标记 [Binding] 的 GetName 方法应被包含
        await Assert.That(names).Contains("GetName");
    }

    [Test]
    public async Task AnalyzeType_ExcludesObjectInheritedMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).DoesNotContain("ToString");
        await Assert.That(names).DoesNotContain("Equals");
        await Assert.That(names).DoesNotContain("GetHashCode");
        await Assert.That(names).DoesNotContain("GetType");
    }

    [Test]
    public async Task AnalyzeType_ExcludesStaticMethodsWithBindingAttribute()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithStatic));

        var names = methods.Select(m => m.MethodName).ToList();
        // 即使标记了 [Binding]，静态方法也会被源生成器过滤
        await Assert.That(names).DoesNotContain("StaticMethod");
        await Assert.That(names).Contains("InstanceMethod");
    }

    [Test]
    public async Task AnalyzeType_ExcludesGenericMethodsWithBindingAttribute()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithGenericMethod));

        var names = methods.Select(m => m.MethodName).ToList();
        // 即使标记了 [Binding]，泛型方法定义也会被源生成器过滤
        await Assert.That(names).DoesNotContain("GenericMethod");
    }

    [Test]
    public async Task AnalyzeType_PopulatesFullNameCorrectly()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));
        var greet = methods.FirstOrDefault(m => m.MethodName == "Greet");

        await Assert.That(greet).IsNotNull();
        await Assert.That(greet!.FullName).IsEqualTo($"{typeof(SimpleService).FullName}.Greet");
    }

    [Test]
    public async Task AnalyzeType_PopulatesNamespaceAndClassName()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));
        var greet = methods.First(m => m.MethodName == "Greet");

        await Assert.That(greet.Namespace).IsEqualTo(typeof(SimpleService).Namespace ?? string.Empty);
        await Assert.That(greet.ClassName).IsEqualTo(nameof(SimpleService));
    }

    [Test]
    public async Task AnalyzeType_GeneratesIdMatchingBindingIdGenerator()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));
        var greet = methods.First(m => m.MethodName == "Greet");

        var expected = BindingIdGenerator.Generate(greet.FullName);
        await Assert.That(greet.ID).IsEqualTo(expected);
    }

    [Test]
    public async Task AnalyzeType_DetectsAsyncMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(AsyncService));

        var fetch = methods.First(m => m.MethodName == "FetchAsync");
        var plain = methods.First(m => m.MethodName == "Compute");

        await Assert.That(fetch.IsAsync).IsTrue();
        await Assert.That(plain.IsAsync).IsFalse();
    }

    [Test]
    public async Task AnalyzeType_MapsParameterTypes()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ParameterService));
        var method = methods.First(m => m.MethodName == "Process");

        await Assert.That(method.Parameters.Count()).IsEqualTo(2);
        await Assert.That(method.Parameters[0].Name).IsEqualTo("name");
        await Assert.That(method.Parameters[0].TypeName).IsEqualTo("string");
        await Assert.That(method.Parameters[1].Name).IsEqualTo("count");
        await Assert.That(method.Parameters[1].TypeName).IsEqualTo("number");
    }

    [Test]
    public async Task AnalyzeType_MarksCancellationTokenParameters()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(CancellationTokenService));
        var method = methods.First(m => m.MethodName == "Run");

        await Assert.That(method.Parameters.Count()).IsEqualTo(2);
        await Assert.That(method.Parameters[0].IsCancellationToken).IsFalse();
        await Assert.That(method.Parameters[1].IsCancellationToken).IsTrue();
    }

    [Test]
    public async Task AnalyzeType_MarksVariadicParameters()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(VariadicService));
        var method = methods.First(m => m.MethodName == "Sum");

        await Assert.That(method.Parameters.Count()).IsEqualTo(1);
        await Assert.That(method.Parameters[0].IsVariadic).IsTrue();
    }

    [Test]
    public async Task AnalyzeInstance_ReturnsMethodsForInstanceType()
    {
        var analyzer = new BindingAnalyzer();
        var instance = new SimpleService();
        var methods = analyzer.AnalyzeInstance(instance);

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).Contains("Greet");
        await Assert.That(names).Contains("Add");
    }

    [Test]
    public async Task AnalyzeAssembly_ReturnsMethodsAcrossTypes()
    {
        var analyzer = new BindingAnalyzer();
        var assembly = typeof(BindingAnalyzerTests).Assembly;
        var methods = analyzer.AnalyzeAssembly(assembly);

        // 测试程序集中至少应包含本测试 fixture 引用的服务类型方法
        await Assert.That(methods.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task AnalyzeType_ReturnsEmptyForTypeWithoutMarkedMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(EmptyService));
        await Assert.That(methods).IsEmpty();
    }
}

public sealed class SimpleService
{
    [Binding]
    public string Greet(string name) => $"Hello, {name}";

    [Binding]
    public int Add(int a, int b) => a + b;
}

public sealed class ServiceWithLifecycle
{
    // 未标记 [Binding]：不在元数据中
    public string ServiceName() => "lifecycle";
    public void ServiceStartup() { }
    public void ServiceShutdown() { }

    [Binding]
    public string DoWork() => "done";
}

public sealed class ServiceWithProperty
{
    public string Name { get; set; } = string.Empty;

    [Binding]
    public string GetName() => Name;
}

public sealed class ServiceWithStatic
{
    // 静态方法即使标记 [Binding] 也会被源生成器过滤
    [Binding]
    public static string StaticMethod() => "static";

    [Binding]
    public string InstanceMethod() => "instance";
}

public sealed class ServiceWithGenericMethod
{
    // 泛型方法即使标记 [Binding] 也会被源生成器过滤
    [Binding]
    public T GenericMethod<T>(T value) => value;
}

public sealed class AsyncService
{
    [Binding]
    public async Task<string> FetchAsync()
    {
        await Task.Delay(1);
        return "data";
    }

    [Binding]
    public int Compute() => 42;
}

public sealed class ParameterService
{
    [Binding]
    public void Process(string name, int count) { }
}

public sealed class CancellationTokenService
{
    [Binding]
    public Task Run(string input, CancellationToken token) => Task.CompletedTask;
}

public sealed class VariadicService
{
    [Binding]
    public int Sum(params int[] values) => values.Sum();
}

public sealed class EmptyService
{
    // 无任何 [Binding] 方法
}
