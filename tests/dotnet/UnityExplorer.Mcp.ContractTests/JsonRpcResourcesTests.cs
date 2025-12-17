using System;
using System.Net.Http;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcResourcesTests
{
    [Fact]
    public async Task ListResources_JsonRpc_Response_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-resources-test",
            method = "list_resources",
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
    public async Task ReadResource_Status_JsonRpc_Response_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
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

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
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
    public async Task Read_Endpoint_Includes_Cors_Headers()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;

        using var res = await client.GetAsync("/read?uri=unity://status");
        res.EnsureSuccessStatusCode();
        res.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        origins.Should().Contain("*");
    }

    [Fact]
    public async Task ReadResource_Unknown_Returns_NotFound_Error_With_Kind()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "read-resource-unknown",
            method = "read_resource",
            @params = new { uri = "unity://no-such-resource" }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
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
