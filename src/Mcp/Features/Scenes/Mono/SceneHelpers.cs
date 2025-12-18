#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private sealed class SceneRoots
        {
            public Scene Scene { get; }
            public List<GameObject> Roots { get; }

            public SceneRoots(Scene scene, List<GameObject> roots)
            {
                Scene = scene;
                Roots = roots;
            }
        }

        private static SceneRoots GetDontDestroyOnLoadRoots()
        {
            var probe = new GameObject("__mcp_ddol_probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            var scene = probe.scene;
            UnityEngine.Object.DestroyImmediate(probe);

            if (!scene.IsValid())
                return new SceneRoots(scene, new List<GameObject>());

            try
            {
                return new SceneRoots(scene, scene.GetRootGameObjects().Where(go => go != null).ToList());
            }
            catch
            {
                return new SceneRoots(scene, new List<GameObject>());
            }
        }

        private static List<GameObject> GetHideAndDontSaveRoots()
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
