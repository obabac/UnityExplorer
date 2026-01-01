#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private const int MaxMemberValueLength = 1024;
        private const int MaxEnumerablePreviewItems = 10;
        private const int MaxValueSummaryDepth = 2;

        private sealed class MemberValueSummary
        {
            public string Text { get; }
            public object? Json { get; }
            public string? RefId { get; }

            public MemberValueSummary(string text, object? json, string? refId = null)
            {
                Text = text;
                Json = json;
                RefId = refId;
            }
        }

        private Component? FindComponent(GameObject go, string typeFullName)
        {
            var comps = go.GetComponents<Component>();
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

        private MemberValueSummary SummarizeValue(object? value, int depth = 0)
        {
            if (value == null) return new MemberValueSummary("<null>", null);

            if (value is string str)
            {
                var t = Truncate(str);
                return new MemberValueSummary(t, t);
            }

            if (value is bool b) return new MemberValueSummary(b ? "true" : "false", b);

            if (value is Enum e)
            {
                var enumText = e.ToString();
                return new MemberValueSummary(Truncate(enumText), enumText);
            }

            switch (value)
            {
                case int i: return new MemberValueSummary(i.ToString(), i);
                case long l: return new MemberValueSummary(l.ToString(), l);
                case float f: return new MemberValueSummary(f.ToString("G"), f);
                case double d: return new MemberValueSummary(d.ToString("G"), d);
                case decimal m: return new MemberValueSummary(m.ToString("G"), m);
                case uint ui: return new MemberValueSummary(ui.ToString(), ui);
                case short sh: return new MemberValueSummary(sh.ToString(), sh);
                case ushort ush: return new MemberValueSummary(ush.ToString(), ush);
                case byte by: return new MemberValueSummary(by.ToString(), by);
                case sbyte sb: return new MemberValueSummary(sb.ToString(), sb);
            }

            if (value is Vector2 v2)
            {
                var text = $"({v2.x}, {v2.y})";
                return new MemberValueSummary(text, new { x = v2.x, y = v2.y });
            }

            if (value is Vector3 v3)
            {
                var text = $"({v3.x}, {v3.y}, {v3.z})";
                return new MemberValueSummary(text, new { x = v3.x, y = v3.y, z = v3.z });
            }

            if (value is Vector4 v4)
            {
                var text = $"({v4.x}, {v4.y}, {v4.z}, {v4.w})";
                return new MemberValueSummary(text, new { x = v4.x, y = v4.y, z = v4.z, w = v4.w });
            }

            if (value is Quaternion q)
            {
                var text = $"({q.x}, {q.y}, {q.z}, {q.w})";
                return new MemberValueSummary(text, new { x = q.x, y = q.y, z = q.z, w = q.w });
            }

            if (value is Color c)
            {
                var text = $"({c.r}, {c.g}, {c.b}, {c.a})";
                return new MemberValueSummary(text, new { r = c.r, g = c.g, b = c.b, a = c.a });
            }

            if (value is UnityEngine.Object uo)
            {
                var id = $"obj:{uo.GetInstanceID()}";
                return new MemberValueSummary(id, id);
            }

            if (depth >= MaxValueSummaryDepth)
            {
                var refId = McpObjectRefs.Capture(value);
                return new MemberValueSummary(SafeTypeName(value.GetType()), null, refId);
            }

            if (value is IDictionary dict)
            {
                var refId = McpObjectRefs.Capture(value);
                var preview = new List<object>();
                var take = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    if (take++ >= MaxEnumerablePreviewItems) break;
                    var k = SummarizeValue(entry.Key, depth + 1);
                    var v = SummarizeValue(entry.Value, depth + 1);
                    preview.Add(new
                    {
                        key = new { text = k.Text, json = k.Json, refId = k.RefId },
                        value = new { text = v.Text, json = v.Json, refId = v.RefId }
                    });
                }
                var text = $"Dictionary<{SafeTypeName(value.GetType())}> (count={dict.Count}, preview={preview.Count})";
                return new MemberValueSummary(Truncate(text), new { kind = "dictionary", count = dict.Count, preview }, refId);
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var refId = McpObjectRefs.Capture(value);
                int? count = null;
                try
                {
                    if (value is ICollection coll) count = coll.Count;
                }
                catch { }

                var preview = new List<object>();
                var take = 0;
                foreach (var item in enumerable)
                {
                    if (take++ >= MaxEnumerablePreviewItems) break;
                    var itemSummary = SummarizeValue(item, depth + 1);
                    preview.Add(new { text = itemSummary.Text, json = itemSummary.Json, refId = itemSummary.RefId });
                }

                var text = count.HasValue
                    ? $"Enumerable<{SafeTypeName(value.GetType())}> (count={count.Value}, preview={preview.Count})"
                    : $"Enumerable<{SafeTypeName(value.GetType())}> (preview={preview.Count})";

                return new MemberValueSummary(Truncate(text), new { kind = "enumerable", count, preview }, refId);
            }

            var objRefId = McpObjectRefs.Capture(value);
            return new MemberValueSummary(SafeTypeName(value.GetType()), null, objRefId);
        }

        private static string FormatMethodType(MethodInfo mi)
        {
            var ret = SafeTypeName(mi.ReturnType);
            var args = mi.GetParameters().Select(p => SafeTypeName(p.ParameterType)).ToArray();
            return args.Length == 0 ? $"{ret}()" : $"{ret}({string.Join(", ", args)})";
        }

        public Page<InspectorMemberDto> ListComponentMembers(string objectId, string componentType, bool includeMethods = false, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");
            if (string.IsNullOrEmpty(componentType) || componentType.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid componentType");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                var comp = FindComponent(go, componentType);
                if (comp == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Component not found");

                var t = comp.GetType();
                var members = new List<InspectorMemberDto>();

                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto { Name = fi.Name, Kind = "field", Type = SafeTypeName(fi.FieldType), CanRead = true, CanWrite = canWrite });
                }

                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto { Name = pi.Name, Kind = "property", Type = SafeTypeName(pi.PropertyType), CanRead = canRead, CanWrite = canWrite });
                }

                if (includeMethods)
                {
                    foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (mi.IsSpecialName) continue;
                        members.Add(new InspectorMemberDto { Name = mi.Name, Kind = "method", Type = FormatMethodType(mi), CanRead = false, CanWrite = false });
                    }
                }

                var total = members.Count;
                var items = members.Skip(off).Take(lim).ToList();
                return new Page<InspectorMemberDto>(total, items);
            });
        }

        public object ReadComponentMember(string objectId, string componentType, string name)
        {
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");
            if (string.IsNullOrEmpty(componentType) || componentType.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid componentType");
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid member name");

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                var comp = FindComponent(go, componentType);
                if (comp == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Component not found");

                var t = comp.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(comp);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not readable");
                    var val = getter.Invoke(comp, new object[0]);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not found");
            });
        }
    }
}
#endif
