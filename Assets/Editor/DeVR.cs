using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;

/// Removes the Oculus runtime components from the scene so the WebGL player
/// never touches OVRPlugin.
///
/// Why: WebGL has no dlopen. OVRManager.Awake() calls into the native
/// "OVRPlugin" library, and Emscripten answers with
///   abort("To use dlopen, you need to use Emscripten's linking support")
/// which kills the renderer a second after the engine starts. Only OVRManager
/// and OVRCameraRig are present in main.unity, and mouse-look drives the
/// CameraHouse transform ABOVE the rig, so the components can be deleted
/// outright while the CenterEyeAnchor camera keeps rendering.
public class DeVR
{
    public static void Strip()
    {
        const string scenePath = "Assets/Scenes/main.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        int removed = 0;
        // Collect first: destroying while iterating invalidates the arrays.
        var roots = scene.GetRootGameObjects();

        var managers = roots.SelectMany(r => r.GetComponentsInChildren<OVRManager>(true)).ToArray();
        foreach (var m in managers)
        {
            Debug.Log("DEVR removing OVRManager on " + m.gameObject.name);
            Object.DestroyImmediate(m, true);
            removed++;
        }

        var rigs = roots.SelectMany(r => r.GetComponentsInChildren<OVRCameraRig>(true)).ToArray();
        foreach (var rig in rigs)
        {
            Debug.Log("DEVR removing OVRCameraRig on " + rig.gameObject.name);
            Object.DestroyImmediate(rig, true);
            removed++;
        }

        // Make sure exactly one enabled camera remains tagged MainCamera, so
        // Unity has an unambiguous render target without the VR rig.
        // Unity 2019 FindObjectsOfType has no includeInactive overload, so walk
        // the scene roots instead (this also catches inactive eye cameras).
        var cams = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToArray();
        var center = cams.FirstOrDefault(c => c.gameObject.name == "CenterEyeAnchor");
        if (center != null)
        {
            center.tag = "MainCamera";
            center.stereoTargetEye = StereoTargetEyeMask.None;
            Debug.Log("DEVR CenterEyeAnchor set as MainCamera, mono");
        }
        foreach (var c in cams)
        {
            if (c.gameObject.name == "LeftEyeAnchor" || c.gameObject.name == "RightEyeAnchor")
            {
                c.enabled = false;
                Debug.Log("DEVR disabled stereo eye camera " + c.gameObject.name);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log("DEVR_DONE removed=" + removed + " cameras=" + cams.Length);
        EditorApplication.Exit(0);
    }
}
