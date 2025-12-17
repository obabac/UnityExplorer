#if MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
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
    }
}

#endif
