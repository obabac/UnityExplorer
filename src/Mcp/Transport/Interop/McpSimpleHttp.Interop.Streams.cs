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
        private sealed class StreamQueueState
        {
            public StreamQueueState(Stream stream) => Stream = stream;
            public Stream Stream { get; }
            public ConcurrentQueue<byte[]> Queue { get; } = new();
            public int WriterActive;
            public int Warned;
            public int Dropped;
        }

        private static byte[] BuildChunk(byte[] payload)
        {
            var prefix = Encoding.ASCII.GetBytes(payload.Length.ToString("X") + "\r\n");
            var suffix = Encoding.ASCII.GetBytes("\r\n");
            var buffer = new byte[prefix.Length + payload.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, buffer, prefix.Length, payload.Length);
            Buffer.BlockCopy(suffix, 0, buffer, prefix.Length + payload.Length, suffix.Length);
            return buffer;
        }

        private void EnqueuePayload(int id, Stream stream, ConcurrentDictionary<int, Stream> streamStore, ConcurrentDictionary<int, StreamQueueState> stateStore, byte[] payload, string kind)
        {
            if (_cts.IsCancellationRequested)
                return;

            var state = stateStore.GetOrAdd(id, _ => new StreamQueueState(stream));
            var queue = state.Queue;

            var dropped = 0;
            while (queue.Count >= StreamQueueLimit && queue.TryDequeue(out _))
            {
                dropped++;
            }

            if (dropped > 0)
            {
                Interlocked.Add(ref state.Dropped, dropped);
                if (Interlocked.CompareExchange(ref state.Warned, 1, 0) == 0)
                {
                    try
                    {
                        ExplorerCore.LogWarning($"[MCP] {kind} stream {id} backpressure: dropped {dropped} pending message(s) (limit {StreamQueueLimit}).");
                    }
                    catch { }
                }
            }

            queue.Enqueue(payload);
            TryStartWriter(state, id, streamStore, stateStore, kind);
        }

        private void TryStartWriter(StreamQueueState state, int id, ConcurrentDictionary<int, Stream> streamStore, ConcurrentDictionary<int, StreamQueueState> stateStore, string kind)
        {
            if (Interlocked.CompareExchange(ref state.WriterActive, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!state.Queue.IsEmpty && !_cts.IsCancellationRequested)
                    {
                        if (!state.Queue.TryDequeue(out var data))
                            continue;

                        try
                        {
                            await state.Stream.WriteAsync(data, 0, data.Length, _cts.Token).ConfigureAwait(false);
                            await state.Stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            RemoveStream(id, streamStore, stateStore);
                            break;
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref state.WriterActive, 0);

                    if (!state.Queue.IsEmpty)
                    {
                        TryStartWriter(state, id, streamStore, stateStore, kind);
                    }
                    else if (state.Dropped > 0)
                    {
                        Interlocked.Exchange(ref state.Warned, 0);
                        Interlocked.Exchange(ref state.Dropped, 0);
                    }
                }
            });
        }

        private void RemoveStream(int id, ConcurrentDictionary<int, Stream> streamStore, ConcurrentDictionary<int, StreamQueueState> stateStore)
        {
            if (streamStore.TryRemove(id, out var s))
            {
                try { s.Dispose(); } catch { }
            }

            stateStore.TryRemove(id, out _);
        }

        private Task BroadcastHttpStreamAsync(string json, CancellationToken ct)
        {
            if (_httpStreams.IsEmpty || ct.IsCancellationRequested)
                return Task.CompletedTask;

            var chunk = BuildChunk(Encoding.UTF8.GetBytes(json + "\n"));

            foreach (var kv in _httpStreams)
            {
                EnqueuePayload(kv.Key, kv.Value, _httpStreams, _httpStreamStates, chunk, "http");
            }

            return Task.CompletedTask;
        }

        private Task BroadcastSseAsync(string json, CancellationToken ct)
        {
            if (_sseStreams.IsEmpty || ct.IsCancellationRequested)
                return Task.CompletedTask;

            var payload = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            foreach (var kv in _sseStreams)
            {
                EnqueuePayload(kv.Key, kv.Value, _sseStreams, _sseStreamStates, payload, "sse");
            }

            return Task.CompletedTask;
        }

        private Task BroadcastAllStreamsAsync(string json, CancellationToken ct)
        {
            BroadcastHttpStreamAsync(json, ct);
            BroadcastSseAsync(json, ct);
            return Task.CompletedTask;
        }

        private async Task WaitForStreamDisconnectAsync(Stream stream, int id, bool isSse, CancellationToken ct)
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
                if (isSse)
                    RemoveStream(id, _sseStreams, _sseStreamStates);
                else
                    RemoveStream(id, _httpStreams, _httpStreamStates);
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
