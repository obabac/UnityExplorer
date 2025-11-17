using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var parseResult = ParsedArgs.Parse(args);
        if (parseResult.ShowHelp)
        {
            ShowHelp();
            return 1;
        }

        var client = CreateClient(parseResult, out var discovery);
        var ct = CancellationToken.None;

        switch (parseResult.Command)
        {
            case "status":
                await GetResourceAsync(client, discovery, "unity://status", ct);
                return 0;
            case "scenes":
                await GetResourceAsync(client, discovery, "unity://scenes", ct);
                return 0;
            case "objects":
                if (parseResult.Arguments.Count < 1)
                {
                    Console.Error.WriteLine("Usage: objects <sceneId>");
                    return 1;
                }
                await GetResourceAsync(client, discovery, $"unity://scene/{parseResult.Arguments[0]}/objects", ct);
                return 0;
            case "search":
                if (parseResult.Arguments.Count < 1)
                {
                    Console.Error.WriteLine("Usage: search <query>");
                    return 1;
                }
                var query = parseResult.Arguments[0];
                await GetResourceAsync(client, discovery, $"unity://search?query={Uri.EscapeDataString(query)}", ct);
                return 0;
            case "camera":
                await GetResourceAsync(client, discovery, "unity://camera/active", ct);
                return 0;
            case "selection":
                await GetResourceAsync(client, discovery, "unity://selection", ct);
                return 0;
            case "logs":
                int count = 200;
                if (parseResult.Arguments.Count > 0 && int.TryParse(parseResult.Arguments[0], out var parsed))
                {
                    count = parsed;
                }
                await GetResourceAsync(client, discovery, $"unity://logs/tail?count={count}", ct);
                return 0;
            case "read":
                if (parseResult.Arguments.Count < 1)
                {
                    Console.Error.WriteLine("Usage: read <unity://uri>");
                    return 1;
                }
                await GetResourceAsync(client, discovery, parseResult.Arguments[0], ct);
                return 0;
            case "list-tools":
                await ListToolsAsync(client, ct);
                return 0;
            default:
                ShowHelp();
                return 1;
        }
    }

    private static HttpClient CreateClient(ParsedArgs parseResult, out Discovery discovery)
    {
        var baseUrl = parseResult.BaseUrl;
        var tokenOverride = parseResult.Token;
        discovery = Discovery.Load();

        var effectiveBase = baseUrl ?? discovery.BaseUrl ?? "http://127.0.0.1:0/";
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(effectiveBase, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var token = tokenOverride ?? discovery.AuthToken;
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task GetResourceAsync(HttpClient client, Discovery discovery, string unityUri, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(unityUri);
        var path = $"read?uri={encoded}";
        var response = await client.GetAsync(path, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (!string.IsNullOrWhiteSpace(body))
            {
                Console.Error.WriteLine(body);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine("<empty>");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var pretty = JsonSerializer.Serialize(doc.RootElement, JsonOptions);
            Console.WriteLine(pretty);
        }
        catch
        {
            Console.WriteLine(body);
        }
    }

    private static async Task ListToolsAsync(HttpClient client, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "list_tools",
            @params = new { }
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("message", content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine("<empty>");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("tools", out var toolsElement) &&
                toolsElement.ValueKind == JsonValueKind.Array)
            {
                var pretty = JsonSerializer.Serialize(toolsElement, JsonOptions);
                Console.WriteLine(pretty);
                return;
            }

            // Fallback: pretty-print whole JSON-RPC envelope.
            var prettyEnvelope = JsonSerializer.Serialize(root, JsonOptions);
            Console.WriteLine(prettyEnvelope);
        }
        catch
        {
            Console.WriteLine(body);
        }
    }

    private sealed record Discovery(string? BaseUrl, string? AuthToken)
    {
        public static Discovery Load()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
                if (!File.Exists(path))
                {
                    return new Discovery(null, null);
                }

                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                var url = root.TryGetProperty("baseUrl", out var u) ? u.GetString() : null;
                var token = root.TryGetProperty("authToken", out var a) ? a.GetString() : null;
                return new Discovery(url, token);
            }
            catch
            {
                return new Discovery(null, null);
            }
        }
    }
    private sealed class ParsedArgs
    {
        public string? BaseUrl { get; init; }
        public string? Token { get; init; }
        public string Command { get; init; } = string.Empty;
        public List<string> Arguments { get; } = new();
        public bool ShowHelp { get; init; }

        public static ParsedArgs Parse(string[] args)
        {
            string? baseUrl = null;
            string? token = null;
            var rest = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--base-url" && i + 1 < args.Length)
                {
                    baseUrl = args[++i];
                }
                else if (arg.StartsWith("--base-url=", StringComparison.Ordinal))
                {
                    baseUrl = arg.Substring("--base-url=".Length);
                }
                else if (arg == "--token" && i + 1 < args.Length)
                {
                    token = args[++i];
                }
                else if (arg.StartsWith("--token=", StringComparison.Ordinal))
                {
                    token = arg.Substring("--token=".Length);
                }
                else
                {
                    rest.Add(arg);
                }
            }

            if (rest.Count == 0)
            {
                return new ParsedArgs { BaseUrl = baseUrl, Token = token, ShowHelp = true };
            }

            var command = rest[0];
            var arguments = rest.Skip(1).ToList();

            return new ParsedArgs
            {
                BaseUrl = baseUrl,
                Token = token,
                Command = command,
            }.WithArguments(arguments);
        }

        private ParsedArgs WithArguments(List<string> args)
        {
            Arguments.AddRange(args);
            return this;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Unity Explorer MCP CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  mcpcli [--base-url URL] [--token TOKEN] <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  status             Get global Unity status");
        Console.WriteLine("  scenes             List loaded scenes");
        Console.WriteLine("  objects <sceneId>  List objects in a scene (e.g. scn:0)");
        Console.WriteLine("  search <query>     Search objects by query string");
        Console.WriteLine("  camera             Get active camera info");
        Console.WriteLine("  selection          Get current Unity selection");
        Console.WriteLine("  logs [count]       Tail recent logs (default 200)");
        Console.WriteLine("  read <unity://uri> Read an arbitrary unity:// resource");
        Console.WriteLine("  list-tools         List MCP tools via JSON-RPC list_tools");
        Console.WriteLine();
        Console.WriteLine("If --base-url/--token are omitted, values from the discovery file");
        Console.WriteLine("at %TEMP%/unity-explorer-mcp.json are used when available.");
    }
}
