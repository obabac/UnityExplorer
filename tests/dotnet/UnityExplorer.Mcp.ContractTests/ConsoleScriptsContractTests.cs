using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class ConsoleScriptsContractTests
{
    private const string EnvFlag = "UE_MCP_CONSOLE_SCRIPT_TEST_ENABLED";

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
            id = $"console-script-tool-{name}",
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
        contentArr.GetArrayLength().Should().BeGreaterThan(0);

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        return jsonEl.Clone();
    }

    [Fact]
    public async Task ConsoleScript_Read_Write_Delete_RoundTrip_When_Flag_Enabled()
    {
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            return;

        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var name = $"mcp-test-{Guid.NewGuid():N}.cs";
        var content = "return 123;";

        try
        {
            _ = await CallToolAsync(http, "SetConfig", new
            {
                allowWrites = (bool?)true,
                requireConfirm = (bool?)true
            }, cts.Token);

            var write = await CallToolAsync(http, "WriteConsoleScript", new
            {
                path = name,
                content,
                confirm = true
            }, cts.Token);
            write.Should().NotBeNull();
            write!.Value.TryGetProperty("ok", out var okProp).Should().BeTrue();
            okProp.ValueKind.Should().Be(JsonValueKind.True);

            var uri = $"unity://console/script?path={Uri.EscapeDataString(name)}";
            using var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}", cts.Token);
            res.EnsureSuccessStatusCode();
            var readJson = await res.Content.ReadAsStringAsync(cts.Token);
            using var readDoc = JsonDocument.Parse(readJson);
            var readRoot = readDoc.RootElement;

            readRoot.TryGetProperty("Name", out var nameProp).Should().BeTrue();
            nameProp.GetString().Should().Be(name);
            readRoot.TryGetProperty("Path", out var pathProp).Should().BeTrue();
            pathProp.GetString().Should().NotBeNullOrWhiteSpace();
            readRoot.TryGetProperty("Content", out var contentProp).Should().BeTrue();
            contentProp.GetString().Should().Be(content);
            readRoot.TryGetProperty("Truncated", out var truncProp).Should().BeTrue();
            truncProp.ValueKind.Should().Be(JsonValueKind.False);
            readRoot.TryGetProperty("SizeBytes", out var sizeProp).Should().BeTrue();
            sizeProp.ValueKind.Should().Be(JsonValueKind.Number);
            readRoot.TryGetProperty("LastModifiedUtc", out var lmProp).Should().BeTrue();
            lmProp.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Number, JsonValueKind.Object);

            var del = await CallToolAsync(http, "DeleteConsoleScript", new
            {
                path = name,
                confirm = true
            }, cts.Token);
            del.Should().NotBeNull();
            del!.Value.TryGetProperty("ok", out var delOk).Should().BeTrue();
            delOk.ValueKind.Should().Be(JsonValueKind.True);
        }
        finally
        {
            try
            {
                _ = await CallToolAsync(http, "DeleteConsoleScript", new { path = name, confirm = true }, cts.Token);
            }
            catch { }

            try
            {
                _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);
            }
            catch { }
        }
    }
}
