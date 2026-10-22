using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Mcp;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// MCP 服务器工具分发与后台任务单元测试（P1-4）。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>mcpServer</c> / <c>mcpJob</c> / <c>mcpJobs</c>。
/// </summary>
[NotInParallel]
public sealed class McpServerTests
{
    private const string Token = "test-token";

    [Test]
    public async Task Initialize_UnknownVersion_ReturnsLatestAndCapabilities()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"1999-01-01\"}}",
                CancellationToken.None);

            var result = (JsonObject)response!["result"]!;
            await Assert.That(result["protocolVersion"]!.GetValue<string>()).IsEqualTo(McpProtocol.LatestVersion);
            await Assert.That(((JsonObject)result["capabilities"]!)["tools"]).IsNotNull();
            await Assert.That(((JsonObject)result["serverInfo"]!)["name"]!.GetValue<string>())
                .IsEqualTo(McpProtocol.ServerName);
            await Assert.That(result["instructions"]!.GetValue<string>()).Contains(Token);
            await Assert.That(result["instructions"]!.GetValue<string>()).Contains(root);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Initialize_SupportedVersion_EchoesRequestedVersion()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}",
                CancellationToken.None);

            var result = (JsonObject)response!["result"]!;
            await Assert.That(result["protocolVersion"]!.GetValue<string>()).IsEqualTo("2024-11-05");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Notification_WithoutId_ReturnsNoResponse()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                CancellationToken.None);

            await Assert.That(response).IsNull();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Ping_ReturnsEmptyResult()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}",
                CancellationToken.None);

            var result = (JsonObject)response!["result"]!;
            await Assert.That(result.Count).IsEqualTo(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"resources/list\"}",
                CancellationToken.None);

            var error = (JsonObject)response!["error"]!;
            await Assert.That(error["code"]!.GetValue<int>()).IsEqualTo(McpProtocol.MethodNotFoundCode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task InvalidJson_ReturnsParseError()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync("{not-json", CancellationToken.None);

            var error = (JsonObject)response!["error"]!;
            await Assert.That(error["code"]!.GetValue<int>()).IsEqualTo(McpProtocol.ParseErrorCode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ToolsList_ContainsAllProjectTools()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await server.HandleLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}",
                CancellationToken.None);

            var tools = (JsonArray)((JsonObject)response!["result"]!)["tools"]!;
            var names = tools.Select(tool => ((JsonObject)tool!)["name"]!.GetValue<string>()).ToList();

            await Assert.That(tools.Count).IsEqualTo(McpToolCatalog.All.Count);
            foreach (var expected in new[]
            {
                McpToolCatalog.Inspect,
                McpToolCatalog.NewProject,
                McpToolCatalog.Doctor,
                McpToolCatalog.Build,
                McpToolCatalog.DevStart,
                McpToolCatalog.GenerateBindings,
                McpToolCatalog.JobStatus,
                McpToolCatalog.JobStop,
            })
            {
                await Assert.That(names).Contains(expected);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task CallTool_UnknownTool_ReturnsInvalidParams()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var response = await CallToolRawAsync(server, "wails_nope", new JsonObject());

            var error = (JsonObject)response["error"]!;
            await Assert.That(error["code"]!.GetValue<int>()).IsEqualTo(McpProtocol.InvalidParamsCode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Inspect_ProjectDirectory_ReportsFlagsAndFiles()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "wails.json"), "{}");
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project />");
            Directory.CreateDirectory(Path.Combine(root, "frontend"));
            File.WriteAllText(Path.Combine(root, "frontend", "package.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "// app");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, ".git", "config"), "x");
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "pkg"));
            File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "index.js"), "x");
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "bin", "app.exe"), "x");

            using var server = CreateServer(root);
            var result = await CallToolAsync(server, McpToolCatalog.Inspect, new JsonObject());
            var structured = (JsonObject)result["structuredContent"]!;

            await Assert.That(result["isError"]!.GetValue<bool>()).IsFalse();
            await Assert.That(structured["exists"]!.GetValue<bool>()).IsTrue();
            await Assert.That(structured["isWailsProject"]!.GetValue<bool>()).IsTrue();
            await Assert.That(structured["wailsJson"]!.GetValue<bool>()).IsTrue();
            await Assert.That(structured["projectFile"]!.GetValue<bool>()).IsTrue();
            await Assert.That(structured["frontend"]!.GetValue<bool>()).IsTrue();
            await Assert.That(structured["fileCount"]!.GetValue<int>()).IsEqualTo(4);
            await Assert.That(((JsonArray)structured["files"]!).Count).IsEqualTo(4);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Inspect_PathOutsideRoot_ReturnsToolError()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var result = await CallToolAsync(
                server,
                McpToolCatalog.Inspect,
                new JsonObject { ["path"] = "../outside" });

            await Assert.That(result["isError"]!.GetValue<bool>()).IsTrue();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Build_WithoutToken_ReturnsToolError()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var result = await CallToolAsync(
                server,
                McpToolCatalog.Build,
                new JsonObject { ["args"] = new JsonArray("--help") });

            await Assert.That(result["isError"]!.GetValue<bool>()).IsTrue();
            await Assert.That(ResultText(result)).Contains("token");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task JobStatus_UnknownJob_ReturnsToolError()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var result = await CallToolAsync(
                server,
                McpToolCatalog.JobStatus,
                new JsonObject { ["jobId"] = "missing" });

            await Assert.That(result["isError"]!.GetValue<bool>()).IsTrue();
            await Assert.That(ResultText(result)).Contains("unknown job");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task JobStop_WithoutToken_ReturnsToolError()
    {
        var root = CreateTempRoot();
        try
        {
            using var server = CreateServer(root);
            var result = await CallToolAsync(
                server,
                McpToolCatalog.JobStop,
                new JsonObject { ["jobId"] = "missing" });

            await Assert.That(result["isError"]!.GetValue<bool>()).IsTrue();
            await Assert.That(ResultText(result)).Contains("token");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Job_Finish_TransitionsToFinalState()
    {
        using var completed = new McpJob(Path.GetTempPath(), CancellationToken.None);
        completed.Finish(canceled: false, exitCode: 0);
        await Assert.That(completed.Snapshot().State).IsEqualTo("completed");

        using var failed = new McpJob(Path.GetTempPath(), CancellationToken.None);
        failed.Finish(canceled: false, exitCode: 3);
        await Assert.That(failed.Snapshot().State).IsEqualTo("failed");

        using var stopped = new McpJob(Path.GetTempPath(), CancellationToken.None);
        stopped.Finish(canceled: true, exitCode: -1);
        await Assert.That(stopped.Snapshot().State).IsEqualTo("stopped");
    }

    [Test]
    public async Task Job_RequestStop_MarksStoppingThenStopped()
    {
        using var job = new McpJob(Path.GetTempPath(), CancellationToken.None);

        job.RequestStop();
        await Assert.That(job.Snapshot().State).IsEqualTo("stopping");

        job.Finish(canceled: true, exitCode: -1);
        await Assert.That(job.Snapshot().State).IsEqualTo("stopped");
        await Assert.That(job.TryGetFinishedAt(out _)).IsTrue();
    }

    [Test]
    public async Task JobManager_TryAdd_RejectsWhenActiveLimitReached()
    {
        using var manager = new McpJobManager();
        var jobs = new List<McpJob>();
        try
        {
            for (var i = 0; i < McpJobManager.MaxJobs; i++)
            {
                var job = new McpJob(Path.GetTempPath(), CancellationToken.None);
                jobs.Add(job);
                await Assert.That(manager.TryAdd(job)).IsTrue();
            }

            var overflow = new McpJob(Path.GetTempPath(), CancellationToken.None);
            jobs.Add(overflow);
            await Assert.That(manager.TryAdd(overflow)).IsFalse();

            jobs[0].Finish(canceled: false, exitCode: 0);
            await Assert.That(manager.TryAdd(overflow)).IsTrue();
            await Assert.That(manager.Get(overflow.Id)).IsNotNull();
        }
        finally
        {
            foreach (var job in jobs)
            {
                job.Dispose();
            }
        }
    }

    [Test]
    public async Task CappedTextBuffer_TruncatesBeyondCapacity()
    {
        var buffer = new CappedTextBuffer(10);

        buffer.Append("abcdefghijkl");

        await Assert.That(buffer.Text).IsEqualTo("abcdefghij");
        await Assert.That(buffer.Truncated).IsTrue();
    }

    private static McpServer CreateServer(string root) =>
        new(new McpOptions(root, Token, Http: false, Stdio: true, Port: 0), McpRootPolicy.Create(root), CancellationToken.None);

    private static async Task<JsonObject> CallToolAsync(McpServer server, string name, JsonObject arguments)
    {
        var response = await CallToolRawAsync(server, name, arguments);
        return (JsonObject)response["result"]!;
    }

    private static async Task<JsonObject> CallToolRawAsync(McpServer server, string name, JsonObject arguments)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = name,
                ["arguments"] = arguments,
            },
        };

        var response = await server.HandleAsync(request, CancellationToken.None);
        return response!;
    }

    private static string ResultText(JsonObject result) =>
        ((JsonObject)((JsonArray)result["content"]!)[0]!)["text"]!.GetValue<string>();

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wailsnet-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响断言
        }
    }
}
