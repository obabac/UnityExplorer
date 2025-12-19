using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class SetMemberValueTypesContractTests
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

    private static string? TryGetBlockId(JsonElement? spawn)
    {
        if (spawn is null) return null;
        var root = spawn.Value;
        if (!root.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array || blocks.GetArrayLength() == 0)
            return null;

        var first = blocks[0];
        if (first.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            return idProp.GetString();
        return null;
    }

    private static void AssertOk(JsonElement json)
    {
        json.TryGetProperty("ok", out var okProp).Should().BeTrue();
        okProp.ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public async Task SetMember_Writes_Vector2_And_Color_RoundTrip()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new
            {
                allowWrites = (bool?)true,
                requireConfirm = (bool?)false,
                reflectionAllowlistMembers = new[]
                {
                    "UnityEngine.RectTransform.anchoredPosition",
                    "UnityEngine.UI.Image.color"
                }
            }, cts.Token);

            var spawn = await CallToolAsync(http, "SpawnTestUi", new { confirm = true }, cts.Token);
            var blockId = TryGetBlockId(spawn);
            if (string.IsNullOrWhiteSpace(blockId)) return;

            var vectorJson = JsonSerializer.Serialize(new { x = 11, y = 22 });
            var setVector = await CallToolAsync(http, "SetMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.RectTransform",
                member = "anchoredPosition",
                jsonValue = vectorJson,
                confirm = true
            }, cts.Token);
            if (setVector is null) return;
            AssertOk(setVector.Value);

            var readVector = await CallToolAsync(http, "ReadComponentMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.RectTransform",
                name = "anchoredPosition"
            }, cts.Token);
            if (readVector is null) return;

            var vectorValue = readVector.Value.GetProperty("valueJson");
            vectorValue.GetProperty("x").GetDouble().Should().BeApproximately(11, 0.1);
            vectorValue.GetProperty("y").GetDouble().Should().BeApproximately(22, 0.1);

            var colorJson = JsonSerializer.Serialize(new { r = 0.1, g = 0.2, b = 0.3, a = 0.4 });
            var setColor = await CallToolAsync(http, "SetMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.UI.Image",
                member = "color",
                jsonValue = colorJson,
                confirm = true
            }, cts.Token);
            if (setColor is null) return;
            AssertOk(setColor.Value);

            var readColor = await CallToolAsync(http, "ReadComponentMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.UI.Image",
                name = "color"
            }, cts.Token);
            if (readColor is null) return;

            var colorValue = readColor.Value.GetProperty("valueJson");
            colorValue.GetProperty("r").GetDouble().Should().BeApproximately(0.1, 0.01);
            colorValue.GetProperty("g").GetDouble().Should().BeApproximately(0.2, 0.01);
            colorValue.GetProperty("b").GetDouble().Should().BeApproximately(0.3, 0.01);
            colorValue.GetProperty("a").GetDouble().Should().BeApproximately(0.4, 0.01);
        }
        finally
        {
            try { await CallToolAsync(http, "DestroyTestUi", new { confirm = true }, cts.Token); } catch { }
            try { await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, cts.Token); } catch { }
        }
    }
}
