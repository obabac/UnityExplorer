#if MONO
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityExplorer;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
        private JObject BuildResultPayload(JToken? idToken, object result)
        {
            return new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", idToken ?? JValue.CreateNull() },
                { "result", result == null ? JValue.CreateNull() : JToken.FromObject(result) }
            };
        }

        private JObject BuildErrorPayload(JToken? idToken, int code, string message, string kind, string? hint, string? detail)
        {
            var data = BuildErrorData(kind, hint, detail);
            return new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", idToken ?? JValue.CreateNull() },
                { "error", new JObject { { "code", code }, { "message", message }, { "data", data } } }
            };
        }

        private static JObject BuildErrorData(string kind, string? hint, string? detail)
        {
            var data = new JObject { { "kind", kind } };
            if (!string.IsNullOrEmpty(hint)) data["hint"] = hint;
            if (!string.IsNullOrEmpty(detail)) data["detail"] = detail;
            return data;
        }

        private void WriteJsonResult(Stream stream, JToken? idToken, object result)
        {
            var payload = BuildResultPayload(idToken, result);
            BroadcastPayload(payload);
            WriteResponse(stream, 200, payload.ToString(Formatting.None), "application/json");
        }

        private void WriteJsonError(Stream stream, JToken? idToken, int code, string message, string kind, string? hint, string? detail, int statusCode)
        {
            var logMessage = $"[MCP] error {code}: {message}";
            try
            {
                MainThread.Run(() =>
                {
                    try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                    try { ExplorerCore.LogWarning(logMessage); } catch { }
                });
            }
            catch
            {
                try { LogBuffer.Add("error", logMessage, "mcp", kind); } catch { }
                try { ExplorerCore.LogWarning(logMessage); } catch { }
            }

            var payload = BuildErrorPayload(idToken, code, message, kind, hint, detail);
            BroadcastPayload(payload);
            WriteResponse(stream, statusCode, payload.ToString(Formatting.None), "application/json");
        }

        private static void WriteResponse(Stream stream, int statusCode, string body, string contentType)
        {
            if (body == null) body = string.Empty;
            var payload = Encoding.UTF8.GetBytes(body);
            var header = "HTTP/1.1 " + statusCode + " " + ReasonPhrase(statusCode) + "\r\n"
                         + CorsHeaders
                         + "Content-Type: " + contentType + "\r\n"
                         + "Content-Length: " + payload.Length + "\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static void WriteEmptyResponse(Stream stream, int statusCode)
        {
            var header = "HTTP/1.1 " + statusCode + " " + ReasonPhrase(statusCode) + "\r\n"
                         + CorsHeaders
                         + "Content-Length: 0\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
        }

        private static void WriteCorsPreflight(Stream stream)
        {
            var header = "HTTP/1.1 204 " + ReasonPhrase(204) + "\r\n"
                         + CorsHeaders
                         + "Content-Length: 0\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
        }

        private static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "OK";
            }
        }
    }
}

#endif
