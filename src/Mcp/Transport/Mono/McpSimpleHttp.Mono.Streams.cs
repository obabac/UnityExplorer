#if MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
        private sealed class StreamQueueState
        {
            public StreamQueueState(Stream stream)
            {
                Stream = stream;
            }

            public Stream Stream { get; }
            public Queue<byte[]> Queue { get; } = new Queue<byte[]>();
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

        private void EnqueuePayload(int id, Stream stream, Dictionary<int, Stream> streamStore, Dictionary<int, StreamQueueState> stateStore, object gate, byte[] payload, string kind)
        {
            if (_disposed)
                return;

            StreamQueueState state;
            lock (gate)
            {
                if (!_running || _disposed)
                    return;

                if (!streamStore.TryGetValue(id, out var current) || !ReferenceEquals(current, stream))
                    return;

                if (!stateStore.TryGetValue(id, out state))
                {
                    state = new StreamQueueState(stream);
                    stateStore[id] = state;
                }
            }

            var startWriter = false;
            var dropped = 0;
            lock (state)
            {
                while (state.Queue.Count >= StreamQueueLimit)
                {
                    state.Queue.Dequeue();
                    dropped++;
                }

                if (dropped > 0)
                {
                    state.Dropped += dropped;
                    if (state.Warned == 0)
                    {
                        state.Warned = 1;
                        try { ExplorerCore.LogWarning($"[MCP] {kind} stream {id} backpressure: dropped {dropped} pending message(s) (limit {StreamQueueLimit})."); } catch { }
                    }
                }

                state.Queue.Enqueue(payload);
                if (state.WriterActive == 0)
                {
                    state.WriterActive = 1;
                    startWriter = true;
                }
            }

            if (startWriter)
            {
                ThreadPool.QueueUserWorkItem(_ => WriteQueue(state, id, streamStore, stateStore, gate, kind));
            }
        }

        private void WriteQueue(StreamQueueState state, int id, Dictionary<int, Stream> streamStore, Dictionary<int, StreamQueueState> stateStore, object gate, string kind)
        {
            while (true)
            {
                byte[]? next;
                lock (state)
                {
                    if (state.Queue.Count == 0)
                    {
                        state.WriterActive = 0;
                        if (state.Dropped > 0)
                        {
                            state.Warned = 0;
                            state.Dropped = 0;
                        }
                        return;
                    }

                    next = state.Queue.Dequeue();
                }

                try
                {
                    state.Stream.Write(next, 0, next.Length);
                    state.Stream.Flush();
                }
                catch
                {
                    RemoveStream(id, streamStore, stateStore, gate);
                    return;
                }
            }
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
            var chunk = BuildChunk(Encoding.UTF8.GetBytes(json + "\n"));
            var sseData = Encoding.UTF8.GetBytes("data: " + json + "\n\n");

            foreach (var kv in snapshot)
            {
                EnqueuePayload(kv.Key, kv.Value, _streams, _streamStates, _streamGate, chunk, "http");
            }

            foreach (var kv in sseSnapshot)
            {
                EnqueuePayload(kv.Key, kv.Value, _sseStreams, _sseStates, _sseGate, sseData, "sse");
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

        private void RemoveStream(int id, Dictionary<int, Stream> streamStore, Dictionary<int, StreamQueueState> stateStore, object gate)
        {
            Stream? s = null;
            StreamQueueState? state = null;
            lock (gate)
            {
                if (streamStore.TryGetValue(id, out s))
                    streamStore.Remove(id);
                if (stateStore.TryGetValue(id, out state))
                    stateStore.Remove(id);
            }

            if (state != null)
            {
                lock (state)
                {
                    state.Queue.Clear();
                    state.WriterActive = 0;
                    state.Warned = 0;
                    state.Dropped = 0;
                }
            }

            if (s != null)
            {
                try { s.Dispose(); } catch { }
            }
        }

        private void RemoveStream(int id)
        {
            RemoveStream(id, _streams, _streamStates, _streamGate);
        }

        private void RemoveSseStream(int id)
        {
            RemoveStream(id, _sseStreams, _sseStates, _sseGate);
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
    }
}

#endif
