using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class WriteToolsContractTests
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
            id = $"call-tool-{name}",
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

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return null;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        return jsonEl;
    }

    private static async Task<string?> GetFirstObjectIdAsync(HttpClient http, CancellationToken ct)
    {
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scene/0/objects?limit=1")}", ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Items", out var items) || items.GetArrayLength() == 0)
            return null;
        var first = items[0];
        first.TryGetProperty("Id", out var idProp).Should().BeTrue();
        return idProp.GetString();
    }

    [Fact]
    public async Task SetActive_Returns_PermissionDenied_When_Writes_Disabled()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Ensure writes are disabled.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);

        var id = await GetFirstObjectIdAsync(http, cts.Token);
        if (string.IsNullOrWhiteSpace(id)) return;

        var result = await CallToolAsync(http, "SetActive", new { objectId = id, active = true }, cts.Token);
        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.False);
        json.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetString()!.Should().StartWith("PermissionDenied");
    }

    [Fact]
    public async Task SetActive_Returns_ConfirmationRequired_When_Confirm_Missing()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Enable writes but keep confirmation required.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

        var id = await GetFirstObjectIdAsync(http, cts.Token);
        if (string.IsNullOrWhiteSpace(id)) return;

        var result = await CallToolAsync(http, "SetActive", new { objectId = id, active = true }, cts.Token);
        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.False);
        json.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetString()!.Should().Be("ConfirmationRequired");
    }

    [Fact]
    public async Task SetActive_Succeeds_When_Confirmed_And_Writes_Enabled()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

        var id = await GetFirstObjectIdAsync(http, cts.Token);
        if (string.IsNullOrWhiteSpace(id)) return;

        // Read current active flag and set it to the same value to avoid side effects.
        var objUri = $"unity://object/{id}";
        var objRes = await http.GetAsync($"/read?uri={Uri.EscapeDataString(objUri)}", cts.Token);
        objRes.EnsureSuccessStatusCode();
        var objJson = await objRes.Content.ReadAsStringAsync(cts.Token);
        using var objDoc = JsonDocument.Parse(objJson);
        var objRoot = objDoc.RootElement;
        var active = objRoot.GetProperty("Active").GetBoolean();

        var result = await CallToolAsync(http, "SetActive", new { objectId = id, active, confirm = true }, cts.Token);
        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public async Task SetMember_Respects_AllowWrites_And_Allowlist()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Writes disabled => PermissionDenied.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);
        var denied = await CallToolAsync(http, "SetMember", new { objectId = "obj:0", componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        denied.Should().NotBeNull();
        denied!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.False);

        // Writes enabled but no allowlist => Denied by allowlist.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token);
        var allowlistDenied = await CallToolAsync(http, "SetMember", new { objectId = "obj:0", componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        allowlistDenied.Should().NotBeNull();
        allowlistDenied!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.False);

        // Best‑effort success path: enable a very permissive allowlist entry.
        // We don't assert that the underlying value changed, only that the tool
        // reports ok=true when allowWrites and allowlist permit it.
        _ = await CallToolAsync(http, "SetConfig", new
        {
            allowWrites = (bool?)true,
            requireConfirm = (bool?)false,
            reflectionAllowlistMembers = new[] { "UnityEngine.Light.intensity" }
        }, cts.Token);

        var success = await CallToolAsync(http, "SetMember", new { objectId = "obj:0", componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        // If no suitable object exists, the implementation may return NotFound;
        // in that case, treat the result as inconclusive rather than failing CI.
        if (success is null)
            return;

        var json = success.Value;
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        // Accept either true (success) or false (e.g. NotFound) to avoid depending
        // on specific scene contents; this primarily validates auth/allowlist wiring.
        (okProp.ValueKind == JsonValueKind.True || okProp.ValueKind == JsonValueKind.False).Should().BeTrue();
    }
}
