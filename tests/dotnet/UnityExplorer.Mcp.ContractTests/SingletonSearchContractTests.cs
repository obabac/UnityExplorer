using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class SingletonSearchContractTests
{
    private static HttpClient? TryCreateClient(out bool available)
    {
        available = Discovery.TryLoad(out var info);
        if (!available || info == null) return null;
        return new HttpClient { BaseAddress = info.EffectiveBaseUrl };
    }

    [Fact]
    public async Task Tools_List_Includes_Singleton_Search_With_Required_Query()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-singletons",
            method = "list_tools",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);

        var singletonTool = tools
            .EnumerateArray()
            .FirstOrDefault(t => string.Equals(t.GetProperty("name").GetString(), "SearchSingletons", StringComparison.OrdinalIgnoreCase));

        singletonTool.ValueKind.Should().Be(JsonValueKind.Object);
        singletonTool.TryGetProperty("inputSchema", out var schema).Should().BeTrue();
        schema.TryGetProperty("properties", out var props).Should().BeTrue();
        props.TryGetProperty("query", out var queryProp).Should().BeTrue();
        queryProp.GetProperty("type").GetString().Should().Be("string");
        schema.TryGetProperty("required", out var required).Should().BeTrue();
        required.EnumerateArray().Select(e => e.GetString()).Should().Contain("query");
    }

    [Fact]
    public async Task CallTool_SearchSingletons_Returns_Page_Shape()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-singletons",
            method = "call_tool",
            @params = new
            {
                name = "SearchSingletons",
                arguments = new { query = "Unity", limit = 5, offset = 0 }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
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
        var page = jsonEl;
        page.TryGetProperty("Total", out var total).Should().BeTrue();
        total.ValueKind.Should().Be(JsonValueKind.Number);
        page.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        if (items.GetArrayLength() == 0)
            return;

        var item = items[0];
        item.TryGetProperty("Id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("DeclaringType", out var decl).Should().BeTrue();
        decl.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("InstanceType", out var inst).Should().BeTrue();
        inst.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("Preview", out var preview).Should().BeTrue();
        preview.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("ObjectId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Resources_List_Includes_Singleton_Search()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new { jsonrpc = "2.0", id = "list-res-singletons", method = "list_resources", @params = new { } };
        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("resources", out var resources).Should().BeTrue();
        resources.ValueKind.Should().Be(JsonValueKind.Array);

        resources.EnumerateArray()
            .Any(r => string.Equals(r.GetProperty("uri").GetString(), "unity://search/singletons", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Read_Singleton_Search_Returns_Page_Shape()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = "unity://search/singletons?query=Unity&limit=5";
        using var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}", cts.Token);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Total", out var total).Should().BeTrue();
        total.ValueKind.Should().Be(JsonValueKind.Number);
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        if (items.GetArrayLength() == 0)
            return;

        var item = items[0];
        item.TryGetProperty("Id", out _).Should().BeTrue();
        item.TryGetProperty("DeclaringType", out _).Should().BeTrue();
        item.TryGetProperty("InstanceType", out _).Should().BeTrue();
        item.TryGetProperty("Preview", out _).Should().BeTrue();
        item.TryGetProperty("ObjectId", out _).Should().BeTrue();
    }
}
