using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// N13 - cap every skybox face at 2048 and force RGB Crunched DXT1 on WebGL.
///
/// Why this exists even though a previous pass (commit a5ace81) already set
/// "CompressedLQ + crunched + max 2048" on all of Assets/Resources:
///
///   That pass left `textureFormat` on Automatic, and ALL 36 six-sided PNG
///   faces under Resources/Skybox are 32-bit RGBA sources. Unity's automatic
///   pick for an RGBA source is DXT5 Crunched - 1 byte/px - not DXT1
///   Crunched at 0.5 byte/px. Measured here, every one of those alpha
///   channels is uniformly 255 (fully opaque), so the alpha block is paying
///   double rate to store "opaque" 4.2M times per face. Forcing
///   RGB_Crunched_DXT1 halves both the payload and the GPU footprint of the
///   dominant asset group with no information loss whatsoever.
///
/// The source image files are never touched; only importer settings change,
/// and Unity downsamples on import. Nothing here changes textureQuality or
/// masterTextureLimit (explicitly out of scope for N13).
public static class SkyboxImportSettings
{
    const string SkyboxRoot = "Assets/Resources/Skybox";
    const string Platform = "WebGL";

    /// Owner decision C5 is resolved to 2048 per face (binding default #5).
    public const int MaxFaceSize = 2048;
    public const int CrunchQuality = 50;

    static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".psd"
    };

    // ------------------------------------------------------------------ apply

    [MenuItem("icecube/Skybox/1. Apply WebGL import settings")]
    public static void Apply()
    {
        int changed = 0, already = 0;
        var paths = FindSkyboxTextures();
        if (paths.Count == 0)
        {
            Debug.LogError("SKYBOX_IMPORT result=Failed reason=no-textures-found root=" + SkyboxRoot);
            Exit(2);
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var path in paths)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    Debug.LogWarning("SKYBOX_IMPORT skip path=" + path + " reason=not-a-texture");
                    continue;
                }

                bool dirty = false;

                // Mipmaps on + trilinear: a skybox face fills the whole frame
                // at the horizon and shrinks hard towards the poles, so the
                // minified mips are what a Chromebook actually samples.
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.filterMode != FilterMode.Trilinear)
                {
                    ti.filterMode = FilterMode.Trilinear;
                    dirty = true;
                }
                // Skyboxes need no alpha, and every measured source is fully
                // opaque. Telling the importer the alpha is unused keeps the
                // automatic path from ever re-promoting these to DXT5.
                if (ti.alphaSource != TextureImporterAlphaSource.None)
                {
                    ti.alphaSource = TextureImporterAlphaSource.None;
                    dirty = true;
                }
                if (ti.alphaIsTransparency) { ti.alphaIsTransparency = false; dirty = true; }
                if (ti.maxTextureSize > MaxFaceSize)
                {
                    ti.maxTextureSize = MaxFaceSize;
                    dirty = true;
                }

                var s = ti.GetPlatformTextureSettings(Platform);
                if (!s.overridden
                    || s.maxTextureSize != MaxFaceSize
                    || s.format != TextureImporterFormat.DXT1Crunched
                    || s.compressionQuality != CrunchQuality
                    || s.textureCompression != TextureImporterCompression.Compressed
                    || !s.crunchedCompression)
                {
                    s.overridden = true;
                    s.maxTextureSize = MaxFaceSize;
                    s.format = TextureImporterFormat.DXT1Crunched;
                    s.textureCompression = TextureImporterCompression.Compressed;
                    s.crunchedCompression = true;
                    s.compressionQuality = CrunchQuality;
                    ti.SetPlatformTextureSettings(s);
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(ti);
                    changed++;
                }
                else
                {
                    already++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Reimport outside the batch so each .meta is flushed to disk.
        foreach (var path in paths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("SKYBOX_IMPORT result=Applied textures=" + paths.Count
                  + " changed=" + changed + " unchanged=" + already
                  + " maxSize=" + MaxFaceSize + " format=DXT1Crunched"
                  + " quality=" + CrunchQuality);

        Verify();
    }

    // ----------------------------------------------------------------- verify

    [MenuItem("icecube/Skybox/2. Verify WebGL import settings")]
    public static void Verify()
    {
        var paths = FindSkyboxTextures();
        var failures = new List<string>();
        long gpuBytes = 0;

        foreach (var path in paths)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            var s = ti.GetPlatformTextureSettings(Platform);

            if (!s.overridden) failures.Add(path + " overridden=false");
            if (s.maxTextureSize != MaxFaceSize)
                failures.Add(path + " maxTextureSize=" + s.maxTextureSize);
            if (s.format != TextureImporterFormat.DXT1Crunched)
                failures.Add(path + " format=" + s.format);
            if (!s.crunchedCompression) failures.Add(path + " crunchedCompression=false");
            if (s.compressionQuality != CrunchQuality)
                failures.Add(path + " compressionQuality=" + s.compressionQuality);
            if (!ti.mipmapEnabled) failures.Add(path + " mipmapEnabled=false");
            if (ti.filterMode != FilterMode.Trilinear)
                failures.Add(path + " filterMode=" + ti.filterMode);

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex != null)
            {
                int w = Mathf.Min(tex.width, MaxFaceSize);
                int h = Mathf.Min(tex.height, MaxFaceSize);
                // DXT1 = 0.5 byte/px, +1/3 for the full mip chain.
                long faces = (tex is Cubemap) ? 6 : 1;
                gpuBytes += (long)(w * h * 0.5 * 4.0 / 3.0) * faces;
                Debug.Log("SKYBOX_TEX path=" + path + " src=" + tex.width + "x" + tex.height
                          + " capped=" + w + "x" + h
                          + " shape=" + (tex is Cubemap ? "Cube" : "2D"));
            }
        }

        if (failures.Count > 0)
        {
            foreach (var f in failures) Debug.LogError("SKYBOX_VERIFY_FAIL " + f);
            Debug.LogError("SKYBOX_VERIFY result=Failed textures=" + paths.Count
                           + " failures=" + failures.Count);
            Exit(3);
            return;
        }

        Debug.Log("SKYBOX_VERIFY result=Succeeded textures=" + paths.Count
                  + " failures=0 gpu_dxt1_mipped_bytes=" + gpuBytes
                  + " gpu_mb=" + (gpuBytes / (1024f * 1024f)).ToString("F1"));
        Exit(0);
    }

    // --------------------------------------------------------------- helpers

    static List<string> FindSkyboxTextures()
    {
        if (!Directory.Exists(SkyboxRoot)) return new List<string>();
        return Directory
            .GetFiles(SkyboxRoot, "*", SearchOption.AllDirectories)
            .Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => p.Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    static void Exit(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
