#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private sealed class MemberNameMatch
        {
            public string Name { get; private set; }
            public string Kind { get; private set; }

            public MemberNameMatch(string name, string kind)
            {
                Name = name ?? string.Empty;
                Kind = kind ?? string.Empty;
            }
        }

        private sealed class TypeMemberMatchCache
        {
            public string TypeName { get; private set; }
            public MemberNameMatch[] Matches { get; private set; }

            public TypeMemberMatchCache(string typeName, MemberNameMatch[] matches)
            {
                TypeName = typeName ?? string.Empty;
                Matches = matches ?? new MemberNameMatch[0];
            }
        }

        public Page<MemberMatchDto> SearchComponentMembers(string query, string? componentType = null, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(query) || query.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "query is required");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var q = query.Trim();
            var typeFilter = string.IsNullOrEmpty(componentType) || componentType.Trim().Length == 0 ? null : componentType.Trim();

            return MainThread.Run(() =>
            {
                var results = new List<MemberMatchDto>(lim);
                var typeCache = new Dictionary<Type, TypeMemberMatchCache>();
                int total = 0;

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var entry in Traverse(root))
                        {
                            var go = entry.GameObject;
                            var path = entry.Path;

                            Component[] comps;
                            try { comps = go.GetComponents<Component>(); }
                            catch { continue; }

                            foreach (var comp in comps)
                            {
                                if (comp == null) continue;
                                var t = comp.GetType();

                                if (typeFilter != null)
                                {
                                    var tn = t.FullName ?? t.Name ?? string.Empty;
                                    if (tn.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                                        continue;
                                }

                                TypeMemberMatchCache cached;
                                if (!typeCache.TryGetValue(t, out cached))
                                {
                                    var tn = t.FullName ?? t.Name ?? "<unknown>";
                                    var matches = new List<MemberNameMatch>();

                                    foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                    {
                                        if (fi.IsSpecialName) continue;
                                        if (fi.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                            matches.Add(new MemberNameMatch(fi.Name, "field"));
                                    }

                                    foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                    {
                                        if (pi.IsSpecialName) continue;
                                        if (pi.GetIndexParameters().Length > 0) continue;
                                        if (pi.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                            matches.Add(new MemberNameMatch(pi.Name, "property"));
                                    }

                                    cached = new TypeMemberMatchCache(tn, matches.ToArray());
                                    typeCache[t] = cached;
                                }

                                if (cached.Matches.Length == 0)
                                    continue;

                                foreach (var m in cached.Matches)
                                {
                                    if (total >= off && results.Count < lim)
                                    {
                                        results.Add(new MemberMatchDto
                                        {
                                            ObjectId = "obj:" + go.GetInstanceID(),
                                            ObjectPath = path,
                                            ComponentType = cached.TypeName,
                                            Member = m.Name,
                                            Kind = m.Kind
                                        });
                                    }

                                    total++;
                                    if (results.Count >= lim)
                                        break;
                                }

                                if (results.Count >= lim)
                                    break;
                            }

                            if (results.Count >= lim)
                                break;
                        }

                        if (results.Count >= lim)
                            break;
                    }

                    if (results.Count >= lim)
                        break;
                }

                return new Page<MemberMatchDto>(total, results);
            });
        }
    }
}
#endif
