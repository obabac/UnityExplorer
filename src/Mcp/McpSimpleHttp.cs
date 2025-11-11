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
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, NetworkStream> _sse = new();
        private int _nextClientId;
        public int Port { get; }

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
                        var payload = JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            id = root.TryGetProperty("id", out var _id) ? _id.Deserialize<object>() : null,
                            result = new { tools }
                        });
                        await BroadcastSseAsync(payload, ct).ConfigureAwait(false);
                        await WriteResponseAsync(stream, 202, "accepted", ct).ConfigureAwait(false);
                        return;
                    }

                    await WriteResponseAsync(stream, 400, "unsupported", ct).ConfigureAwait(false);
                    return;
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

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }
    }

    internal static class McpReflection
    {
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
    }
#endif
}

