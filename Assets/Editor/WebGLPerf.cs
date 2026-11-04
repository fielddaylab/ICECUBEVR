using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// Chromebook performance pass + helmet removal for the WebGL build.
///
/// Measured problem: the scene renders with SEVEN enabled cameras every frame
/// (CenterEyeAnchor, Helmet_Camera, AR_Camera_Static/Project/Result,
/// Portal_Camera_Next), three of which drive RenderTextures, plus 13 realtime
/// point/directional lights of which 4 cast realtime shadows. WebGL also fell
/// through to quality level 3 ("High": 2 pixel lights, shadows on, reflection
/// probes, lodBias 1.0) because QualitySettings had no WebGL override worth
/// using for a Chromebook.
///
/// IMPORTANT: Main.cs resolves Helmet, Helmet_Light, AR_Camera_* and
/// Portal_Camera_Next with GameObject.Find and dereferences them (e.g.
/// helmet.transform.position, helmet_light.GetComponent<Light>()). So the
/// GameObjects MUST keep existing - we disable the *rendering* components and
/// hide the mesh instead of deleting objects, or the game null-refs on boot.
public class WebGLPerf
{
    const string ScenePath = "Assets/Scenes/main.unity";

    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        var all = new List<GameObject>();
        foreach (var r in roots) CollectAll(r.transform, all);
        Debug.Log("PERF scanned " + all.Count + " GameObjects");

        // ---- 1. Helmet visual: pointless without a headset ----
        // Disable the renderers under Helmet (keeps the GameObject + transform
        // alive for Main.cs) and turn off the helmet-only camera and its light.
        int helmetRenderers = 0;
        var helmet = all.FirstOrDefault(g => g.name == "Helmet");
        if (helmet != null)
        {
            foreach (var rend in helmet.GetComponentsInChildren<Renderer>(true))
            {
                rend.enabled = false;
                helmetRenderers++;
            }
            Debug.Log("PERF helmet renderers disabled=" + helmetRenderers);
        }
        else Debug.LogWarning("PERF Helmet GameObject not found");

        var helmetCam = all.FirstOrDefault(g => g.name == "Helmet_Camera");
        if (helmetCam != null)
        {
            var c = helmetCam.GetComponent<Camera>();
            if (c != null) { c.enabled = false; Debug.Log("PERF Helmet_Camera disabled"); }
        }

        var helmetLight = all.FirstOrDefault(g => g.name == "Helmet_Light");
        if (helmetLight != null)
        {
            var l = helmetLight.GetComponent<Light>();
            // Keep the component (Main.cs does GetComponent<Light>() on it and
            // drives its intensity) but stop it lighting the scene.
            if (l != null) { l.enabled = false; Debug.Log("PERF Helmet_Light disabled"); }
        }

        // ---- 2. Realtime shadows off, everywhere ----
        int shadowsOff = 0;
        foreach (var g in all)
        {
            var l = g.GetComponent<Light>();
            if (l != null && l.shadows != LightShadows.None)
            {
                l.shadows = LightShadows.None;
                shadowsOff++;
            }
            foreach (var rend in g.GetComponents<Renderer>())
            {
                // Shadow casting/receiving costs draw calls even with shadows
                // disabled on some paths; make it explicit.
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
        }
        Debug.Log("PERF lights with shadows disabled=" + shadowsOff);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("PERF_DONE helmetRenderers=" + helmetRenderers +
                  " shadowsOff=" + shadowsOff);
        EditorApplication.Exit(0);
    }

    static void CollectAll(Transform t, List<GameObject> into)
    {
        into.Add(t.gameObject);
        for (int i = 0; i < t.childCount; i++) CollectAll(t.GetChild(i), into);
    }
}
