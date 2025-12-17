using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

internal static class JsonRpcTestClient
{
    public static bool TryCreate(out HttpClient http)
    {
        http = null!;
        if (!Discovery.TryLoad(out var info))
            return false;

        http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        return true;
    }

    public static StringContent CreateContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public static Task<HttpResponseMessage> PostMessageAsync(HttpClient http, object payload, CancellationToken ct = default)
    {
        return http.PostAsync("/message", CreateContent(payload), ct);
    }

    public static async Task<JsonElement?> CallToolAsync(HttpClient http, string name, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = $"jsonrpc-call-{name}-{Guid.NewGuid():N}",
            method = "call_tool",
            @params = new
            {
                name,
                arguments
            }
        };

        using var res = await PostMessageAsync(http, payload, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("result", out var result))
            return null;
        if (!result.TryGetProperty("content", out var contentArr) || contentArr.ValueKind != JsonValueKind.Array || contentArr.GetArrayLength() == 0)
            return null;

        var first = contentArr[0];
        return first.TryGetProperty("json", out var jsonEl) ? jsonEl.Clone() : (JsonElement?)null;
    }
}
