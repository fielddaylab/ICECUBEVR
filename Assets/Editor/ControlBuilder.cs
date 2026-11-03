using UnityEditor;
using UnityEngine;

/// Builds a near-empty scene with the SAME player settings as the real build.
/// This is the control: if this runs in the verification browser but the full
/// build wedges, the difference is content scale (memory), not the pipeline.
public class ControlBuilder
{
    public static void Build()
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.memorySize = 512;
        PlayerSettings.virtualRealitySupported = false;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;

        var scenePath = "Assets/Scenes/__control.unity";
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "ControlCube";
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);

        var opts = new BuildPlayerOptions {
            scenes = new[] { scenePath },
            locationPathName = "/out/control",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("CONTROL_RESULT result=" + report.summary.result +
                  " errors=" + report.summary.totalErrors +
                  " size=" + report.summary.totalSize);
        EditorApplication.Exit(report.summary.totalErrors > 0 ? 1 : 0);
    }
}
