#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityExplorer.Hooks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private static bool IsHookAllowedType(string typeFullName)
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

        [McpServerTool, Description("List hook-allowed types (mirrors config hookAllowlistSignatures).")]
        public static object HookListAllowedTypes()
        {
            var cfg = McpConfig.Load();
            return new { ok = true, items = cfg.HookAllowlistSignatures ?? Array.Empty<string>() };
        }

        [McpServerTool, Description("List methods for a hook-allowed type (paged).")]
        public static async Task<Page<HookMethodDto>> HookListMethods(string type, string? filter, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(type))
                    throw new ArgumentException("type is required", nameof(type));
                if (!IsHookAllowedType(type))
                    throw new InvalidOperationException("PermissionDenied");

                var t = ReflectionUtility.GetTypeByName(type);
                if (t == null) throw new InvalidOperationException("Type not found");

                var methods = t.GetMethods(ReflectionUtility.FLAGS);
                IEnumerable<System.Reflection.MethodInfo> query = methods;
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    query = query.Where(m =>
                    {
                        if (m == null) return false;
                        var sig = m.FullDescription();
                        return m.Name.Contains(filter!, StringComparison.OrdinalIgnoreCase)
                            || sig.Contains(filter!, StringComparison.OrdinalIgnoreCase);
                    });
                }

                var ordered = query
                    .Select(m => new HookMethodDto(m.Name, m.FullDescription()))
                    .OrderBy(m => m.Signature, StringComparer.Ordinal)
                    .ToList();

                var total = ordered.Count;
                var items = ordered.Skip(off).Take(lim).ToArray();
                return new Page<HookMethodDto>(total, items);
            });
        }

        [McpServerTool, Description("Get hook patch source by signature.")]
        public static async Task<object> HookGetSource(string signature, CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(signature))
                    throw new ArgumentException("signature is required", nameof(signature));

                if (!HookList.currentHooks.Contains(signature))
                    throw new InvalidOperationException("Hook not found");

                var hook = (HookInstance)HookList.currentHooks[signature]!;
                return new { ok = true, signature, source = hook.PatchSourceCode ?? string.Empty };
            });
        }
    }
}
#endif
