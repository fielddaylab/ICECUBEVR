using UnityEditor;
using UnityEngine;
using System;

public class WebGLBuilder
{
    public static void Build()
    {
        string outDir = "/out/webgl";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.memorySize = 512;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetVirtualRealitySupported(BuildTargetGroup.WebGL, false);
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;

        var opts = new BuildPlayerOptions();
        opts.scenes = new string[] { "Assets/Scenes/main.unity" };
        opts.locationPathName = outDir;
        opts.target = BuildTarget.WebGL;
        opts.options = BuildOptions.None;

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log("BUILD_RESULT result=" + s.result + " errors=" + s.totalErrors + " warnings=" + s.totalWarnings + " size=" + s.totalSize);
        EditorApplication.Exit(s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
