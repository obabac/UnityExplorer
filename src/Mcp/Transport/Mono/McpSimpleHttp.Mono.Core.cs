#if MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
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
            TcpClient? client = null;
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
    }
}

#endif
