using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class CallMethodContractTests
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
            id = $"call-method-{name}",
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

    private static void AssertToolError(JsonElement? json, string expectedKind)
    {
        json.HasValue.Should().BeTrue();
        var root = json!.Value;
        root.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.False);
        root.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("kind", out var kind).Should().BeTrue();
        kind.GetString().Should().Be(expectedKind);
    }

    private static async Task<string?> GetFirstObjectIdAsync(HttpClient http, CancellationToken ct)
    {
        var sceneId = await McpTestHelpers.TryGetFirstSceneIdAsync(http, ct);
        return await McpTestHelpers.TryGetFirstObjectIdAsync(http, sceneId, ct);
    }

    [Fact]
    public async Task CallMethod_Denied_When_Writes_Disabled()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);

            var oid = await GetFirstObjectIdAsync(http, cts.Token);
            if (string.IsNullOrWhiteSpace(oid)) return;

            var res = await CallToolAsync(http, "CallMethod", new { objectId = oid, componentType = "UnityEngine.Transform", method = "GetInstanceID", argsJson = "[]", confirm = true }, cts.Token);
            AssertToolError(res, "PermissionDenied");
        }
        finally
        {
            try { _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token); } catch { }
        }
    }

    [Fact]
    public async Task CallMethod_Denied_When_Confirm_Missing()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new
            {
                allowWrites = (bool?)true,
                requireConfirm = (bool?)true,
                reflectionAllowlistMembers = new[] { "UnityEngine.Transform.GetInstanceID" }
            }, cts.Token);

            var oid = await GetFirstObjectIdAsync(http, cts.Token);
            if (string.IsNullOrWhiteSpace(oid)) return;

            var res = await CallToolAsync(http, "CallMethod", new { objectId = oid, componentType = "UnityEngine.Transform", method = "GetInstanceID", argsJson = "[]" }, cts.Token);
            AssertToolError(res, "PermissionDenied");
        }
        finally
        {
            try { _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token); } catch { }
        }
    }

    [Fact]
    public async Task CallMethod_Denied_When_Allowlist_Missing()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)false, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token);

            var oid = await GetFirstObjectIdAsync(http, cts.Token);
            if (string.IsNullOrWhiteSpace(oid)) return;

            var res = await CallToolAsync(http, "CallMethod", new { objectId = oid, componentType = "UnityEngine.Transform", method = "GetInstanceID", argsJson = "[]", confirm = true }, cts.Token);
            AssertToolError(res, "PermissionDenied");
        }
        finally
        {
            try { _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token); } catch { }
        }
    }

    [Fact]
    public async Task CallMethod_Succeeds_With_Allowlist()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new
            {
                allowWrites = (bool?)true,
                requireConfirm = (bool?)true,
                reflectionAllowlistMembers = new[] { "UnityEngine.Transform.GetInstanceID" }
            }, cts.Token);

            var oid = await GetFirstObjectIdAsync(http, cts.Token);
            if (string.IsNullOrWhiteSpace(oid)) return;

            var res = await CallToolAsync(http, "CallMethod", new { objectId = oid, componentType = "UnityEngine.Transform", method = "GetInstanceID", argsJson = "[]", confirm = true }, cts.Token);
            res.Should().NotBeNull();
            res!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);
            res.Value.TryGetProperty("result", out var resultProp).Should().BeTrue();
            resultProp.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Number, JsonValueKind.Null);
        }
        finally
        {
            try { _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token); } catch { }
        }
    }
}
