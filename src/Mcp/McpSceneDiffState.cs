using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
#if INTEROP
    // Tracks scene add/remove diffs for MCP notifications.
    internal static class McpSceneDiffState
    {
        private static readonly HashSet<int> _lastSceneHandles = new();

        public static void UpdateScenes(IReadOnlyList<Scene> scenes)
        {
            try
            {
                var added = new List<object>();
                var removed = new List<int>();

                // current set
                var current = new HashSet<int>();
                foreach (var s in scenes)
                {
                    current.Add(s.handle);
                    if (!_lastSceneHandles.Contains(s.handle))
                        added.Add(new { name = s.name, handle = s.handle, isLoaded = s.isLoaded });
                }

                foreach (var h in _lastSceneHandles)
                {
                    if (!current.Contains(h)) removed.Add(h);
                }

                _lastSceneHandles.Clear();
                foreach (var h in current) _lastSceneHandles.Add(h);

                if ((added.Count > 0 || removed.Count > 0) && McpSimpleHttp.Current != null)
                {
                    _ = McpSimpleHttp.Current.BroadcastNotificationAsync("scenes_diff", new { added, removed });
                }
            }
            catch { }
        }
    }
#endif
}
