using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcErrorsTests
{
    [Fact]
    public async Task Initialize_JsonRpc_Response_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
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

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
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
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "ping-test",
            method = "ping",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Notifications_Initialized_Is_Accepted_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "notifications-initialized-test",
            method = "notifications/initialized",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
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
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Cors_Preflight_Allows_Message_Posts()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        using var req = new HttpRequestMessage(HttpMethod.Options, "/message");
        req.Headers.Add("Origin", "http://example.com");
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "Content-Type, Authorization");

        using var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        res.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        origins.Should().Contain("*");
        res.Headers.TryGetValues("Access-Control-Allow-Methods", out var methods).Should().BeTrue();
        methods.Should().Contain(m => m.Contains("POST"));
        res.Headers.TryGetValues("Access-Control-Allow-Headers", out var headers).Should().BeTrue();
        headers.Should().Contain(h => h.IndexOf("content-type", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public async Task Cors_Headers_Are_Present_On_Message_And_Sse()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        var payload = new
        {
            jsonrpc = "2.0",
            id = "cors-ping",
            method = "ping",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload);
        res.EnsureSuccessStatusCode();
        res.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        origins.Should().Contain("*");

        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        sseRequest.Headers.Accept.Clear();
        sseRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var sseResponse = await client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        sseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        sseResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var sseOrigins).Should().BeTrue();
        sseOrigins.Should().Contain("*");
    }

    [Fact]
    public async Task RateLimit_Does_Not_Crash_Server_When_Many_Concurrent_Requests()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
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
            tasks.Add(client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token));
        }

        var responses = await Task.WhenAll(tasks);
        responses.Length.Should().BeGreaterThan(0);

        foreach (var res in responses)
        {
            if (res.IsSuccessStatusCode)
                continue;

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
}
