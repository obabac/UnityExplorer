using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace UnityExplorer.Mcp.ContractTests;

public class ReflectionInspectorReadContractTests
{
    [Fact]
    public async Task ReadStaticMember_TimeScale_Returns_Value()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var json = await JsonRpcTestClient.CallToolAsync(client, "ReadStaticMember", new { typeFullName = "UnityEngine.Time", name = "timeScale" }, cts.Token);
        json.HasValue.Should().BeTrue();
        var result = json!.Value;
        result.TryGetProperty("ok", out var ok).Should().BeTrue();
        ok.ValueKind.Should().Be(JsonValueKind.True);
        result.TryGetProperty("type", out var type).Should().BeTrue();
        type.ValueKind.Should().Be(JsonValueKind.String);
        result.TryGetProperty("valueText", out var valueText).Should().BeTrue();
        valueText.ValueKind.Should().Be(JsonValueKind.String);
        result.TryGetProperty("valueJson", out var valueJson).Should().BeTrue();
        valueJson.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.String, JsonValueKind.Null);
    }

    [Fact]
    public async Task Singleton_Reads_Return_Page_And_Value()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var searchJson = await JsonRpcTestClient.CallToolAsync(client, "SearchSingletons", new { query = "UnityExplorer", limit = 1, offset = 0 }, cts.Token);
        if (!searchJson.HasValue)
            return;

        var search = searchJson!.Value;
        if (!search.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return;

        var firstItem = items[0];
        var singletonId = firstItem.GetProperty("Id").GetString();
        if (string.IsNullOrEmpty(singletonId))
            return;

        var membersJson = await JsonRpcTestClient.CallToolAsync(client, "ListSingletonMembers", new { singletonId, limit = 20 }, cts.Token);
        membersJson.HasValue.Should().BeTrue();
        var members = membersJson!.Value;
        members.TryGetProperty("Total", out var total).Should().BeTrue();
        total.ValueKind.Should().Be(JsonValueKind.Number);
        members.TryGetProperty("Items", out var memberItems).Should().BeTrue();
        memberItems.ValueKind.Should().Be(JsonValueKind.Array);
        if (memberItems.GetArrayLength() == 0)
            return;

        string? memberName = null;
        foreach (var m in memberItems.EnumerateArray())
        {
            var kind = m.GetProperty("Kind").GetString();
            var canRead = m.GetProperty("CanRead").GetBoolean();
            if (canRead && (string.Equals(kind, "field", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "property", StringComparison.OrdinalIgnoreCase)))
            {
                memberName = m.GetProperty("Name").GetString();
                break;
            }
        }

        if (string.IsNullOrEmpty(memberName))
            return;

        var readJson = await JsonRpcTestClient.CallToolAsync(client, "ReadSingletonMember", new { singletonId, name = memberName }, cts.Token);
        readJson.HasValue.Should().BeTrue();
        var read = readJson!.Value;
        read.TryGetProperty("ok", out var ok).Should().BeTrue();
        ok.ValueKind.Should().Be(JsonValueKind.True);
        read.TryGetProperty("type", out var type).Should().BeTrue();
        type.ValueKind.Should().Be(JsonValueKind.String);
        read.TryGetProperty("valueText", out var valueText).Should().BeTrue();
        valueText.ValueKind.Should().Be(JsonValueKind.String);
        read.TryGetProperty("valueJson", out var valueJson).Should().BeTrue();
        valueJson.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Number, JsonValueKind.Null, JsonValueKind.True, JsonValueKind.False);
    }
}
