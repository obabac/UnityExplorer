using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class StaticClassSearchContractTests
{
    private static HttpClient? TryCreateClient(out bool available)
    {
        available = Discovery.TryLoad(out var info);
        if (!available || info == null) return null;
        return new HttpClient { BaseAddress = info.EffectiveBaseUrl };
    }

    [Fact]
    public async Task Tools_List_Includes_StaticClass_Search_Tools()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-static-classes",
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

        var searchTool = tools.EnumerateArray().FirstOrDefault(t => string.Equals(t.GetProperty("name").GetString(), "SearchStaticClasses", StringComparison.OrdinalIgnoreCase));
        searchTool.ValueKind.Should().Be(JsonValueKind.Object);
        searchTool.TryGetProperty("inputSchema", out var searchSchema).Should().BeTrue();
        searchSchema.TryGetProperty("required", out var searchReq).Should().BeTrue();
        searchReq.EnumerateArray().Select(e => e.GetString()).Should().Contain("query");

        var membersTool = tools.EnumerateArray().FirstOrDefault(t => string.Equals(t.GetProperty("name").GetString(), "ListStaticMembers", StringComparison.OrdinalIgnoreCase));
        membersTool.ValueKind.Should().Be(JsonValueKind.Object);
        membersTool.TryGetProperty("inputSchema", out var membersSchema).Should().BeTrue();
        membersSchema.TryGetProperty("required", out var membersReq).Should().BeTrue();
        membersReq.EnumerateArray().Select(e => e.GetString()).Should().Contain("typeFullName");
    }

    [Fact]
    public async Task Resources_List_Includes_StaticClass_Search()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var payload = new { jsonrpc = "2.0", id = "list-res-static-classes", method = "list_resources", @params = new { } };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("resources", out var resources).Should().BeTrue();
        resources.ValueKind.Should().Be(JsonValueKind.Array);

        resources.EnumerateArray()
            .Any(r => string.Equals(r.GetProperty("uri").GetString(), "unity://search/static-classes", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
        resources.EnumerateArray()
            .Any(r => string.Equals(r.GetProperty("uri").GetString(), "unity://type/{typeFullName}/static-members", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Read_Static_Class_Search_Returns_Page_Shape()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = "unity://search/static-classes?query=Unity&limit=5";
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
        item.TryGetProperty("Id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("Type", out var type).Should().BeTrue();
        type.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("Assembly", out var asm).Should().BeTrue();
        asm.ValueKind.Should().Be(JsonValueKind.String);
        item.TryGetProperty("MemberCount", out var members).Should().BeTrue();
        members.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task CallTool_ListStaticMembers_Returns_Page_Shape()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = "unity://search/static-classes?query=Unity&limit=5";
        using var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}", cts.Token);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return;
        var typeFullName = items[0].GetProperty("Type").GetString();
        if (string.IsNullOrEmpty(typeFullName)) return;

        if (!JsonRpcTestClient.TryCreate(out var rpcClient))
            return;

        using var client = rpcClient;
        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-static-members",
            method = "call_tool",
            @params = new
            {
                name = "ListStaticMembers",
                arguments = new { typeFullName = typeFullName, includeMethods = true, limit = 5, offset = 0 }
            }
        };

        using var rpcRes = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        rpcRes.EnsureSuccessStatusCode();
        var body = await rpcRes.Content.ReadAsStringAsync(cts.Token);
        using var rpcDoc = JsonDocument.Parse(body);
        var rpcRoot = rpcDoc.RootElement;
        rpcRoot.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        jsonEl.TryGetProperty("Total", out var total).Should().BeTrue();
        total.ValueKind.Should().Be(JsonValueKind.Number);
        jsonEl.TryGetProperty("Items", out var rpcItems).Should().BeTrue();
        rpcItems.ValueKind.Should().Be(JsonValueKind.Array);
        if (rpcItems.GetArrayLength() == 0)
            return;

        var member = rpcItems[0];
        member.TryGetProperty("Name", out var name).Should().BeTrue();
        name.ValueKind.Should().Be(JsonValueKind.String);
        member.TryGetProperty("Kind", out var kind).Should().BeTrue();
        kind.ValueKind.Should().Be(JsonValueKind.String);
        member.TryGetProperty("Type", out var typeProp).Should().BeTrue();
        typeProp.ValueKind.Should().Be(JsonValueKind.String);
        member.TryGetProperty("CanRead", out var canRead).Should().BeTrue();
        canRead.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        member.TryGetProperty("CanWrite", out var canWrite).Should().BeTrue();
        canWrite.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }
}
