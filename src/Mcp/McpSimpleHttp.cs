using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal sealed class McpSimpleHttp : IDisposable
    {
        public static McpSimpleHttp? Current { get; private set; }
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, NetworkStream> _sse = new();
        private int _nextClientId;
        public int Port { get; }
        private readonly string? _authToken;

        public McpSimpleHttp(string bindAddress, int port, string? authToken)
        {
            var ip = IPAddress.Parse(string.IsNullOrWhiteSpace(bindAddress) ? "127.0.0.1" : bindAddress);
            if (port == 0)
            {
                var tmp = new TcpListener(ip, 0);
                tmp.Start();
                port = ((IPEndPoint)tmp.LocalEndpoint).Port;
                tmp.Stop();
            }
            Port = port;
            _listener = new TcpListener(ip, Port);
            _authToken = authToken;
        }

        public void Start()
        {
            _listener.Start();
            Current = this;
            _ = AcceptLoopAsync(_cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _ = HandleClientAsync(client, ct);
                }
            }
            catch { }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 15000;
                stream.WriteTimeout = 15000;

                // minimal HTTP parse
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine)) return;
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;
                var method = parts[0];
                var target = parts[1];
                // read headers
                long contentLength = 0;
                string? acceptHeader = null;
                string? contentType = null;
                string? authorization = null;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                {
                    int idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var name = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(val, out contentLength);
                    if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase)) acceptHeader = val;
                    if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = val;
                    if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) authorization = val;
                }

                if (!Authorize(authorization)) { await WriteResponseAsync(stream, 401, "unauthorized", ct).ConfigureAwait(false); return; }

                if (method == "GET" && target.StartsWith("/sse"))
                {
                    var header = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: text/event-stream\r\n" +
                                 "Cache-Control: no-cache\r\n" +
                                 "Connection: keep-alive\r\n\r\n";
                    var headerBytes = Encoding.UTF8.GetBytes(header);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                    int id = Interlocked.Increment(ref _nextClientId);
                    _sse[id] = stream;
                    // keep open until disconnected
                    try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
                    catch { }
                    _sse.TryRemove(id, out _);
                    return;
                }

                string body = string.Empty;
                if (method == "POST" && contentLength > 0)
                {
                    char[] buf = ArrayPool<char>.Shared.Rent((int)contentLength);
                    try
                    {
                        int read = await reader.ReadBlockAsync(buf, 0, (int)contentLength).ConfigureAwait(false);
                        body = new string(buf, 0, read);
                    }
                    finally { ArrayPool<char>.Shared.Return(buf); }
                }

                if (method == "POST" && target.StartsWith("/message"))
                {
                    // minimal JSON-RPC: support list_tools only
                    using var doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                    var root = doc.RootElement;
                    string methodName = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
                    string id = root.TryGetProperty("id", out var rid) ? rid.GetRawText() : "null";
                    if (methodName == "list_tools")
                    {
                        var tools = McpReflection.ListTools();
                        await SendJsonRpcResultAsync(root, new { tools }, ct).ConfigureAwait(false);
                        await WriteResponseAsync(stream, 202, "accepted", ct).ConfigureAwait(false);
                        return;
                    }

                    if (methodName == "call_tool")
                    {
                        var pr = root.GetProperty("params");
                        var name = pr.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        var args = pr.TryGetProperty("arguments", out var a) ? a : default;
                        var result = await McpReflection.InvokeToolAsync(name, args).ConfigureAwait(false);
                        await SendJsonRpcResultAsync(root, new { content = new[] { new { type = "json", json = result } } }, ct).ConfigureAwait(false);
                        try { await BroadcastNotificationAsync("tool_result", new { name, result }, ct).ConfigureAwait(false); } catch { }
                        await WriteResponseAsync(stream, 202, "accepted", ct).ConfigureAwait(false);
                        return;
                    }

                    if (methodName == "read_resource")
                    {
                        var pr = root.GetProperty("params");
                        var uri = pr.TryGetProperty("uri", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                        var rr = await McpReflection.ReadResourceAsync(uri).ConfigureAwait(false);
                        await SendJsonRpcResultAsync(root, new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize(rr) } } }, ct).ConfigureAwait(false);
                        await WriteResponseAsync(stream, 202, "accepted", ct).ConfigureAwait(false);
                        return;
                    }

                    await WriteResponseAsync(stream, 400, "unsupported", ct).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && target.StartsWith("/read"))
                {
                    // convenient testing endpoint: GET /read?uri=...
                    var q = McpReflection.ParseQuery(new Uri("http://localhost" + target).Query);
                    if (!q.TryGetValue("uri", out var uriParam))
                    {
                        await WriteResponseAsync(stream, 400, "missing uri", ct).ConfigureAwait(false);
                        return;
                    }
                    try
                    {
                        var obj = await McpReflection.ReadResourceAsync(uriParam).ConfigureAwait(false);
                        var json = JsonSerializer.Serialize(obj);
                        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n";
                        var headerBytes = Encoding.UTF8.GetBytes(header);
                        await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(json), 0, Encoding.UTF8.GetByteCount(json), ct).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        await WriteResponseAsync(stream, 400, ex.Message, ct).ConfigureAwait(false);
                        return;
                    }
                }

                // default: health
                await WriteResponseAsync(stream, 200, "ok", ct).ConfigureAwait(false);
            }
        }

        private static async Task WriteResponseAsync(Stream stream, int code, string text, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(text);
            var header = $"HTTP/1.1 {code} OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private async Task BroadcastSseAsync(string json, CancellationToken ct)
        {
            var data = $"event: message\n" +
                       $"data: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(data);
            foreach (var kv in _sse)
            {
                var s = kv.Value;
                try { await s.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await s.FlushAsync(ct).ConfigureAwait(false); }
                catch { _sse.TryRemove(kv.Key, out _); }
            }
        }

        public Task BroadcastNotificationAsync(string @event, object payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notification", @params = new { @event, payload } });
            return BroadcastSseAsync(json, ct);
        }

        private bool Authorize(string? authHeader)
        {
            if (string.IsNullOrEmpty(_authToken)) return true;
            if (string.IsNullOrEmpty(authHeader)) return false;
            const string prefix = "Bearer ";
            if (authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring(prefix.Length).Trim();
                return string.Equals(token, _authToken, StringComparison.Ordinal);
            }
            return false;
        }

        private async Task SendJsonRpcResultAsync(JsonElement requestRoot, object result, CancellationToken ct)
        {
            object? idVal = null;
            if (requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idVal, result });
            await BroadcastSseAsync(payload, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }
    }

    internal static class McpReflection
    {
        public static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split('=', 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                dict[k] = v;
            }
            return dict;
        }

        public static object[] ListTools()
        {
            var list = new System.Collections.Generic.List<object>();
            var type = typeof(UnityReadTools);
            foreach (var mi in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (Attribute.IsDefined(mi, typeof(ModelContextProtocol.Server.McpServerToolAttribute)))
                {
                    var name = mi.Name;
                    var desc = mi.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                    string? d = (desc.Length > 0) ? ((System.ComponentModel.DescriptionAttribute)desc[0]).Description : null;
                    list.Add(new { name, description = d });
                }
            }
            return list.ToArray();
        }

        public static async Task<object?> InvokeToolAsync(string name, JsonElement args)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("tool name required");
            var type = typeof(UnityReadTools);
            var mi = Array.Find(type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
                m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
                     Attribute.IsDefined(m, typeof(ModelContextProtocol.Server.McpServerToolAttribute)));
            if (mi == null) throw new InvalidOperationException($"Tool not found: {name}");

            var parameters = mi.GetParameters();
            var values = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(System.Threading.CancellationToken)) { values[i] = default(System.Threading.CancellationToken); continue; }
                object? val = null;
                if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(p.Name!, out var v))
                {
                    val = v.Deserialize(p.ParameterType);
                }
                else if (p.HasDefaultValue)
                {
                    val = p.DefaultValue;
                }
                values[i] = val;
            }
            var ret = mi.Invoke(null, values);
            if (ret is Task t)
            {
                await t.ConfigureAwait(false);
                var prop = t.GetType().GetProperty("Result");
                return prop != null ? prop.GetValue(t) : null;
            }
            return ret;
        }

        public static async Task<object?> ReadResourceAsync(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("uri required");
            // simple router based on path segment
            Uri u;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out u)) throw new ArgumentException("invalid uri");
            var path = u.AbsolutePath.Trim('/');
            var query = ParseQuery(u.Query);

            if (path.Equals("status", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetStatus(default);
            if (path.Equals("scenes", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.ListScenes(TryInt(query, "limit"), TryInt(query, "offset"), default);
            if (path.StartsWith("scene/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/objects", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = path.Substring(6, path.Length - 6 - "/objects".Length);
                return await UnityReadTools.ListObjects(sceneId, query["name"], query["type"], TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length);
                return await UnityReadTools.GetObject(id, default);
            }
            if (path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/components".Length);
                return await UnityReadTools.GetComponents(id, TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                return await UnityReadTools.SearchObjects(query["query"], query["name"], query["type"], query["path"], TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.Equals("camera/active", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetCameraInfo(default);
            if (path.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetSelection(default);
            if (path.Equals("logs/tail", StringComparison.OrdinalIgnoreCase))
                return UnityReadTools.TailLogs(TryInt(query, "count") ?? 200, default);

            throw new NotSupportedException("resource not supported");
        }

        private static int? TryInt(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
        private static bool? TryBool(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : (bool?)null;
    }
#endif
}
