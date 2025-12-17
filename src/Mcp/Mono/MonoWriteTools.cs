#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityExplorer.CSConsole;
using UnityExplorer.Hooks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        private readonly MonoReadTools _read;
        private static GameObject? _testUiRoot;
        private static GameObject? _testUiLeft;
        private static GameObject? _testUiRight;
        private const int MaxConsoleScriptBytes = 256 * 1024;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public MonoWriteTools(MonoReadTools read)
        {
            _read = read;
        }

        private static string StripLeadingBom(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s[0] == '\uFEFF' ? s.Substring(1) : s;
        }

        private static string ObjectId(GameObject go) => $"obj:{go.GetInstanceID()}";

        private static bool IsTestUiObject(GameObject go)
        {
            if (_testUiRoot == null) return false;
            var t = go.transform;
            while (t != null)
            {
                if (t.gameObject == _testUiRoot) return true;
                t = t.parent;
            }
            return false;
        }

        private static object ToolError(string kind, string message, string? hint = null)
            => new { ok = false, error = new { kind, message, hint } };

        private static object ToolErrorFromException(Exception ex)
        {
            if (ex is InvalidOperationException inv)
            {
                switch (inv.Message)
                {
                    case "NotFound": return ToolError("NotFound", "Not found");
                    case "PermissionDenied": return ToolError("PermissionDenied", "Permission denied");
                    case "ConfirmationRequired": return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
                    case "Denied by allowlist": return ToolError("PermissionDenied", "Denied by allowlist");
                    case "Component not found": return ToolError("NotFound", "Component not found");
                    case "Method overload not found": return ToolError("NotFound", "Method overload not found");
                    case "Method not found": return ToolError("NotFound", "Method not found");
                    case "Type not found": return ToolError("NotFound", "Type not found");
                    case "Hook not found": return ToolError("NotFound", "Hook not found");
                    default: return ToolError("InvalidArgument", inv.Message);
                }
            }

            if (ex is ArgumentException arg)
                return ToolError("InvalidArgument", arg.Message);

            return ToolError("Internal", ex.Message);
        }

        private static bool IsAllowed(string typeFullName, string member)
        {
            var cfg = McpConfig.Load();
            if (cfg.ReflectionAllowlistMembers == null || cfg.ReflectionAllowlistMembers.Length == 0) return false;
            var key = typeFullName + "." + member;
            foreach (var entry in cfg.ReflectionAllowlistMembers)
            {
                if (string.Equals(entry, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool TryGetComponent(GameObject go, string typeFullName, out UnityEngine.Component comp)
        {
            comp = null;
            var comps = go.GetComponents<UnityEngine.Component>();
            foreach (var c in comps)
            {
                if (c != null && string.Equals(c.GetType().FullName, typeFullName, StringComparison.Ordinal))
                {
                    comp = c;
                    return true;
                }
            }
            return false;
        }

        private static bool IsHookAllowed(string typeFullName)
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

        private static bool LooksLikeHarmonyFullDescription(string methodOrSignature)
        {
            if (string.IsNullOrEmpty(methodOrSignature) || methodOrSignature.Trim().Length == 0) return false;
            return methodOrSignature.Contains("::") && methodOrSignature.Contains("(") && methodOrSignature.Contains(")");
        }

        private static object DeserializeTo(string json, Type type)
        {
            try { return JsonConvert.DeserializeObject(json, type); } catch { }
            try { return Convert.ChangeType(json, type); } catch { }
            return null;
        }

        private static string ResolveConsoleScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
                throw new ArgumentException("path is required");
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only .cs files are allowed");

            var scriptsFolder = ConsoleController.ScriptsFolder;
            if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                throw new InvalidOperationException("NotReady");

            var scriptsRoot = Path.GetFullPath(scriptsFolder);
            if (!scriptsRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) && !scriptsRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                scriptsRoot += Path.DirectorySeparatorChar;

            var candidate = Path.IsPathRooted(path) ? path : Path.Combine(scriptsRoot, path);
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("path must stay inside the Scripts folder");

            return full;
        }
    }
}
#endif
