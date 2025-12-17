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

                                try
                                {
                                    var scenes = await UnityReadTools.ListScenes(null, null, ct).ConfigureAwait(false);
                                    var notificationJson = JsonSerializer.Serialize(new
                                    {
                                        jsonrpc = "2.0",
                                        method = "notification",
                                        @params = new { @event = "scenes", payload = scenes }
                                    });
                                    var lineJson = notificationJson + "\n";
                                    var payload = Encoding.UTF8.GetBytes(lineJson);
                                    var prefix = Encoding.ASCII.GetBytes(payload.Length.ToString("X") + "\r\n");
                                    var suffix = Encoding.ASCII.GetBytes("\r\n");
                                    await stream.WriteAsync(prefix, 0, prefix.Length, ct).ConfigureAwait(false);
                                    await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
                                    await stream.WriteAsync(suffix, 0, suffix.Length, ct).ConfigureAwait(false);
                                    await stream.FlushAsync(ct).ConfigureAwait(false);
                                }
                                catch { }

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
        private readonly object _broadcastGate = new object();
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

                try
                {
                    var scenes = _handlers.ReadResource("unity://scenes");
                    var notification = new JObject
                    {
                        { "jsonrpc", "2.0" },
                        { "method", "notification" },
                        { "params", new JObject { { "event", "scenes" }, { "payload", scenes == null ? JValue.CreateNull() : JToken.FromObject(scenes) } } }
                    };
                    var json = notification.ToString(Formatting.None);
                    var chunkData = json + "\n";
                    var data = Encoding.UTF8.GetBytes(chunkData);
                    var prefix = Encoding.ASCII.GetBytes(data.Length.ToString("X") + "\r\n");
                    var suffix = Encoding.ASCII.GetBytes("\r\n");
                    lock (_broadcastGate)
                    {
                        stream.Write(prefix, 0, prefix.Length);
                        stream.Write(data, 0, data.Length);
                        stream.Write(suffix, 0, suffix.Length);
                        stream.Flush();
                    }
                }
                catch { }

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

            lock (_broadcastGate)
            {
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