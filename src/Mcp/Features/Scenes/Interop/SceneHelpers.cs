#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private static (Scene Scene, IReadOnlyList<GameObject> Roots) GetDontDestroyOnLoadRoots()
        {
            var probe = new GameObject("__mcp_ddol_probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            var scene = probe.scene;
            UnityEngine.Object.DestroyImmediate(probe);

            if (!scene.IsValid())
                return (scene, Array.Empty<GameObject>());

            try
            {
                return (scene, scene.GetRootGameObjects().Where(go => go != null).ToArray());
            }
            catch
            {
                return (scene, Array.Empty<GameObject>());
            }
        }

        private static IReadOnlyList<GameObject> GetHideAndDontSaveRoots()
        {
            var list = new List<GameObject>();
            try
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go == null) continue;
                    if (go.scene.IsValid()) continue;

                    var transform = go.transform;
                    if (transform != null && transform.parent != null) continue;

                    list.Add(go);
                }
            }
            catch { }
            return list;
        }
    }
}
#endif
