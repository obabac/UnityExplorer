#if INTEROP
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private const int MaxMemberValueLength = 1024;

        private static UnityEngine.Component? FindComponent(GameObject go, string typeFullName)
        {
            var comps = go.GetComponents<UnityEngine.Component>();
            foreach (var c in comps)
            {
                if (c != null && string.Equals(c.GetType().FullName, typeFullName, StringComparison.Ordinal))
                    return c;
            }
            return null;
        }

        private static string SafeTypeName(Type? t)
        {
            if (t == null) return "<unknown>";
            return string.IsNullOrEmpty(t.FullName) ? t.Name : t.FullName!;
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            if (value.Length <= MaxMemberValueLength) return value;
            return value.Substring(0, MaxMemberValueLength) + "...";
        }

        private static (string text, object? json) SummarizeValue(object? value)
        {
            if (value == null) return ("<null>", null);

            if (value is string s)
            {
                var t = Truncate(s);
                return (t, t);
            }

            if (value is bool b) return (b ? "true" : "false", b);

            if (value is Enum e)
            {
                var str = e.ToString();
                return (Truncate(str), str);
            }

            switch (value)
            {
                case int i: return (i.ToString(), i);
                case long l: return (l.ToString(), l);
                case float f: return (f.ToString("G"), f);
                case double d: return (d.ToString("G"), d);
                case decimal m: return (m.ToString("G"), m);
                case uint ui: return (ui.ToString(), ui);
                case short sh: return (sh.ToString(), sh);
                case ushort ush: return (ush.ToString(), ush);
                case byte by: return (by.ToString(), by);
                case sbyte sb: return (sb.ToString(), sb);
            }

            if (value is UnityEngine.Vector2 v2)
            {
                var text = $"({v2.x}, {v2.y})";
                return (text, new { x = v2.x, y = v2.y });
            }

            if (value is UnityEngine.Vector3 v3)
            {
                var text = $"({v3.x}, {v3.y}, {v3.z})";
                return (text, new { x = v3.x, y = v3.y, z = v3.z });
            }

            if (value is UnityEngine.Vector4 v4)
            {
                var text = $"({v4.x}, {v4.y}, {v4.z}, {v4.w})";
                return (text, new { x = v4.x, y = v4.y, z = v4.z, w = v4.w });
            }

            if (value is UnityEngine.Quaternion q)
            {
                var text = $"({q.x}, {q.y}, {q.z}, {q.w})";
                return (text, new { x = q.x, y = q.y, z = q.z, w = q.w });
            }

            if (value is UnityEngine.Color c)
            {
                var text = $"({c.r}, {c.g}, {c.b}, {c.a})";
                return (text, new { r = c.r, g = c.g, b = c.b, a = c.a });
            }

            if (value is UnityEngine.Object uo)
            {
                var id = $"obj:{uo.GetInstanceID()}";
                return (id, id);
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                int count = 0;
                foreach (var _ in enumerable)
                {
                    count++;
                    if (count > 32) break;
                }
                var text = $"Enumerable<{SafeTypeName(value.GetType())}> (count~{count})";
                return (text, null);
            }

            var strValue = Truncate(value.ToString() ?? "<null>");
            return (strValue, null);
        }

        private static string FormatMethodType(MethodInfo mi)
        {
            var ret = SafeTypeName(mi.ReturnType);
            var args = mi.GetParameters().Select(p => SafeTypeName(p.ParameterType)).ToArray();
            return args.Length == 0 ? $"{ret}()" : $"{ret}({string.Join(", ", args)})";
        }

        [McpServerTool, Description("List members for a component (fields, properties, and optionally methods).")]
        public static async Task<Page<InspectorMemberDto>> ListComponentMembers(string objectId, string componentType, bool includeMethods = false, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                throw new ArgumentException("Invalid objectId; expected 'obj:<instanceId>'", nameof(objectId));
            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new ArgumentException("Invalid instance id", nameof(objectId));
            if (string.IsNullOrWhiteSpace(componentType))
                throw new ArgumentException("Invalid componentType", nameof(componentType));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                var comp = FindComponent(go, componentType);
                if (comp == null) throw new InvalidOperationException("NotFound");

                var t = comp.GetType();
                var members = new List<InspectorMemberDto>();

                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto(fi.Name, "field", SafeTypeName(fi.FieldType), true, canWrite));
                }

                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto(pi.Name, "property", SafeTypeName(pi.PropertyType), canRead, canWrite));
                }

                if (includeMethods)
                {
                    foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (mi.IsSpecialName) continue;
                        members.Add(new InspectorMemberDto(mi.Name, "method", FormatMethodType(mi), false, false));
                    }
                }

                var total = members.Count;
                var items = members.Skip(off).Take(lim).ToArray();
                return new Page<InspectorMemberDto>(total, items);
            });
        }

        [McpServerTool, Description("Read a component member value (safe, bounded).")]
        public static async Task<object> ReadComponentMember(string objectId, string componentType, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                throw new ArgumentException("Invalid objectId; expected 'obj:<instanceId>'", nameof(objectId));
            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new ArgumentException("Invalid instance id", nameof(objectId));
            if (string.IsNullOrWhiteSpace(componentType))
                throw new ArgumentException("Invalid componentType", nameof(componentType));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Invalid member name", nameof(name));

            return await MainThread.Run(() =>
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                var comp = FindComponent(go, componentType);
                if (comp == null) throw new InvalidOperationException("NotFound");

                var t = comp.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(comp);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.text, valueJson = summary.json };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new InvalidOperationException("NotFound");
                    var val = getter.Invoke(comp, Array.Empty<object>());
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.text, valueJson = summary.json };
                }

                throw new InvalidOperationException("NotFound");
            });
        }
    }
}
#endif
