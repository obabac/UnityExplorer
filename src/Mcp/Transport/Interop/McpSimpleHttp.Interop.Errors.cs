#if INTEROP
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class McpSimpleHttp : IDisposable
    {
        private static ErrorShape MapExceptionToError(Exception ex)
        {
            if (ex is ArgumentException arg)
                return new ErrorShape(ErrorInvalidParams, 400, "InvalidArgument", arg.Message, null, null);

            if (ex is InvalidOperationException inv)
            {
                return inv.Message switch
                {
                    "NotFound" => new ErrorShape(ErrorNotFound, 404, "NotFound", "Not found", null, null),
                    "PermissionDenied" => new ErrorShape(ErrorPermissionDenied, 403, "PermissionDenied", "Permission denied", null, null),
                    "ConfirmationRequired" => new ErrorShape(ErrorPermissionDenied, 403, "PermissionDenied", "Confirmation required", "resend with confirm=true", null),
                    "NotReady" => new ErrorShape(ErrorNotReady, 503, "NotReady", "Not ready", null, null),
                    _ => new ErrorShape(ErrorInvalidParams, 400, "InvalidArgument", inv.Message, null, null)
                };
            }

            if (ex is NotSupportedException notSup)
                return new ErrorShape(ErrorNotFound, 404, "NotFound", notSup.Message, null, null);

            return new ErrorShape(ErrorInternal, 500, "Internal", "Internal error", null, ex.Message);
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

        private static async Task WriteResponseAsync(Stream stream, int code, string text, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(text);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteJsonResponseAsync(Stream stream, int code, object payload, CancellationToken ct)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteEmptyResponseAsync(Stream stream, int code, CancellationToken ct)
        {
            var header = $"HTTP/1.1 {code} {ReasonPhrase(code)}\r\n" +
                         CorsHeaders +
                         $"Content-Length: 0\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteCorsPreflightAsync(Stream stream, CancellationToken ct)
        {
            var header = $"HTTP/1.1 204 {ReasonPhrase(204)}\r\n" +
                         CorsHeaders +
                         "Content-Length: 0\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
        }
    }
}

#endif
