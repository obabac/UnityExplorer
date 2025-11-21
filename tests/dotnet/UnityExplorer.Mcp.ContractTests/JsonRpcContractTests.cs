using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcContractTests
{
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
        // Id may be null when nothing is under the mouse; only assert the property exists.
        pick.TryGetProperty("Id", out _).Should().BeTrue();
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

            // Accept 429 as a valid rate‑limit signal.
            ((int)res.StatusCode == 429).Should().BeTrue();
        }
    }
}
