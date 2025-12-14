using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcContractTests
{
    private static async Task<JsonElement?> CallToolAsync(HttpClient http, string name, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = $"jsonrpc-call-{name}-{Guid.NewGuid():N}",
            method = "call_tool",
            @params = new
            {
                name,
                arguments
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("result", out var result))
            return null;
        if (!result.TryGetProperty("content", out var contentArr) || contentArr.ValueKind != JsonValueKind.Array || contentArr.GetArrayLength() == 0)
            return null;

        var first = contentArr[0];
        return first.TryGetProperty("json", out var jsonEl) ? jsonEl.Clone() : (JsonElement?)null;
    }

    [Fact]
    public async Task ListTools_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-test",
            method = "list_tools",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ListTools_Includes_InputSchema_For_All_Tools()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-inputschema-test",
            method = "list_tools",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var tool in tools.EnumerateArray())
        {
            tool.TryGetProperty("name", out var name).Should().BeTrue();
            name.ValueKind.Should().Be(JsonValueKind.String);

            tool.TryGetProperty("inputSchema", out var schema).Should().BeTrue();
            schema.ValueKind.Should().Be(JsonValueKind.Object);
        }

        var toolMap = tools
            .EnumerateArray()
            .Where(t => t.TryGetProperty("name", out _))
            .ToDictionary(t => t.GetProperty("name").GetString()!);

        foreach (var schema in toolMap.Values.Select(t => t.GetProperty("inputSchema")))
        {
            schema.TryGetProperty("properties", out var props).Should().BeTrue();
            props.ValueKind.Should().Be(JsonValueKind.Object);
            schema.TryGetProperty("additionalProperties", out var additional).Should().BeTrue();
            additional.GetBoolean().Should().BeFalse();
        }

        if (toolMap.TryGetValue("MousePick", out var mouseTool))
        {
            var schema = mouseTool.GetProperty("inputSchema");
            var props = schema.GetProperty("properties");
            props.TryGetProperty("mode", out var modeProp).Should().BeTrue();
            modeProp.GetProperty("type").GetString().Should().Be("string");
            modeProp.TryGetProperty("enum", out var enumProp).Should().BeTrue();
            enumProp.EnumerateArray().Select(e => e.GetString()).Should().Contain(new[] { "world", "ui" });
            props.TryGetProperty("normalized", out var normalized).Should().BeTrue();
            normalized.GetProperty("type").GetString().Should().Be("boolean");
        }

        if (toolMap.TryGetValue("SetActive", out var setActive))
        {
            var schema = setActive.GetProperty("inputSchema");
            schema.TryGetProperty("required", out var required).Should().BeTrue();
            var requiredFields = required.EnumerateArray().Select(e => e.GetString()).ToArray();
            requiredFields.Should().Contain(new[] { "objectId", "active" });
            var props = schema.GetProperty("properties");
            props.TryGetProperty("objectId", out var objectId).Should().BeTrue();
            objectId.GetProperty("type").GetString().Should().Be("string");
            props.TryGetProperty("confirm", out var confirm).Should().BeTrue();
            confirm.GetProperty("type").GetString().Should().Be("boolean");
        }
    }

    [Fact]
    public async Task ListResources_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-resources-test",
            method = "list_resources",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("resources", out var resources).Should().BeTrue();
        resources.ValueKind.Should().Be(JsonValueKind.Array);
        resources.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var resource in resources.EnumerateArray())
        {
            resource.TryGetProperty("uri", out var uri).Should().BeTrue();
            uri.ValueKind.Should().Be(JsonValueKind.String);
            resource.TryGetProperty("name", out var name).Should().BeTrue();
            name.ValueKind.Should().Be(JsonValueKind.String);
            resource.TryGetProperty("description", out var description).Should().BeTrue();
            description.ValueKind.Should().Be(JsonValueKind.String);
            resource.TryGetProperty("mimeType", out var mime).Should().BeTrue();
            mime.GetString().Should().Be("application/json");
        }
    }

    [Fact]
    public async Task CallTool_GetStatus_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-status-test",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task CallTool_GetVersion_Returns_Version_Info_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-getversion-test",
            method = "call_tool",
            @params = new
            {
                name = "GetVersion",
                arguments = new { }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var version = jsonEl;
        version.TryGetProperty("ExplorerVersion", out _).Should().BeTrue();
        version.TryGetProperty("McpVersion", out _).Should().BeTrue();
        version.TryGetProperty("UnityVersion", out _).Should().BeTrue();
        version.TryGetProperty("Runtime", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CallTool_Returns_Text_And_Json_Content_For_Inspector_Clients()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-content-shape",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        contentArr.GetArrayLength().Should().BeGreaterThan(0);

        var first = contentArr[0];
        first.GetProperty("type").GetString().Should().Be("text");
        first.TryGetProperty("mimeType", out var mimeType).Should().BeTrue();
        mimeType.GetString().Should().Be("application/json");
        first.TryGetProperty("text", out var text).Should().BeTrue();
        text.ValueKind.Should().Be(JsonValueKind.String);
        text.GetString().Should().NotBeNullOrWhiteSpace();
        first.TryGetProperty("json", out var jsonPayload).Should().BeTrue();
        jsonPayload.ValueKind.Should().Be(JsonValueKind.Object);

        var parsedFromText = JsonDocument.Parse(text.GetString()!).RootElement;
        parsedFromText.TryGetProperty("Ready", out _).Should().BeTrue();
        parsedFromText.TryGetProperty("ScenesLoaded", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "initialize-test",
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new { name = "contract-tests", version = "0.0.1" },
                capabilities = new { }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.GetProperty("protocolVersion").GetString().Should().NotBeNullOrWhiteSpace();
        result.TryGetProperty("serverInfo", out var serverInfo).Should().BeTrue();
        serverInfo.TryGetProperty("name", out var name).Should().BeTrue();
        name.ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Ping_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "ping-test",
            method = "ping",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ReadResource_Status_JsonRpc_Response_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "read-resource-status-test",
            method = "read_resource",
            @params = new
            {
                uri = "unity://status"
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("contents", out var contents).Should().BeTrue();
        contents.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Notifications_Initialized_Is_Accepted_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "notifications-initialized-test",
            method = "notifications/initialized",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("ok", out var ok).Should().BeTrue();
        ok.ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public async Task Notifications_Initialized_Without_Id_Returns_202()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task StreamEvents_Endpoint_Responds_With_Chunked_Json_When_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-test",
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = content
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        res.EnsureSuccessStatusCode();
        res.Headers.TransferEncodingChunked.Should().BeTrue();
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Sse_Root_Stream_Emits_ToolResult_When_Tool_Called()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        sseRequest.Headers.Accept.Clear();
        sseRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var sseResponse = await http.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        sseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        sseResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using var stream = await sseResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var callPayload = new
        {
            jsonrpc = "2.0",
            id = "sse-tool-call",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        var callJson = JsonSerializer.Serialize(callPayload);
        using var callContent = new StringContent(callJson, Encoding.UTF8, "application/json");
        using var callResponse = await http.PostAsync("/message", callContent, cts.Token);
        callResponse.EnsureSuccessStatusCode();

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        waitCts.CancelAfter(TimeSpan.FromSeconds(5));

        while (!waitCts.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var jsonLine = line.Substring("data:".Length).Trim();
            if (string.IsNullOrWhiteSpace(jsonLine))
                continue;

            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "notification")
                continue;

            if (!root.TryGetProperty("params", out var @params))
                continue;

            if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "tool_result")
                continue;

            @params.TryGetProperty("payload", out var payload).Should().BeTrue();
            payload.TryGetProperty("name", out _).Should().BeTrue();
            return;
        }

        Assert.Fail("Expected a 'tool_result' notification on SSE stream after calling a tool.");
    }

    [Fact]
    public async Task StreamEvents_Idle_Stream_Remains_Open_For_A_Short_Period()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-idle-test",
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = content
        };

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        while (!cts.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timed out waiting for data; treat as success since the
                // connection remained open without protocol errors.
                return;
            }

            if (line is null)
            {
                // End-of-stream arrived unexpectedly soon.
                Assert.Fail("stream_events HTTP stream ended unexpectedly while idle.");
            }

            // Ignore any actual notifications; this test only cares that the
            // stream stays readable and does not terminate immediately.
        }
    }

    [Fact]
    public async Task StreamEvents_Allows_Reconnect_After_Client_Close()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (int i = 0; i < 3; i++)
        {
            var payload = new
            {
                jsonrpc = "2.0",
                id = $"stream-events-reconnect-{i}",
                method = "stream_events",
                @params = new { }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
            {
                Content = content
            };

            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            res.EnsureSuccessStatusCode();
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
            // Drop the connection immediately to simulate an inspector disconnect.
        }

        var pingPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-reconnect-ping",
            method = "ping",
            @params = new { }
        };
        var pingJson = JsonSerializer.Serialize(pingPayload);
        using var pingContent = new StringContent(pingJson, Encoding.UTF8, "application/json");
        using var pingRes = await http.PostAsync("/message", pingContent, cts.Token);
        pingRes.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StreamEvents_Emits_ToolResult_Notification_When_Tool_Called()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };

        // Open the stream_events channel first
        var streamPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-tool-result-test",
            method = "stream_events",
            @params = new { }
        };

        var streamJson = JsonSerializer.Serialize(streamPayload);
        using var streamContent = new StringContent(streamJson, Encoding.UTF8, "application/json");
        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = streamContent
        };

        using var streamResponse = await http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
        streamResponse.EnsureSuccessStatusCode();

        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Fire a tool call that should emit a tool_result notification
        var callPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-tool-call",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        var callJson = JsonSerializer.Serialize(callPayload);
        using var callContent = new StringContent(callJson, Encoding.UTF8, "application/json");
        using var callResponse = await http.PostAsync("/message", callContent);
        callResponse.EnsureSuccessStatusCode();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token));
            if (completed != readTask)
                continue;

            var line = await readTask;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0")
                continue;

            if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "notification")
                continue;

            if (!root.TryGetProperty("params", out var @params))
                continue;

            if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "tool_result")
                continue;

            @params.TryGetProperty("payload", out var payload).Should().BeTrue();
            payload.TryGetProperty("name", out var name).Should().BeTrue();
            name.GetString().Should().NotBeNullOrWhiteSpace();
            payload.TryGetProperty("ok", out var ok).Should().BeTrue();
            (ok.ValueKind == JsonValueKind.True || ok.ValueKind == JsonValueKind.False).Should().BeTrue();

            // On success, result is present; on error, error is present.
            var hasResult = payload.TryGetProperty("result", out _);
            var hasError = payload.TryGetProperty("error", out _);
            (hasResult || hasError).Should().BeTrue();

            return; // success path
        }

        // If we time out without observing a tool_result, fail for technical validation.
        Assert.Fail("Expected a 'tool_result' notification on stream_events after invoking call_tool.");
    }

    [Fact]
    public async Task StreamEvents_Emits_Selection_With_Payload_Matching_Selection_Resource()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)false }, cts.Token);

            var streamPayload = new
            {
                jsonrpc = "2.0",
                id = "stream-events-selection-test",
                method = "stream_events",
                @params = new { }
            };

            var streamJson = JsonSerializer.Serialize(streamPayload);
            using var streamContent = new StringContent(streamJson, Encoding.UTF8, "application/json");
            using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
            {
                Content = streamContent
            };

            using var streamResponse = await http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            streamResponse.EnsureSuccessStatusCode();

            await using var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var sceneId = await McpTestHelpers.TryGetFirstSceneIdAsync(http, cts.Token);
            var objectId = await McpTestHelpers.TryGetFirstObjectIdAsync(http, sceneId, cts.Token);
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            _ = await CallToolAsync(http, "SelectObject", new { objectId }, cts.Token);

            JsonElement? selectionPayload = null;
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            waitCts.CancelAfter(TimeSpan.FromSeconds(5));

            while (!waitCts.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(waitCts.Token); }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "notification")
                    continue;

                if (!root.TryGetProperty("params", out var @params))
                    continue;

                if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "selection")
                    continue;

                if (@params.TryGetProperty("payload", out var payloadEl))
                {
                    selectionPayload = payloadEl.Clone();
                    break;
                }
            }

            selectionPayload.Should().NotBeNull("selection notification should be emitted after SelectObject");
            if (selectionPayload == null)
                return;

            var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://selection")}", cts.Token);
            res.EnsureSuccessStatusCode();
            var resText = await res.Content.ReadAsStringAsync(cts.Token);
            using var resDoc = JsonDocument.Parse(resText);
            var resRoot = resDoc.RootElement;

            selectionPayload.Value.TryGetProperty("ActiveId", out var streamActive).Should().BeTrue();
            resRoot.TryGetProperty("ActiveId", out var resourceActive).Should().BeTrue();
            streamActive.GetString().Should().Be(resourceActive.GetString());

            selectionPayload.Value.TryGetProperty("Items", out var streamItems).Should().BeTrue();
            streamItems.ValueKind.Should().Be(JsonValueKind.Array);
            resRoot.TryGetProperty("Items", out var resourceItems).Should().BeTrue();
            resourceItems.ValueKind.Should().Be(JsonValueKind.Array);

            var streamList = streamItems.EnumerateArray().Select(i => i.GetString()).ToArray();
            var resourceList = resourceItems.EnumerateArray().Select(i => i.GetString()).ToArray();
            streamList.Should().Equal(resourceList);
        }
        finally
        {
            try
            {
                using var resetCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, resetCts.Token);
            }
            catch { }
        }
    }

    [Fact]
    public async Task StreamEvents_Notification_Has_Generic_Shape_When_Present()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-generic-shape-test",
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = content
        };

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0")
                continue;

            if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "notification")
                continue;

            if (!root.TryGetProperty("params", out var @params))
                continue;

            @params.TryGetProperty("event", out var eventEl).Should().BeTrue();
            eventEl.ValueKind.Should().Be(JsonValueKind.String);
            @params.TryGetProperty("payload", out var payloadEl).Should().BeTrue();
            payloadEl.ValueKind.Should().Be(JsonValueKind.Object);

            return; // found at least one well-formed notification
        }

        // It's valid for a server to have no notifications during the short window,
        // so treat lack of notifications as inconclusive rather than failure here.
    }

    [Fact]
    public async Task CallTool_MousePick_Returns_Result_Shape_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-mousepick-test",
            method = "call_tool",
            @params = new
            {
                name = "MousePick",
                arguments = new { mode = "world" }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var pick = jsonEl;
        pick.TryGetProperty("Mode", out var mode).Should().BeTrue();
        mode.GetString()!.Should().NotBeNullOrWhiteSpace();
        pick.TryGetProperty("Hit", out var hit).Should().BeTrue();
        (hit.ValueKind == JsonValueKind.True || hit.ValueKind == JsonValueKind.False).Should().BeTrue();
        pick.TryGetProperty("Id", out _).Should().BeTrue();
        pick.TryGetProperty("Items", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CallTool_MousePick_Ui_Returns_Items_When_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-mousepick-ui-test",
            method = "call_tool",
            @params = new
            {
                name = "MousePick",
                arguments = new { mode = "ui", x = 0.5, y = 0.5, normalized = true }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var pick = jsonEl;
        pick.TryGetProperty("Mode", out var mode).Should().BeTrue();
        mode.GetString().Should().Be("ui");
        pick.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        if (items.GetArrayLength() < 1)
            return; // skip if no UI hits available in the scene

        // Should expose primary Id for top-most hit
        pick.TryGetProperty("Id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.String);

        // Ensure each item has Id/Name/Path
        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("Id", out var iid).Should().BeTrue();
            iid.ValueKind.Should().Be(JsonValueKind.String);
            item.TryGetProperty("Name", out _).Should().BeTrue();
            item.TryGetProperty("Path", out _).Should().BeTrue();
        }

        // Follow-up GetObject on primary if present
        var primaryId = id.GetString();
        if (!string.IsNullOrWhiteSpace(primaryId))
        {
            var followPayload = new
            {
                jsonrpc = "2.0",
                id = "call-tool-getobject-from-ui-pick",
                method = "call_tool",
                @params = new
                {
                    name = "GetObject",
                    arguments = new { id = primaryId }
                }
            };
            var followJson = JsonSerializer.Serialize(followPayload);
            using var followContent = new StringContent(followJson, Encoding.UTF8, "application/json");
            using var followRes = await http.PostAsync("/message", followContent, cts.Token);
            followRes.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task CallTool_TailLogs_Returns_Items_Array_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-taillogs-test",
            method = "call_tool",
            @params = new
            {
                name = "TailLogs",
                arguments = new { count = 20 }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var logs = jsonEl;
        logs.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task RateLimit_Does_Not_Crash_Server_When_Many_Concurrent_Requests()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "rate-limit-test",
            method = "ping",
            @params = new { }
        };
        var json = JsonSerializer.Serialize(payload);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 80; i++)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var req = new HttpRequestMessage(HttpMethod.Post, "/message")
            {
                Content = content
            };
            tasks.Add(http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token));
        }

        var responses = await Task.WhenAll(tasks);
        responses.Length.Should().BeGreaterThan(0);

        foreach (var res in responses)
        {
            if (res.IsSuccessStatusCode)
                continue;

            // Accept 429 as a valid rate‑limit signal with structured error body.
            ((int)res.StatusCode == 429).Should().BeTrue();
            var body = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            root.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetProperty("code").GetInt32().Should().Be(-32005);
            error.GetProperty("message").GetString()!.Should().Contain("parallel requests");
            error.TryGetProperty("data", out var data).Should().BeTrue();
            data.GetProperty("kind").GetString().Should().Be("RateLimited");
            data.TryGetProperty("hint", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ReadResource_Unknown_Returns_NotFound_Error_With_Kind()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "read-resource-unknown",
            method = "read_resource",
            @params = new { uri = "unity://no-such-resource" }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32004);
        var data = error.GetProperty("data");
        data.GetProperty("kind").GetString().Should().Be("NotFound");
    }
}