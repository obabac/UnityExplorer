using System.Net.Http;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class ScenesContractTests
{
    [Fact]
    public async Task Read_Scenes_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info)) return;
        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scenes")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CallTool_ListBuildScenes_Returns_Page_Shape()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-listbuildscenes",
            method = "call_tool",
            @params = new
            {
                name = "ListBuildScenes",
                arguments = new { limit = 5, offset = 0 }
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
        jsonEl.TryGetProperty("Total", out var total).Should().BeTrue();
        (total.ValueKind == JsonValueKind.Number).Should().BeTrue();
        jsonEl.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task CallTool_LoadScene_Is_Guarded_When_Writes_Disabled()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-loadscene-guarded",
            method = "call_tool",
            @params = new
            {
                name = "LoadScene",
                arguments = new { name = "TestScene", mode = "single" }
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
        jsonEl.TryGetProperty("ok", out var ok).Should().BeTrue();
        ok.ValueKind.Should().Be(JsonValueKind.False);
        jsonEl.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("kind", out var kind).Should().BeTrue();
        kind.GetString().Should().Be("PermissionDenied");
    }
}

