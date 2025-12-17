#if INTEROP
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
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
            catch
            {
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }
    }
}

#endif
