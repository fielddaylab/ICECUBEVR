using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;

/// Puts the SceneCompositor GameObject into main.unity (idempotent).
///
/// The camera/RT wiring itself is done at runtime by SceneCompositor so that
/// the scene diff stays at one GameObject: editing targetTexture on nine
/// cameras in the YAML would collide with every other card that touches a
/// camera, and the AR/portal cameras must keep their own RenderTextures.
/// What has to persist in the scene is the script's presence, which is what
/// this does.
public class SceneCompositorAttach
{
    const string ScenePath = "Assets/Scenes/main.unity";

    public static void Attach()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        var existing = roots.FirstOrDefault(g => g.name == SceneCompositor.GameObjectName);
        if (existing == null)
        {
            var go = new GameObject(SceneCompositor.GameObjectName);
            var sc = go.AddComponent<SceneCompositor>();
            sc.compositorEnabled = true;
            sc.defaultLadderIndex = 0;      // scale 1.0: N5 is behaviour-neutral
            sc.useRgb565 = false;
            sc.depthBits = 24;
            sc.verboseLog = false;
            Debug.Log("ATTACH_COMPOSITOR created " + SceneCompositor.GameObjectName);
        }
        else if (existing.GetComponent<SceneCompositor>() == null)
        {
            existing.AddComponent<SceneCompositor>();
            Debug.Log("ATTACH_COMPOSITOR component added to existing GameObject");
        }
        else
        {
            Debug.Log("ATTACH_COMPOSITOR already present");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("ATTACH_COMPOSITOR_DONE");
        EditorApplication.Exit(0);
    }
}
