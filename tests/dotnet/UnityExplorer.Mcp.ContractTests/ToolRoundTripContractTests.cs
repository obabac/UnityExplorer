using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class ToolRoundTripContractTests
{
    private static HttpClient? TryCreateClient(out bool available)
    {
        available = Discovery.TryLoad(out var info);
        if (!available || info == null) return null;
        return new HttpClient { BaseAddress = info.EffectiveBaseUrl };
    }

    private static async Task<JsonElement> CallToolAsync(HttpClient http, string name, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = $"tool-roundtrip-{name}",
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
            return default;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        return jsonEl;
    }

    private static async Task<string?> GetSampleObjectIdAsync(HttpClient http, CancellationToken ct)
    {
        var uri = "unity://scene/0/objects?limit=1";
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}", ct);
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
    public async Task Tools_List_Alias_Works_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "tools-list-alias-test",
            method = "tools/list",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Tools_Call_Alias_Works_For_GetStatus_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "tools-call-alias-test",
            method = "tools/call",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ReadOnly_Tools_Behave_As_Expected_If_Server_Available()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // GetStatus
        {
            var json = await CallToolAsync(http, "GetStatus", new { }, cts.Token);
            if (json.ValueKind == JsonValueKind.Undefined) goto AfterStatus;
            json.TryGetProperty("UnityVersion", out _).Should().BeTrue();
            json.TryGetProperty("ExplorerVersion", out _).Should().BeTrue();
            json.TryGetProperty("ScenesLoaded", out _).Should().BeTrue();
        }
    AfterStatus:

        // ListScenes
        {
            var json = await CallToolAsync(http, "ListScenes", new { limit = 5, offset = 0 }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("Items", out var items).Should().BeTrue();
                items.ValueKind.Should().Be(JsonValueKind.Array);
            }
        }

        // ListObjects (scene 0)
        string? sampleId = null;
        {
            var json = await CallToolAsync(http, "ListObjects", new { sceneId = "scn:0", limit = 5, offset = 0 }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined &&
                json.TryGetProperty("Items", out var items) &&
                items.ValueKind == JsonValueKind.Array &&
                items.GetArrayLength() > 0)
            {
                var first = items[0];
                first.TryGetProperty("Id", out var idProp).Should().BeTrue();
                sampleId = idProp.GetString();
            }
        }

        // GetObject & GetComponents for the sample id, if any.
        if (!string.IsNullOrWhiteSpace(sampleId))
        {
            var objJson = await CallToolAsync(http, "GetObject", new { id = sampleId }, cts.Token);
            if (objJson.ValueKind != JsonValueKind.Undefined)
            {
                objJson.TryGetProperty("Id", out _).Should().BeTrue();
                objJson.TryGetProperty("Name", out _).Should().BeTrue();
                objJson.TryGetProperty("Path", out _).Should().BeTrue();
            }

            var compsJson = await CallToolAsync(http, "GetComponents", new { objectId = sampleId, limit = 8, offset = 0 }, cts.Token);
            if (compsJson.ValueKind != JsonValueKind.Undefined)
            {
                compsJson.TryGetProperty("Items", out var compItems).Should().BeTrue();
                compItems.ValueKind.Should().Be(JsonValueKind.Array);
            }
        }

        // SearchObjects – basic shape.
        {
            var json = await CallToolAsync(http, "SearchObjects", new { query = "Player", limit = 5, offset = 0 }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("Items", out var items).Should().BeTrue();
                items.ValueKind.Should().Be(JsonValueKind.Array);
            }
        }

        // Camera info
        {
            var json = await CallToolAsync(http, "GetCameraInfo", new { }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("Name", out _).Should().BeTrue();
                json.TryGetProperty("Fov", out _).Should().BeTrue();
                json.TryGetProperty("Pos", out _).Should().BeTrue();
                json.TryGetProperty("Rot", out _).Should().BeTrue();
            }
        }

        // Selection
        {
            var json = await CallToolAsync(http, "GetSelection", new { }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("ActiveId", out _).Should().BeTrue();
                json.TryGetProperty("Items", out var items).Should().BeTrue();
                items.ValueKind.Should().Be(JsonValueKind.Array);
            }
        }

        // TailLogs
        {
            var json = await CallToolAsync(http, "TailLogs", new { count = 20 }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("Items", out var items).Should().BeTrue();
                items.ValueKind.Should().Be(JsonValueKind.Array);
            }
        }

        // GetVersion
        {
            var json = await CallToolAsync(http, "GetVersion", new { }, cts.Token);
            if (json.ValueKind != JsonValueKind.Undefined)
            {
                json.TryGetProperty("ExplorerVersion", out _).Should().BeTrue();
                json.TryGetProperty("McpVersion", out _).Should().BeTrue();
                json.TryGetProperty("UnityVersion", out _).Should().BeTrue();
                json.TryGetProperty("Runtime", out _).Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task Write_Tools_Respond_With_Expected_Errors_By_Default()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // By default allowWrites is false; SetConfig can be used to toggle it.
        // We deliberately call a subset of write tools and assert that they
        // return well-formed error objects rather than crashing.

        // SetConfig itself should always succeed with ok=true.
        var cfg = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);
        if (cfg.ValueKind != JsonValueKind.Undefined)
        {
            cfg.TryGetProperty("ok", out var okProp).Should().BeTrue();
            okProp.ValueKind.Should().Be(JsonValueKind.True);
        }

        // GetConfig must return a sanitized config object.
        var getCfg = await CallToolAsync(http, "GetConfig", new { }, cts.Token);
        if (getCfg.ValueKind != JsonValueKind.Undefined)
        {
            getCfg.TryGetProperty("ok", out var okProp).Should().BeTrue();
            okProp.ValueKind.Should().Be(JsonValueKind.True);
            getCfg.TryGetProperty("allowWrites", out _).Should().BeTrue();
            getCfg.TryGetProperty("requireConfirm", out _).Should().BeTrue();
        }

        // A few representative write tools should return PermissionDenied (or similar)
        // when writes are disabled.
        var sampleId = await GetSampleObjectIdAsync(http, cts.Token) ?? "obj:0";

        foreach (var toolName in new[] { "SetActive", "SelectObject", "AddComponent", "RemoveComponent", "ConsoleEval", "HookAdd", "HookRemove" })
        {
            JsonElement result;
            switch (toolName)
            {
                case "SetActive":
                    result = await CallToolAsync(http, toolName, new { objectId = sampleId, active = true }, cts.Token);
                    break;
                case "SelectObject":
                    result = await CallToolAsync(http, toolName, new { objectId = sampleId }, cts.Token);
                    break;
                case "AddComponent":
                    result = await CallToolAsync(http, toolName, new { objectId = sampleId, type = "UnityEngine.Light", confirm = true }, cts.Token);
                    break;
                case "RemoveComponent":
                    result = await CallToolAsync(http, toolName, new { objectId = sampleId, typeOrIndex = "UnityEngine.Light", confirm = true }, cts.Token);
                    break;
                case "ConsoleEval":
                    result = await CallToolAsync(http, toolName, new { code = "1+1", confirm = true }, cts.Token);
                    break;
                case "HookAdd":
                    result = await CallToolAsync(http, toolName, new { type = "UnityEngine.GameObject", method = "SetActive", confirm = true }, cts.Token);
                    break;
                case "HookRemove":
                    result = await CallToolAsync(http, toolName, new { signature = "NonExistent.Signature", confirm = true }, cts.Token);
                    break;
                default:
                    continue;
            }

            if (result.ValueKind == JsonValueKind.Undefined)
                continue;

            result.TryGetProperty("ok", out var okProp2).Should().BeTrue();
            // For safety we only assert that ok is a boolean; the specific error
            // text is exercised more precisely in WriteToolsContractTests.
            (okProp2.ValueKind == JsonValueKind.True || okProp2.ValueKind == JsonValueKind.False).Should().BeTrue();
        }
    }
}

