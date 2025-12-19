using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class ComponentTypeMatchingContractTests
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
    public async Task CallMethod_And_SetMember_Allow_Base_ComponentType()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new
            {
                allowWrites = (bool?)true,
                requireConfirm = (bool?)true,
                reflectionAllowlistMembers = new[]
                {
                    "UnityEngine.Transform.GetInstanceID",
                    "UnityEngine.Transform.localScale"
                }
            }, cts.Token);

            var spawn = await CallToolAsync(http, "SpawnTestUi", new { confirm = true }, cts.Token);
            var blockId = TryGetBlockId(spawn);
            if (string.IsNullOrWhiteSpace(blockId)) return;

            var call = await CallToolAsync(http, "CallMethod", new
            {
                objectId = blockId,
                componentType = "UnityEngine.Transform",
                method = "GetInstanceID",
                argsJson = "[]",
                confirm = true
            }, cts.Token);
            if (call is null) return;
            AssertOk(call.Value);
            call.Value.TryGetProperty("result", out var resultProp).Should().BeTrue();
            resultProp.ValueKind.Should().NotBe(JsonValueKind.Undefined);

            var jsonValue = JsonSerializer.Serialize(new { x = 1.1, y = 1.2, z = 1.3 });
            var setMember = await CallToolAsync(http, "SetMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.Transform",
                member = "localScale",
                jsonValue,
                confirm = true
            }, cts.Token);
            if (setMember is null) return;
            AssertOk(setMember.Value);

            var read = await CallToolAsync(http, "ReadComponentMember", new
            {
                objectId = blockId,
                componentType = "UnityEngine.RectTransform",
                name = "localScale"
            }, cts.Token);
            if (read is null) return;

            read.Value.TryGetProperty("valueJson", out var valueJson).Should().BeTrue();
            valueJson.GetProperty("x").GetDouble().Should().BeApproximately(1.1, 0.05);
            valueJson.GetProperty("y").GetDouble().Should().BeApproximately(1.2, 0.05);
            valueJson.GetProperty("z").GetDouble().Should().BeApproximately(1.3, 0.05);
        }
        finally
        {
            try { await CallToolAsync(http, "DestroyTestUi", new { confirm = true }, cts.Token); } catch { }
            try { await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true, reflectionAllowlistMembers = Array.Empty<string>() }, cts.Token); } catch { }
        }
    }
}
