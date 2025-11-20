using System.Net.Http;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class HttpContractTests
{
    [Fact]
    public async Task Read_Status_Resource_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info)) return; // skip when not running

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // hit the convenience GET /read endpoint
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://status")}", cts.Token);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("UnityVersion", out _).Should().BeTrue();
        root.TryGetProperty("ExplorerVersion", out _).Should().BeTrue();
    }
}

