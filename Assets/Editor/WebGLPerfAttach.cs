using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// Attaches the runtime governor to the scene (idempotent).
public class WebGLPerfAttach
{
    const string ScenePath = "Assets/Scenes/main.unity";

    public static void Attach()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        var existing = roots.FirstOrDefault(g => g.name == "__WebGLPerfGovernor");
        if (existing != null)
        {
            Debug.Log("ATTACH governor already present");
        }
        else
        {
            var go = new GameObject("__WebGLPerfGovernor");
            go.AddComponent<WebGLPerfGovernor>();
            Debug.Log("ATTACH governor created");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("ATTACH_DONE");
        EditorApplication.Exit(0);
    }
}
