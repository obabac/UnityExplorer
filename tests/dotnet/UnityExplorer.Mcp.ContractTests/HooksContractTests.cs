using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class HooksContractTests
{
    private const string EnvFlag = "UE_MCP_HOOK_TEST_ENABLED";
    private const string AllowType = "UnityEngine.GameObject";

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
            id = $"hook-tool-{name}",
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

    private static async Task<JsonElement?> ReadHooksAsync(HttpClient http, CancellationToken ct)
    {
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://hooks")}", ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string? FindSignature(JsonElement root, string needle)
    {
        if (!root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("Signature", out var sigProp) || sigProp.ValueKind != JsonValueKind.String)
                continue;

            var sig = sigProp.GetString();
            if (string.IsNullOrWhiteSpace(sig))
                continue;

            if (sig.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return sig;
        }

        return items.EnumerateArray()
            .Select(i => i.TryGetProperty("Signature", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
    }

    [Fact]
    public async Task Hook_Lifecycle_Add_List_Remove_When_Flag_Enabled()
    {
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            return;

        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _ = await CallToolAsync(http, "SetConfig", new
        {
            allowWrites = (bool?)true,
            requireConfirm = (bool?)true,
            hookAllowlistSignatures = new[] { AllowType }
        }, cts.Token);

        var initialHooks = await ReadHooksAsync(http, cts.Token);
        if (initialHooks.HasValue)
        {
            var existingSig = FindSignature(initialHooks.Value, "SetActive") ?? FindSignature(initialHooks.Value, AllowType);
            if (!string.IsNullOrWhiteSpace(existingSig))
                _ = await CallToolAsync(http, "HookRemove", new { signature = existingSig, confirm = true }, cts.Token);
        }

        var added = await CallToolAsync(http, "HookAdd", new { type = AllowType, method = "SetActive", confirm = true }, cts.Token);
        added.Should().NotBeNull();
        var addedJson = added!.Value;
        addedJson.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.True);

        var hooks = await ReadHooksAsync(http, cts.Token);
        hooks.HasValue.Should().BeTrue();
        var hooksRoot = hooks!.Value;
        hooksRoot.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        items.GetArrayLength().Should().BeGreaterThan(0);

        string? signature = null;
        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("Signature", out var sigProp).Should().BeTrue();
            sigProp.ValueKind.Should().Be(JsonValueKind.String);
            item.TryGetProperty("Enabled", out var enabledProp).Should().BeTrue();
            (enabledProp.ValueKind == JsonValueKind.True || enabledProp.ValueKind == JsonValueKind.False).Should().BeTrue();

            var sigStr = sigProp.GetString();
            if (signature == null && !string.IsNullOrWhiteSpace(sigStr) && sigStr.Contains("SetActive", StringComparison.OrdinalIgnoreCase))
                signature = sigStr;
        }

        signature ??= FindSignature(hooksRoot, "SetActive");
        signature.Should().NotBeNullOrWhiteSpace();

        var removed = await CallToolAsync(http, "HookRemove", new { signature = signature!, confirm = true }, cts.Token);
        removed.Should().NotBeNull();
        var removedJson = removed!.Value;
        removedJson.TryGetProperty("ok", out var removeOk).Should().BeTrue();
        removeOk.ValueKind.Should().Be(JsonValueKind.True);

        var afterRemove = await ReadHooksAsync(http, cts.Token);
        if (afterRemove.HasValue && afterRemove.Value.TryGetProperty("Items", out var afterItems) && afterItems.ValueKind == JsonValueKind.Array)
        {
            var stillPresent = afterItems.EnumerateArray()
                .Select(i => i.TryGetProperty("Signature", out var sig) && sig.ValueKind == JsonValueKind.String ? sig.GetString() : null)
                .Any(s => string.Equals(s, signature, StringComparison.Ordinal));
            stillPresent.Should().BeFalse();
        }
    }
}
