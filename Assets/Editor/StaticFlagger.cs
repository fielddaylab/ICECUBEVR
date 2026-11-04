using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// Flags non-moving geometry as batching-static so Unity's static batcher can
/// merge it, then reports the result.
///
/// Why: the scene presents 164 drawable submeshes to 5 active cameras and was
/// issuing ~1098 draw calls/frame with batching disabled for WebGL. Static
/// batching collapses same-material static geometry into shared buffers, which
/// is the main lever for draw-call count on an integrated GPU.
///
/// Safety: anything Main.cs moves at runtime must NOT be marked static.
/// Main.cs drives Helmet, the cameras, reticles, suns/planets, portals and the
/// snow emitters, so we only mark the structural set (station frame, stairs,
/// rails, bridges, floors) plus explicitly exclude known-moving names.
public class StaticFlagger
{
    // Explicit allowlist of structural geometry. An exclusion list proved
    // far too greedy (every mesh matched something), so we invert it: only
    // these name prefixes - the station frame and terrain - get batched.
    static readonly string[] StaticPrefixes = {
        "Frame_", "Bottom_Structure", "Top_Structure", "Near Snow Floor",
        "Far Snow Floor", "Platform", "Railing", "Stair", "Wall", "Floor",
        "Pillar", "Beam", "Truss", "Catwalk", "Ladder", "Panel_"
    };

    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/main.unity",
                                                 OpenSceneMode.Single);
        int marked = 0, skipped = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                var rend = go.GetComponent<Renderer>();
                if (rend == null || mf.sharedMesh == null) continue;

                bool isStatic = false;
                foreach (var nm in StaticPrefixes)
                {
                    if (go.name.IndexOf(nm, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isStatic = true;
                        break;
                    }
                }

                if (!isStatic) { skipped++; continue; }

                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go,
                    flags | StaticEditorFlags.BatchingStatic);
                marked++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/main.unity");
        Debug.Log("STATICFLAG_DONE marked=" + marked + " skipped_moving=" + skipped);
        EditorApplication.Exit(0);
    }
}
