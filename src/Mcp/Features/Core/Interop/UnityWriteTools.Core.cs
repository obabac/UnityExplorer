#if INTEROP
#nullable enable
using System;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private static object ToolError(string kind, string message, string? hint = null)
            => new { ok = false, error = new { kind, message, hint } };

        private static object ToolErrorFromException(Exception ex)
        {
            if (ex is InvalidOperationException inv)
            {
                return inv.Message switch
                {
                    "NotFound" => ToolError("NotFound", "Not found"),
                    "PermissionDenied" => ToolError("PermissionDenied", "Permission denied"),
                    "ConfirmationRequired" => ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true"),
                    "Denied by allowlist" => ToolError("PermissionDenied", "Denied by allowlist"),
                    "Component not found" => ToolError("NotFound", "Component not found"),
                    "Method overload not found" => ToolError("NotFound", "Method overload not found"),
                    "Method not found" => ToolError("NotFound", "Method not found"),
                    "Type not found" => ToolError("NotFound", "Type not found"),
                    "Hook not found" => ToolError("NotFound", "Hook not found"),
                    _ => ToolError("InvalidArgument", inv.Message)
                };
            }

            if (ex is ArgumentException arg)
                return ToolError("InvalidArgument", arg.Message);

            return ToolError("Internal", ex.Message);
        }
    }
}
#endif
