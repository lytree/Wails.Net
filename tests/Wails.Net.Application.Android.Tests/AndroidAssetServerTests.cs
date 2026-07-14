using System.Reflection;
using TUnit.Core;
using Wails.Net.Application.Platform;
using Wails.Net.AssetServer;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// AndroidAssetServer 的单元测试（TUnit）。
/// 测试非 Android 环境下（AssetManager 为 null）的降级行为。
/// 注意：<c>ReadAssetCore</c> 为 protected override 方法，
/// 测试通过反射调用以验证降级行为；同时通过公共 <c>ServeAsync</c> 验证端到端行为。
/// </summary>
[NotInParallel]
public sealed class AndroidAssetServerTests
{
    /// <summary>
    /// 通过反射调用 protected 的 ReadAssetCore 方法。
    /// </summary>
    /// <param name="server">AndroidAssetServer 实例。</param>
    /// <param name="path">资源路径。</param>
    /// <returns>资源内容字节数组，或 null。</returns>
    private static byte[]? InvokeReadAssetCore(AndroidAssetServer server, string path)
    {
        var method = typeof(global::Wails.Net.AssetServer.AssetServer).GetMethod(
            "ReadAssetCore",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[]?)method?.Invoke(server, new object[] { path });
    }

    /// <summary>
    /// 通过反射读取私有 _assetPrefix 字段。
    /// </summary>
    /// <param name="server">AndroidAssetServer 实例。</param>
    /// <returns>_assetPrefix 字段值。</returns>
    private static string GetAssetPrefix(AndroidAssetServer server)
    {
        var field = typeof(AndroidAssetServer).GetField(
            "_assetPrefix",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (string?)field?.GetValue(server) ?? string.Empty;
    }

    [Test]
    public async Task Constructor_DoesNotThrow_WhenNullAssetManagerProvided()
    {
        // 操作与断言：null AssetManager 应被允许（非 Android 环境降级）
        var server = new AndroidAssetServer(null, "assets");
        await Assert.That(server).IsNotNull();
    }

    [Test]
    public async Task Constructor_DoesNotThrow_WhenEmptyAssetPrefixProvided()
    {
        // 操作与断言：空 assetPrefix 应被允许
        var server = new AndroidAssetServer(null, "");
        await Assert.That(server).IsNotNull();
    }

    [Test]
    public async Task Constructor_ConvertsNullAssetPrefixToEmptyString()
    {
        // 安排与操作：传入 null assetPrefix
        var server = new AndroidAssetServer(null, null!);

        // 断言：_assetPrefix 应被转为空字符串
        var prefix = GetAssetPrefix(server);
        await Assert.That(prefix).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadAssetCore_ReturnsNull_WhenAssetManagerIsNull()
    {
        // 安排：null AssetManager
        var server = new AndroidAssetServer(null, "assets");

        // 操作
        var result = InvokeReadAssetCore(server, "index.html");

        // 断言：null AssetManager 时返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAssetCore_ReturnsNull_WhenEmptyPathProvided()
    {
        // 安排
        var server = new AndroidAssetServer(null, "assets");

        // 操作
        var result = InvokeReadAssetCore(server, "");

        // 断言：空路径时返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAssetCore_ReturnsNull_WhenNullPathProvided()
    {
        // 安排
        var server = new AndroidAssetServer(null, "assets");

        // 操作
        var result = InvokeReadAssetCore(server, null!);

        // 断言：null 路径时返回 null（_assetManager 为 null 提前返回）
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ServeAsync_ReturnsEmptyArray_WhenAssetManagerIsNull()
    {
        // 安排：null AssetManager，有效路径
        var server = new AndroidAssetServer(null, "assets");

        // 操作：通过公共 API 验证降级行为（ReadAssetCore 返回 null，ServeAsync 返回空数组）
        var result = await server.ServeAsync("index.html");

        // 断言：返回空数组（非 null）
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ServeAsync_ReturnsEmptyArray_WhenAssetNotFound()
    {
        // 安排
        var server = new AndroidAssetServer(null, "assets");

        // 操作：请求一个不存在的资源
        var result = await server.ServeAsync("nonexistent.txt");

        // 断言：返回空数组
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Length).IsEqualTo(0);
    }

    [Test]
    public async Task GetMimeType_ReturnsDefaultContentType_ForUnknownExtension()
    {
        // 安排
        var server = new AndroidAssetServer(null, "assets");

        // 操作：查询未知扩展名的 MIME 类型
        var mimeType = server.GetMimeType("file.unknownext");

        // 断言：返回默认 application/octet-stream
        await Assert.That(mimeType).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task GetMimeType_ReturnsHtml_ForHtmlFile()
    {
        // 安排
        var server = new AndroidAssetServer(null, "assets");

        // 操作
        var mimeType = server.GetMimeType("index.html");

        // 断言
        await Assert.That(mimeType).IsEqualTo("text/html");
    }
}
