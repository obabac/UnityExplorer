#if INTEROP
using System;
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

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
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
    }
}

#endif
