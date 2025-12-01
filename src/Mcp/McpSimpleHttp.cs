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
        private readonly ConcurrentDictionary<int, Stream> _httpStreams = new();
        private readonly SemaphoreSlim _requestSlots = new(ConcurrencyLimit, ConcurrencyLimit);
        private int _nextClientId;
        public int Port { get; }

        // JSON-RPC codes (spec + domain)
        private const int ErrorInvalidRequest = -32600;
        private const int ErrorMethodNotFound = -32601;
        private const int ErrorInvalidParams = -32602;
        private const int ErrorInternal = -32603;
        private const int ErrorNotReady = -32002;
        private const int ErrorPermissionDenied = -32003;
        private const int ErrorNotFound = -32004;
        private const int ErrorRateLimited = -32005;
        private const int ConcurrencyLimit = 32;

        private readonly record struct ErrorShape(int Code, int HttpStatus, string Kind, string Message, string? Hint, string? Detail);

        public McpSimpleHttp(string bindAddress, int port)
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

                if (method == "POST" && (target.StartsWith("/message") || target == "/" || target.StartsWith("/?")))
                {
                    // Minimal JSON-RPC over HTTP. Results and errors are returned in the HTTP body
                    // and also broadcast on any open streaming connections.
                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                    }
                    catch (JsonException ex)
                    {
                        await SendJsonRpcErrorAsync(-32700, "Parse error", "InvalidRequest", null, ex.Message, ct).ConfigureAwait(false);
                        var errorPayload = BuildJsonRpcErrorPayload(-32700, "Parse error", "InvalidRequest", null, ex.Message);
                        await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                        return;
                    }

                    using (doc)
                    {
                        var root = doc.RootElement;
                        string methodName = root.TryGetProperty("method", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                        if (string.IsNullOrWhiteSpace(methodName))
                        {
                            await SendJsonRpcErrorAsync(root, ErrorInvalidRequest, "Invalid request: 'method' is required.", "InvalidRequest", null, null, ct).ConfigureAwait(false);
                            var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInvalidRequest, "Invalid request: 'method' is required.", "InvalidRequest", null, null);
                            await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                            return;
                        }

                        // Support both UnityExplorer-specific and MCP-standard method names.
                        if (string.Equals(methodName, "tools/list", StringComparison.OrdinalIgnoreCase))
                            methodName = "list_tools";
                        else if (string.Equals(methodName, "tools/call", StringComparison.OrdinalIgnoreCase))
                            methodName = "call_tool";
                        else if (string.Equals(methodName, "resources/read", StringComparison.OrdinalIgnoreCase))
                            methodName = "read_resource";

                        var acquiredSlot = false;
                        try
                        {
                            if (!string.Equals(methodName, "stream_events", StringComparison.OrdinalIgnoreCase))
                            {
                                acquiredSlot = _requestSlots.Wait(0);
                                if (!acquiredSlot)
                                {
                                    var msg = $"Cannot have more than {ConcurrencyLimit} parallel requests. Please slow down.";
                                    await SendJsonRpcErrorAsync(root, ErrorRateLimited, msg, "RateLimited", null, null, ct).ConfigureAwait(false);
                                    var errorPayload = BuildJsonRpcErrorPayload(root, ErrorRateLimited, msg, "RateLimited", null, null);
                                    await WriteJsonResponseAsync(stream, 429, errorPayload, ct).ConfigureAwait(false);
                                    return;
                                }
                            }

                        // MCP initialize handshake support for inspector and generic clients.
                        if (string.Equals(methodName, "initialize", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                const string protocolVersion = "2024-11-05";
                                var serverInfo = new
                                {
                                    name = "UnityExplorer.Mcp",
                                    version = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0"
                                };
                                var capabilities = new
                                {
                                    tools = new { listChanged = true },
                                    resources = new { listChanged = true },
                                    experimental = new { streamEvents = true }
                                };

                                var instructions =
                                    "Unity Explorer MCP exposes Unity scenes, objects, and logs as tools " +
                                    "and resources. Use list_tools + call_tool for inspection, and " +
                                    "read_resource with unity:// URIs for structured state.";

                                var resultObj = new { protocolVersion, capabilities, serverInfo, instructions };
                                await SendJsonRpcResultAsync(root, resultObj, ct).ConfigureAwait(false);
                                var responsePayload = BuildJsonRpcResultPayload(root, resultObj);
                                await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInternal, "Internal error", "Internal", null, ex.Message, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInternal, "Internal error", "Internal", null, ex.Message);
                                await WriteJsonResponseAsync(stream, 500, errorPayload, ct).ConfigureAwait(false);
                            }
                            return;
                        }

                        // Client-side notification after initialize; accept and ignore.
                        if (string.Equals(methodName, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
                        {
                            var payload = new
                            {
                                jsonrpc = "2.0",
                                id = (object?)null,
                                result = new { ok = true }
                            };
                            await WriteJsonResponseAsync(stream, 200, payload, ct).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(methodName, "ping", StringComparison.OrdinalIgnoreCase))
                        {
                            var resultObj = new { };
                            await SendJsonRpcResultAsync(root, resultObj, ct).ConfigureAwait(false);
                            var responsePayload = BuildJsonRpcResultPayload(root, resultObj);
                            await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(methodName, "list_tools", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var tools = McpReflection.ListTools();
                                var resultObj = new { tools };
                                await SendJsonRpcResultAsync(root, resultObj, ct).ConfigureAwait(false);
                                var responsePayload = BuildJsonRpcResultPayload(root, resultObj);
                                await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInternal, "Internal error", "Internal", null, ex.Message, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInternal, "Internal error", "Internal", null, ex.Message);
                                await WriteJsonResponseAsync(stream, 500, errorPayload, ct).ConfigureAwait(false);
                            }
                            return;
                        }

                        if (string.Equals(methodName, "call_tool", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!root.TryGetProperty("params", out var pr))
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInvalidParams, "Invalid params: 'params' object is required.", "InvalidArgument", null, null, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInvalidParams, "Invalid params: 'params' object is required.", "InvalidArgument", null, null);
                                await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                                return;
                            }

                            var name = pr.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                            var args = pr.TryGetProperty("arguments", out var a) ? a : default;
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInvalidParams, "Invalid params: 'name' is required.", "InvalidArgument", null, null, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInvalidParams, "Invalid params: 'name' is required.", "InvalidArgument", null, null);
                                await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                                return;
                            }

                            try
                            {
                                var result = await McpReflection.InvokeToolAsync(name, args).ConfigureAwait(false);
                                var wrapped = new { content = new[] { new { type = "json", json = result } } };
                                await SendJsonRpcResultAsync(root, wrapped, ct).ConfigureAwait(false);
                                try { await BroadcastNotificationAsync("tool_result", new { name, ok = true, result }, ct).ConfigureAwait(false); } catch { }
                                var responsePayload = BuildJsonRpcResultPayload(root, wrapped);
                                await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                var err = MapExceptionToError(ex);
                                await SendJsonRpcErrorAsync(root, err.Code, err.Message, err.Kind, err.Hint, err.Detail, ct).ConfigureAwait(false);
                                try
                                {
                                    await BroadcastNotificationAsync("tool_result", new { name, ok = false, error = new { code = err.Code, message = err.Message, data = BuildErrorData(err.Kind, err.Hint, err.Detail) } }, ct).ConfigureAwait(false);
                                }
                                catch { }
                                var errorPayload = BuildJsonRpcErrorPayload(root, err.Code, err.Message, err.Kind, err.Hint, err.Detail);
                                await WriteJsonResponseAsync(stream, err.HttpStatus, errorPayload, ct).ConfigureAwait(false);
                            }
                            return;
                        }

                        if (string.Equals(methodName, "read_resource", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!root.TryGetProperty("params", out var pr))
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInvalidParams, "Invalid params: 'params' object is required.", "InvalidArgument", null, null, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInvalidParams, "Invalid params: 'params' object is required.", "InvalidArgument", null, null);
                                await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                                return;
                            }

                            var uri = pr.TryGetProperty("uri", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                            if (string.IsNullOrWhiteSpace(uri))
                            {
                                await SendJsonRpcErrorAsync(root, ErrorInvalidParams, "Invalid params: 'uri' is required.", "InvalidArgument", null, null, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, ErrorInvalidParams, "Invalid params: 'uri' is required.", "InvalidArgument", null, null);
                                await WriteJsonResponseAsync(stream, 400, errorPayload, ct).ConfigureAwait(false);
                                return;
                            }

                            try
                            {
                                var rr = await McpReflection.ReadResourceAsync(uri).ConfigureAwait(false);
                                var resultObj = new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize(rr) } } };
                                await SendJsonRpcResultAsync(root, resultObj, ct).ConfigureAwait(false);
                                var responsePayload = BuildJsonRpcResultPayload(root, resultObj);
                                await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                var err = MapExceptionToError(ex);
                                await SendJsonRpcErrorAsync(root, err.Code, err.Message, err.Kind, err.Hint, err.Detail, ct).ConfigureAwait(false);
                                var errorPayload = BuildJsonRpcErrorPayload(root, err.Code, err.Message, err.Kind, err.Hint, err.Detail);
                                await WriteJsonResponseAsync(stream, err.HttpStatus, errorPayload, ct).ConfigureAwait(false);
                            }
                            return;
                        }

                        if (string.Equals(methodName, "stream_events", StringComparison.OrdinalIgnoreCase))
                        {
                            // Stream notifications over HTTP using chunked transfer encoding.
                            var header = "HTTP/1.1 200 OK\r\n" +
                                         "Content-Type: application/json\r\n" +
                                         "Transfer-Encoding: chunked\r\n" +
                                         "Connection: keep-alive\r\n\r\n";
                            var headerBytes = Encoding.UTF8.GetBytes(header);
                            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                            await stream.FlushAsync(ct).ConfigureAwait(false);

                            int id = Interlocked.Increment(ref _nextClientId);
                            _httpStreams[id] = stream;
                            try
                            {
                                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                            _httpStreams.TryRemove(id, out _);
                            return;
                        }

                        await SendJsonRpcErrorAsync(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null, ct).ConfigureAwait(false);
                        var notFoundPayload = BuildJsonRpcErrorPayload(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null);
                        await WriteJsonResponseAsync(stream, 400, notFoundPayload, ct).ConfigureAwait(false);
                        return;
                    }
                }
                finally
                {
                    if (acquiredSlot) _requestSlots.Release();
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

        private static string ReasonPhrase(int statusCode) => statusCode switch
        {
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "OK"
        };

        private static async Task WriteResponseAsync(Stream stream, int code, string text, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(text);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteJsonResponseAsync(Stream stream, int code, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private async Task BroadcastHttpStreamAsync(string json, CancellationToken ct)
        {
            if (_httpStreams.IsEmpty) return;
            var data = json + "\n";
            var payload = Encoding.UTF8.GetBytes(data);
            var prefix = Encoding.ASCII.GetBytes(payload.Length.ToString("X") + "\r\n");
            var suffix = Encoding.ASCII.GetBytes("\r\n");
            foreach (var kv in _httpStreams)
            {
                var s = kv.Value;
                try
                {
                    await s.WriteAsync(prefix, 0, prefix.Length, ct).ConfigureAwait(false);
                    await s.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
                    await s.WriteAsync(suffix, 0, suffix.Length, ct).ConfigureAwait(false);
                    await s.FlushAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    _httpStreams.TryRemove(kv.Key, out _);
                }
            }
        }

        public Task BroadcastNotificationAsync(string @event, object payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notification", @params = new { @event, payload } });
            return BroadcastHttpStreamAsync(json, ct);
        }

        private async Task SendJsonRpcResultAsync(JsonElement requestRoot, object result, CancellationToken ct)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idVal, result });
            await BroadcastHttpStreamAsync(payload, ct).ConfigureAwait(false);
        }

        private async Task SendJsonRpcErrorAsync(JsonElement requestRoot, int code, string message, string kind, string? hint, string? detail, CancellationToken ct)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            try { ExplorerCore.LogWarning($"[MCP] error {code}: {message}"); LogBuffer.Add("error", message, "mcp", kind); } catch { }
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = idVal,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            });
            await BroadcastHttpStreamAsync(payload, ct).ConfigureAwait(false);
        }

        private async Task SendJsonRpcErrorAsync(int code, string message, string kind, string? hint, string? detail, CancellationToken ct)
        {
            try { ExplorerCore.LogWarning($"[MCP] error {code}: {message}"); LogBuffer.Add("error", message, "mcp", kind); } catch { }
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            });
            await BroadcastHttpStreamAsync(payload, ct).ConfigureAwait(false);
        }

        private static object BuildJsonRpcResultPayload(JsonElement requestRoot, object result)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            return new { jsonrpc = "2.0", id = idVal, result };
        }

        private static object BuildJsonRpcErrorPayload(JsonElement requestRoot, int code, string message, string kind, string? hint = null, string? detail = null)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            return new
            {
                jsonrpc = "2.0",
                id = idVal,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            };
        }

        private static object BuildJsonRpcErrorPayload(int code, string message, string kind, string? hint = null, string? detail = null)
        {
            return new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            };
        }

        private static object BuildErrorData(string kind, string? hint, string? detail)
        {
            return detail != null
                ? new { kind, hint, detail }
                : new { kind, hint };
        }

        private static ErrorShape MapExceptionToError(Exception ex)
        {
            if (ex is ArgumentException arg)
                return new ErrorShape(ErrorInvalidParams, 400, "InvalidArgument", arg.Message, null, null);

            if (ex is InvalidOperationException inv)
            {
                return inv.Message switch
                {
                    "NotFound" => new ErrorShape(ErrorNotFound, 404, "NotFound", "Not found", null, null),
                    "PermissionDenied" => new ErrorShape(ErrorPermissionDenied, 403, "PermissionDenied", "Permission denied", null, null),
                    "ConfirmationRequired" => new ErrorShape(ErrorPermissionDenied, 403, "PermissionDenied", "Confirmation required", "resend with confirm=true", null),
                    "NotReady" => new ErrorShape(ErrorNotReady, 503, "NotReady", "Not ready", null, null),
                    _ => new ErrorShape(ErrorInvalidParams, 400, "InvalidArgument", inv.Message, null, null)
                };
            }

            if (ex is NotSupportedException notSup)
                return new ErrorShape(ErrorNotFound, 404, "NotFound", notSup.Message, null, null);

            return new ErrorShape(ErrorInternal, 500, "Internal", "Internal error", null, ex.Message);
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
            var toolTypes = new[] { typeof(UnityReadTools), typeof(UnityWriteTools) };
            foreach (var type in toolTypes)
            {
                foreach (var mi in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!Attribute.IsDefined(mi, typeof(McpServerToolAttribute)))
                        continue;

                    var name = mi.Name;
                    var desc = mi.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                    string? d = (desc.Length > 0) ? ((System.ComponentModel.DescriptionAttribute)desc[0]).Description : null;
                    // MCP inspector expects an inputSchema object; use a minimal JSON
                    // schema describing an object with arbitrary properties.
                    var inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        additionalProperties = true
                    };
                    list.Add(new { name, description = d, inputSchema });
                }
            }
            return list.ToArray();
        }

        public static async Task<object?> InvokeToolAsync(string name, JsonElement args)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("tool name required");
            var toolTypes = new[] { typeof(UnityReadTools), typeof(UnityWriteTools) };
            System.Reflection.MethodInfo? mi = null;
            foreach (var type in toolTypes)
            {
                mi = Array.Find(type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
                    m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
                         Attribute.IsDefined(m, typeof(McpServerToolAttribute)));
                if (mi != null) break;
            }
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
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) throw new ArgumentException("invalid uri");

            // Treat the host as the first path segment so that
            // both `unity://status` and `unity://scene/0/objects` map
            // to logical paths like `status` or `scene/0/objects`.
            var path = u.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(u.Host))
            {
                path = string.IsNullOrEmpty(path) ? u.Host : $"{u.Host}/{path}";
            }
            var query = ParseQuery(u.Query);

            if (path.Equals("status", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetStatus(default);
            if (path.Equals("scenes", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.ListScenes(TryInt(query, "limit"), TryInt(query, "offset"), default);
            if (path.StartsWith("scene/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/objects", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = path.Substring(6, path.Length - 6 - "/objects".Length);
                return await UnityReadTools.ListObjects(
                    sceneId,
                    TryString(query, "name"),
                    TryString(query, "type"),
                    TryBool(query, "activeOnly"),
                    TryInt(query, "limit"),
                    TryInt(query, "offset"),
                    default);
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
                return await UnityReadTools.SearchObjects(
                    TryString(query, "query"),
                    TryString(query, "name"),
                    TryString(query, "type"),
                    TryString(query, "path"),
                    TryBool(query, "activeOnly"),
                    TryInt(query, "limit"),
                    TryInt(query, "offset"),
                    default);
            }
            if (path.Equals("camera/active", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetCameraInfo(default);
            if (path.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetSelection(default);
            if (path.Equals("logs/tail", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.TailLogs(TryInt(query, "count") ?? 200, default);
            if (path.Equals("console/scripts", StringComparison.OrdinalIgnoreCase))
                return await UnityResources.ConsoleScripts(TryInt(query, "limit"), TryInt(query, "offset"), default);
            if (path.Equals("hooks", StringComparison.OrdinalIgnoreCase))
                return await UnityResources.Hooks(TryInt(query, "limit"), TryInt(query, "offset"), default);

            throw new NotSupportedException("resource not supported");
        }

        private static int? TryInt(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
        private static bool? TryBool(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : (bool?)null;
        private static string? TryString(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    }
#else
    // Stub implementation for non-INTEROP targets (e.g., net35/net472) so that
    // builds that reference UnityExplorer.Mcp.McpSimpleHttp still compile even
    // though the full streaming HTTP server is only available for INTEROP targets.
    internal sealed class McpSimpleHttp : IDisposable
    {
        public static McpSimpleHttp? Current { get; private set; }
        public int Port { get; }

        public McpSimpleHttp(string bindAddress, int port)
        {
            Port = port;
        }

        public void Start()
        {
            Current = this;
        }

        public void Dispose()
        {
            Current = null;
        }
    }
#endif
}
