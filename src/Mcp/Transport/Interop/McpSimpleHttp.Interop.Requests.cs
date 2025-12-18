#if INTEROP
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
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

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 15000;
                stream.WriteTimeout = 15000;

                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine)) return;
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;
                var method = parts[0];
                var target = parts[1];

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
                    await HandleJsonRpcAsync(stream, body, ct).ConfigureAwait(false);
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
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);

                    int sseId = Interlocked.Increment(ref _nextSseClientId);
                    _sseStreams[sseId] = stream;
                    _sseStreamStates[sseId] = new StreamQueueState(stream);

                    await WaitForStreamDisconnectAsync(stream, sseId, isSse: true, ct).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && target.StartsWith("/read"))
                {
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

                await WriteResponseAsync(stream, 200, "ok", ct).ConfigureAwait(false);
            }
        }

        private async Task HandleJsonRpcAsync(Stream stream, string body, CancellationToken ct)
        {
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
                        _httpStreamStates[id] = new StreamQueueState(stream);

                        try
                        {
                            var scenes = await UnityReadTools.ListScenes(null, null, ct).ConfigureAwait(false);
                            var notificationJson = JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                method = "notification",
                                @params = new { @event = "scenes", payload = scenes }
                            });
                            var chunk = BuildChunk(Encoding.UTF8.GetBytes(notificationJson + "\n"));
                            EnqueuePayload(id, stream, _httpStreams, _httpStreamStates, chunk, "http");
                        }
                        catch { }

                        await WaitForStreamDisconnectAsync(stream, id, isSse: false, ct).ConfigureAwait(false);
                        return;
                    }

                    await SendJsonRpcErrorAsync(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null, ct).ConfigureAwait(false);
                    var notFoundPayload = BuildJsonRpcErrorPayload(root, ErrorMethodNotFound, $"Method not found: {methodName}", "MethodNotFound", null, null);
                    await WriteJsonResponseAsync(stream, 400, notFoundPayload, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (acquiredSlot) _requestSlots.Release();
                }
            }
        }
    }
}

#endif
