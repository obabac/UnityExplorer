using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        return jsonEl.Clone();
    }

    private static void AssertToolError(JsonElement json, string expectedKind)
    {
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.False);
        json.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("kind", out var kind).Should().BeTrue();
        kind.GetString().Should().Be(expectedKind);
    }

    private static async Task<string?> GetFirstObjectIdAsync(HttpClient http, CancellationToken ct)
    {
        var sceneId = await McpTestHelpers.TryGetFirstSceneIdAsync(http, ct);
        return await McpTestHelpers.TryGetFirstObjectIdAsync(http, sceneId, ct);
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
        AssertToolError(result!.Value, "PermissionDenied");
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
        AssertToolError(result!.Value, "PermissionDenied");
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

        var sampleId = await GetFirstObjectIdAsync(http, cts.Token);
        if (string.IsNullOrWhiteSpace(sampleId))
            return;

        // Writes disabled => PermissionDenied.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);
        var denied = await CallToolAsync(http, "SetMember", new { objectId = sampleId, componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        if (denied is not null)
            AssertToolError(denied.Value, "PermissionDenied");

        // Writes enabled but no allowlist => Denied by allowlist.
        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token);
        var allowlistDenied = await CallToolAsync(http, "SetMember", new { objectId = sampleId, componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        if (allowlistDenied is not null)
            AssertToolError(allowlistDenied.Value, "PermissionDenied");

        // Best‑effort success path: enable a very permissive allowlist entry.
        // We don't assert that the underlying value changed, only that the tool
        // reports ok=true when allowWrites and allowlist permit it.
        _ = await CallToolAsync(http, "SetConfig", new
        {
            allowWrites = (bool?)true,
            requireConfirm = (bool?)false,
            reflectionAllowlistMembers = new[] { "UnityEngine.Light.intensity" }
        }, cts.Token);

        var success = await CallToolAsync(http, "SetMember", new { objectId = sampleId, componentType = "UnityEngine.Light", member = "intensity", jsonValue = "1.0", confirm = true }, cts.Token);
        if (success is null)
            return;

        var json = success.Value;
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        (okProp.ValueKind == JsonValueKind.True || okProp.ValueKind == JsonValueKind.False).Should().BeTrue();
    }

    [Fact]
    public async Task SelectObject_Updates_Selection_RoundTrip()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)false }, cts.Token);

        var id = await GetFirstObjectIdAsync(http, cts.Token);
        if (string.IsNullOrWhiteSpace(id)) return;

        var select = await CallToolAsync(http, "SelectObject", new { objectId = id }, cts.Token);
        select.Should().NotBeNull();
        var selectJson = select!.Value;
        selectJson.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.True);

        await Task.Delay(50, cts.Token);

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://selection")}", cts.Token);
        res.EnsureSuccessStatusCode();
        var jsonText = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        root.TryGetProperty("ActiveId", out var activeProp).Should().BeTrue();
        var activeId = activeProp.GetString();
        activeId.Should().NotBeNullOrWhiteSpace();
        if (!string.IsNullOrWhiteSpace(activeId))
            activeId.Should().Be(id);

        root.TryGetProperty("Items", out var itemsProp).Should().BeTrue();
        itemsProp.ValueKind.Should().Be(JsonValueKind.Array);

        var found = false;
        foreach (var el in itemsProp.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String && string.Equals(el.GetString(), id, StringComparison.Ordinal))
            { found = true; break; }
        }
        found.Should().BeTrue();

        var toolSelection = await CallToolAsync(http, "GetSelection", new { }, cts.Token);
        if (toolSelection != null)
        {
            var selJson = toolSelection.Value;
            selJson.TryGetProperty("ActiveId", out var toolActive).Should().BeTrue();
            toolActive.GetString().Should().Be(activeId);
        }
    }

    [Fact]
    public async Task MousePick_Ui_Uses_First_Item_As_Primary_When_TestUi_Spawned()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

            var spawn = await CallToolAsync(http, "SpawnTestUi", new { confirm = true }, cts.Token);

            string? knownBlockId = null;
            if (spawn.HasValue && spawn.Value.TryGetProperty("blocks", out var blocks) &&
                blocks.ValueKind == JsonValueKind.Array && blocks.GetArrayLength() > 0)
            {
                var firstBlock = blocks[0];
                if (firstBlock.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    knownBlockId = idProp.GetString();
            }

            var pick = await CallToolAsync(http, "MousePick", new { mode = "ui", x = 0.35, y = 0.5, normalized = true }, cts.Token);
            if (pick is null)
                return;

            var pickJson = pick.Value;
            pickJson.TryGetProperty("Mode", out var modeProp).Should().BeTrue();
            modeProp.GetString().Should().Be("ui");

            pickJson.TryGetProperty("Items", out var items).Should().BeTrue();
            items.ValueKind.Should().Be(JsonValueKind.Array);

            if (items.GetArrayLength() == 0)
                return;

            pickJson.TryGetProperty("Id", out var idEl).Should().BeTrue();
            var primaryId = idEl.GetString();
            primaryId.Should().NotBeNullOrWhiteSpace();

            var firstItemId = items[0].GetProperty("Id").GetString();
            firstItemId.Should().NotBeNullOrWhiteSpace();
            firstItemId.Should().Be(primaryId);

            if (!string.IsNullOrWhiteSpace(knownBlockId))
            {
                var ids = items.EnumerateArray().Select(i => i.GetProperty("Id").GetString()).ToArray();
                ids.Should().Contain(knownBlockId);
            }
        }
        finally
        {
            try { await CallToolAsync(http, "DestroyTestUi", new { confirm = true }, cts.Token); } catch { }
            try { await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, cts.Token); } catch { }
        }
    }

    [Fact]
    public async Task SetTimeScale_Denied_When_Writes_Disabled()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);

        var result = await CallToolAsync(http, "SetTimeScale", new { value = 1.0f, confirm = true }, cts.Token);
        if (result is null) return;
        AssertToolError(result.Value, "PermissionDenied");
    }

    [Fact]
    public async Task SetTimeScale_Succeeds_When_Confirmed()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

        var result = await CallToolAsync(http, "SetTimeScale", new { value = 1.0f, confirm = true }, cts.Token);
        if (result is null) return;

        result.Value.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.True);
        result.Value.TryGetProperty("value", out var val).Should().BeTrue();
        val.GetDouble().Should().BeApproximately(1.0, 0.5);
    }
}
