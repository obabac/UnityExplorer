#if MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

                if (method == "GET" && IsStreamPath(target) && !string.IsNullOrEmpty(acceptHeader) && acceptHeader!.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
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
                        _sseStates[sseId] = new StreamQueueState(stream);
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
                    var result = _handlers.ReadResource(uri!);
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
                    _streamStates[id] = new StreamQueueState(stream);
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
                    var chunk = BuildChunk(Encoding.UTF8.GetBytes(json + "\n"));
                    EnqueuePayload(id, stream, _streams, _streamStates, _streamGate, chunk, "http");
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
    }
}

#endif
