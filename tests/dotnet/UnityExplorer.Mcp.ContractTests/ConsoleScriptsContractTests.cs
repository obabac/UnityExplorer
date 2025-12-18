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
        if (contentArr.GetArrayLength() == 0)
            return null;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        return jsonEl.Clone();
    }

    private sealed record StartupState(bool Enabled, string Path, string? Content);

    private static StartupState? ParseStartupState(JsonElement? json)
    {
        if (!json.HasValue) return null;
        var root = json.Value;
        if (root.TryGetProperty("enabled", out var enabledProp) && root.TryGetProperty("path", out var pathProp))
        {
            var enabled = enabledProp.GetBoolean();
            var path = pathProp.GetString() ?? string.Empty;
            string? content = null;
            if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                content = contentProp.GetString();
            return new StartupState(enabled, path, content);
        }
        return null;
    }

    private static async Task<StartupState?> GetStartupStateAsync(HttpClient http, CancellationToken ct)
    {
        var stateJson = await CallToolAsync(http, "GetStartupScript", new { }, ct);
        return ParseStartupState(stateJson);
    }

    private static async Task RestoreStartupStateAsync(HttpClient http, StartupState? state, CancellationToken ct)
    {
        if (state == null) return;

        if (!string.IsNullOrEmpty(state.Content))
        {
            _ = await CallToolAsync(http, "WriteStartupScript", new { content = state.Content, confirm = true }, ct);
            if (!state.Enabled)
                _ = await CallToolAsync(http, "SetStartupScriptEnabled", new { enabled = false, confirm = true }, ct);
            else
                _ = await CallToolAsync(http, "SetStartupScriptEnabled", new { enabled = true, confirm = true }, ct);
        }
        else
        {
            try { _ = await CallToolAsync(http, "DeleteConsoleScript", new { path = "startup.cs", confirm = true }, ct); } catch { }
            try { _ = await CallToolAsync(http, "DeleteConsoleScript", new { path = "startup.disabled.cs", confirm = true }, ct); } catch { }
        }
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

    [Fact]
    public async Task ConsoleScripts_Run_And_Startup_Lifecycle_When_Flag_Enabled()
    {
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            return;

        var http = TryCreateClient(out var ok);
        if (!ok || http == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var name = $"mcp-run-{Guid.NewGuid():N}.cs";
        var content = "return 7+5;";
        var startupContent = "return \"startup-ok\";";
        StartupState? originalState = null;

        try
        {
            originalState = await GetStartupStateAsync(http, cts.Token);

            _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)true, requireConfirm = (bool?)true, enableConsoleEval = (bool?)true }, cts.Token);

            var write = await CallToolAsync(http, "WriteConsoleScript", new { path = name, content, confirm = true }, cts.Token);
            write.Should().NotBeNull();
            write!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var run = await CallToolAsync(http, "RunConsoleScript", new { path = name, confirm = true }, cts.Token);
            run.Should().NotBeNull();
            run!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);
            var runResult = run.Value.GetProperty("result").GetString();
            runResult.Should().NotBeNullOrWhiteSpace();

            var del = await CallToolAsync(http, "DeleteConsoleScript", new { path = name, confirm = true }, cts.Token);
            del.Should().NotBeNull();
            del!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var writeStartup = await CallToolAsync(http, "WriteStartupScript", new { content = startupContent, confirm = true }, cts.Token);
            writeStartup.Should().NotBeNull();
            writeStartup!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var afterWrite = await GetStartupStateAsync(http, cts.Token);
            afterWrite.Should().NotBeNull();
            afterWrite!.Enabled.Should().BeTrue();
            afterWrite.Content.Should().Be(startupContent);

            var disable = await CallToolAsync(http, "SetStartupScriptEnabled", new { enabled = false, confirm = true }, cts.Token);
            disable.Should().NotBeNull();
            disable!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var afterDisable = await GetStartupStateAsync(http, cts.Token);
            afterDisable.Should().NotBeNull();
            afterDisable!.Enabled.Should().BeFalse();
            afterDisable.Content.Should().Be(startupContent);

            var runStartup = await CallToolAsync(http, "RunStartupScript", new { confirm = true }, cts.Token);
            runStartup.Should().NotBeNull();
            runStartup!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);
            runStartup.Value.TryGetProperty("path", out var runPath).Should().BeTrue();
            runPath.GetString().Should().NotBeNullOrWhiteSpace();
            var startupResult = runStartup.Value.GetProperty("result").GetString();
            startupResult.Should().Contain("startup-ok");

            var enable = await CallToolAsync(http, "SetStartupScriptEnabled", new { enabled = true, confirm = true }, cts.Token);
            enable.Should().NotBeNull();
            enable!.Value.GetProperty("ok").ValueKind.Should().Be(JsonValueKind.True);

            var afterEnable = await GetStartupStateAsync(http, cts.Token);
            afterEnable.Should().NotBeNull();
            afterEnable!.Enabled.Should().BeTrue();
            afterEnable.Content.Should().Be(startupContent);
        }
        finally
        {
            try { _ = await CallToolAsync(http, "DeleteConsoleScript", new { path = name, confirm = true }, cts.Token); } catch { }
            try { await RestoreStartupStateAsync(http, originalState, cts.Token); } catch { }
            try { _ = await CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false, enableConsoleEval = (bool?)false }, cts.Token); } catch { }
        }
    }
}
