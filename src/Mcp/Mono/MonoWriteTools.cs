#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        private static bool TryGetFloat(JToken token, out float value)
        {
            value = 0f;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<float>();
                return true;
            }
            return false;
        }

        private static bool TryGetPropertyFloat(JObject obj, string name, out float value)
        {
            value = 0f;
            var prop = obj.Property(name, StringComparison.OrdinalIgnoreCase);
            if (prop == null) return false;
            return TryGetFloat(prop.Value, out value);
        }

        private static bool TryParseVector(JToken token, int length, out float[] values)
        {
            values = new float[0];
            if (token is JArray arr && arr.Count == length)
            {
                var tmp = new float[length];
                for (int i = 0; i < length; i++)
                {
                    if (!TryGetFloat(arr[i], out var v)) return false;
                    tmp[i] = v;
                }
                values = tmp;
                return true;
            }

            if (token is JObject obj)
            {
                var names = length switch
                {
                    2 => new[] { "x", "y" },
                    3 => new[] { "x", "y", "z" },
                    4 => new[] { "x", "y", "z", "w" },
                    _ => new string[0]
                };
                if (names.Length != length) return false;

                var tmp = new float[length];
                for (int i = 0; i < length; i++)
                {
                    if (!TryGetPropertyFloat(obj, names[i], out var v)) return false;
                    tmp[i] = v;
                }
                values = tmp;
                return true;
            }

            return false;
        }

        private static object? DeserializeUnityValue(string json, Type type)
        {
            try
            {
                var token = JToken.Parse(json);
                if (type.IsEnum)
                {
                    if (token.Type == JTokenType.String)
                    {
                        var name = token.Value<string>();
                        if (!string.IsNullOrEmpty(name) && name.Trim().Length > 0)
                        {
                            try { return Enum.Parse(type, name, true); } catch { }
                        }
                    }
                    else if (token.Type == JTokenType.Integer)
                    {
                        return Enum.ToObject(type, token.Value<long>());
                    }
                }

                if (type == typeof(Vector2) && TryParseVector(token, 2, out var v2))
                    return new Vector2(v2[0], v2[1]);
                if (type == typeof(Vector3) && TryParseVector(token, 3, out var v3))
                    return new Vector3(v3[0], v3[1], v3[2]);
                if (type == typeof(Vector4) && TryParseVector(token, 4, out var v4))
                    return new Vector4(v4[0], v4[1], v4[2], v4[3]);
                if (type == typeof(Quaternion) && TryParseVector(token, 4, out var q))
                    return new Quaternion(q[0], q[1], q[2], q[3]);
                if (type == typeof(Color) && token is JObject obj)
                {
                    if (TryGetPropertyFloat(obj, "r", out var r) &&
                        TryGetPropertyFloat(obj, "g", out var g) &&
                        TryGetPropertyFloat(obj, "b", out var b))
                    {
                        var a = TryGetPropertyFloat(obj, "a", out var aVal) ? aVal : 1f;
                        return new Color(r, g, b, a);
                    }
                }
            }
            catch { }

            return null;
        }

        private static object DeserializeTo(string json, Type type)
        {
            var special = DeserializeUnityValue(json, type);
            if (special != null) return special;
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
