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
            if (!string.IsNullOrEmpty(kind)) logMessage += $" ({kind})";
            if (!string.IsNullOrEmpty(hint)) logMessage += $"\nHint: {hint}";
            if (!string.IsNullOrEmpty(detail)) logMessage += $"\n{Truncate(detail, MaxErrorDetailChars)}";
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
            if (!string.IsNullOrEmpty(kind)) logMessage += $" ({kind})";
            if (!string.IsNullOrEmpty(hint)) logMessage += $"\nHint: {hint}";
            if (!string.IsNullOrEmpty(detail)) logMessage += $"\n{Truncate(detail, MaxErrorDetailChars)}";
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
    }
}

#endif
