using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// N13 - cap every skybox face at 2048 and pin RGB Crunched DXT1 on WebGL.
///
/// Context: commit a5ace81 already set "CompressedLQ + crunched + max 2048"
/// on all of Assets/Resources with `textureFormat` left on Automatic. The 36
/// six-sided PNG faces are RGBA sources whose alpha is uniformly 255, and the
/// automatic picker treats fully-opaque alpha as "no alpha", so it already
/// resolved to DXT1Crunched. Pinning the format therefore changes no bytes
/// today; it guarantees a future source edit with one stray non-opaque pixel
/// cannot silently double the GPU footprint by flipping to DXT5Crunched.
/// Trilinear filtering and alphaSource=None are the other two gaps the card
/// asks for; both are asserted here and in tools/verify_skybox_meta.py.
///
/// Kill criterion (card N13): if crunch shows visible blocking on gradients
/// at 1366x768 on hardware, flip UseCrunch to false and rerun Apply. Every
/// texture then imports as plain DXT1 (~12 MB per 2048 cubemap) and Verify /
/// verify_skybox_meta.py --expect-format dxt1 accept that state instead.
/// There is deliberately NO automatic per-texture fallback: the Library
/// artifact size is padded to the uncompressed DXT size and cannot be used
/// to judge whether crunch helped (an earlier draft did exactly that and
/// grew the payload by 5 MB on Gam/Voyager).
///
/// Source image files are never touched; only importer settings change and
/// Unity downsamples on import. Nothing here changes textureQuality or
/// masterTextureLimit (explicitly out of scope for N13).
public static class SkyboxImportSettings
{
    const string SkyboxRoot = "Assets/Resources/Skybox";
    const string Platform = "WebGL";

    /// Owner decision C5 is resolved to 2048 per face (binding default #5).
    public const int MaxFaceSize = 2048;
    public const int CrunchQuality = 50;

    /// Default per the card. Set to false ONLY under the kill criterion.
    public const bool UseCrunch = true;

    public static TextureImporterFormat TargetFormat =>
        UseCrunch ? TextureImporterFormat.DXT1Crunched : TextureImporterFormat.DXT1;

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

        var dirtyPaths = new List<string>();
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
                if (!IsTargetState(s))
                {
                    s.overridden = true;
                    s.maxTextureSize = MaxFaceSize;
                    s.format = TargetFormat;
                    s.textureCompression = TextureImporterCompression.Compressed;
                    s.crunchedCompression = UseCrunch;
                    s.compressionQuality = CrunchQuality;
                    ti.SetPlatformTextureSettings(s);
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(ti);
                    dirtyPaths.Add(path);
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

        // Reimport outside the batch so each touched .meta is flushed to disk.
        foreach (var path in dirtyPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("SKYBOX_IMPORT result=Applied textures=" + paths.Count
                  + " changed=" + changed + " unchanged=" + already
                  + " maxSize=" + MaxFaceSize + " format=" + TargetFormat
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
        int cubemaps = 0, faces = 0;

        foreach (var path in paths)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            var s = ti.GetPlatformTextureSettings(Platform);

            if (!s.overridden) failures.Add(path + " overridden=false");
            if (s.maxTextureSize != MaxFaceSize)
                failures.Add(path + " maxTextureSize=" + s.maxTextureSize);
            if (s.compressionQuality != CrunchQuality)
                failures.Add(path + " compressionQuality=" + s.compressionQuality);
            if (s.textureCompression != TextureImporterCompression.Compressed)
                failures.Add(path + " textureCompression=" + s.textureCompression);
            if (s.format != TargetFormat || s.crunchedCompression != UseCrunch)
                failures.Add(path + " format=" + s.format + " crunched=" + s.crunchedCompression
                             + " expected " + TargetFormat + " crunched=" + UseCrunch);
            if (!ti.mipmapEnabled) failures.Add(path + " mipmapEnabled=false");
            if (ti.filterMode != FilterMode.Trilinear)
                failures.Add(path + " filterMode=" + ti.filterMode);
            if (ti.alphaSource != TextureImporterAlphaSource.None)
                failures.Add(path + " alphaSource=" + ti.alphaSource);

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex != null)
            {
                int w = Mathf.Min(tex.width, MaxFaceSize);
                int h = Mathf.Min(tex.height, MaxFaceSize);
                if (w > MaxFaceSize || h > MaxFaceSize || tex.width > MaxFaceSize || tex.height > MaxFaceSize)
                    failures.Add(path + " imported=" + tex.width + "x" + tex.height + " exceeds cap");
                bool cube = tex is Cubemap;
                if (cube) cubemaps++; else faces++;
                long dxt1 = TheoreticalDxt1MippedBytes(tex);
                gpuBytes += dxt1;
                Debug.Log("SKYBOX_TEX path=" + path + " src=" + tex.width + "x" + tex.height
                          + " capped=" + w + "x" + h
                          + " shape=" + (cube ? "Cube" : "2D")
                          + " format=" + s.format
                          + " gpu_dxt1_mipped_bytes=" + dxt1);
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
                  + " failures=0 format=" + TargetFormat
                  + " cubemaps=" + cubemaps + " faces=" + faces
                  + " gpu_dxt1_mipped_bytes=" + gpuBytes
                  + " gpu_mb=" + (gpuBytes / (1024f * 1024f)).ToString("F1"));
        Exit(0);
    }

    // --------------------------------------------------------------- helpers

    /// True when the WebGL block is already exactly the configured end state,
    /// which is what makes Apply idempotent (second run: changed=0).
    static bool IsTargetState(TextureImporterPlatformSettings s)
    {
        return s.overridden
            && s.maxTextureSize == MaxFaceSize
            && s.compressionQuality == CrunchQuality
            && s.textureCompression == TextureImporterCompression.Compressed
            && s.format == TargetFormat
            && s.crunchedCompression == UseCrunch;
    }

    /// GPU-resident size on an S3TC-capable device: DXT1 = 0.5 byte/px, +1/3
    /// for the full mip chain, x6 faces for a cubemap, at the imported
    /// (already capped) resolution. Crunch is a transport format only - it is
    /// transcoded to plain DXT1 before upload, so this number is the same for
    /// both accepted formats.
    static long TheoreticalDxt1MippedBytes(Texture tex)
    {
        int w = Mathf.Min(tex.width, MaxFaceSize);
        int h = Mathf.Min(tex.height, MaxFaceSize);
        long faces = (tex is Cubemap) ? 6 : 1;
        return (long)(w * h * 0.5 * 4.0 / 3.0) * faces;
    }

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
