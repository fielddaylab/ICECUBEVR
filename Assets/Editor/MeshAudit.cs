using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// Reports per-mesh triangle counts in the scene so optimization targets the
/// actual cost instead of guesswork. Browser instrumentation measured ~8.5M
/// triangles/frame, which no Chromebook iGPU will push at 30fps.
public class MeshAudit
{
    public static void Report()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/main.unity",
                                                 OpenSceneMode.Single);
        var rows = new List<string>();
        long total = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var rend = mf.GetComponent<Renderer>();
                bool on = rend != null && rend.enabled && mf.gameObject.activeInHierarchy;
                int tris = m.triangles.Length / 3;
                total += on ? tris : 0;
                rows.Add(string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                    tris, on ? "ON " : "off", mf.gameObject.layer,
                    m.name, mf.gameObject.name,
                    rend != null ? rend.sharedMaterials.Length : 0));
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (m == null) continue;
                int tris = m.triangles.Length / 3;
                bool on = smr.enabled && smr.gameObject.activeInHierarchy;
                total += on ? tris : 0;
                rows.Add(string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                    tris, on ? "ON " : "off", smr.gameObject.layer,
                    m.name, smr.gameObject.name + "(skinned)",
                    smr.sharedMaterials.Length));
            }
        }

        rows.Sort((a, b) => int.Parse(b.Split('|')[0]) - int.Parse(a.Split('|')[0]));
        Debug.Log("MESHAUDIT total_enabled_tris=" + total + " meshes=" + rows.Count);
        foreach (var r in rows.Take(40))
            Debug.Log("MESHROW " + r);
        EditorApplication.Exit(0);
    }
}
