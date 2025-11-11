using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class UnityQuery
    {
        public static IEnumerable<GameObject> EnumerateAllRootGameObjects()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                foreach (var go in s.GetRootGameObjects())
                    yield return go;
            }
        }

        public static IEnumerable<(GameObject go, string path)> Traverse(GameObject root)
        {
            var stack = new Stack<(Transform t, string path)>();
            stack.Push((root.transform, "/" + root.name));
            while (stack.Count > 0)
            {
                var (t, p) = stack.Pop();
                yield return (t.gameObject, p);
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var c = t.GetChild(i);
                    stack.Push((c, p + "/" + c.name));
                }
            }
        }

        public static GameObject? FindByInstanceId(int instanceId)
        {
            foreach (var root in EnumerateAllRootGameObjects())
            {
                foreach (var (go, _) in Traverse(root))
                {
                    if (go.GetInstanceID() == instanceId)
                        return go;
                }
            }
            return null;
        }
    }
#endif
}

