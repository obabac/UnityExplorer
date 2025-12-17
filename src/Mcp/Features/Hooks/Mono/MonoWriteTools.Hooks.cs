#if MONO && !INTEROP
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityExplorer.Hooks;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object HookAdd(string type, string method, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsHookAllowed(type)) return ToolError("PermissionDenied", "Denied by hook allowlist");

            try
            {
                object? early = null;
                MainThread.Run(() =>
                {
                    var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                    if (t == null) throw new InvalidOperationException("Type not found");

                    MethodInfo? mi;
                    if (LooksLikeHarmonyFullDescription(method))
                    {
                        mi = t.GetMethods(UniverseLib.ReflectionUtility.FLAGS)
                            .FirstOrDefault(m => string.Equals(m.FullDescription(), method, StringComparison.Ordinal));
                        if (mi == null) throw new InvalidOperationException("Method overload not found");
                    }
                    else
                    {
                        var candidates = t.GetMethods(UniverseLib.ReflectionUtility.FLAGS)
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
                });
                return early ?? new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object HookSetEnabled(string signature, bool enabled, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    var declaringType = hook.TargetMethod?.DeclaringType?.FullName;
                    if (string.IsNullOrEmpty(declaringType) || !IsHookAllowed(declaringType))
                        throw new InvalidOperationException("PermissionDenied");

                    if (enabled) hook.Patch();
                    else hook.Unpatch();
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        public object HookSetSource(string signature, string source, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");

            try
            {
                MainThread.Run(() =>
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
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        public object HookRemove(string signature, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    hook.Unpatch();
                    HookList.currentHooks.Remove(signature);
                    HookList.hookedSignatures.Remove(signature);
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
