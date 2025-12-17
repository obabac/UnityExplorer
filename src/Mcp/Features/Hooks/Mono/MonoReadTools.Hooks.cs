#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityExplorer.Hooks;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private static bool IsHookAllowedType(string typeFullName)
        {
            var allow = McpConfig.Load().HookAllowlistSignatures;
            if (allow == null || allow.Length == 0) return false;
            foreach (var entry in allow)
            {
                if (string.Equals(entry, typeFullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public object HookListAllowedTypes()
        {
            var cfg = McpConfig.Load();
            return new { ok = true, items = cfg.HookAllowlistSignatures ?? new string[0] };
        }

        public Page<HookMethodDto> HookListMethods(string type, string? filter, int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                if (string.IsNullOrEmpty(type) || type.Trim().Length == 0)
                    throw new ArgumentException("type is required", nameof(type));
                if (!IsHookAllowedType(type))
                    throw new InvalidOperationException("PermissionDenied");

                var t = ReflectionUtility.GetTypeByName(type);
                if (t == null) throw new InvalidOperationException("Type not found");

                var methods = t.GetMethods(ReflectionUtility.FLAGS);
                IEnumerable<System.Reflection.MethodInfo> query = methods;
                if (!string.IsNullOrEmpty(filter) && filter.Trim().Length > 0)
                {
                    query = query.Where(m =>
                    {
                        if (m == null) return false;
                        var sig = m.FullDescription();
                        var f = filter!;
                        return m.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                            || sig.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                }

                var ordered = query
                    .Select(m => new HookMethodDto(m.Name, m.FullDescription()))
                    .OrderBy(m => m.Signature, StringComparer.Ordinal)
                    .ToList();

                var total = ordered.Count;
                var items = ordered.Skip(off).Take(lim).ToList();
                return new Page<HookMethodDto>(total, items);
            });
        }

        public object HookGetSource(string signature)
        {
            return MainThread.Run(() =>
            {
                if (string.IsNullOrEmpty(signature) || signature.Trim().Length == 0)
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
