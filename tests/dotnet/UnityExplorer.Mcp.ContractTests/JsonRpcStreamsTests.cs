using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcStreamsTests
{
    [Fact]
    public async Task StreamEvents_Endpoint_Responds_With_Chunked_Json_When_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-test",
            method = "stream_events",
            @params = new { }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(payload)
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        res.EnsureSuccessStatusCode();
        res.Headers.TransferEncodingChunked.Should().BeTrue();
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task StreamEvents_Emits_Scenes_Snapshot_On_Open()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-open-scenes",
            method = "stream_events",
            @params = new { }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(payload)
        };

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

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

            if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "scenes")
                continue;

            @params.TryGetProperty("payload", out var payloadEl).Should().BeTrue();
            payloadEl.TryGetProperty("Total", out var totalEl).Should().BeTrue();
            totalEl.ValueKind.Should().Be(JsonValueKind.Number);
            payloadEl.TryGetProperty("Items", out var itemsEl).Should().BeTrue();
            itemsEl.ValueKind.Should().Be(JsonValueKind.Array);
            return;
        }

        Assert.Fail("Expected a 'scenes' notification on stream_events immediately after opening the stream.");
    }

    [Fact]
    public async Task Sse_Root_Stream_Emits_ToolResult_When_Tool_Called()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        sseRequest.Headers.Accept.Clear();
        sseRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var sseResponse = await client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
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

        using var callResponse = await JsonRpcTestClient.PostMessageAsync(client, callPayload, cts.Token);
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
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-idle-test",
            method = "stream_events",
            @params = new { }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(payload)
        };

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
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
                return;
            }

            if (line is null)
            {
                Assert.Fail("stream_events HTTP stream ended unexpectedly while idle.");
            }
        }
    }

    [Fact]
    public async Task StreamEvents_Allows_Reconnect_After_Client_Close()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
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

            using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
            {
                Content = JsonRpcTestClient.CreateContent(payload)
            };

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            res.EnsureSuccessStatusCode();
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        }

        var pingPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-reconnect-ping",
            method = "ping",
            @params = new { }
        };
        using var pingRes = await JsonRpcTestClient.PostMessageAsync(client, pingPayload, cts.Token);
        pingRes.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StreamEvents_Emits_ToolResult_Notification_When_Tool_Called()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        var streamPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-tool-result-test",
            method = "stream_events",
            @params = new { }
        };

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(streamPayload)
        };

        using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
        streamResponse.EnsureSuccessStatusCode();

        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

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

        using var callResponse = await JsonRpcTestClient.PostMessageAsync(client, callPayload);
        callResponse.EnsureSuccessStatusCode();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(cts.Token); }
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

            if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "tool_result")
                continue;

            @params.TryGetProperty("payload", out var payload).Should().BeTrue();
            payload.TryGetProperty("name", out var name).Should().BeTrue();
            name.GetString().Should().NotBeNullOrWhiteSpace();
            payload.TryGetProperty("ok", out var ok).Should().BeTrue();
            (ok.ValueKind == JsonValueKind.True || ok.ValueKind == JsonValueKind.False).Should().BeTrue();

            var hasResult = payload.TryGetProperty("result", out _);
            var hasError = payload.TryGetProperty("error", out _);
            (hasResult || hasError).Should().BeTrue();

            return;
        }

        Assert.Fail("Expected a 'tool_result' notification on stream_events after invoking call_tool.");
    }

    [Fact]
    public async Task StreamEvents_Serializes_Many_Tool_Results_With_Backpressure()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var streamPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-stress",
            method = "stream_events",
            @params = new { }
        };

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(streamPayload)
        };

        using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        streamResponse.EnsureSuccessStatusCode();

        await using var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        const int totalCalls = 20;

        var callTasks = Enumerable.Range(0, totalCalls)
            .Select(i =>
            {
                var payload = new
                {
                    jsonrpc = "2.0",
                    id = $"stream-events-stress-{i}",
                    method = "call_tool",
                    @params = new
                    {
                        name = "GetStatus",
                        arguments = new { }
                    }
                };

                return JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
            })
            .ToArray();

        var readTask = Task.Run(async () =>
        {
            var seen = 0;
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            readCts.CancelAfter(TimeSpan.FromSeconds(10));

            while (!readCts.IsCancellationRequested && seen < totalCalls)
            {
                string? line;
                try { line = await reader.ReadLineAsync(readCts.Token); }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                root.TryGetProperty("jsonrpc", out var jsonrpc).Should().BeTrue();
                jsonrpc.GetString().Should().Be("2.0");

                if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "notification")
                    continue;

                if (!root.TryGetProperty("params", out var @params))
                    continue;

                if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "tool_result")
                    continue;

                @params.TryGetProperty("payload", out var payload).Should().BeTrue();
                payload.ValueKind.Should().Be(JsonValueKind.Object);
                payload.TryGetProperty("name", out var nameEl).Should().BeTrue();
                nameEl.GetString().Should().Be("GetStatus");
                payload.TryGetProperty("ok", out var okEl).Should().BeTrue();
                (okEl.ValueKind == JsonValueKind.True || okEl.ValueKind == JsonValueKind.False).Should().BeTrue();

                var hasResult = payload.TryGetProperty("result", out _);
                var hasError = payload.TryGetProperty("error", out _);
                (hasResult || hasError).Should().BeTrue();

                seen++;
            }

            return seen;
        });

        var responses = await Task.WhenAll(callTasks);
        foreach (var res in responses)
        {
            using (res)
            {
                res.EnsureSuccessStatusCode();
            }
        }

        var observed = await readTask;
        observed.Should().Be(totalCalls, $"expected {totalCalls} tool_result notifications but received {observed}");
    }

    [Fact]
    public async Task StreamEvents_Emits_Selection_With_Payload_Matching_Selection_Resource()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await JsonRpcTestClient.CallToolAsync(client, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)false }, cts.Token);

            var streamPayload = new
            {
                jsonrpc = "2.0",
                id = "stream-events-selection-test",
                method = "stream_events",
                @params = new { }
            };

            using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
            {
                Content = JsonRpcTestClient.CreateContent(streamPayload)
            };

            using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            streamResponse.EnsureSuccessStatusCode();

            await using var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var sceneId = await McpTestHelpers.TryGetFirstSceneIdAsync(client, cts.Token);
            var objectId = await McpTestHelpers.TryGetFirstObjectIdAsync(client, sceneId, cts.Token);
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            _ = await JsonRpcTestClient.CallToolAsync(client, "SelectObject", new { objectId }, cts.Token);

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

            var res = await client.GetAsync($"/read?uri={Uri.EscapeDataString("unity://selection")}", cts.Token);
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
                await JsonRpcTestClient.CallToolAsync(client, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, resetCts.Token);
            }
            catch { }
        }
    }

    [Fact]
    public async Task StreamEvents_Emits_Log_Notification_When_Error_Logged()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        var streamPayload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-log-test",
            method = "stream_events",
            @params = new { }
        };

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(streamPayload)
        };

        using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
        streamResponse.EnsureSuccessStatusCode();

        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var badPayload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-invalid",
            method = "call_tool",
            @params = new
            {
                name = "NonExistentTool",
                arguments = new { }
            }
        };

        using var badResponse = await JsonRpcTestClient.PostMessageAsync(client, badPayload);
        badResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(cts.Token); }
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

            if (!@params.TryGetProperty("event", out var eventEl) || eventEl.GetString() != "log")
                continue;

            @params.TryGetProperty("payload", out var payload).Should().BeTrue();
            payload.TryGetProperty("level", out var level).Should().BeTrue();
            level.GetString().Should().NotBeNullOrWhiteSpace();
            payload.TryGetProperty("message", out var message).Should().BeTrue();
            message.GetString().Should().NotBeNullOrWhiteSpace();
            payload.TryGetProperty("source", out var source).Should().BeTrue();
            source.GetString().Should().NotBeNullOrWhiteSpace();

            return;
        }

        Assert.Fail("Expected a 'log' notification on stream_events after triggering an MCP error.");
    }

    [Fact]
    public async Task StreamEvents_Notification_Has_Generic_Shape_When_Present()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        var payload = new
        {
            jsonrpc = "2.0",
            id = "stream-events-generic-shape-test",
            method = "stream_events",
            @params = new { }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/message")
        {
            Content = JsonRpcTestClient.CreateContent(payload)
        };

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
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

            return;
        }
    }
}
