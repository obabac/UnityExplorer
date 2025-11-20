using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public static class Discovery
{
    public sealed record Info(int Pid, int Port, Uri? BaseUrl, string[]? Modes, DateTimeOffset? StartedAt, string? AuthToken)
    {
        public Uri EffectiveBaseUrl => BaseUrl ?? new Uri($"http://127.0.0.1:{Port}/");
    }

    public static bool TryLoad(out Info? info)
    {
        info = null;
        try
        {
            var path = Environment.GetEnvironmentVariable("UE_MCP_DISCOVERY");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");

            if (!File.Exists(path))
                return false;

            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            var pid = root.GetPropertyOrDefault("pid")?.GetInt32() ?? -1;
            var port = root.GetPropertyOrDefault("port")?.GetInt32() ?? -1;
            var baseUrlStr = root.GetPropertyOrDefault("baseUrl")?.GetString();
            Uri? baseUrl = null;
            if (!string.IsNullOrWhiteSpace(baseUrlStr) && Uri.TryCreate(baseUrlStr, UriKind.Absolute, out var u))
                baseUrl = u;
            var modes = root.GetPropertyOrDefault("modeHints")?.EnumerateArray().Select(e => e.GetString()!).Where(s => s != null).ToArray();
            DateTimeOffset? started = null;
            var startedStr = root.GetPropertyOrDefault("startedAt")?.GetString();
            if (!string.IsNullOrWhiteSpace(startedStr) && DateTimeOffset.TryParse(startedStr, out var dto))
                started = dto;
            var token = root.GetPropertyOrDefault("authToken")?.GetString();

            if (pid < 0 || port <= 0)
                return false;

            info = new Info(pid, port, baseUrl, modes, started, token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonElement? GetPropertyOrDefault(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v : null;
}
