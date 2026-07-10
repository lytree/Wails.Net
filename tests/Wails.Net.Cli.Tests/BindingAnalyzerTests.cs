using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 绑定分析器单元测试。
/// 验证反射分析方法筛选规则：排除服务内部方法、特殊方法、静态方法、Object 继承方法、泛型方法定义。
/// </summary>
[NotInParallel]
public sealed class BindingAnalyzerTests
{
    [Test]
    public async Task AnalyzeType_IncludesPublicInstanceMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(SimpleService));

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).Contains("Greet");
        await Assert.That(names).Contains("Add");
    }

    [Test]
    public async Task AnalyzeType_ExcludesServiceInternalMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithLifecycle));

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).DoesNotContain("ServiceName");
        await Assert.That(names).DoesNotContain("ServiceStartup");
        await Assert.That(names).DoesNotContain("ServiceShutdown");
    }

    [Test]
    public async Task AnalyzeType_ExcludesSpecialNameMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithProperty));

        var names = methods.Select(m => m.MethodName).ToList();
        // get_Name / set_Name 由 IsSpecialName 标记，应被排除
        await Assert.That(names).DoesNotContain("get_Name");
        await Assert.That(names).DoesNotContain("set_Name");
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
    public async Task AnalyzeType_ExcludesStaticMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithStatic));

        var names = methods.Select(m => m.MethodName).ToList();
        await Assert.That(names).DoesNotContain("StaticMethod");
        await Assert.That(names).Contains("InstanceMethod");
    }

    [Test]
    public async Task AnalyzeType_ExcludesGenericMethodDefinitions()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(ServiceWithGenericMethod));

        var names = methods.Select(m => m.MethodName).ToList();
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
    public async Task AnalyzeType_ReturnsEmptyForTypeWithOnlyExcludedMethods()
    {
        var analyzer = new BindingAnalyzer();
        var methods = analyzer.AnalyzeType(typeof(EmptyService));
        await Assert.That(methods).IsEmpty();
    }
}

public sealed class SimpleService
{
    public string Greet(string name) => $"Hello, {name}";
    public int Add(int a, int b) => a + b;
}

public sealed class ServiceWithLifecycle
{
    public string ServiceName() => "lifecycle";
    public void ServiceStartup() { }
    public void ServiceShutdown() { }
    public string DoWork() => "done";
}

public sealed class ServiceWithProperty
{
    public string Name { get; set; } = string.Empty;
    public string GetName() => Name;
}

public sealed class ServiceWithStatic
{
    public static string StaticMethod() => "static";
    public string InstanceMethod() => "instance";
}

public sealed class ServiceWithGenericMethod
{
    public T GenericMethod<T>(T value) => value;
}

public sealed class AsyncService
{
    public async Task<string> FetchAsync()
    {
        await Task.Delay(1);
        return "data";
    }

    public int Compute() => 42;
}

public sealed class ParameterService
{
    public void Process(string name, int count) { }
}

public sealed class CancellationTokenService
{
    public Task Run(string input, CancellationToken token) => Task.CompletedTask;
}

public sealed class VariadicService
{
    public int Sum(params int[] values) => values.Sum();
}

public sealed class EmptyService
{
    // 仅有继承自 Object 的方法，应无绑定方法
}
