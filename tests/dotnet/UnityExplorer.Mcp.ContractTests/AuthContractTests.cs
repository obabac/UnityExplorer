using System.Net;
using System.Net.Http;

namespace UnityExplorer.Mcp.ContractTests;

public class AuthContractTests
{
    [Fact]
    public async Task Unauthorized_Request_Fails_When_AuthToken_Configured()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        if (string.IsNullOrEmpty(info!.AuthToken))
            return; // auth not configured; nothing to validate

        using var http = new HttpClient { BaseAddress = info.EffectiveBaseUrl };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://status")}", cts.Token);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorized_Request_Succeeds_When_AuthToken_Configured()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        if (string.IsNullOrEmpty(info!.AuthToken))
            return; // auth not configured; nothing to validate

        using var http = new HttpClient { BaseAddress = info.EffectiveBaseUrl };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", info.AuthToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString("unity://status")}", cts.Token);
        res.IsSuccessStatusCode.Should().BeTrue();
    }
}

