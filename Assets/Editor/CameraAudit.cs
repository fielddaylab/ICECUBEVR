using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// Per-camera render cost, respecting culling masks.
///
/// A flat "enabled triangles" count is misleading here: the scene holds three
/// art variants (Viz/Gam/Neu) on separate layers, and each camera's culling
/// mask selects one. What matters for frame time is the sum over ENABLED
/// cameras of the geometry their masks actually admit.
public class CameraAudit
{
    public static void Report()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/main.unity",
                                                 OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        // Gather renderers with triangle counts + layer.
        var items = new List<KeyValuePair<int, int>>();   // layer, tris
        int drawables = 0;
        foreach (var root in roots)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var rend = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || rend == null) continue;
                if (!rend.enabled || !mf.gameObject.activeInHierarchy) continue;
                int tris = mf.sharedMesh.triangles.Length / 3;
                items.Add(new KeyValuePair<int, int>(mf.gameObject.layer, tris));
                drawables += Mathf.Max(1, rend.sharedMaterials.Length);
            }
        }
        Debug.Log("CAMAUDIT drawable_submeshes=" + drawables);

        long grandTotal = 0;
        int activeCams = 0;
        foreach (var root in roots)
        {
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                bool on = cam.enabled && cam.gameObject.activeInHierarchy;
                long tris = 0;
                int objs = 0;
                foreach (var kv in items)
                {
                    if ((cam.cullingMask & (1 << kv.Key)) != 0)
                    {
                        tris += kv.Value;
                        objs++;
                    }
                }
                if (on) { grandTotal += tris; activeCams++; }
                Debug.Log(string.Format(
                    "CAMROW {0}|{1}|depth={2}|mask={3}|objs={4}|tris={5}|rt={6}",
                    cam.gameObject.name, on ? "ON " : "off", cam.depth,
                    cam.cullingMask, objs, tris,
                    cam.targetTexture != null ? "yes" : "no"));
            }
        }
        Debug.Log("CAMAUDIT active_cameras=" + activeCams +
                  " tris_per_frame_all_cams=" + grandTotal);
        EditorApplication.Exit(0);
    }
}
