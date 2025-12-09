using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

internal static class McpTestHelpers
{
    public static async Task<string?> TryGetFirstSceneIdAsync(HttpClient http, CancellationToken ct)
    {
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scenes?limit=1")}", ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return null;

        var first = items[0];
        return first.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
    }

    public static async Task<string?> TryGetFirstObjectIdAsync(HttpClient http, string? sceneId, CancellationToken ct, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            return null;

        var uri = $"unity://scene/{sceneId}/objects?limit={limit}";
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}", ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Items", out var items) || items.GetArrayLength() == 0)
            return null;

        var first = items[0];
        return first.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
    }
}
