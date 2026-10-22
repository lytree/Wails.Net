using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;
using Wails.Net.Cli.Mcp;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// MCP 协议层、令牌、HTTP 认证辅助与 doctor JSON 报告单元测试（P1-4）。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c>（beta.9 PR #5896 / beta.19 PR #6092）。
/// </summary>
[NotInParallel]
public sealed class McpProtocolTests
{
    [Test]
    public async Task Token_Generate_ReturnsUrlSafeUniqueToken()
    {
        var first = McpToken.Generate();
        var second = McpToken.Generate();

        await Assert.That(first.Length).IsEqualTo(43);
        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')).IsTrue();
    }

    [Test]
    public async Task Token_Matches_ComparesExactValue()
    {
        await Assert.That(McpToken.Matches("abc", "abc")).IsTrue();
        await Assert.That(McpToken.Matches("abc", "abd")).IsFalse();
        await Assert.That(McpToken.Matches("ab", "abc")).IsFalse();
        await Assert.That(McpToken.Matches(null, "abc")).IsFalse();
        await Assert.That(McpToken.Matches(string.Empty, "abc")).IsFalse();
    }

    [Test]
    public async Task NegotiateVersion_EchoesSupportedVersion()
    {
        await Assert.That(McpProtocol.NegotiateVersion("2024-11-05")).IsEqualTo("2024-11-05");
        await Assert.That(McpProtocol.NegotiateVersion("2025-03-26")).IsEqualTo("2025-03-26");
    }

    [Test]
    public async Task NegotiateVersion_UnknownOrMissing_ReturnsLatest()
    {
        await Assert.That(McpProtocol.NegotiateVersion("1999-01-01")).IsEqualTo(McpProtocol.LatestVersion);
        await Assert.That(McpProtocol.NegotiateVersion(null)).IsEqualTo(McpProtocol.LatestVersion);
        await Assert.That(McpProtocol.NegotiateVersion(string.Empty)).IsEqualTo(McpProtocol.LatestVersion);
    }

    [Test]
    public async Task Success_And_Error_BuildJsonRpcEnvelope()
    {
        var success = McpProtocol.Success(JsonValue.Create(7), new JsonObject { ["ok"] = true });
        await Assert.That(success["jsonrpc"]!.GetValue<string>()).IsEqualTo("2.0");
        await Assert.That(success["id"]!.GetValue<int>()).IsEqualTo(7);
        await Assert.That(((JsonObject)success["result"]!)["ok"]!.GetValue<bool>()).IsTrue();
        await Assert.That(success["error"]).IsNull();

        var error = McpProtocol.Error(JsonValue.Create("a"), McpProtocol.MethodNotFoundCode, "unknown");
        var body = (JsonObject)error["error"]!;
        await Assert.That(body["code"]!.GetValue<int>()).IsEqualTo(-32601);
        await Assert.That(body["message"]!.GetValue<string>()).IsEqualTo("unknown");
    }

    [Test]
    public async Task IsLoopbackHost_AcceptsLocalhostAndLoopbackAddresses()
    {
        await Assert.That(McpHttpTransport.IsLoopbackHost("127.0.0.1:8080")).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackHost("localhost:5000")).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackHost("[::1]:5000")).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackHost("::1")).IsTrue();
    }

    [Test]
    public async Task IsLoopbackHost_RejectsRemoteOrMalformedHosts()
    {
        await Assert.That(McpHttpTransport.IsLoopbackHost("example.com:80")).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackHost("127.0.0.1.evil.example")).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackHost(string.Empty)).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackHost(null)).IsFalse();
    }

    [Test]
    public async Task IsLoopbackOrigin_AllowsMissingOrHttpLoopbackOrigin()
    {
        await Assert.That(McpHttpTransport.IsLoopbackOrigin(null)).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin(string.Empty)).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("http://localhost:3000")).IsTrue();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("http://127.0.0.1:3000")).IsTrue();
    }

    [Test]
    public async Task IsLoopbackOrigin_RejectsHttpsRemoteAndUserInfo()
    {
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("https://localhost:3000")).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("http://evil.example")).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("http://user@localhost:3000")).IsFalse();
        await Assert.That(McpHttpTransport.IsLoopbackOrigin("not-a-uri")).IsFalse();
    }

    [Test]
    public async Task FindFreeLoopbackPort_ReturnsUsablePort()
    {
        var port = McpHttpTransport.FindFreeLoopbackPort();

        await Assert.That(port).IsGreaterThan(0);
        await Assert.That(port).IsLessThanOrEqualTo(65535);
    }

    [Test]
    public async Task ResolveToken_PrefersOptionThenEnvironmentThenRandom()
    {
        var previous = Environment.GetEnvironmentVariable(McpServer.TokenEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(McpServer.TokenEnvironmentVariable, null);
            await Assert.That(McpServer.ResolveToken(" explicit ")).IsEqualTo("explicit");

            Environment.SetEnvironmentVariable(McpServer.TokenEnvironmentVariable, "env-token");
            await Assert.That(McpServer.ResolveToken(null)).IsEqualTo("env-token");
            await Assert.That(McpServer.ResolveToken("   ")).IsEqualTo("env-token");

            Environment.SetEnvironmentVariable(McpServer.TokenEnvironmentVariable, string.Empty);
            var generated = McpServer.ResolveToken(null);
            await Assert.That(generated.Length).IsEqualTo(43);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpServer.TokenEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task BuildJsonReport_SummarizesDiagnostics()
    {
        var checks = new List<DoctorCommand.DiagnosticResult>
        {
            new("dotnet SDK", DoctorCommand.DiagnosticStatus.Pass, "已安装"),
            new("NSIS", DoctorCommand.DiagnosticStatus.Warn, "未安装"),
            new("GTK4", DoctorCommand.DiagnosticStatus.Fail, "缺失"),
        };

        var report = (JsonObject)JsonNode.Parse(DoctorCommand.BuildJsonReport(checks))!;
        var summary = (JsonObject)report["summary"]!;
        var items = (JsonArray)report["checks"]!;

        await Assert.That(report["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(items.Count).IsEqualTo(3);
        await Assert.That(summary["passed"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(summary["warned"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(summary["failed"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(summary["total"]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(((JsonObject)items[2]!)["status"]!.GetValue<string>()).IsEqualTo("fail");
    }
}
