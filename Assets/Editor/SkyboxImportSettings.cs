using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// N13 - cap every skybox face at 2048 and force RGB Crunched DXT1 on WebGL,
/// falling back to plain DXT1 on any texture where crunch makes it BIGGER.
///
/// Context: commit a5ace81 already set "CompressedLQ + crunched + max 2048"
/// on all of Assets/Resources with `textureFormat` left on Automatic. All 36
/// six-sided PNG faces here are RGBA sources, but every alpha channel is
/// uniformly 255, and Unity's automatic picker treats a fully-opaque alpha as
/// "no alpha" - so Automatic already resolved to DXT1Crunched. Forcing the
/// format therefore changes no bytes today; it pins the choice so a future
/// source edit with a stray non-opaque pixel cannot silently double the
/// footprint by flipping to DXT5Crunched. Trilinear + alphaSource=None are
/// the other two gaps the card asks for.
///
/// The measurable win is the crunch fallback: crunch is entropy-coded on top
/// of DXT1 and usually shrinks it 3-5x, but on a very noisy source (a
/// JPEG-artefacted starfield) its codebooks can exceed the raw DXT1 size.
/// After the first import this script compares each imported artifact against
/// the theoretical DXT1+mips size and, where crunch lost, reimports that one
/// texture as uncrunched DXT1 (the card's kill-criterion format), which is
/// both smaller and lossless relative to the crunched version.
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
                if (!IsTargetState(s))
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

        int fallbacks = ApplyCrunchFallback(paths);

        Debug.Log("SKYBOX_IMPORT result=Applied textures=" + paths.Count
                  + " changed=" + changed + " unchanged=" + already
                  + " crunchFallbacks=" + fallbacks
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
        long gpuBytes = 0, artifactBytes = 0;
        int crunchedCount = 0, uncrunchedCount = 0;

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
            bool crunched = s.format == TextureImporterFormat.DXT1Crunched && s.crunchedCompression;
            bool plain = s.format == TextureImporterFormat.DXT1 && !s.crunchedCompression;
            if (crunched) crunchedCount++;
            else if (plain) uncrunchedCount++;
            else failures.Add(path + " format=" + s.format + " crunched=" + s.crunchedCompression);
            if (!ti.mipmapEnabled) failures.Add(path + " mipmapEnabled=false");
            if (ti.filterMode != FilterMode.Trilinear)
                failures.Add(path + " filterMode=" + ti.filterMode);

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex != null)
            {
                int w = Mathf.Min(tex.width, MaxFaceSize);
                int h = Mathf.Min(tex.height, MaxFaceSize);
                // DXT1 = 0.5 byte/px, +1/3 for the full mip chain.
                long dxt1 = TheoreticalDxt1MippedBytes(tex);
                long artifact = ImportedArtifactBytes(path);
                gpuBytes += dxt1;
                artifactBytes += artifact;
                Debug.Log("SKYBOX_TEX path=" + path + " src=" + tex.width + "x" + tex.height
                          + " capped=" + w + "x" + h
                          + " shape=" + (tex is Cubemap ? "Cube" : "2D")
                          + " format=" + s.format
                          + " dxt1_bytes=" + dxt1 + " artifact_bytes=" + artifact);
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
                  + " failures=0 crunched=" + crunchedCount + " uncrunched=" + uncrunchedCount
                  + " gpu_dxt1_mipped_bytes=" + gpuBytes
                  + " gpu_mb=" + (gpuBytes / (1024f * 1024f)).ToString("F1")
                  + " imported_artifact_bytes=" + artifactBytes
                  + " artifact_mb=" + (artifactBytes / (1024f * 1024f)).ToString("F1"));
        Exit(0);
    }

    // ------------------------------------------------------- crunch fallback

    /// True when the platform block is already in one of the two accepted end
    /// states: the default DXT1Crunched, or the deliberate DXT1-uncrunched
    /// fallback chosen by ApplyCrunchFallback on a previous run. Treating the
    /// fallback as a valid state is what makes Apply idempotent.
    static bool IsTargetState(TextureImporterPlatformSettings s)
    {
        if (!s.overridden) return false;
        if (s.maxTextureSize != MaxFaceSize) return false;
        if (s.compressionQuality != CrunchQuality) return false;
        if (s.textureCompression != TextureImporterCompression.Compressed) return false;
        bool crunched = s.format == TextureImporterFormat.DXT1Crunched && s.crunchedCompression;
        bool plain = s.format == TextureImporterFormat.DXT1 && !s.crunchedCompression;
        return crunched || plain;
    }

    /// For every texture currently imported as DXT1Crunched, compare the size
    /// of the imported artifact in Library/ against the theoretical size of the
    /// same texture as plain DXT1 with a full mip chain. Where crunch came out
    /// larger it is strictly worse (bigger AND lossier), so switch that texture
    /// to uncrunched DXT1 - the card's kill-criterion format - and reimport.
    /// Returns the number of textures switched on this run.
    static int ApplyCrunchFallback(List<string> paths)
    {
        int switched = 0;
        foreach (var path in paths)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            var s = ti.GetPlatformTextureSettings(Platform);
            if (s.format != TextureImporterFormat.DXT1Crunched || !s.crunchedCompression) continue;

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex == null) continue;
            long dxt1 = TheoreticalDxt1MippedBytes(tex);
            long artifact = ImportedArtifactBytes(path);
            if (artifact <= 0 || dxt1 <= 0) continue; // cannot measure - leave crunched

            if (artifact > dxt1)
            {
                s.format = TextureImporterFormat.DXT1;
                s.crunchedCompression = false;
                ti.SetPlatformTextureSettings(s);
                EditorUtility.SetDirty(ti);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                long after = ImportedArtifactBytes(path);
                Debug.Log("SKYBOX_CRUNCH_FALLBACK path=" + path
                          + " crunched_bytes=" + artifact + " dxt1_bytes=" + dxt1
                          + " after_bytes=" + after + " reason=crunch-expands");
                switched++;
            }
        }
        if (switched > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        return switched;
    }

    /// DXT1 = 0.5 byte/px, +1/3 for the full mip chain, x6 faces for a cubemap,
    /// at the imported (already capped) resolution.
    static long TheoreticalDxt1MippedBytes(Texture tex)
    {
        int w = Mathf.Min(tex.width, MaxFaceSize);
        int h = Mathf.Min(tex.height, MaxFaceSize);
        long faces = (tex is Cubemap) ? 6 : 1;
        return (long)(w * h * 0.5 * 4.0 / 3.0) * faces;
    }

    /// Size on disk of the imported texture artifact. On the 2019.4 asset
    /// database (v1) this is Library/metadata/<guid[0..1]>/<guid>; the file
    /// holds the serialized Texture2D/Cubemap including its (crunched or raw)
    /// image data, so its size tracks what the build will carry. Returns -1
    /// when the artifact cannot be located so callers can skip the decision.
    static long ImportedArtifactBytes(string assetPath)
    {
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid) || guid.Length < 2) return -1;
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var candidate = Path.Combine(projectRoot, "Library", "metadata", guid.Substring(0, 2), guid);
        if (File.Exists(candidate)) return new FileInfo(candidate).Length;
        return -1;
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
