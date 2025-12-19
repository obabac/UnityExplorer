using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace UnityExplorer.Mcp.ContractTests;

public class FreecamContractTests
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
            id = $"call-tool-{name}-{Guid.NewGuid():N}",
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

    [Fact]
    public async Task Resources_List_Includes_Freecam()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var payload = new { jsonrpc = "2.0", id = "list-res-freecam", method = "list_resources" };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, cts.Token);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("resources", out var resources).Should().BeTrue();
        resources.ValueKind.Should().Be(JsonValueKind.Array);
        resources.EnumerateArray().Any(r => r.TryGetProperty("uri", out var uri) && string.Equals(uri.GetString(), "unity://freecam", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Freecam_Has_Expected_Shape()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://freecam")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("Enabled", out var enabled).Should().BeTrue();
        enabled.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("UsingGameCamera", out var usingGameCam).Should().BeTrue();
        usingGameCam.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("Speed", out var speed).Should().BeTrue();
        speed.ValueKind.Should().Be(JsonValueKind.Number);

        root.TryGetProperty("Pos", out var pos).Should().BeTrue();
        pos.TryGetProperty("X", out _).Should().BeTrue();
        pos.TryGetProperty("Y", out _).Should().BeTrue();
        pos.TryGetProperty("Z", out _).Should().BeTrue();

        root.TryGetProperty("Rot", out var rot).Should().BeTrue();
        rot.TryGetProperty("X", out _).Should().BeTrue();
        rot.TryGetProperty("Y", out _).Should().BeTrue();
        rot.TryGetProperty("Z", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SetFreecamEnabled_Denied_When_Writes_Disabled()
    {
        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);

        var result = await CallToolAsync(http, "SetFreecamEnabled", new { enabled = true }, cts.Token);
        if (result is null) return;
        AssertToolError(result.Value, "PermissionDenied");
    }

    [Fact]
    public async Task Freecam_Stateful_RoundTrip_When_Enabled()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UE_MCP_FREECAM_TEST_ENABLED")))
            return;

        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        var targetSpeed = 7.5f;
        var targetPos = new { X = 1f, Y = 2f, Z = 3f };
        var targetRot = new { X = 10f, Y = 20f, Z = 30f };

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true }, cts.Token);

            var enabled = await CallToolAsync(http, "SetFreecamEnabled", new { enabled = true, confirm = true }, cts.Token);
            if (enabled is null) return;

            _ = await CallToolAsync(http, "SetFreecamSpeed", new { speed = targetSpeed, confirm = true }, cts.Token);
            _ = await CallToolAsync(http, "SetFreecamPose", new { pos = targetPos, rot = targetRot, confirm = true }, cts.Token);

            await Task.Delay(100, cts.Token);

            var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://freecam")}", cts.Token);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("Enabled").GetBoolean().Should().BeTrue();
            root.GetProperty("Speed").GetDouble().Should().BeApproximately(targetSpeed, 0.5);

            var pos = root.GetProperty("Pos");
            pos.GetProperty("X").GetDouble().Should().BeApproximately(targetPos.X, 0.5);
            pos.GetProperty("Y").GetDouble().Should().BeApproximately(targetPos.Y, 0.5);
            pos.GetProperty("Z").GetDouble().Should().BeApproximately(targetPos.Z, 0.5);

            var rot = root.GetProperty("Rot");
            rot.GetProperty("X").GetDouble().Should().BeApproximately(targetRot.X, 1.0);
            rot.GetProperty("Y").GetDouble().Should().BeApproximately(targetRot.Y, 1.0);
            rot.GetProperty("Z").GetDouble().Should().BeApproximately(targetRot.Z, 1.0);
        }
        finally
        {
            try { await CallToolAsync(http, "SetFreecamEnabled", new { enabled = false, confirm = true }, cts.Token); } catch { }
            try { await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, requireConfirm = (bool?)true }, cts.Token); } catch { }
        }
    }
}
