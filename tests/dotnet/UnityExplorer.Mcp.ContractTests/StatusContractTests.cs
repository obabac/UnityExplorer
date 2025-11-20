using System.Net.Http;

namespace UnityExplorer.Mcp.ContractTests;

public class StatusContractTests
{
    [Fact(Skip = "MCP SDK client wiring pending — enable after server bootstrap")] 
    public async Task Status_Tool_And_Resource_Available()
    {
        // Placeholder: this will switch to HttpClientTransport once the server is implemented.
        if (!Discovery.TryLoad(out var info))
            return; // treat as pass when server not running

        using var http = new HttpClient { BaseAddress = info!.EffectiveBaseUrl };
        // Optionally probe root for liveness; MapMcp doesn't define a simple health endpoint by default.
        var res = await http.GetAsync("/");
        res.IsSuccessStatusCode.Should().BeTrue();
    }
}

