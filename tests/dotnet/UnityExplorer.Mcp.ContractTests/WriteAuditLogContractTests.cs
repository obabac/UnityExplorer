using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;

namespace UnityExplorer.Mcp.ContractTests;

public class WriteAuditLogContractTests
{
    [Fact]
    public async Task WriteTool_Emits_Audit_Log_Line()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await JsonRpcTestClient.CallToolAsync(
            http,
            "SetConfig",
            new { allowWrites = (bool?)true, requireConfirm = (bool?)true },
            cts.Token);

        try
        {
            var setTimeScaleResult = await JsonRpcTestClient.CallToolAsync(
                http,
                "SetTimeScale",
                new { value = 1f, @lock = (bool?)false, confirm = true },
                cts.Token);
            setTimeScaleResult.Should().NotBeNull();
            setTimeScaleResult!.Value.TryGetProperty("ok", out var okProp).Should().BeTrue();
            okProp.GetBoolean().Should().BeTrue();

            var logs = await JsonRpcTestClient.CallToolAsync(http, "TailLogs", new { count = 200 }, cts.Token);
            logs.Should().NotBeNull();

            var found = logs!.Value.TryGetProperty("Items", out var items)
                && items.ValueKind == JsonValueKind.Array
                && items.EnumerateArray().Any(item =>
                {
                    var hasCategory = item.TryGetProperty("Category", out var cat) && string.Equals(cat.GetString(), "audit", StringComparison.OrdinalIgnoreCase);
                    var hasMessage = item.TryGetProperty("Message", out var messageEl)
                        && messageEl.GetString() is string msg
                        && msg.Contains("SetTimeScale", StringComparison.OrdinalIgnoreCase)
                        && msg.Contains("ok=true", StringComparison.OrdinalIgnoreCase);
                    return hasCategory && hasMessage;
                });

            found.Should().BeTrue("Expected audit log entry for SetTimeScale with ok=true");
        }
        finally
        {
            await JsonRpcTestClient.CallToolAsync(http, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);
        }
    }
}
