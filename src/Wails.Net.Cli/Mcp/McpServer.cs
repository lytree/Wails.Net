using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP 服务器启动选项。
/// </summary>
/// <param name="Root">允许的项目根目录；null 表示当前目录。</param>
/// <param name="Token">会话令牌；null/空时读取环境变量或随机生成。</param>
/// <param name="Http">强制使用 Streamable HTTP。</param>
/// <param name="Stdio">强制使用 stdio。</param>
/// <param name="Port">HTTP 端口；0 表示自动选择空闲的 loopback 端口。</param>
internal sealed record McpOptions(string? Root, string? Token, bool Http, bool Stdio, int Port);

/// <summary>
/// 本地项目 MCP 服务器：处理 JSON-RPC 消息、暴露项目生命周期工具。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>mcpServer</c>。
/// </summary>
internal sealed class McpServer : IDisposable
{
    /// <summary>环境变量中的会话令牌。</summary>
    public const string TokenEnvironmentVariable = "WAILS_MCP_TOKEN";

    private readonly McpRootPolicy _policy;
    private readonly McpJobManager _jobs = new();
    private readonly CancellationToken _lifetime;
    private readonly string _instructions;
    private bool _disposed;

    /// <summary>
    /// 创建 MCP 服务器。
    /// </summary>
    /// <param name="options">启动选项。</param>
    /// <param name="policy">根目录策略。</param>
    /// <param name="lifetime">服务端生命周期令牌。</param>
    public McpServer(McpOptions options, McpRootPolicy policy, CancellationToken lifetime)
    {
        _policy = policy;
        _lifetime = lifetime;
        Token = ResolveToken(options.Token);
        _instructions =
            $"Wails.Net project MCP session. Allowed root: {policy.Root}. Session token: {Token}. " +
            "Include the token in mutating and process-control tool arguments as `token`. " +
            "The token is scoped to this server process and is not a substitute for user approval.";
    }

    /// <summary>允许的根目录。</summary>
    public string Root => _policy.Root;

    /// <summary>会话令牌。</summary>
    public string Token { get; }

    /// <summary>服务端版本。</summary>
    public static string ServerVersion =>
        string.IsNullOrWhiteSpace(VersionConstants.Version) ? "development" : VersionConstants.Version;

    /// <summary>
    /// 解析会话令牌：显式参数 → 环境变量 → 随机生成。
    /// </summary>
    /// <param name="optionValue">命令行传入的令牌。</param>
    /// <returns>会话令牌。</returns>
    public static string ResolveToken(string? optionValue)
    {
        var token = optionValue?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable)?.Trim();
        }

        return string.IsNullOrEmpty(token) ? McpToken.Generate() : token;
    }

    /// <summary>
    /// 解析单行 JSON-RPC 消息；解析失败时返回解析错误响应。
    /// </summary>
    /// <param name="line">JSON 文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应；通知消息返回 null。</returns>
    public async Task<JsonObject?> HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
            return McpProtocol.Error(null, McpProtocol.ParseErrorCode, $"invalid JSON: {ex.Message}");
        }

        return await HandleAsync(node, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理一条 JSON-RPC 消息。
    /// </summary>
    /// <param name="message">消息节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应；通知消息返回 null。</returns>
    public async Task<JsonObject?> HandleAsync(JsonNode? message, CancellationToken cancellationToken)
    {
        if (message is not JsonObject request)
        {
            return McpProtocol.Error(null, McpProtocol.InvalidRequestCode, "expected a JSON-RPC object");
        }

        var id = request["id"];
        if (request["method"] is not JsonValue methodValue || !methodValue.TryGetValue<string>(out var method))
        {
            return McpProtocol.Error(id, McpProtocol.InvalidRequestCode, "missing method");
        }

        // 无 id 的消息是通知（如 notifications/initialized），无需响应
        if (id is null)
        {
            return null;
        }

        try
        {
            return method switch
            {
                "initialize" => Initialize(request),
                "ping" => McpProtocol.Success(id, new JsonObject()),
                "tools/list" => ToolsList(id),
                "tools/call" => await CallToolAsync(id, request["params"] as JsonObject, cancellationToken)
                    .ConfigureAwait(false),
                _ => McpProtocol.Error(id, McpProtocol.MethodNotFoundCode, $"unknown method: {method}"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return McpProtocol.Error(id, McpProtocol.InternalErrorCode, ex.Message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _jobs.Dispose();
    }

    private JsonObject Initialize(JsonObject request)
    {
        var parameters = request["params"] as JsonObject;
        var requested = GetString(parameters, "protocolVersion");
        var result = new JsonObject
        {
            ["protocolVersion"] = McpProtocol.NegotiateVersion(requested),
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = McpProtocol.ServerName,
                ["version"] = ServerVersion,
            },
            ["instructions"] = _instructions,
        };

        return McpProtocol.Success(request["id"], result);
    }

    private static JsonObject ToolsList(JsonNode? id)
    {
        var tools = new JsonArray();
        foreach (var tool in McpToolCatalog.All)
        {
            tools.Add(tool.ToJson());
        }

        return McpProtocol.Success(id, new JsonObject { ["tools"] = tools });
    }

    private async Task<JsonObject> CallToolAsync(
        JsonNode? id,
        JsonObject? parameters,
        CancellationToken cancellationToken)
    {
        var name = GetString(parameters, "name");
        if (string.IsNullOrEmpty(name))
        {
            return McpProtocol.Error(id, McpProtocol.InvalidParamsCode, "tool name is required");
        }

        if (McpToolCatalog.All.All(tool => !string.Equals(tool.Name, name, StringComparison.Ordinal)))
        {
            return McpProtocol.Error(id, McpProtocol.InvalidParamsCode, $"unknown tool: {name}");
        }

        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            var result = name switch
            {
                McpToolCatalog.Inspect => Inspect(arguments),
                McpToolCatalog.NewProject => await NewProjectAsync(arguments, cancellationToken)
                    .ConfigureAwait(false),
                McpToolCatalog.Doctor => await DoctorAsync(cancellationToken).ConfigureAwait(false),
                McpToolCatalog.Build => StartJob(arguments, "build"),
                McpToolCatalog.DevStart => StartJob(arguments, "dev"),
                McpToolCatalog.GenerateBindings => StartJob(arguments, "generate"),
                McpToolCatalog.JobStatus => JobStatus(arguments),
                McpToolCatalog.JobStop => JobStop(arguments),
                _ => throw new InvalidOperationException($"unknown tool: {name}"),
            };

            return McpProtocol.Success(id, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return McpProtocol.Success(id, ToolText(ex.Message, isError: true));
        }
    }

    private JsonObject Inspect(JsonObject arguments)
    {
        var path = _policy.ResolvePath(GetString(arguments, "path"));
        var result = new JsonObject
        {
            ["root"] = _policy.Root,
            ["path"] = path,
        };

        if (!Directory.Exists(path))
        {
            result["exists"] = File.Exists(path);
            result["isDirectory"] = false;
            return ToolJson(result);
        }

        result["exists"] = true;
        result["isDirectory"] = true;

        var files = new List<string>();
        var fileCount = 0;
        var truncated = false;
        var wailsJson = false;
        var projectFile = false;
        var frontend = false;

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] subdirectories;
            string[] filenames;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                filenames = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                if (name is ".git" or "node_modules" or "bin" or "obj" or ".vs")
                {
                    continue;
                }

                // 跳过符号链接目录，避免遍历成环
                try
                {
                    var attributes = new DirectoryInfo(subdirectory).Attributes;
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                pending.Push(subdirectory);
            }

            foreach (var file in filenames)
            {
                fileCount++;
                var relative = Path.GetRelativePath(path, file).Replace('\\', '/');
                if (files.Count < 1000)
                {
                    files.Add(relative);
                }
                else
                {
                    truncated = true;
                }

                if (string.Equals(directory, path, StringComparison.Ordinal))
                {
                    if (relative == "wails.json")
                    {
                        wailsJson = true;
                    }

                    if (relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        projectFile = true;
                    }
                }

                if (relative == "frontend/package.json")
                {
                    frontend = true;
                }
            }
        }

        var fileArray = new JsonArray();
        foreach (var file in files)
        {
            fileArray.Add(file);
        }

        result["files"] = fileArray;
        result["fileCount"] = fileCount;
        result["truncated"] = truncated;
        result["isWailsProject"] = wailsJson && projectFile;
        result["wailsJson"] = wailsJson;
        result["projectFile"] = projectFile;
        result["frontend"] = frontend;
        return ToolJson(result);
    }

    private async Task<JsonObject> NewProjectAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        if (!HasValidToken(arguments))
        {
            return Unauthorized();
        }

        var name = GetString(arguments, "name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ToolText("name is required", isError: true);
        }

        if (name.Contains('\n') || name.Contains('\r') || name.Contains('\0'))
        {
            return ToolText("name may not contain newlines or NUL", isError: true);
        }

        var template = GetString(arguments, "template");
        if (string.IsNullOrEmpty(template))
        {
            template = "vanilla-ts";
        }

        if (template.Contains('\n') || template.Contains('\r') || template.Contains('\0'))
        {
            return ToolText("template may not contain newlines or NUL", isError: true);
        }

        if (template.Contains("://", StringComparison.Ordinal) && !GetBool(arguments, "allowExternal"))
        {
            return ToolText("remote templates require allowExternal=true", isError: true);
        }

        var directory = _policy.ResolvePath(GetString(arguments, "directory"));
        var result = await McpSelfInvoker.RunAsync(
            _policy.Root,
            ["new", name, "--directory", directory, "--template", template],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return ToolText($"initialize project failed ({result.ExitCode}): {result.Output}", isError: true);
        }

        var projectPath = Path.Combine(directory, name);
        var payload = new JsonObject
        {
            ["root"] = _policy.Root,
            ["path"] = projectPath,
            ["exists"] = Directory.Exists(projectPath),
            ["output"] = result.Output,
        };

        return ToolJson(payload);
    }

    private async Task<JsonObject> DoctorAsync(CancellationToken cancellationToken)
    {
        var result = await McpSelfInvoker.RunAsync(_policy.Root, ["doctor", "--json"], cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return ToolText($"doctor: exit {result.ExitCode}: {result.Output}", isError: true);
        }

        JsonNode? report = null;
        var trimmed = result.Output.Trim();
        if (trimmed.Length > 0)
        {
            try
            {
                report = JsonNode.Parse(trimmed);
            }
            catch (JsonException)
            {
                report = JsonValue.Create(trimmed);
            }
        }

        var payload = new JsonObject
        {
            ["root"] = _policy.Root,
            ["report"] = report,
        };

        return ToolJson(payload);
    }

    private JsonObject StartJob(JsonObject arguments, string command)
    {
        if (!HasValidToken(arguments))
        {
            return Unauthorized();
        }

        var path = _policy.ResolvePath(GetString(arguments, "path"));
        var extra = GetStringArray(arguments, "args");
        _policy.ValidateArguments(path, extra);

        var job = new McpJob(path, _lifetime);
        if (!_jobs.TryAdd(job))
        {
            job.Dispose();
            return ToolText("too many active or retained MCP jobs", isError: true);
        }

        var childArguments = new List<string> { command };
        childArguments.AddRange(extra);

        try
        {
            McpSelfInvoker.StartJob(job, childArguments);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            job.Finish(canceled: true, exitCode: -1);
            return ToolText(ex.Message, isError: true);
        }

        return ToolJson(JobSnapshotJson(job));
    }

    private JsonObject JobStatus(JsonObject arguments)
    {
        var job = _jobs.Get(GetString(arguments, "jobId"));
        return job is null
            ? ToolText("unknown job", isError: true)
            : ToolJson(JobSnapshotJson(job));
    }

    private JsonObject JobStop(JsonObject arguments)
    {
        if (!HasValidToken(arguments))
        {
            return Unauthorized();
        }

        var job = _jobs.Get(GetString(arguments, "jobId"));
        if (job is null)
        {
            return ToolText("unknown job", isError: true);
        }

        job.RequestStop();
        return ToolJson(JobSnapshotJson(job));
    }

    private bool HasValidToken(JsonObject arguments) => McpToken.Matches(GetString(arguments, "token"), Token);

    private static JsonObject Unauthorized() =>
        ToolText("invalid or missing MCP session token", isError: true);

    private static JsonObject JobSnapshotJson(McpJob job)
    {
        var snapshot = job.Snapshot();
        return new JsonObject
        {
            ["jobId"] = snapshot.JobId,
            ["state"] = snapshot.State,
            ["path"] = snapshot.Path,
            ["output"] = snapshot.Output,
        };
    }

    private static JsonObject ToolText(string text, bool isError) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
            },
        },
        ["isError"] = isError,
    };

    private static JsonObject ToolJson(JsonNode payload) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = payload.ToJsonString(McpProtocol.SerializerOptions),
            },
        },
        ["structuredContent"] = payload.DeepClone(),
        ["isError"] = false,
    };

    private static string? GetString(JsonObject? source, string name) =>
        source?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool GetBool(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static List<string> GetStringArray(JsonObject source, string name)
    {
        if (source[name] is not JsonArray array)
        {
            return [];
        }

        var result = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                throw new InvalidOperationException("args must be an array of strings");
            }

            result.Add(text);
        }

        return result;
    }
}
