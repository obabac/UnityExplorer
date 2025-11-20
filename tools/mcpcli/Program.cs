using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

static class Discovery
{
    public sealed record Info(int Pid, int Port, Uri? BaseUrl, string? AuthToken)
    {
        public Uri EffectiveBaseUrl => BaseUrl ?? new Uri($"http://127.0.0.1:{Port}/");
    }

    public static Info? Load()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("UE_MCP_DISCOVERY");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;
            var pid = root.GetProperty("pid").GetInt32();
            var port = root.GetProperty("port").GetInt32();
            Uri? baseUrl = null;
            if (root.TryGetProperty("baseUrl", out var bu) && Uri.TryCreate(bu.GetString(), UriKind.Absolute, out var u))
                baseUrl = u;
            var token = root.TryGetProperty("authToken", out var tok) ? tok.GetString() : null;
            return new Info(pid, port, baseUrl, token);
        }
        catch { return null; }
    }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("mcpcli commands:\n  list-tools\n  read <uri>\n  call <toolName> [jsonArgs]\n  status\n  scenes\n  objects [sceneId]\n  search <q>\n  camera\n  selection\n  logs [count]\n  pick [world|ui]\n  stream-events\n  set-active <objId> <true|false> [--confirm]\n  add-comp <objId> <FullTypeName> [--confirm]\n  rm-comp <objId> <typeOrIndex> [--confirm]\n  reparent <childId> <parentId> [--confirm]\n  destroy <objId> [--confirm]\n  select <objId>\n  rename <objId> <newName> [--confirm]\n  set-tag <objId> <tag> [--confirm]\n  set-layer <objId> <layer> [--confirm]\n  config writes <on|off> [--no-confirm]\n  config token <value> [--restart]\n");
            return 0;
        }

        var info = Discovery.Load();
        if (info is null) { Console.Error.WriteLine("No discovery file; server not running?"); return 2; }
        using var http = new HttpClient { BaseAddress = info.EffectiveBaseUrl };
        if (!string.IsNullOrEmpty(info.AuthToken))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", info.AuthToken);
        http.Timeout = TimeSpan.FromSeconds(10);

        var cmd = args[0];
        if (cmd == "read")
        {
            if (args.Length < 2) { Console.Error.WriteLine("read requires a uri"); return 2; }
            var uri = args[1];
            var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}");
            res.EnsureSuccessStatusCode();
            Console.WriteLine(await res.Content.ReadAsStringAsync());
            return 0;
        }

        // Convenience commands via GET /read
        if (cmd == "status")
        {
            await ReadAsync(http, "unity://status");
            return 0;
        }
        if (cmd == "scenes")
        {
            await ReadAsync(http, "unity://scenes");
            return 0;
        }
        if (cmd == "objects")
        {
            var scene = args.Length >= 2 ? args[1] : string.Empty;
            var uri = string.IsNullOrEmpty(scene) ? "unity://scene//objects" : $"unity://scene/{scene}/objects";
            await ReadAsync(http, uri);
            return 0;
        }
        if (cmd == "search")
        {
            if (args.Length < 2) { Console.Error.WriteLine("search requires query"); return 2; }
            var q = Uri.EscapeDataString(args[1]);
            await ReadAsync(http, $"unity://search?query={q}");
            return 0;
        }
        if (cmd == "camera") { await ReadAsync(http, "unity://camera/active"); return 0; }
        if (cmd == "selection") { await ReadAsync(http, "unity://selection"); return 0; }
        if (cmd == "logs") { var n = args.Length>=2? args[1]:"200"; await ReadAsync(http, $"unity://logs/tail?count={n}"); return 0; }

        if (cmd == "list-tools")
        {
            var root = await PostJsonRpcAsync(http, new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "list_tools" });
            if (root.TryGetProperty("result", out var result) && result.TryGetProperty("tools", out var tools))
            {
                Console.WriteLine(JsonSerializer.Serialize(tools, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(root.GetRawText());
            }
            return 0;
        }

        if (cmd == "call")
        {
            if (args.Length < 2) { Console.Error.WriteLine("call requires a tool name"); return 2; }
            string name = args[1];
            JsonElement arguments = default;
            if (args.Length >= 3)
            {
                using var doc = JsonDocument.Parse(args[2]);
                arguments = doc.RootElement.Clone();
            }
            var root = await PostJsonRpcAsync(http, new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "call_tool", @params = new { name, arguments } });
            Console.WriteLine(root.GetRawText());
            return 0;
        }

        if (cmd == "pick")
        {
            var mode = args.Length >= 2 ? args[1] : "world";
            var root = await PostJsonRpcAsync(http, new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "call_tool", @params = new { name = "MousePick", arguments = new { mode } } });
            Console.WriteLine(root.GetRawText());
            return 0;
        }

        if (cmd == "stream-events")
        {
            return await StreamEventsAsync(http);
        }

        if (cmd == "set-active")
        {
            if (args.Length < 3) { Console.Error.WriteLine("set-active <objId> <true|false> [--confirm]"); return 2; }
            var id = args[1];
            if (!bool.TryParse(args[2], out var active)) { Console.Error.WriteLine("second arg must be true|false"); return 2; }
            var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await SetActiveAsync(http, id, active, confirm);
        }

        if (cmd == "add-comp")
        {
            if (args.Length < 3) { Console.Error.WriteLine("add-comp <objId> <FullTypeName> [--confirm]"); return 2; }
            var id = args[1]; var type = args[2]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "AddComponent", new { objectId = id, type, confirm });
        }
        if (cmd == "rm-comp")
        {
            if (args.Length < 3) { Console.Error.WriteLine("rm-comp <objId> <typeOrIndex> [--confirm]"); return 2; }
            var id = args[1]; var val = args[2]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "RemoveComponent", new { objectId = id, typeOrIndex = val, confirm });
        }
        if (cmd == "reparent")
        {
            if (args.Length < 3) { Console.Error.WriteLine("reparent <childId> <parentId> [--confirm]"); return 2; }
            var child = args[1]; var parent = args[2]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "Reparent", new { objectId = child, newParentId = parent, confirm });
        }
        if (cmd == "destroy")
        {
            if (args.Length < 2) { Console.Error.WriteLine("destroy <objId> [--confirm]"); return 2; }
            var id = args[1]; var confirm = args.Length >= 3 && args[2] == "--confirm";
            return await CallToolAsync(http, "DestroyObject", new { objectId = id, confirm });
        }
        if (cmd == "select")
        {
            if (args.Length < 2) { Console.Error.WriteLine("select <objId>"); return 2; }
            var id = args[1];
            return await CallToolAsync(http, "SelectObject", new { objectId = id });
        }
        if (cmd == "rename")
        {
            if (args.Length < 3) { Console.Error.WriteLine("rename <objId> <newName> [--confirm]"); return 2; }
            var id = args[1]; var name = args[2]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "SetName", new { objectId = id, name, confirm });
        }
        if (cmd == "set-tag")
        {
            if (args.Length < 3) { Console.Error.WriteLine("set-tag <objId> <tag> [--confirm]"); return 2; }
            var id = args[1]; var tag = args[2]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "SetTag", new { objectId = id, tag, confirm });
        }
        if (cmd == "set-layer")
        {
            if (args.Length < 3 || !int.TryParse(args[2], out var layer)) { Console.Error.WriteLine("set-layer <objId> <layer:int> [--confirm]"); return 2; }
            var id = args[1]; var confirm = args.Length >= 4 && args[3] == "--confirm";
            return await CallToolAsync(http, "SetLayer", new { objectId = id, layer, confirm });
        }

        if (cmd == "config" && args.Length >= 2)
        {
            var sub = args[1];
            if (sub == "writes")
            {
                if (args.Length < 3) { Console.Error.WriteLine("config writes <on|off> [--no-confirm]"); return 2; }
                var allow = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
                var requireConfirm = !(args.Length >= 4 && args[3] == "--no-confirm");
                return await CallToolAsync(http, "SetConfig", new { allowWrites = allow, requireConfirm });
            }
            if (sub == "token")
            {
                if (args.Length < 3) { Console.Error.WriteLine("config token <value> [--restart]"); return 2; }
                var token = args[2]; var restart = args.Length >= 4 && args[3] == "--restart";
                return await CallToolAsync(http, "SetConfig", new { authToken = token, restart });
            }
        }

        Console.Error.WriteLine($"Unknown command: {cmd}");
        return 2;
    }

    static async Task<JsonElement> PostJsonRpcAsync(HttpClient http, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message");
        req.Content = content;
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    static async Task ReadAsync(HttpClient http, string uri)
    {
        var res = await http.GetAsync($"/read?uri={Uri.EscapeDataString(uri)}");
        res.EnsureSuccessStatusCode();
        Console.WriteLine(await res.Content.ReadAsStringAsync());
    }

    // helper for set-active
    static async Task<int> SetActiveAsync(HttpClient http, string objectId, bool active, bool confirm)
    {
        var root = await PostJsonRpcAsync(http, new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "call_tool",
            @params = new { name = "SetActive", arguments = new { objectId, active, confirm } }
        });
        Console.WriteLine(root.GetRawText());
        return 0;
    }

    static async Task<int> CallToolAsync(HttpClient http, string name, object args)
    {
        var root = await PostJsonRpcAsync(http, new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "call_tool",
            @params = new { name, arguments = args }
        });
        Console.WriteLine(root.GetRawText());
        return 0;
    }

    static async Task<int> StreamEventsAsync(HttpClient http)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message");
        req.Content = content;
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            Console.WriteLine(line);
        }

        return 0;
    }
}
