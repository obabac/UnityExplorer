#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityExplorer.Hooks;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private static bool IsHookAllowed(string typeFullName)
        {
            var cfg = McpConfig.Load();
            var allow = cfg.HookAllowlistSignatures;
            if (allow == null || allow.Length == 0) return false;
            foreach (var entry in allow)
            {
                if (string.Equals(entry, typeFullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool LooksLikeHarmonyFullDescription(string methodOrSignature)
        {
            if (string.IsNullOrWhiteSpace(methodOrSignature)) return false;
            return methodOrSignature.Contains("::", StringComparison.Ordinal) &&
                methodOrSignature.Contains("(", StringComparison.Ordinal) &&
                methodOrSignature.Contains(")", StringComparison.Ordinal);
        }

        [McpServerTool, Description("Add a Harmony hook for the given type and method (guarded by hook allowlist).")]
        public static async Task<object> HookAdd(string type, string method, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsHookAllowed(type)) return ToolError("PermissionDenied", "Denied by hook allowlist");

            try
            {
                object? early = null;
                await MainThread.RunAsync(async () =>
                {
                    var t = ReflectionUtility.GetTypeByName(type);
                    if (t == null) throw new InvalidOperationException("Type not found");

                    System.Reflection.MethodInfo? mi;
                    if (LooksLikeHarmonyFullDescription(method))
                    {
                        mi = t.GetMethods(ReflectionUtility.FLAGS)
                            .FirstOrDefault(m => string.Equals(m.FullDescription(), method, StringComparison.Ordinal));
                        if (mi == null) throw new InvalidOperationException("Method overload not found");
                    }
                    else
                    {
                        var candidates = t.GetMethods(ReflectionUtility.FLAGS)
                            .Where(m => string.Equals(m.Name, method, StringComparison.Ordinal))
                            .ToList();
                        if (candidates.Count == 0) throw new InvalidOperationException("Method not found");
                        if (candidates.Count > 1)
                        {
                            var example = candidates[0].FullDescription();
                            early = ToolError("InvalidArgument", $"Multiple overloads found for '{type}.{method}'.", $"pass the full signature in method (example: {example})");
                            return;
                        }
                        mi = candidates[0];
                    }

                    var sig = mi.FullDescription();
                    if (HookList.hookedSignatures.Contains(sig))
                        throw new InvalidOperationException("Method is already hooked");

                    var hook = new HookInstance(mi);
                    HookList.hookedSignatures.Add(sig);
                    HookList.currentHooks.Add(sig, hook);
                    await Task.CompletedTask;
                });
                return early ?? new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Enable or disable a previously added Harmony hook by signature.")]
        public static async Task<object> HookSetEnabled(string signature, bool enabled, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    var declaringType = hook.TargetMethod?.DeclaringType?.FullName;
                    if (string.IsNullOrEmpty(declaringType) || !IsHookAllowed(declaringType))
                        throw new InvalidOperationException("PermissionDenied");

                    if (enabled) hook.Patch();
                    else hook.Unpatch();
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Update the patch source for a previously added Harmony hook (requires enableConsoleEval).")]
        public static async Task<object> HookSetSource(string signature, string source, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    var declaringType = hook.TargetMethod?.DeclaringType?.FullName;
                    if (string.IsNullOrEmpty(declaringType) || !IsHookAllowed(declaringType))
                        throw new InvalidOperationException("PermissionDenied");

                    var wasEnabled = hook.Enabled;
                    hook.PatchSourceCode = source ?? string.Empty;
                    var ok = hook.CompileAndGenerateProcessor(hook.PatchSourceCode);
                    if (!ok)
                        throw new InvalidOperationException("Compile failed");

                    if (wasEnabled) hook.Patch();
                    else hook.Unpatch();

                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Remove a previously added Harmony hook by signature.")]
        public static async Task<object> HookRemove(string signature, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    hook.Unpatch();
                    HookList.currentHooks.Remove(signature);
                    HookList.hookedSignatures.Remove(signature);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
