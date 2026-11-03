using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

/// Re-imports heavy Resources assets with WebGL-friendly settings.
/// Resources/ is force-included in the build, and this project loads from it
/// by name at runtime, so assets cannot be deleted - only compressed.
public class WebGLOptimize
{
    public static void Run()
    {
        int tex = 0, aud = 0;
        var guids = AssetDatabase.FindAssets("", new[] { "Assets/Resources", "Assets/Textures" });
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var imp = AssetImporter.GetAtPath(path);

            if (imp is TextureImporter ti)
            {
                // Let Unity pick an alpha-correct format; just cap size and
                // use crunched LQ compression. Forcing DXT1 would silently
                // destroy alpha on any texture that has it.
                var s = ti.GetPlatformTextureSettings("WebGL");
                s.overridden = true;
                s.maxTextureSize = 2048;
                s.textureCompression = TextureImporterCompression.CompressedLQ;
                s.crunchedCompression = true;
                s.compressionQuality = 50;
                ti.SetPlatformTextureSettings(s);
                ti.SaveAndReimport();
                tex++;
            }
            else if (imp is AudioImporter ai)
            {
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.4f;
                ai.defaultSampleSettings = s;
                ai.forceToMono = true;
                ai.SaveAndReimport();
                aud++;
            }
        }
        Debug.Log("OPTIMIZE_DONE textures=" + tex + " audio=" + aud);
        EditorApplication.Exit(0);
    }
}
