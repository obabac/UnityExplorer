using System;
#if INTEROP
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
#endif
#if MONO
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityExplorer.CSConsole;
using UnityExplorer.Hooks;
using UnityExplorer.ObjectExplorer;
using UnityExplorer.UI.Panels;
using UnityExplorer.UI.Widgets;
using UniverseLib.Input;
using UniverseLib.Utility;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal sealed class McpSimpleHttp : IDisposable
    {
        public static McpSimpleHttp? Current { get; private set; }
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, Stream> _httpStreams = new();
        private readonly ConcurrentDictionary<int, Stream> _sseStreams = new();
        private readonly SemaphoreSlim _requestSlots = new(ConcurrencyLimit, ConcurrencyLimit);
        private int _nextClientId;
        private int _nextSseClientId;
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
        private const string CorsHeaders =
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Headers: Content-Type, Authorization\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Max-Age: 86400\r\n";

        private readonly record struct ErrorShape(int Code, int HttpStatus, string Kind, string Message, string? Hint, string? Detail);

        private static bool IsJsonRpcPath(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return target == "/"
                || target.StartsWith("/?", StringComparison.Ordinal)
                || target.StartsWith("/message", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStreamPath(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return target == "/"
                || target.StartsWith("/?", StringComparison.Ordinal)
                || target.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
        }

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

                if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteCorsPreflightAsync(stream, ct).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && IsJsonRpcPath(target))
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

                    bool acquiredSlot = false;
                    using (doc)
                    {
                        try
                        {
                            var root = doc.RootElement;
                            var hasId = HasId(root);
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
                            else if (string.Equals(methodName, "resources/list", StringComparison.OrdinalIgnoreCase))
                                methodName = "list_resources";

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
                                        experimental = new { streamEvents = new { } }
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
                                if (!hasId)
                                {
                                    await WriteEmptyResponseAsync(stream, 202, ct).ConfigureAwait(false);
                                }
                                else
                                {
                                    var resultObj = new { ok = true };
                                    await SendJsonRpcResultAsync(root, resultObj, ct).ConfigureAwait(false);
                                    var responsePayload = BuildJsonRpcResultPayload(root, resultObj);
                                    await WriteJsonResponseAsync(stream, 200, responsePayload, ct).ConfigureAwait(false);
                                }
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

                            if (string.Equals(methodName, "list_resources", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var resources = McpReflection.ListResources();
                                    var resultObj = new { resources };
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
                                    var wrapped = new
                                    {
                                        content = new[]
                                        {
                                            new
                                            {
                                                type = "text",
                                                text = JsonSerializer.Serialize(result),
                                                mimeType = "application/json",
                                                json = result
                                            }
                                        }
                                    };
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
                                stream.ReadTimeout = Timeout.Infinite;
                                stream.WriteTimeout = Timeout.Infinite;

                                var header = "HTTP/1.1 200 OK\r\n" +
                                             CorsHeaders +
                                             "Content-Type: application/json\r\n" +
                                             "Transfer-Encoding: chunked\r\n" +
                                             "Connection: keep-alive\r\n\r\n";
                                var headerBytes = Encoding.UTF8.GetBytes(header);
                                await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                                await stream.FlushAsync(ct).ConfigureAwait(false);

                                int id = Interlocked.Increment(ref _nextClientId);
                                _httpStreams[id] = stream;

                                await WaitForStreamDisconnectAsync(stream, id, _httpStreams, ct).ConfigureAwait(false);
                                return;
                            }

                            await SendJsonRpcErrorAsync(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null, ct).ConfigureAwait(false);
                            var notFoundPayload = BuildJsonRpcErrorPayload(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null);
                            await WriteJsonResponseAsync(stream, 400, notFoundPayload, ct).ConfigureAwait(false);
                            return;
                        }
                        finally
                        {
                            if (acquiredSlot) _requestSlots.Release();
                        }
                    }
                }

                if (method == "GET" && IsStreamPath(target) && !string.IsNullOrEmpty(acceptHeader) && acceptHeader.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stream.ReadTimeout = Timeout.Infinite;
                    stream.WriteTimeout = Timeout.Infinite;

                    var header = "HTTP/1.1 200 OK\r\n" +
                                 CorsHeaders +
                                 "Content-Type: text/event-stream\r\n" +
                                 "Cache-Control: no-cache\r\n" +
                                 "Connection: keep-alive\r\n\r\n";
                    var headerBytes = Encoding.UTF8.GetBytes(header);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);

                    int sseId = Interlocked.Increment(ref _nextSseClientId);
                    _sseStreams[sseId] = stream;

                    await WaitForStreamDisconnectAsync(stream, sseId, _sseStreams, ct).ConfigureAwait(false);
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
                        await WriteJsonResponseAsync(stream, 200, obj!, ct).ConfigureAwait(false);
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

        private static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "OK";
            }
        }

        private static async Task WriteResponseAsync(Stream stream, int code, string text, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(text);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteJsonResponseAsync(Stream stream, int code, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteEmptyResponseAsync(Stream stream, int code, CancellationToken ct)
        {
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Length: 0\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteCorsPreflightAsync(Stream stream, CancellationToken ct)
        {
            var header = $"HTTP/1.1 204 {ReasonPhrase(204)}\r\n" +
                         CorsHeaders +
                         "Content-Length: 0\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
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

        private async Task BroadcastSseAsync(string json, CancellationToken ct)
        {
            if (_sseStreams.IsEmpty) return;
            var payload = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            foreach (var kv in _sseStreams)
            {
                var s = kv.Value;
                try
                {
                    await s.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
                    await s.FlushAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    _sseStreams.TryRemove(kv.Key, out _);
                }
            }
        }

        private async Task BroadcastAllStreamsAsync(string json, CancellationToken ct)
        {
            await BroadcastHttpStreamAsync(json, ct).ConfigureAwait(false);
            await BroadcastSseAsync(json, ct).ConfigureAwait(false);
        }

        private async Task WaitForStreamDisconnectAsync(Stream stream, int id, ConcurrentDictionary<int, Stream> store, CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(buffer, 0, 1, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (read == 0)
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                store.TryRemove(id, out _);
            }
        }

        public Task BroadcastNotificationAsync(string @event, object payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notification", @params = new { @event, payload } });
            return BroadcastAllStreamsAsync(json, ct);
        }

        private async Task SendJsonRpcResultAsync(JsonElement requestRoot, object result, CancellationToken ct)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idVal, result });
            await BroadcastAllStreamsAsync(payload, ct).ConfigureAwait(false);
        }

        private async Task SendJsonRpcErrorAsync(JsonElement requestRoot, int code, string message, string kind, string? hint, string? detail, CancellationToken ct)
        {
            object? idVal = null;
            if (requestRoot.ValueKind == JsonValueKind.Object && requestRoot.TryGetProperty("id", out var idEl))
            {
                try { idVal = JsonSerializer.Deserialize<object>(idEl.GetRawText()); } catch { idVal = null; }
            }
            var logMessage = $"[MCP] error {code}: {message}";
            try
            {
                await MainThread.Run(() =>
                {
                    try { ExplorerCore.LogWarning(logMessage); } catch { }
                    try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                }).ConfigureAwait(false);
            }
            catch { }
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = idVal,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            });
            await BroadcastAllStreamsAsync(payload, ct).ConfigureAwait(false);
        }

        private async Task SendJsonRpcErrorAsync(int code, string message, string kind, string? hint, string? detail, CancellationToken ct)
        {
            var logMessage = $"[MCP] error {code}: {message}";
            try
            {
                await MainThread.Run(() =>
                {
                    try { ExplorerCore.LogWarning(logMessage); } catch { }
                    try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                }).ConfigureAwait(false);
            }
            catch { }
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new { code, message, data = BuildErrorData(kind, hint, detail) }
            });
            await BroadcastAllStreamsAsync(payload, ct).ConfigureAwait(false);
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

        private static bool HasId(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("id", out var idEl)) return false;
            return idEl.ValueKind != JsonValueKind.Null && idEl.ValueKind != JsonValueKind.Undefined;
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
        private static readonly NullabilityInfoContext Nullability = new();

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
                    var inputSchema = BuildInputSchema(mi);
                    list.Add(new { name, description = d, inputSchema });
                }
            }
            return list.ToArray();
        }

        public static object[] ListResources()
        {
            static object Resource(string uri, string name, string description)
                => new { uri, name, description, mimeType = "application/json" };

            return new object[]
            {
                Resource("unity://status", "Status", "Status snapshot resource."),
                Resource("unity://scenes", "Scenes", "List scenes resource."),
                Resource("unity://scene/{sceneId}/objects", "Scene objects", "List objects under a scene (paged)."),
                Resource("unity://object/{id}", "Object detail", "Object details by id."),
                Resource("unity://object/{id}/components", "Object components", "Components for object id (paged)."),
                Resource("unity://search", "Search objects", "Search objects across scenes."),
                Resource("unity://camera/active", "Active camera", "Active camera info."),
                Resource("unity://selection", "Selection", "Current selection / inspected tabs."),
                Resource("unity://logs/tail", "Log tail", "Tail recent MCP log buffer."),
                Resource("unity://console/scripts", "Console scripts", "List C# console scripts (from the Scripts folder)."),
                Resource("unity://hooks", "Hooks", "List active method hooks."),
            };
        }

        private static JsonSchema BuildInputSchema(System.Reflection.MethodInfo mi)
        {
            var properties = new System.Collections.Generic.Dictionary<string, JsonSchemaProperty>(StringComparer.OrdinalIgnoreCase);
            var required = new System.Collections.Generic.List<string>();
            foreach (var p in mi.GetParameters())
            {
                if (p.ParameterType == typeof(System.Threading.CancellationToken))
                    continue;

                var schema = BuildParameterSchema(mi, p);
                if (schema != null)
                {
                    properties[p.Name!] = schema;
                    if (!IsOptionalParameter(p))
                        required.Add(p.Name!);
                }
            }

            return new JsonSchema
            {
                Type = "object",
                Properties = properties,
                Required = required.Count > 0 ? required.ToArray() : null,
                AdditionalProperties = false
            };
        }

        private static JsonSchemaProperty? BuildParameterSchema(System.Reflection.MethodInfo mi, System.Reflection.ParameterInfo p)
        {
            var paramType = p.ParameterType;
            var underlying = Nullable.GetUnderlyingType(paramType) ?? paramType;
            var isArray = underlying.IsArray;

            var typeName = isArray ? "array" : MapJsonType(underlying);
            object? items = null;
            if (isArray)
            {
                var elementType = underlying.GetElementType() ?? typeof(object);
                items = new { type = MapJsonType(elementType) };
            }

            string[]? enumValues = null;
            if (string.Equals(mi.Name, "MousePick", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Name, "mode", StringComparison.OrdinalIgnoreCase))
            {
                enumValues = new[] { "world", "ui" };
            }

            object? defaultValue = null;
            if (p.HasDefaultValue)
                defaultValue = p.DefaultValue;

            return new JsonSchemaProperty
            {
                Type = typeName,
                Items = items,
                Enum = enumValues,
                Default = defaultValue
            };
        }

        private static bool IsOptionalParameter(System.Reflection.ParameterInfo p)
        {
            if (p.ParameterType == typeof(System.Threading.CancellationToken))
                return true;
            if (Nullable.GetUnderlyingType(p.ParameterType) != null)
                return true;
            if (p.HasDefaultValue)
                return true;
            if (!p.ParameterType.IsValueType)
            {
                var nullability = Nullability.Create(p);
                if (nullability.ReadState == NullabilityState.Nullable || nullability.WriteState == NullabilityState.Nullable)
                    return true;
            }
            return false;
        }

        private static string MapJsonType(Type type)
        {
            if (type.IsEnum) return "string";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type.IsArray) return "array";
            return "object";
        }

        private sealed class JsonSchema
        {
            [JsonPropertyName("type")]
            public string Type { get; init; } = "object";

            [JsonPropertyName("properties")]
            public System.Collections.Generic.Dictionary<string, JsonSchemaProperty> Properties { get; init; } = new(System.StringComparer.OrdinalIgnoreCase);

            [JsonPropertyName("required")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string[]? Required { get; init; }

            [JsonPropertyName("additionalProperties")]
            public bool AdditionalProperties { get; init; }
        }

        private sealed class JsonSchemaProperty
        {
            [JsonPropertyName("type")]
            public string Type { get; init; } = "string";

            [JsonPropertyName("items")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object? Items { get; init; }

            [JsonPropertyName("enum")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string[]? Enum { get; init; }

            [JsonPropertyName("default")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object? Default { get; init; }
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
#elif MONO
    internal sealed class McpSimpleHttp : IDisposable
    {
        public static McpSimpleHttp? Current { get; private set; }
        private readonly TcpListener _listener;
        private volatile bool _running;
        private readonly MonoMcpHandlers _handlers = new MonoMcpHandlers();
        private readonly int _concurrencyLimit = 16;
        private readonly int _streamLimit = 16;
        private const string CorsHeaders =
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Headers: Content-Type, Authorization\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Max-Age: 86400\r\n";
        private readonly Dictionary<int, Stream> _streams = new Dictionary<int, Stream>();
        private readonly object _streamGate = new object();
        private readonly Dictionary<int, Stream> _sseStreams = new Dictionary<int, Stream>();
        private readonly object _sseGate = new object();
        private volatile bool _disposed;
        private int _active;
        private int _nextStreamId;
        private int _nextSseId;
        public int Port { get; private set; }

        private static bool IsJsonRpcPath(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return target == "/"
                || target.StartsWith("/?", StringComparison.Ordinal)
                || target.StartsWith("/message", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStreamPath(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return target == "/"
                || target.StartsWith("/?", StringComparison.Ordinal)
                || target.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
        }

        public McpSimpleHttp(string bindAddress, int port)
        {
            IPAddress ip = IPAddress.Any;
            if (!string.IsNullOrEmpty(bindAddress) && IPAddress.TryParse(bindAddress, out var parsed))
                ip = parsed;
            _listener = new TcpListener(ip, port <= 0 ? 0 : port);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            Current = this;
            try { _listener.BeginAcceptTcpClient(OnAccept, null); } catch { }
        }

        private void OnAccept(IAsyncResult ar)
        {
            if (!_running) return;
            TcpClient client = null;
            try { client = _listener.EndAcceptTcpClient(ar); }
            catch { }
            finally
            {
                if (_running)
                {
                    try { _listener.BeginAcceptTcpClient(OnAccept, null); } catch { }
                }
            }
            if (client == null) return;
            ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                stream.ReadTimeout = 15000;
                stream.WriteTimeout = 15000;

                string? requestLine = reader.ReadLine();
                if (string.IsNullOrEmpty(requestLine)) return;
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;
                var method = parts[0];
                var target = parts[1];

                string? line;
                long contentLength = 0;
                string? acceptHeader = null;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    int idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var name = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(val, out contentLength);
                    if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase)) acceptHeader = val;
                }

                string body = string.Empty;
                if (method == "POST" && contentLength > 0)
                {
                    var buf = new char[contentLength];
                    int read = reader.ReadBlock(buf, 0, (int)contentLength);
                    body = new string(buf, 0, read);
                }

                if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    WriteCorsPreflight(stream);
                    return;
                }

                if (method == "GET" && IsStreamPath(target) && !string.IsNullOrEmpty(acceptHeader) && acceptHeader.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stream.ReadTimeout = Timeout.Infinite;
                    stream.WriteTimeout = Timeout.Infinite;

                    var header = "HTTP/1.1 200 OK\r\n" +
                                 CorsHeaders +
                                 "Content-Type: text/event-stream\r\n" +
                                 "Cache-Control: no-cache\r\n" +
                                 "Connection: keep-alive\r\n\r\n";
                    var headerBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Flush();

                    var sseId = Interlocked.Increment(ref _nextSseId);
                    lock (_sseGate)
                    {
                        _sseStreams[sseId] = stream;
                    }

                    WaitForStreamDisconnect(stream, sseId, true);
                    return;
                }

                if (method == "POST" && IsJsonRpcPath(target))
                {
                    ProcessJsonRpc(stream, body);
                    return;
                }

                if (method == "GET" && target.StartsWith("/read"))
                {
                    HandleRead(stream, target);
                    return;
                }

                WriteResponse(stream, 200, "ok", "text/plain");
            }
        }

        private void ProcessJsonRpc(Stream stream, string body)
        {
            JObject root;
            try
            {
                root = string.IsNullOrEmpty(body) ? new JObject() : JObject.Parse(body);
            }
            catch (Exception ex)
            {
                WriteJsonError(stream, null, -32700, "Parse error", "InvalidRequest", null, ex.Message, 400);
                return;
            }

            var idToken = root["id"];
            var hasId = idToken != null && idToken.Type != JTokenType.Null && idToken.Type != JTokenType.Undefined;
            var methodName = root.Value<string>("method") ?? string.Empty;
            if (string.IsNullOrEmpty(methodName))
            {
                WriteJsonError(stream, idToken, -32600, "Invalid request: 'method' is required.", "InvalidRequest", null, null, 400);
                return;
            }

            if (string.Equals(methodName, "tools/list", StringComparison.OrdinalIgnoreCase)) methodName = "list_tools";
            else if (string.Equals(methodName, "tools/call", StringComparison.OrdinalIgnoreCase)) methodName = "call_tool";
            else if (string.Equals(methodName, "resources/read", StringComparison.OrdinalIgnoreCase)) methodName = "read_resource";
            else if (string.Equals(methodName, "resources/list", StringComparison.OrdinalIgnoreCase)) methodName = "list_resources";

            if (string.Equals(methodName, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
            {
                if (hasId)
                {
                    WriteJsonResult(stream, idToken, new { ok = true });
                }
                else
                {
                    WriteEmptyResponse(stream, 202);
                }
                return;
            }

            if (string.Equals(methodName, "ping", StringComparison.OrdinalIgnoreCase))
            {
                WriteJsonResult(stream, idToken, new { });
                return;
            }

            if (string.Equals(methodName, "initialize", StringComparison.OrdinalIgnoreCase))
            {
                var result = _handlers.BuildInitializeResult();
                WriteJsonResult(stream, idToken, result);
                return;
            }

            if (string.Equals(methodName, "list_tools", StringComparison.OrdinalIgnoreCase))
            {
                WriteJsonResult(stream, idToken, new { tools = _handlers.ListTools() });
                return;
            }

            if (string.Equals(methodName, "list_resources", StringComparison.OrdinalIgnoreCase))
            {
                WriteJsonResult(stream, idToken, new { resources = _handlers.ListResources() });
                return;
            }

            if (string.Equals(methodName, "call_tool", StringComparison.OrdinalIgnoreCase))
            {
                if (!CheckSlot(stream, idToken)) return;
                string name = string.Empty;
                try
                {
                    var args = root["params"]?["arguments"] as JObject;
                    name = (root["params"]?["name"] ?? root["params"]?["tool"] ?? string.Empty).ToString();
                    if (string.IsNullOrEmpty(name))
                        throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid params: 'name' is required.");
                    var result = _handlers.CallTool(name, args);
                    WriteJsonResult(stream, idToken, new { content = new[] { new { type = "text", text = JsonConvert.SerializeObject(result), mimeType = "application/json", json = result } } });
                    try { BroadcastNotificationAsync("tool_result", new { name, ok = true, result }); } catch { }
                }
                catch (MonoMcpHandlers.McpError err)
                {
                    WriteJsonError(stream, idToken, err.Code, err.Message, err.Kind, err.Hint, err.Detail, err.HttpStatus);
                    try
                    {
                        var data = BuildErrorData(err.Kind, err.Hint, err.Detail);
                        BroadcastNotificationAsync("tool_result", new { name, ok = false, error = new { code = err.Code, message = err.Message, data } });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    WriteJsonError(stream, idToken, -32603, "Internal error", "Internal", null, ex.Message, 500);
                    try
                    {
                        var data = BuildErrorData("Internal", null, ex.Message);
                        BroadcastNotificationAsync("tool_result", new { name, ok = false, error = new { code = -32603, message = "Internal error", data } });
                    }
                    catch { }
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
                return;
            }

            if (string.Equals(methodName, "read_resource", StringComparison.OrdinalIgnoreCase))
            {
                if (!CheckSlot(stream, idToken)) return;
                try
                {
                    var uri = root["params"]?["uri"] != null ? root["params"]!["uri"]!.ToString() : null;
                    if (string.IsNullOrEmpty(uri))
                        throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid params: 'uri' is required.");
                    var result = _handlers.ReadResource(uri);
                    var json = JsonConvert.SerializeObject(result);
                    WriteJsonResult(stream, idToken, new { contents = new[] { new { uri, mimeType = "application/json", text = json } } });
                }
                catch (MonoMcpHandlers.McpError err)
                {
                    WriteJsonError(stream, idToken, err.Code, err.Message, err.Kind, err.Hint, err.Detail, err.HttpStatus);
                }
                catch (Exception ex)
                {
                    WriteJsonError(stream, idToken, -32603, "Internal error", "Internal", null, ex.Message, 500);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
                return;
            }

            if (string.Equals(methodName, "stream_events", StringComparison.OrdinalIgnoreCase))
            {
                if (!_running)
                {
                    WriteJsonError(stream, idToken, -32002, "Not ready", "NotReady", null, null, 503);
                    return;
                }

                lock (_streamGate)
                {
                    if (_streams.Count >= _streamLimit)
                    {
                        var msg = $"Cannot have more than {_streamLimit} parallel streams. Please slow down.";
                        WriteJsonError(stream, idToken, -32005, msg, "RateLimited", null, null, 429);
                        return;
                    }
                }

                stream.ReadTimeout = Timeout.Infinite;
                stream.WriteTimeout = Timeout.Infinite;

                var header = "HTTP/1.1 200 OK\r\n" +
                             CorsHeaders +
                             "Content-Type: application/json\r\n" +
                             "Transfer-Encoding: chunked\r\n" +
                             "Connection: keep-alive\r\n\r\n";
                var headerBytes = Encoding.UTF8.GetBytes(header);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Flush();

                var id = Interlocked.Increment(ref _nextStreamId);
                lock (_streamGate)
                {
                    _streams[id] = stream;
                }

                WaitForStreamDisconnect(stream, id);
                return;
            }

            WriteJsonError(stream, idToken, -32601, "Method not found: " + methodName, "MethodNotFound", null, null, 400);
        }

        private bool CheckSlot(Stream stream, JToken? idToken)
        {
            var active = Interlocked.Increment(ref _active);
            if (active > _concurrencyLimit)
            {
                Interlocked.Decrement(ref _active);
                var msg = $"Cannot have more than {_concurrencyLimit} parallel requests. Please slow down.";
                WriteJsonError(stream, idToken, -32005, msg, "RateLimited", null, null, 429);
                return false;
            }
            return true;
        }

        private void HandleRead(Stream stream, string target)
        {
            Dictionary<string, string> query;
            try
            {
                var parsed = new Uri("http://localhost" + target);
                query = MonoMcpHandlers.ParseQuery(parsed.Query);
            }
            catch
            {
                WriteResponse(stream, 400, "missing uri", "text/plain");
                return;
            }

            if (!query.TryGetValue("uri", out var uri) || string.IsNullOrEmpty(uri))
            {
                WriteResponse(stream, 400, "missing uri", "text/plain");
                return;
            }
            try
            {
                var obj = _handlers.ReadResource(uri);
                var json = JsonConvert.SerializeObject(obj);
                WriteResponse(stream, 200, json, "application/json");
            }
            catch (Exception ex)
            {
                WriteResponse(stream, 400, ex.Message, "text/plain");
            }
        }

        internal SelectionDto GetSelectionSnapshot()
        {
            try
            {
                var obj = _handlers.ReadResource("unity://selection") as SelectionDto;
                return obj ?? new SelectionDto();
            }
            catch { return new SelectionDto(); }
        }

        public void Dispose()
        {
            _running = false;
            _disposed = true;
            try
            {
                lock (_streamGate)
                {
                    foreach (var kv in _streams)
                    {
                        try { kv.Value.Dispose(); } catch { }
                    }
                    _streams.Clear();
                }
            }
            catch { }
            try
            {
                lock (_sseGate)
                {
                    foreach (var kv in _sseStreams)
                    {
                        try { kv.Value.Dispose(); } catch { }
                    }
                    _sseStreams.Clear();
                }
            }
            catch { }
            try { _listener.Stop(); } catch { }
            Current = null;
        }

        private static void WriteResponse(Stream stream, int statusCode, string body, string contentType)
        {
            if (body == null) body = string.Empty;
            var payload = Encoding.UTF8.GetBytes(body);
            var header = "HTTP/1.1 " + statusCode + " " + ReasonPhrase(statusCode) + "\r\n"
                         + CorsHeaders
                         + "Content-Type: " + contentType + "\r\n"
                         + "Content-Length: " + payload.Length + "\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static void WriteEmptyResponse(Stream stream, int statusCode)
        {
            var header = "HTTP/1.1 " + statusCode + " " + ReasonPhrase(statusCode) + "\r\n"
                         + CorsHeaders
                         + "Content-Length: 0\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
        }

        private static void WriteCorsPreflight(Stream stream)
        {
            var header = "HTTP/1.1 204 " + ReasonPhrase(204) + "\r\n"
                         + CorsHeaders
                         + "Content-Length: 0\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
        }

        private void BroadcastPayload(JObject payload)
        {
            if (_disposed)
                return;

            List<KeyValuePair<int, Stream>> snapshot;
            List<KeyValuePair<int, Stream>> sseSnapshot;
            lock (_streamGate)
            {
                snapshot = new List<KeyValuePair<int, Stream>>(_streams);
            }
            lock (_sseGate)
            {
                sseSnapshot = new List<KeyValuePair<int, Stream>>(_sseStreams);
            }

            if (snapshot.Count == 0 && sseSnapshot.Count == 0)
                return;

            var json = payload.ToString(Formatting.None);
            var chunkData = json + "\n";
            var data = Encoding.UTF8.GetBytes(chunkData);
            var prefix = Encoding.ASCII.GetBytes(data.Length.ToString("X") + "\r\n");
            var suffix = Encoding.ASCII.GetBytes("\r\n");
            var sseData = Encoding.UTF8.GetBytes("data: " + json + "\n\n");

            foreach (var kv in snapshot)
            {
                if (_disposed) break;
                var s = kv.Value;
                try
                {
                    s.Write(prefix, 0, prefix.Length);
                    s.Write(data, 0, data.Length);
                    s.Write(suffix, 0, suffix.Length);
                    s.Flush();
                }
                catch
                {
                    RemoveStream(kv.Key);
                }
            }

            foreach (var kv in sseSnapshot)
            {
                if (_disposed) break;
                var s = kv.Value;
                try
                {
                    s.Write(sseData, 0, sseData.Length);
                    s.Flush();
                }
                catch
                {
                    RemoveSseStream(kv.Key);
                }
            }
        }

        public void BroadcastNotificationAsync(string @event, object payload)
        {
            var body = new JObject
            {
                { "jsonrpc", "2.0" },
                { "method", "notification" },
                { "params", new JObject { { "event", @event }, { "payload", payload == null ? JValue.CreateNull() : JToken.FromObject(payload) } } }
            };
            BroadcastPayload(body);
        }

        private JObject BuildResultPayload(JToken? idToken, object result)
        {
            return new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", idToken ?? JValue.CreateNull() },
                { "result", result == null ? JValue.CreateNull() : JToken.FromObject(result) }
            };
        }

        private JObject BuildErrorPayload(JToken? idToken, int code, string message, string kind, string? hint, string? detail)
        {
            var data = BuildErrorData(kind, hint, detail);
            return new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", idToken ?? JValue.CreateNull() },
                { "error", new JObject { { "code", code }, { "message", message }, { "data", data } } }
            };
        }

        private static JObject BuildErrorData(string kind, string? hint, string? detail)
        {
            var data = new JObject { { "kind", kind } };
            if (!string.IsNullOrEmpty(hint)) data["hint"] = hint;
            if (!string.IsNullOrEmpty(detail)) data["detail"] = detail;
            return data;
        }

        private void RemoveStream(int id)
        {
            lock (_streamGate)
            {
                if (_streams.TryGetValue(id, out var s))
                {
                    try { s.Dispose(); } catch { }
                    _streams.Remove(id);
                }
            }
        }

        private void RemoveSseStream(int id)
        {
            lock (_sseGate)
            {
                if (_sseStreams.TryGetValue(id, out var s))
                {
                    try { s.Dispose(); } catch { }
                    _sseStreams.Remove(id);
                }
            }
        }

        private void WaitForStreamDisconnect(Stream stream, int id, bool isSse = false)
        {
            var buffer = new byte[1];
            try
            {
                while (_running && !_disposed)
                {
                    int read;
                    try { read = stream.Read(buffer, 0, 1); }
                    catch { break; }
                    if (read <= 0) break;
                }
            }
            finally
            {
                if (isSse)
                    RemoveSseStream(id);
                else
                    RemoveStream(id);
            }
        }

        private void WriteJsonResult(Stream stream, JToken? idToken, object result)
        {
            var payload = BuildResultPayload(idToken, result);
            BroadcastPayload(payload);
            WriteResponse(stream, 200, payload.ToString(Formatting.None), "application/json");
        }

        private void WriteJsonError(Stream stream, JToken? idToken, int code, string message, string kind, string? hint, string? detail, int statusCode)
        {
            var logMessage = $"[MCP] error {code}: {message}";
            try
            {
                MainThread.Run(() =>
                {
                    try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                    try { ExplorerCore.LogWarning(logMessage); } catch { }
                });
            }
            catch
            {
                try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                try { ExplorerCore.LogWarning(logMessage); } catch { }
            }

            var payload = BuildErrorPayload(idToken, code, message, kind, hint, detail);
            BroadcastPayload(payload);
            WriteResponse(stream, statusCode, payload.ToString(Formatting.None), "application/json");
        }

        private static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "OK";
            }
        }
    }

    internal sealed class MonoMcpHandlers
    {
        internal sealed class McpError : Exception
        {
            public int Code { get; }
            public int HttpStatus { get; }
            public string Kind { get; }
            public string? Hint { get; }
            public string? Detail { get; }

            public McpError(int code, int httpStatus, string kind, string message, string? hint = null, string? detail = null)
                : base(message)
            {
                Code = code;
                HttpStatus = httpStatus;
                Kind = kind;
                Hint = hint;
                Detail = detail;
            }
        }

        private readonly MonoReadTools _tools = new MonoReadTools();
        private readonly MonoWriteTools _write;

        public MonoMcpHandlers()
        {
            _write = new MonoWriteTools(_tools);
        }

        public object BuildInitializeResult()
        {
            var protocolVersion = "2024-11-05";
            var serverInfo = new
            {
                name = "UnityExplorer.Mcp.Mono",
                version = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0"
            };
            var capabilities = new
            {
                tools = new { listChanged = true },
                resources = new { listChanged = true },
                experimental = new { streamEvents = new { } }
                
            };
            var instructions = "Unity Explorer MCP (Mono) exposes status, scenes, objects, selection, logs, camera, and mouse pick over streamable-http. Guarded writes (SetActive, SetMember, ConsoleEval, AddComponent, RemoveComponent, HookAdd, HookRemove, Reparent, DestroyObject, SelectObject, SetTimeScale, SpawnTestUi, DestroyTestUi) are available when allowWrites=true (requireConfirm recommended; use SpawnTestUi blocks as safe targets and keep the component/hook allowlists configured). stream_events provides log/scene/selection/tool_result notifications.";
            return new { protocolVersion, capabilities, serverInfo, instructions };
        }

        public object[] ListTools()
        {
            var list = new List<object>();
            list.Add(new { name = "SetConfig", description = "Update MCP config settings and optionally restart the server.", inputSchema = Schema(new Dictionary<string, object> { { "allowWrites", Bool() }, { "requireConfirm", Bool() }, { "enableConsoleEval", Bool() }, { "componentAllowlist", new { type = "array", items = String() } }, { "reflectionAllowlistMembers", new { type = "array", items = String() } }, { "hookAllowlistSignatures", new { type = "array", items = String() } }, { "restart", Bool() } }) });
            list.Add(new { name = "GetConfig", description = "Read current MCP config (sanitized).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "GetStatus", description = "Status snapshot of Unity Explorer.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "ListScenes", description = "List scenes (paged).", inputSchema = Schema(new Dictionary<string, object> { { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "ListObjects", description = "List objects in a scene or all scenes.", inputSchema = Schema(new Dictionary<string, object> { { "sceneId", String() }, { "name", String() }, { "type", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "GetObject", description = "Get object details by id.", inputSchema = Schema(new Dictionary<string, object> { { "id", String() } }, new[] { "id" }) });
            list.Add(new { name = "GetComponents", description = "List component cards for an object.", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "objectId" }) });
            list.Add(new { name = "GetVersion", description = "Version info for Unity Explorer MCP.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SearchObjects", description = "Search objects by name/type/path.", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "name", String() }, { "type", String() }, { "path", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "GetCameraInfo", description = "Get active camera info.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new
            {
                name = "MousePick",
                description = "Raycast at current mouse position to pick a world or UI object.",
                inputSchema = Schema(new Dictionary<string, object>
                {
                    { "mode", new { type = "string", @enum = new[] { "world", "ui" }, @default = "world" } },
                    { "x", Number() },
                    { "y", Number() },
                    { "normalized", Bool(false) }
                })
            });
            list.Add(new { name = "TailLogs", description = "Tail recent logs.", inputSchema = Schema(new Dictionary<string, object> { { "count", Integer(200) } }) });
            list.Add(new { name = "GetSelection", description = "Current selection / inspected tabs.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetActive", description = "Set GameObject active state (guarded by allowWrites/confirm).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "active", Bool() }, { "confirm", Bool(false) } }, new[] { "objectId", "active" }) });
            list.Add(new { name = "SetMember", description = "Set a field or property on a component (allowlist enforced).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "componentType", String() }, { "member", String() }, { "jsonValue", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "componentType", "member", "jsonValue" }) });
            list.Add(new { name = "ConsoleEval", description = "Evaluate a small C# snippet in the UnityExplorer console context (guarded by config).", inputSchema = Schema(new Dictionary<string, object> { { "code", String() }, { "confirm", Bool(false) } }, new[] { "code" }) });
            list.Add(new { name = "AddComponent", description = "Add a component by full type name to a GameObject (guarded by allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "type", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "type" }) });
            list.Add(new { name = "RemoveComponent", description = "Remove a component by full type name or index from a GameObject (allowlist enforced when by type).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "typeOrIndex", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "typeOrIndex" }) });
            list.Add(new { name = "HookAdd", description = "Add a Harmony hook for the given type and method (guarded by hook allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "type", String() }, { "method", String() }, { "confirm", Bool(false) } }, new[] { "type", "method" }) });
            list.Add(new { name = "HookRemove", description = "Remove a previously added Harmony hook by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "confirm", Bool(false) } }, new[] { "signature" }) });
            list.Add(new { name = "Reparent", description = "Reparent a GameObject under a new parent (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "newParentId", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "newParentId" }) });
            list.Add(new { name = "DestroyObject", description = "Destroy a GameObject (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "confirm", Bool(false) } }, new[] { "objectId" }) });
            list.Add(new { name = "SelectObject", description = "Select a GameObject in the inspector (requires allowWrites).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() } }, new[] { "objectId" }) });
            list.Add(new { name = "GetTimeScale", description = "Get current time-scale (read-only).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetTimeScale", description = "Set Unity time-scale (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "value", Number() }, { "lock", Bool() }, { "confirm", Bool(false) } }, new[] { "value" }) });
            list.Add(new { name = "SpawnTestUi", description = "Spawn a simple UI canvas for MousePick UI validation (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
            list.Add(new { name = "DestroyTestUi", description = "Destroy the test UI canvas (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
            return list.ToArray();
        }

        public object[] ListResources()
        {
            static object Resource(string uri, string name, string description)
                => new { uri, name, description, mimeType = "application/json" };

            return new object[]
            {
                Resource("unity://status", "Status", "Status snapshot resource."),
                Resource("unity://scenes", "Scenes", "List scenes resource."),
                Resource("unity://scene/{sceneId}/objects", "Scene objects", "List objects under a scene (paged)."),
                Resource("unity://object/{id}", "Object detail", "Object details by id."),
                Resource("unity://object/{id}/components", "Object components", "Components for object id (paged)."),
                Resource("unity://search", "Search objects", "Search objects across scenes."),
                Resource("unity://camera/active", "Active camera", "Active camera info."),
                Resource("unity://selection", "Selection", "Current selection / inspected tabs."),
                Resource("unity://logs/tail", "Log tail", "Tail recent MCP log buffer."),
            };
        }

        public object CallTool(string name, JObject? args)
        {
            var key = (name ?? string.Empty).ToLowerInvariant();
            try { LogBuffer.Add("debug", "call_tool:" + key, "mcp"); } catch { }
            switch (key)
            {
                case "setconfig":
                    return _write.SetConfig(
                        GetBool(args, "allowWrites"),
                        GetBool(args, "requireConfirm"),
                        GetBool(args, "enableConsoleEval"),
                        GetStringArray(args, "componentAllowlist"),
                        GetStringArray(args, "reflectionAllowlistMembers"),
                        GetStringArray(args, "hookAllowlistSignatures"),
                        GetBool(args, "restart") ?? false);
                case "getconfig":
                    return _write.GetConfig();
                case "getstatus":
                    return _tools.GetStatus();
                case "listscenes":
                    return _tools.ListScenes(GetInt(args, "limit"), GetInt(args, "offset"));
                case "listobjects":
                    return _tools.ListObjects(GetString(args, "sceneId"), GetString(args, "name"), GetString(args, "type"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                case "getobject":
                    {
                        var id = RequireString(args, "id", "Invalid params: 'id' is required.");
                        return _tools.GetObject(id);
                    }
                case "getcomponents":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _tools.GetComponents(oid, GetInt(args, "limit"), GetInt(args, "offset"));
                    }
                case "getversion":
                    return _tools.GetVersion();
                case "searchobjects":
                    return _tools.SearchObjects(GetString(args, "query"), GetString(args, "name"), GetString(args, "type"), GetString(args, "path"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                case "getcamerainfo":
                    return _tools.GetCameraInfo();
                case "mousepick":
                    return _tools.MousePick(GetString(args, "mode"), GetFloat(args, "x"), GetFloat(args, "y"), GetBool(args, "normalized") ?? false);
                case "taillogs":
                    return _tools.TailLogs(GetInt(args, "count") ?? 200);
                case "getselection":
                    return _tools.GetSelection();
                case "setactive":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var active = GetBool(args, "active");
                        if (active == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'active' is required.");
                        return _write.SetActive(oid, active.Value, GetBool(args, "confirm") ?? false);
                    }
                case "setmember":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "componentType", "Invalid params: 'componentType' is required.");
                        var member = RequireString(args, "member", "Invalid params: 'member' is required.");
                        var jsonValue = RequireString(args, "jsonValue", "Invalid params: 'jsonValue' is required.");
                        return _write.SetMember(oid, type, member, jsonValue, GetBool(args, "confirm") ?? false);
                    }
                case "consoleeval":
                    {
                        var code = RequireString(args, "code", "Invalid params: 'code' is required.");
                        return _write.ConsoleEval(code, GetBool(args, "confirm") ?? false);
                    }
                case "addcomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        return _write.AddComponent(oid, type, GetBool(args, "confirm") ?? false);
                    }
                case "removecomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var typeOrIndex = RequireString(args, "typeOrIndex", "Invalid params: 'typeOrIndex' is required.");
                        return _write.RemoveComponent(oid, typeOrIndex, GetBool(args, "confirm") ?? false);
                    }
                case "hookadd":
                    {
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        var method = RequireString(args, "method", "Invalid params: 'method' is required.");
                        return _write.HookAdd(type, method, GetBool(args, "confirm") ?? false);
                    }
                case "hookremove":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        return _write.HookRemove(signature, GetBool(args, "confirm") ?? false);
                    }
                case "reparent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var pid = RequireString(args, "newParentId", "Invalid params: 'newParentId' is required.");
                        return _write.Reparent(oid, pid, GetBool(args, "confirm") ?? false);
                    }
                case "destroyobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _write.DestroyObject(oid, GetBool(args, "confirm") ?? false);
                    }
                case "selectobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _write.SelectObject(oid);
                    }
                case "gettimescale":
                    return _write.GetTimeScale();
                case "settimescale":
                    {
                        var val = GetFloat(args, "value");
                        if (val == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'value' is required.");
                        return _write.SetTimeScale(val.Value, GetBool(args, "lock"), GetBool(args, "confirm") ?? false);
                    }
                case "spawntestui":
                    return _write.SpawnTestUi(GetBool(args, "confirm") ?? false);
                case "destroytestui":
                    return _write.DestroyTestUi(GetBool(args, "confirm") ?? false);
                default:
                    throw new McpError(-32004, 404, "NotFound", "Tool not found: " + name);
            }
        }

        public object ReadResource(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                throw new McpError(-32602, 400, "InvalidArgument", "uri required");

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
                throw new McpError(-32602, 400, "InvalidArgument", "invalid uri");

            var path = u.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(u.Host))
                path = string.IsNullOrEmpty(path) ? u.Host : $"{u.Host}/{path}";
            var query = ParseQuery(u.Query);

            if (path.Equals("status", StringComparison.OrdinalIgnoreCase)) return _tools.GetStatus();
            if (path.Equals("scenes", StringComparison.OrdinalIgnoreCase)) return _tools.ListScenes(TryInt(query, "limit"), TryInt(query, "offset"));
            if (path.StartsWith("scene/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/objects", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = path.Substring(6, path.Length - 6 - "/objects".Length);
                return _tools.ListObjects(sceneId, TryString(query, "name"), TryString(query, "type"), TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length);
                return _tools.GetObject(id);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/components".Length);
                return _tools.GetComponents(id, TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                return _tools.SearchObjects(TryString(query, "query"), TryString(query, "name"), TryString(query, "type"), TryString(query, "path"), TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.Equals("camera/active", StringComparison.OrdinalIgnoreCase)) return _tools.GetCameraInfo();
            if (path.Equals("selection", StringComparison.OrdinalIgnoreCase)) return _tools.GetSelection();
            if (path.Equals("logs/tail", StringComparison.OrdinalIgnoreCase)) return _tools.TailLogs(TryInt(query, "count") ?? 200);

            throw new McpError(-32004, 404, "NotFound", "resource not supported");
        }

        internal static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            var pairs = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split(new[] { '=' }, 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                dict[k] = v;
            }
            return dict;
        }

        private static object Schema(Dictionary<string, object> props, string[]? required = null)
        {
            return new
            {
                type = "object",
                properties = props,
                required = required != null && required.Length > 0 ? required : new string[0],
                additionalProperties = false
            };
        }

        private static object String() => new { type = "string" };
        private static object Integer(object? defaultValue = null) => defaultValue == null ? new { type = "integer" } : new { type = "integer", @default = defaultValue };
        private static object Number(object? defaultValue = null) => defaultValue == null ? new { type = "number" } : new { type = "number", @default = defaultValue };
        private static object Bool(object? defaultValue = null) => defaultValue == null ? new { type = "boolean" } : new { type = "boolean", @default = defaultValue };

        private static int? GetInt(JObject? args, string name)
            => args != null && args[name] != null && int.TryParse(args[name]!.ToString(), out var v) ? v : (int?)null;
        private static float? GetFloat(JObject? args, string name)
            => args != null && args[name] != null && float.TryParse(args[name]!.ToString(), out var v) ? v : (float?)null;
        private static bool? GetBool(JObject? args, string name)
            => args != null && args[name] != null && bool.TryParse(args[name]!.ToString(), out var v) ? v : (bool?)null;
        private static string? GetString(JObject? args, string name)
            => args != null && args[name] != null ? args[name]!.ToString() : null;
        private static string[]? GetStringArray(JObject? args, string name)
            => args != null && args[name] is JArray arr ? arr.Select(v => v.ToString()).ToArray() : null;
        private static string RequireString(JObject? args, string name, string message)
        {
            var s = GetString(args, name);
            if (string.IsNullOrEmpty(s)) throw new McpError(-32602, 400, "InvalidArgument", message);
            return s!;
        }

        private static int? TryInt(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
        private static bool? TryBool(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : (bool?)null;
        private static string? TryString(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && !IsNullOrWhiteSpace(s) ? s : null;

        private static bool IsNullOrWhiteSpace(string? value)
        {
            return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
        }
    }

    internal sealed class MonoReadTools
    {
        private string? _fallbackSelectionActive;
        private readonly List<string> _fallbackSelectionItems = new List<string>();

        internal void RecordSelection(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return;
            _fallbackSelectionActive = objectId;
            if (!_fallbackSelectionItems.Contains(objectId))
                _fallbackSelectionItems.Insert(0, objectId);
        }

        private sealed class TraversalEntry
        {
            public GameObject GameObject { get; }
            public string Path { get; }

            public TraversalEntry(GameObject go, string path)
            {
                GameObject = go;
                Path = path;
            }
        }

        private IEnumerable<TraversalEntry> Traverse(GameObject root)
        {
            var stack = new Stack<TraversalEntry>();
            stack.Push(new TraversalEntry(root, "/" + root.name));
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                yield return entry;
                var t = entry.GameObject.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var c = t.GetChild(i);
                    stack.Push(new TraversalEntry(c.gameObject, entry.Path + "/" + c.name));
                }
            }
        }

        internal GameObject? FindByInstanceId(int instanceId)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root.GetInstanceID() == instanceId)
                        return root;
                    foreach (var entry in Traverse(root))
                    {
                        if (entry.GameObject.GetInstanceID() == instanceId)
                            return entry.GameObject;
                    }
                }
            }

            try
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go != null && go.GetInstanceID() == instanceId)
                        return go;
                }
            }
            catch { }

            return null;
        }

        private string BuildPath(Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }

        private sealed class SelectionSnapshot
        {
            public string? ActiveId { get; set; }
            public List<string> Items { get; set; } = new List<string>();
        }

        private SelectionSnapshot CaptureSelection()
        {
            var snap = new SelectionSnapshot();
            try
            {
                if (InspectorManager.ActiveInspector?.Target is GameObject ago)
                    snap.ActiveId = "obj:" + ago.GetInstanceID();
                foreach (var ins in InspectorManager.Inspectors)
                {
                    if (ins.Target is GameObject go)
                    {
                        var id = "obj:" + go.GetInstanceID();
                        if (!snap.Items.Contains(id))
                            snap.Items.Add(id);
                    }
                }
            }
            catch { }

            if (snap.ActiveId != null)
                RecordSelection(snap.ActiveId);

            if (snap.Items.Count == 0 && _fallbackSelectionItems.Count > 0)
            {
                foreach (var id in _fallbackSelectionItems)
                {
                    if (!snap.Items.Contains(id))
                        snap.Items.Add(id);
                }
            }

            if (string.IsNullOrEmpty(snap.ActiveId) && !string.IsNullOrEmpty(_fallbackSelectionActive))
                snap.ActiveId = _fallbackSelectionActive;

            if (snap.ActiveId != null && !snap.Items.Contains(snap.ActiveId))
                snap.Items.Insert(0, snap.ActiveId);
            return snap;
        }

        public StatusDto GetStatus()
        {
            return MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var selection = CaptureSelection().Items;
                return new StatusDto
                {
                    Version = "0.1.0",
                    UnityVersion = Application.unityVersion,
                    Platform = platform,
                    Runtime = runtime,
                    ExplorerVersion = ExplorerCore.VERSION,
                    Ready = scenesLoaded > 0,
                    ScenesLoaded = scenesLoaded,
                    Selection = selection
                };
            });
        }

        public Page<SceneDto> ListScenes(int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var scenes = new List<SceneDto>();
                var total = SceneManager.sceneCount;
                for (int i = 0; i < total; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    scenes.Add(new SceneDto { Id = "scn:" + i, Name = s.name, Index = i, IsLoaded = s.isLoaded, RootCount = s.rootCount });
                }
                var items = scenes.Skip(off).Take(lim).ToList();
                return new Page<SceneDto>(total, items);
            });
        }

        public Page<ObjectCardDto> ListObjects(string? sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;

                IEnumerable<GameObject> AllRoots()
                {
                    if (!string.IsNullOrEmpty(sceneId) && sceneId!.StartsWith("scn:"))
                    {
                        if (int.TryParse(sceneId.Substring(4), out var idx) && idx >= 0 && idx < SceneManager.sceneCount)
                            return SceneManager.GetSceneAt(idx).GetRootGameObjects();
                        return new GameObject[0];
                    }
                    var list = new List<GameObject>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        list.AddRange(SceneManager.GetSceneAt(i).GetRootGameObjects());
                    return list;
                }

                foreach (var root in AllRoots())
                {
                    foreach (var entry in Traverse(root))
                    {
                        var go = entry.GameObject;
                        var path = entry.Path;
                        if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                        if (!string.IsNullOrEmpty(name) && (go.name == null || go.name.IndexOf(name!, StringComparison.OrdinalIgnoreCase) < 0)) { total++; continue; }
                        if (!string.IsNullOrEmpty(type) && go.GetComponent(type!) == null) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { var comps = go.GetComponents<Component>(); compCount = comps != null ? comps.Length : 0; } catch { }
                            results.Add(new ObjectCardDto
                            {
                                Id = "obj:" + go.GetInstanceID(),
                                Name = go.name,
                                Path = path,
                                Tag = SafeTag(go),
                                Layer = go.layer,
                                Active = go.activeSelf,
                                ComponentCount = compCount
                            });
                        }
                        total++;
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(total, results);
            });
        }

        private static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch { return string.Empty; }
        }

        public ObjectCardDto GetObject(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Trim().Length == 0 || !id.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid id; expected 'obj:<instanceId>'");

            if (!int.TryParse(id.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                var path = BuildPath(go.transform);
                int compCount = 0;
                try { var comps2 = go.GetComponents<Component>(); compCount = comps2 != null ? comps2.Length : 0; } catch { }
                return new ObjectCardDto
                {
                    Id = "obj:" + go.GetInstanceID(),
                    Name = go.name,
                    Path = path,
                    Tag = SafeTag(go),
                    Layer = go.layer,
                    Active = go.activeSelf,
                    ComponentCount = compCount
                };
            });
        }

        public Page<ComponentCardDto> GetComponents(string objectId, int? limit, int? offset)
        {
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");

            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                Component[] comps;
                try { comps = go.GetComponents<Component>(); }
                catch { comps = new Component[0]; }
                var total = comps.Length;
                var list = new List<ComponentCardDto>();
                for (int i = off; i < Math.Min(total, off + lim); i++)
                {
                    var c = comps[i];
                    string typeName;
                    string summary;
                    if (c == null)
                    {
                        typeName = "<null>";
                        summary = "<null>";
                    }
                    else
                    {
                        var t = c.GetType();
                        typeName = t != null ? (t.FullName ?? "<null>") : "<null>";
                        var s = c.ToString();
                        summary = string.IsNullOrEmpty(s) ? "<null>" : s;
                    }
                    list.Add(new ComponentCardDto { Type = typeName, Summary = summary });
                }
                return new Page<ComponentCardDto>(total, list);
            });
        }

        public VersionInfoDto GetVersion()
        {
            return MainThread.Run(() =>
            {
                var explorerVersion = ExplorerCore.VERSION;
                var mcpVersion = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var unityVersion = Application.unityVersion;
                var runtime = Universe.Context.ToString();
                return new VersionInfoDto
                {
                    ExplorerVersion = explorerVersion,
                    McpVersion = mcpVersion,
                    UnityVersion = unityVersion,
                    Runtime = runtime
                };
            });
        }

        public Page<ObjectCardDto> SearchObjects(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var entry in Traverse(root))
                        {
                            var go = entry.GameObject;
                            var p = entry.Path;
                            if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                            var nm = go.name ?? string.Empty;
                            var match = true;
                            if (!string.IsNullOrEmpty(query))
                                match &= nm.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!string.IsNullOrEmpty(name))
                                match &= nm.IndexOf(name!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!string.IsNullOrEmpty(type))
                                match &= go.GetComponent(type!) != null;
                            if (!string.IsNullOrEmpty(path))
                                match &= p.IndexOf(path!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match) { total++; continue; }

                            if (total >= off && results.Count < lim)
                            {
                                int compCount = 0;
                                try { var comps3 = go.GetComponents<Component>(); compCount = comps3 != null ? comps3.Length : 0; } catch { }
                                results.Add(new ObjectCardDto
                                {
                                    Id = "obj:" + go.GetInstanceID(),
                                    Name = nm,
                                    Path = p,
                                    Tag = SafeTag(go),
                                    Layer = go.layer,
                                    Active = go.activeSelf,
                                    ComponentCount = compCount
                                });
                            }
                            total++;
                            if (results.Count >= lim) break;
                        }
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(total, results);
            });
        }

        public CameraInfoDto GetCameraInfo()
        {
            return MainThread.Run(() =>
            {
                var freecam = FreeCamPanel.inFreeCamMode;
                Camera cam = null;

                if (freecam)
                {
                    if (FreeCamPanel.ourCamera != null)
                    {
                        cam = FreeCamPanel.ourCamera;
                    }
                    else if (FreeCamPanel.lastMainCamera != null)
                    {
                        cam = FreeCamPanel.lastMainCamera;
                    }
                    else if (Camera.main != null)
                    {
                        cam = Camera.main;
                    }
                }

                if (cam == null && Camera.main != null)
                    cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];

                if (cam == null)
                    return new CameraInfoDto
                    {
                        IsFreecam = freecam,
                        Name = "<none>",
                        Fov = 0f,
                        Pos = new Vector3Dto { X = 0f, Y = 0f, Z = 0f },
                        Rot = new Vector3Dto { X = 0f, Y = 0f, Z = 0f }
                    };

                var pos = cam.transform.position;
                var rot = cam.transform.eulerAngles;
                return new CameraInfoDto
                {
                    IsFreecam = freecam,
                    Name = cam.name,
                    Fov = cam.fieldOfView,
                    Pos = new Vector3Dto { X = pos.x, Y = pos.y, Z = pos.z },
                    Rot = new Vector3Dto { X = rot.x, Y = rot.y, Z = rot.z }
                };
            });
        }

        public PickResultDto MousePick(string? mode = "world", float? x = null, float? y = null, bool normalized = false)
        {
            return MainThread.Run(() =>
            {
                var normalizedMode = string.IsNullOrEmpty(mode) ? "world" : mode.ToLowerInvariant();
                var pos = InputManager.MousePosition;

                if (x.HasValue || y.HasValue)
                {
                    if (normalized)
                    {
                        var nx = Mathf.Clamp01(x ?? 0f);
                        var ny = Mathf.Clamp01(y ?? 0f);
                        pos = new Vector2(nx * Screen.width, ny * Screen.height);
                    }
                    else
                    {
                        pos = new Vector2(x ?? pos.x, y ?? pos.y);
                    }
                }

                if (normalizedMode == "ui")
                {
                    var eventSystem = EventSystem.current;
                    if (eventSystem == null)
                        return new PickResultDto
                        {
                            Mode = "ui",
                            Hit = false,
                            Id = null,
                            Items = new List<PickHit>()
                        };

                    var pointer = new PointerEventData(eventSystem)
                    {
                        position = pos
                    };
                    var raycastResults = new List<RaycastResult>();
                    eventSystem.RaycastAll(pointer, raycastResults);
                    var items = new List<PickHit>(raycastResults.Count);
                    foreach (var rr in raycastResults)
                    {
                        var go = rr.gameObject;
                        if (go == null) continue;

                        var resolved = FindByInstanceId(go.GetInstanceID());
                        if (resolved == null) continue;

                        var id = "obj:" + resolved.GetInstanceID();
                        var path = BuildPath(resolved.transform);
                        items.Add(new PickHit { Id = id, Name = resolved.name, Path = path });
                    }

                    var primaryId = items.Count > 0 ? items[0].Id : null;
                    return new PickResultDto
                    {
                        Mode = "ui",
                        Hit = items.Count > 0,
                        Id = primaryId,
                        Items = items
                    };
                }

                var cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];
                if (cam == null)
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = false,
                        Id = null,
                        Items = null
                    };

                var ray = cam.ScreenPointToRay(pos);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    var go = hit.collider != null ? hit.collider.gameObject : null;
                    var id = go != null ? "obj:" + go.GetInstanceID() : null;
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = go != null,
                        Id = id,
                        Items = null
                    };
                }

                return new PickResultDto
                {
                    Mode = "world",
                    Hit = false,
                    Id = null,
                    Items = null
                };
            });
        }

        public LogTailDto TailLogs(int count = 200)
        {
            return LogBuffer.Tail(count);
        }

        public SelectionDto GetSelection()
        {
            return MainThread.Run(() =>
            {
                var snap = CaptureSelection();
                return new SelectionDto { ActiveId = snap.ActiveId, Items = snap.Items };
            });
        }
    }

    internal sealed class MonoWriteTools
    {
        private readonly MonoReadTools _read;
        private static GameObject? _testUiRoot;
        private static GameObject? _testUiLeft;
        private static GameObject? _testUiRight;

        public MonoWriteTools(MonoReadTools read)
        {
            _read = read;
        }

        private static string ObjectId(GameObject go) => $"obj:{go.GetInstanceID()}";

        private static bool IsTestUiObject(GameObject go)
        {
            if (_testUiRoot == null) return false;
            var t = go.transform;
            while (t != null)
            {
                if (t.gameObject == _testUiRoot) return true;
                t = t.parent;
            }
            return false;
        }

        private static object ToolError(string kind, string message, string? hint = null)
            => new { ok = false, error = new { kind, message, hint } };

        private static object ToolErrorFromException(Exception ex)
        {
            if (ex is InvalidOperationException inv)
            {
                switch (inv.Message)
                {
                    case "NotFound": return ToolError("NotFound", "Not found");
                    case "PermissionDenied": return ToolError("PermissionDenied", "Permission denied");
                    case "ConfirmationRequired": return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
                    case "Denied by allowlist": return ToolError("PermissionDenied", "Denied by allowlist");
                    case "Component not found": return ToolError("NotFound", "Component not found");
                    case "Method overload not found": return ToolError("NotFound", "Method overload not found");
                    case "Method not found": return ToolError("NotFound", "Method not found");
                    case "Type not found": return ToolError("NotFound", "Type not found");
                    case "Hook not found": return ToolError("NotFound", "Hook not found");
                    default: return ToolError("InvalidArgument", inv.Message);
                }
            }

            if (ex is ArgumentException arg)
                return ToolError("InvalidArgument", arg.Message);

            return ToolError("Internal", ex.Message);
        }

        private static bool IsAllowed(string typeFullName, string member)
        {
            var cfg = McpConfig.Load();
            if (cfg.ReflectionAllowlistMembers == null || cfg.ReflectionAllowlistMembers.Length == 0) return false;
            var key = typeFullName + "." + member;
            foreach (var entry in cfg.ReflectionAllowlistMembers)
            {
                if (string.Equals(entry, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool TryGetComponent(GameObject go, string typeFullName, out UnityEngine.Component comp)
        {
            comp = null;
            var comps = go.GetComponents<UnityEngine.Component>();
            foreach (var c in comps)
            {
                if (c != null && string.Equals(c.GetType().FullName, typeFullName, StringComparison.Ordinal))
                {
                    comp = c;
                    return true;
                }
            }
            return false;
        }

        private static bool IsHookAllowed(string typeFullName)
        {
            var allow = McpConfig.Load().HookAllowlistSignatures;
            if (allow == null || allow.Length == 0) return false;
            foreach (var entry in allow)
            {
                if (string.Equals(entry, typeFullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static object DeserializeTo(string json, Type type)
        {
            try { return JsonConvert.DeserializeObject(json, type); } catch { }
            try { return Convert.ChangeType(json, type); } catch { }
            return null;
        }

        public object SetConfig(
            bool? allowWrites = null,
            bool? requireConfirm = null,
            bool? enableConsoleEval = null,
            string[]? componentAllowlist = null,
            string[]? reflectionAllowlistMembers = null,
            string[]? hookAllowlistSignatures = null,
            bool restart = false)
        {
            try
            {
                var cfg = McpConfig.Load();
                if (allowWrites.HasValue) cfg.AllowWrites = allowWrites.Value;
                if (requireConfirm.HasValue) cfg.RequireConfirm = requireConfirm.Value;
                if (enableConsoleEval.HasValue) cfg.EnableConsoleEval = enableConsoleEval.Value;
                if (componentAllowlist != null) cfg.ComponentAllowlist = componentAllowlist;
                if (reflectionAllowlistMembers != null) cfg.ReflectionAllowlistMembers = reflectionAllowlistMembers;
                if (hookAllowlistSignatures != null) cfg.HookAllowlistSignatures = hookAllowlistSignatures;
                McpConfig.Save(cfg);
                if (restart)
                {
                    McpHost.Stop();
                    McpHost.StartIfEnabled();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolError("Internal", ex.Message);
            }
        }

        public object GetConfig()
        {
            try
            {
                var cfg = McpConfig.Load();
                return new
                {
                    ok = true,
                    enabled = cfg.Enabled,
                    bindAddress = cfg.BindAddress,
                    port = cfg.Port,
                    allowWrites = cfg.AllowWrites,
                    requireConfirm = cfg.RequireConfirm,
                    exportRoot = cfg.ExportRoot,
                    logLevel = cfg.LogLevel,
                    componentAllowlist = cfg.ComponentAllowlist,
                    reflectionAllowlistMembers = cfg.ReflectionAllowlistMembers,
                    enableConsoleEval = cfg.EnableConsoleEval,
                    hookAllowlistSignatures = cfg.HookAllowlistSignatures
                };
            }
            catch (Exception ex)
            {
                return ToolError("Internal", ex.Message);
            }
        }

        public object SetActive(string objectId, bool active, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.SetActive(active);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SetMember(string objectId, string componentType, string member, string jsonValue, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, member)) return ToolError("PermissionDenied", "Denied by allowlist");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component comp;
                    if (!TryGetComponent(go, componentType, out comp) || comp == null)
                        throw new InvalidOperationException("Component not found");

                    var t = comp.GetType();
                    var fi = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                    {
                        var val = DeserializeTo(jsonValue, fi.FieldType);
                        fi.SetValue(comp, val);
                        return;
                    }

                    var pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi == null || !pi.CanWrite)
                        throw new InvalidOperationException("Member not writable");
                    var valProp = DeserializeTo(jsonValue, pi.PropertyType);
                    pi.SetValue(comp, valProp, null);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object ConsoleEval(string code, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(code) || code.Trim().Length == 0)
                return new { ok = true, result = string.Empty };

            try
            {
                string? result = null;
                MainThread.Run(() =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        var compiled = evaluator.Compile(code);
                        if (compiled != null)
                        {
                            object? ret = null;
                            compiled.Invoke(ref ret);
                            result = ret?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                    }
                });

                return new { ok = true, result };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object AddComponent(string objectId, string type, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (cfg.ComponentAllowlist == null || cfg.ComponentAllowlist.Length == 0)
                return ToolError("PermissionDenied", "No components are allowlisted");
            if (Array.IndexOf(cfg.ComponentAllowlist, type) < 0)
                return ToolError("PermissionDenied", "Denied by allowlist");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                if (t == null || !typeof(UnityEngine.Component).IsAssignableFrom(t))
                    return ToolError("InvalidArgument", "Type not found or not a Component");

                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.AddComponent(t);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object RemoveComponent(string objectId, string typeOrIndex, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component target = null;
                    if (int.TryParse(typeOrIndex, out var idx))
                    {
                        var comps = go.GetComponents<UnityEngine.Component>();
                        if (idx >= 0 && idx < comps.Length) target = comps[idx];
                    }
                    else
                    {
                        var t = UniverseLib.ReflectionUtility.GetTypeByName(typeOrIndex);
                        if (t != null)
                        {
                            if (cfg.ComponentAllowlist == null || Array.IndexOf(cfg.ComponentAllowlist, t.FullName) < 0)
                                throw new InvalidOperationException("Denied by allowlist");
                            var comps = go.GetComponents<UnityEngine.Component>();
                            foreach (var c in comps)
                            {
                                if (c != null && c.GetType().FullName == t.FullName)
                                {
                                    target = c;
                                    break;
                                }
                            }
                        }
                    }

                    if (target == null) throw new InvalidOperationException("Component not found");
                    UnityEngine.Object.Destroy(target);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object HookAdd(string type, string method, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsHookAllowed(type)) return ToolError("PermissionDenied", "Denied by hook allowlist");

            try
            {
                MainThread.Run(() =>
                {
                    var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                    if (t == null) throw new InvalidOperationException("Type not found");
                    var mi = t.GetMethod(method, UniverseLib.ReflectionUtility.FLAGS);
                    if (mi == null) throw new InvalidOperationException("Method not found");

                    var sig = mi.FullDescription();
                    if (HookList.hookedSignatures.Contains(sig))
                        throw new InvalidOperationException("Method is already hooked");

                    var hook = new HookInstance(mi);
                    HookList.hookedSignatures.Add(sig);
                    HookList.currentHooks.Add(sig, hook);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object HookRemove(string signature, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    hook.Unpatch();
                    HookList.currentHooks.Remove(signature);
                    HookList.hookedSignatures.Remove(signature);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object Reparent(string objectId, string newParentId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var childId))
                return ToolError("InvalidArgument", "Invalid child id");
            if (!int.TryParse(newParentId.StartsWith("obj:") ? newParentId.Substring(4) : string.Empty, out var parentId))
                return ToolError("InvalidArgument", "Invalid parent id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var child = _read.FindByInstanceId(childId);
                    var parent = _read.FindByInstanceId(parentId);
                    if (child == null || parent == null) throw new InvalidOperationException("NotFound");
                    if (child == parent) throw new InvalidOperationException("InvalidArgument");
                    if (!IsTestUiObject(child) || !IsTestUiObject(parent))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be reparented (SpawnTestUi)");
                        return;
                    }

                    child.transform.SetParent(parent.transform, true);
                });
                if (response != null) return response;
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyObject(string objectId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    if (!IsTestUiObject(go))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be destroyed (SpawnTestUi)");
                        return;
                    }

                    UnityEngine.Object.Destroy(go);
                    if (_testUiRoot == go)
                    {
                        _testUiRoot = null;
                        _testUiLeft = null;
                        _testUiRight = null;
                    }
                    else
                    {
                        if (_testUiLeft == go) _testUiLeft = null;
                        if (_testUiRight == go) _testUiRight = null;
                    }
                });
                if (response != null) return response;
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SelectObject(string objectId)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                SelectionDto? selection = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    try { InspectorManager.Inspect(go); } catch { }
                    _read.RecordSelection(objectId);
                });

                selection = _read.GetSelection();
                try
                {
                    var http = McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("selection", selection);
                }
                catch { }

                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object GetTimeScale()
        {
            bool locked = false;
            float value = Time.timeScale;
            MainThread.Run(() =>
            {
                TryGetTimeScaleState(TimeScaleWidget.Instance, out locked, out value);
            });
            return new { ok = true, value, locked };
        }

        public object SetTimeScale(float value, bool? @lock = null, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            var clamped = Mathf.Clamp(value, 0f, 4f);
            try
            {
                bool locked = false;
                float applied = clamped;
                MainThread.Run(() =>
                {
                    var widget = TimeScaleWidget.Instance;
                    if (@lock == true && widget != null)
                    {
                        widget.LockTo(clamped);
                    }
                    else
                    {
                        Time.timeScale = clamped;
                        if (@lock == false && widget != null)
                        {
                            UnlockTimeScale(widget);
                        }
                    }

                    TryGetTimeScaleState(widget, out locked, out applied);
                });
                return new { ok = true, value = applied, locked };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SpawnTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        if (_testUiLeft == null)
                        {
                            var foundLeft = _testUiRoot.transform.Find("McpTestBlock_Left");
                            _testUiLeft = foundLeft != null ? foundLeft.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                        }
                        if (_testUiRight == null)
                        {
                            var foundRight = _testUiRoot.transform.Find("McpTestBlock_Right");
                            _testUiRight = foundRight != null ? foundRight.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                        }
                        return;
                    }

                    if (EventSystem.current == null)
                    {
                        var es = new GameObject("McpTest_EventSystem");
                        es.AddComponent<EventSystem>();
                        es.AddComponent<StandaloneInputModule>();
                        es.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }

                    var root = new GameObject("McpTestCanvas");
                    var canvas = root.AddComponent<Canvas>();
                    root.AddComponent<CanvasScaler>();
                    root.AddComponent<GraphicRaycaster>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                    var scaler = root.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;

                    _testUiRoot = root;
                    _testUiLeft = AddTestBlock(root, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                    _testUiRight = AddTestBlock(root, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                });

                var blocks = new List<object>();
                if (_testUiLeft != null) blocks.Add(new { name = _testUiLeft.name, id = ObjectId(_testUiLeft) });
                if (_testUiRight != null) blocks.Add(new { name = _testUiRight.name, id = ObjectId(_testUiRight) });

                return new
                {
                    ok = true,
                    rootId = _testUiRoot != null ? ObjectId(_testUiRoot) : null,
                    blocks = blocks.ToArray()
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        try { GameObject.Destroy(_testUiRoot); } catch { }
                    }
                    _testUiRoot = null;
                    _testUiLeft = null;
                    _testUiRight = null;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static GameObject AddTestBlock(GameObject root, string name, Color color, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            img.color = color;
            img.raycastTarget = true;
            return go;
        }

        private static void UnlockTimeScale(TimeScaleWidget widget)
        {
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                lockedField?.SetValue(widget, false);
                var updateUi = typeof(TimeScaleWidget).GetMethod("UpdateUi", BindingFlags.NonPublic | BindingFlags.Instance);
                updateUi?.Invoke(widget, null);
            }
            catch { }
        }

        private static void TryGetTimeScaleState(TimeScaleWidget? widget, out bool locked, out float value)
        {
            locked = false;
            value = Time.timeScale;
            if (widget == null) return;
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                var lockedVal = lockedField?.GetValue(widget);
                if (lockedVal is bool b) locked = b;
            }
            catch { }
        }
    }
#else
    // Stub implementation for non-INTEROP targets so that builds that reference UnityExplorer.Mcp.McpSimpleHttp still compile.
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