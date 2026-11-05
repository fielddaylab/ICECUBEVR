using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

/// Read-only diagnostic: what format does the WebGL importer ACTUALLY resolve
/// for each skybox texture, and what would Automatic have picked? Used to
/// decide whether forcing DXT1 is a real win or a no-op. Writes nothing.
public static class SkyboxFormatProbe
{
    public static void Report()
    {
        var paths = Directory.GetFiles("Assets/Resources/Skybox", "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p).ToList();

        foreach (var path in paths)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            var s = ti.GetPlatformTextureSettings("WebGL");
            var auto = ti.GetAutomaticFormat("WebGL");
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            string actual = "null";
            if (tex is Texture2D t2) actual = t2.format.ToString();
            else if (tex is Cubemap cm) actual = cm.format.ToString();

            Debug.Log("PROBE path=" + path
                      + " set_format=" + s.format
                      + " automatic_would_be=" + auto
                      + " imported_actual=" + actual
                      + " alphaSource=" + ti.alphaSource
                      + " doesSourceHaveAlpha=" + ti.DoesSourceTextureHaveAlpha());
        }
        Debug.Log("PROBE_DONE n=" + paths.Count);
        EditorApplication.Exit(0);
    }
}
