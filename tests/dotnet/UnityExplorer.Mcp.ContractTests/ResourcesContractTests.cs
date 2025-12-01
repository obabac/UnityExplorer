using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class ResourcesContractTests
{
    private static HttpClient? TryCreateClient(out bool available)
    {
        available = Discovery.TryLoad(out var info);
        if (!available || info == null) return null;
        return new HttpClient { BaseAddress = info.EffectiveBaseUrl };
    }

    [Fact]
    public async Task Read_Scene_Objects_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scene/0/objects?limit=5")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Read_Selection_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://selection")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("ActiveId", out _).Should().BeTrue();
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Read_Camera_Active_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://camera/active")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Name", out _).Should().BeTrue();
        root.TryGetProperty("Pos", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Logs_Tail_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://logs/tail?count=20")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        // When logs are available, ensure they have the expected shape.
        if (items.GetArrayLength() > 0)
        {
            var first = items[0];
            first.TryGetProperty("T", out _).Should().BeTrue();
            first.TryGetProperty("Level", out _).Should().BeTrue();
            first.TryGetProperty("Message", out _).Should().BeTrue();
            first.TryGetProperty("Source", out var source).Should().BeTrue();
            source.GetString().Should().NotBeNullOrWhiteSpace();
            if (first.TryGetProperty("Category", out var cat))
            {
                cat.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
            }
        }
    }

    [Fact]
    public async Task Read_Camera_Active_Freecam_Info_Present()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://camera/active")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("IsFreecam", out var freecam).Should().BeTrue();
        freecam.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("Name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Search_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var uri = "unity://search?query=Player&limit=5";
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Read_Search_With_Filters_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        // First, fetch a small sample of objects from scene 0.
        var seedRes = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scene/0/objects?limit=10")}");
        seedRes.EnsureSuccessStatusCode();
        var seedJson = await seedRes.Content.ReadAsStringAsync();
        using var seedDoc = JsonDocument.Parse(seedJson);
        var seedRoot = seedDoc.RootElement;
        if (!seedRoot.TryGetProperty("Items", out var seedItems) || seedItems.GetArrayLength() == 0)
            return; // nothing to validate in this scene

        var sample = seedItems[0];
        var name = sample.GetProperty("Name").GetString() ?? string.Empty;
        var path = sample.GetProperty("Path").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return;

        // Use both name and a path fragment as filters.
        var pathFragment = path.Split('/').LastOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? path;
        var uri = $"unity://search?name={Uri.EscapeDataString(name)}&path={Uri.EscapeDataString(pathFragment)}&limit=10";
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("Name", out var n).Should().BeTrue();
            item.TryGetProperty("Path", out var p).Should().BeTrue();
            var nStr = n.GetString() ?? string.Empty;
            var pStr = p.GetString() ?? string.Empty;
            nStr.Contains(name, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            pStr.Contains(pathFragment, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Logs_Tail_Includes_Mcp_Error_Lines_When_Errors_Occur()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        // Trigger a simple JSON‑RPC error by omitting the 'method' field.
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var payload = new
            {
                jsonrpc = "2.0",
                id = "mcp-error-test"
                // no method => Invalid request
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8);
            using var res = await http.PostAsync("/message", content, cts.Token);
        }

        // Read logs and look for an [MCP] error line.
        var resLogs = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://logs/tail?count=200")}");
        resLogs.EnsureSuccessStatusCode();
        var jsonLogs = await resLogs.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonLogs);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);

        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("Message", out var msgEl))
                continue;
            var msg = msgEl.GetString() ?? string.Empty;
            if (msg.Contains("[MCP] error", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        found.Should().BeTrue();
    }

    [Fact]
    public async Task Read_Object_And_Components_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        // Discover a sample object from scene 0.
        var sceneRes = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scene/0/objects?limit=1")}");
        sceneRes.EnsureSuccessStatusCode();
        var sceneJson = await sceneRes.Content.ReadAsStringAsync();
        using var sceneDoc = JsonDocument.Parse(sceneJson);
        var sceneRoot = sceneDoc.RootElement;
        if (!sceneRoot.TryGetProperty("Items", out var sceneItems) || sceneItems.GetArrayLength() == 0)
            return; // nothing to validate in this scene

        var first = sceneItems[0];
        first.TryGetProperty("Id", out var idProp).Should().BeTrue();
        var id = idProp.GetString();
        id.Should().NotBeNullOrWhiteSpace();

        // Read the object card via /read.
        var objUri = $"unity://object/{id}";
        var objRes = await http.GetAsync($"/read?uri={Uri.EscapeDataString(objUri)}");
        objRes.EnsureSuccessStatusCode();
        var objJson = await objRes.Content.ReadAsStringAsync();
        using var objDoc = JsonDocument.Parse(objJson);
        var objRoot = objDoc.RootElement;
        objRoot.TryGetProperty("Id", out _).Should().BeTrue();
        objRoot.TryGetProperty("Name", out _).Should().BeTrue();
        objRoot.TryGetProperty("Path", out _).Should().BeTrue();
        objRoot.TryGetProperty("Active", out _).Should().BeTrue();

        // Read the components page via /read.
        var compUri = $"unity://object/{id}/components?limit=8";
        var compRes = await http.GetAsync($"/read?uri={Uri.EscapeDataString(compUri)}");
        compRes.EnsureSuccessStatusCode();
        var compJson = await compRes.Content.ReadAsStringAsync();
        using var compDoc = JsonDocument.Parse(compJson);
        var compRoot = compDoc.RootElement;
        compRoot.TryGetProperty("Items", out var comps).Should().BeTrue();
        comps.ValueKind.Should().Be(JsonValueKind.Array);
        if (comps.GetArrayLength() > 0)
        {
            var c0 = comps[0];
            c0.TryGetProperty("Type", out _).Should().BeTrue();
            // Summary may be null or string; just ensure the property exists.
            c0.TryGetProperty("Summary", out _).Should().BeTrue();
        }
    }
}
