#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityExplorer.UI.Panels;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private const int MaxConsoleScriptBytes = 256 * 1024;

        private string? _fallbackSelectionActive;
        private readonly List<string> _fallbackSelectionItems = new List<string>();

        internal void RecordSelection(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return;
            _fallbackSelectionActive = objectId;
            if (!_fallbackSelectionItems.Contains(objectId))
                _fallbackSelectionItems.Insert(0, objectId);
        }

        private sealed class TraversalEntry
        {
            public GameObject GameObject { get; }
            public string Path { get; }

            public TraversalEntry(GameObject go, string path)
            {
                GameObject = go;
                Path = path;
            }
        }

        private IEnumerable<TraversalEntry> Traverse(GameObject root)
        {
            var stack = new Stack<TraversalEntry>();
            stack.Push(new TraversalEntry(root, "/" + root.name));
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                yield return entry;
                var t = entry.GameObject.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var c = t.GetChild(i);
                    stack.Push(new TraversalEntry(c.gameObject, entry.Path + "/" + c.name));
                }
            }
        }

        internal GameObject? FindByInstanceId(int instanceId)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root.GetInstanceID() == instanceId)
                        return root;
                    foreach (var entry in Traverse(root))
                    {
                        if (entry.GameObject.GetInstanceID() == instanceId)
                            return entry.GameObject;
                    }
                }
            }

            try
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go != null && go.GetInstanceID() == instanceId)
                        return go;
                }
            }
            catch { }

            return null;
        }

        private string BuildPath(Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }

        private sealed class SelectionSnapshot
        {
            public string? ActiveId { get; set; }
            public List<string> Items { get; set; } = new List<string>();
        }

        private SelectionSnapshot CaptureSelection()
        {
            var snap = new SelectionSnapshot();
            try
            {
                if (InspectorManager.ActiveInspector?.Target is GameObject ago)
                    snap.ActiveId = "obj:" + ago.GetInstanceID();
                foreach (var ins in InspectorManager.Inspectors)
                {
                    if (ins.Target is GameObject go)
                    {
                        var id = "obj:" + go.GetInstanceID();
                        if (!snap.Items.Contains(id))
                            snap.Items.Add(id);
                    }
                }
            }
            catch { }

            if (snap.ActiveId != null)
                RecordSelection(snap.ActiveId);

            if (snap.Items.Count == 0 && _fallbackSelectionItems.Count > 0)
            {
                foreach (var id in _fallbackSelectionItems)
                {
                    if (!snap.Items.Contains(id))
                        snap.Items.Add(id);
                }
            }

            if (string.IsNullOrEmpty(snap.ActiveId) && !string.IsNullOrEmpty(_fallbackSelectionActive))
                snap.ActiveId = _fallbackSelectionActive;

            if (snap.ActiveId != null && !snap.Items.Contains(snap.ActiveId))
                snap.Items.Insert(0, snap.ActiveId);
            return snap;
        }
    }
}
#endif
