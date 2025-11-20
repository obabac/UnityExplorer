using System.Net.Http;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class ScenesContractTests
{
    [Fact]
    public async Task Read_Scenes_If_Server_Available()
    {
        if (!Discovery.TryLoad(out var info)) return;
        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://scenes")}");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("Items", out _).Should().BeTrue();
    }
}

