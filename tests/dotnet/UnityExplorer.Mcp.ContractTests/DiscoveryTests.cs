namespace UnityExplorer.Mcp.ContractTests;

public class DiscoveryTests
{
    [Fact]
    public void Discovery_File_Is_Parsable_When_Present()
    {
        if (!Discovery.TryLoad(out var info))
            return; // treat as pass if server not running
        info!.Pid.Should().BeGreaterThan(0);
        info.Port.Should().BeGreaterThan(0);
        info.EffectiveBaseUrl.Should().NotBeNull();
    }

    [Fact]
    public void Discovery_ModeHints_Include_StreamableHttp_When_Server_Running()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        info!.Modes.Should().NotBeNull();
        info.Modes!.Should().Contain("streamable-http");
    }

    [Fact]
    public void Discovery_BaseUrl_Is_Present_When_Server_Running()
    {
        if (!Discovery.TryLoad(out var info))
            return;

        info!.BaseUrl.Should().NotBeNull();
        info.BaseUrl!.IsAbsoluteUri.Should().BeTrue();
    }
}
