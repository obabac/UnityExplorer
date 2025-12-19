using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class ClipboardContractTests
{
    private static HttpClient? TryCreateClient(out bool available)
    {
        available = Discovery.TryLoad(out var info);
        if (!available || info == null) return null;
        return new HttpClient { BaseAddress = info.EffectiveBaseUrl };
    }

    private static async Task<JsonElement?> CallToolAsync(HttpClient http, string name, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = $"clipboard-{name}-{Guid.NewGuid():N}",
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

    private static void AssertToolError(JsonElement json, string expectedKind)
    {
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.False);
        json.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("kind", out var kind).Should().BeTrue();
        kind.GetString().Should().Be(expectedKind);
    }

    [Fact]
    public async Task ListResources_Includes_Clipboard()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-resources-clipboard",
            method = "list_resources",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("resources", out var resources).Should().BeTrue();
        resources.ValueKind.Should().Be(JsonValueKind.Array);

        var found = resources.EnumerateArray().Any(r => string.Equals(r.GetProperty("uri").GetString(), "unity://clipboard", StringComparison.OrdinalIgnoreCase));
        found.Should().BeTrue();
    }

    [Fact]
    public async Task ReadResource_Clipboard_Returns_Shape()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://clipboard")}", cts.Token);
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("HasValue", out var hasValue).Should().BeTrue();
        hasValue.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("Type", out var type).Should().BeTrue();
        type.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        root.TryGetProperty("Preview", out var preview).Should().BeTrue();
        preview.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        root.TryGetProperty("ObjectId", out var objectId).Should().BeTrue();
        objectId.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
    }

    [Fact]
    public async Task Clipboard_Guarded_Write_Flow_Works_With_Confirm()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

            var denied = await CallToolAsync(http, "SetClipboardText", new { text = "hello" }, cts.Token);
            if (denied is not null)
                AssertToolError(denied.Value, "PermissionDenied");

            var set = await CallToolAsync(http, "SetClipboardText", new { text = "hello", confirm = true }, cts.Token);
            set.Should().NotBeNull();
            set!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://clipboard")}", cts.Token);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(cts.Token);
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                root.GetProperty("HasValue").GetBoolean().Should().BeTrue();
                var type = root.GetProperty("Type").GetString();
                type.Should().NotBeNullOrWhiteSpace();
                var preview = root.GetProperty("Preview").GetString();
                preview.Should().NotBeNull();
                preview!.ToLowerInvariant().Should().Contain("hello");
            }

            var cleared = await CallToolAsync(http, "ClearClipboard", new { confirm = true }, cts.Token);
            cleared.Should().NotBeNull();
            cleared!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var resCleared = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://clipboard")}", cts.Token);
            resCleared.EnsureSuccessStatusCode();
            var clearedJson = await resCleared.Content.ReadAsStringAsync(cts.Token);
            using var clearedDoc = JsonDocument.Parse(clearedJson);
            clearedDoc.RootElement.GetProperty("HasValue").GetBoolean().Should().BeFalse();
        }
        finally
        {
            try { await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, cts.Token); } catch { }
        }
    }
}
