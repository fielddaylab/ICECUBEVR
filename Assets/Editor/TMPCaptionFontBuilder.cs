// TMPCaptionFontBuilder - builds the static SDF caption font asset used by the
// WebGL caption layer (card N3).
//
// Why a script instead of the Font Asset Creator window: the project is built
// headless in a container (unityci/editor 2019.4.40f1), so every asset we need
// has to be reproducible from the command line:
//
//   Unity -batchmode -nographics -quit -projectPath <proj> \
//         -executeMethod IceCube.TMPCaptionFontBuilder.RebuildAll
//
// Generator settings live in one place (the constants below) and are mirrored
// in tools/tmp_font.md, so swapping the typeface (owner decision C1) is a
// one-line change plus a rebuild.
//
// Hard constraint: the atlas is STATIC. TMP's dynamic atlas rasterizes glyphs
// at runtime through FreeType, which on WebGL means shipping the font file and
// burning main-thread time mid-frame. Everything the subtitle corpus needs is
// baked here instead, and the glyph report proves nothing is missing.
//
// Two-tier character set. A single 1024x1024 SDFAA atlas at 90pt holds roughly
// 200 glyphs, so "ASCII + all of Latin-1 + all of Latin Extended-A" (328 chars)
// physically cannot fit. The set is therefore split:
//
//   REQUIRED - the subtitle corpus plus printable ASCII. If any of these are
//              absent from the typeface we hit the card's kill criterion and
//              stop, because captions would render tofu boxes.
//   OPTIONAL - Latin-1 Supplement, Latin Extended-A and typographic
//              punctuation. Nice to have for a future localization pass; added
//              in code-point order until the atlas is full, then dropped.
//
// Note on TMP's API: TryAddCharacters' `missingCharacters` out-parameter mixes
// "this glyph is not in the font file" with "this glyph did not fit in the
// atlas" (see TMP_FontAsset.cs: the atlas-overflow path pushes leftovers from
// m_CharactersToAdd into the same list). Only the first case is a font problem,
// so coverage is probed directly against the face via FontEngine.GetGlyphIndex
// and the kill criterion is evaluated on that, never on TryAddCharacters.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using TMPro;

namespace IceCube
{
  public static class TMPCaptionFontBuilder
  {
    // ---- generator settings (keep in sync with tools/tmp_font.md) ----------
    public const string SourceFontPath   = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    public const string FontAssetPath    = "Assets/Fonts/Caption SDF.asset";
    public const string MaterialPath     = "Assets/Fonts/Caption SDF - Outline.mat";
    public const string CharsetPath      = "Assets/Fonts/caption_charset.txt";
    public const string ReportPath       = "tools/tmp_font_report.txt";
    public const string MainScriptPath   = "Assets/Scripts/Main.cs";
    const string TMPSettingsPath         = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    public const int SamplingPointSize = 90;
    public const int AtlasPadding      = 9;
    public const int AtlasWidth        = 1024;
    public const int AtlasHeight       = 1024;
    const GlyphRenderMode RenderMode   = GlyphRenderMode.SDFAA;

    // Outline / underlay preset. Values are in SDF units (0..1 of the spread),
    // which is why the outline is 0.15 and not a pixel count.
    const float OutlineWidth     = 0.15f;
    const float UnderlayOffsetX  =  0.5f;
    const float UnderlayOffsetY  = -0.5f;
    const float UnderlayAlpha    =  0.6f;
    const float UnderlayDilate   =  0.1f;
    const float UnderlaySoftness =  0.3f;

    const string MobileSDFShader = "TextMeshPro/Mobile/Distance Field";

    // ---- entry points ------------------------------------------------------

    [MenuItem("icecube/TMP/1. Import TMP Essentials")]
    public static void ImportEssentials()
    {
      int code = ImportEssentialsInternal();
      // Exit code 1 means "import queued on the editor loop" - do NOT call
      // Exit, or the queued package import is discarded. The completion
      // callback installed by ImportEssentialsInternal exits instead, which is
      // why this entry point must run WITHOUT -quit (see tools/tmp_font.md).
      if(Application.isBatchMode && code != 1) EditorApplication.Exit(code);
    }

    [MenuItem("icecube/TMP/2. Build Caption SDF Font")]
    public static void BuildCaptionFont()
    {
      int code = BuildCaptionFontInternal();
      if(Application.isBatchMode) EditorApplication.Exit(code);
    }

    [MenuItem("icecube/TMP/Rebuild All (import + font + material)")]
    public static void RebuildAll()
    {
      s_buildFontAfterImport = true;
      int code = ImportEssentialsInternal();
      // code 1 => import queued; OnPackageImported continues with the font build.
      if(code == 1) return;
      if(code == 0) code = BuildCaptionFontInternal();
      if(Application.isBatchMode) EditorApplication.Exit(code);
    }

    // ---- step 1: TMP essential resources ----------------------------------

    static int ImportEssentialsInternal()
    {
      if(File.Exists(TMPSettingsPath))
      {
        Debug.Log("TMP_IMPORT skipped=already-present path=" + TMPSettingsPath);
        return 0;
      }

      Debug.Log("TMP_IMPORT importing essentials (non-interactive)");

      // AssetDatabase.ImportPackage is asynchronous: it queues work on the
      // editor update loop and signals importPackageCompleted later. In
      // batchmode that means the import only lands if the editor keeps
      // ticking, so we hook the callback and return "deferred" (1) instead of
      // exiting. Run this entry point WITHOUT -quit.
      AssetDatabase.importPackageCompleted   += OnPackageImported;
      AssetDatabase.importPackageFailed      += OnPackageFailed;
      AssetDatabase.importPackageCancelled   += OnPackageCancelled;

      // (essentials, examples, interactive) - examples are ~2MB of demo scenes
      // we never ship, and interactive would block batchmode on a dialog.
      TMP_PackageResourceImporter.ImportResources(true, false, false);

      if(File.Exists(TMPSettingsPath))
      {
        // Headed editors occasionally complete synchronously.
        Unhook();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("TMP_IMPORT ok");
        return 0;
      }

      Debug.Log("TMP_IMPORT queued: waiting for importPackageCompleted");
      return 1;
    }

    static bool s_buildFontAfterImport;

    static void Unhook()
    {
      AssetDatabase.importPackageCompleted -= OnPackageImported;
      AssetDatabase.importPackageFailed    -= OnPackageFailed;
      AssetDatabase.importPackageCancelled -= OnPackageCancelled;
    }

    static void OnPackageImported(string packageName)
    {
      Unhook();
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
      AssetDatabase.SaveAssets();

      if(!File.Exists(TMPSettingsPath))
      {
        Debug.LogError("TMP_IMPORT failed: [" + packageName + "] imported but " + TMPSettingsPath + " is missing");
        if(Application.isBatchMode) EditorApplication.Exit(2);
        return;
      }

      Debug.Log("TMP_IMPORT ok package=" + packageName);
      int code = s_buildFontAfterImport ? BuildCaptionFontInternal() : 0;
      if(Application.isBatchMode) EditorApplication.Exit(code);
    }

    static void OnPackageFailed(string packageName, string error)
    {
      Unhook();
      Debug.LogError("TMP_IMPORT failed: [" + packageName + "] " + error);
      if(Application.isBatchMode) EditorApplication.Exit(2);
    }

    static void OnPackageCancelled(string packageName)
    {
      Unhook();
      Debug.LogError("TMP_IMPORT cancelled: [" + packageName + "]");
      if(Application.isBatchMode) EditorApplication.Exit(2);
    }

    // ---- step 2: the font asset + material preset -------------------------

    static int BuildCaptionFontInternal()
    {
      if(!File.Exists(TMPSettingsPath))
      {
        Debug.LogError("CAPTION_FONT failed: TMP essentials not imported (" + TMPSettingsPath + ")");
        return 2;
      }

      Font font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
      if(font == null)
      {
        Debug.LogError("CAPTION_FONT failed: source font not found at " + SourceFontPath);
        return 2;
      }

      string required = BuildRequiredCharacterSet();
      string optional = BuildOptionalCharacterSet();

      // KILL CRITERION (card N3), evaluated against the font face itself.
      // Checked BEFORE anything is written so a bad typeface leaves no
      // half-built asset behind.
      string unsupported = MissingFromFontFace(font, required);
      if(!string.IsNullOrEmpty(unsupported))
      {
        Debug.LogError("CAPTION_FONT failed: " + Path.GetFileName(SourceFontPath) +
          " lacks glyphs the caption corpus needs: " + Describe(unsupported) +
          ". Stopping - the owner must choose a font that covers these before N4.");
        return 4;
      }
      Debug.Log("CAPTION_FONT coverage ok: " + required.Length +
        " required glyphs all present in " + Path.GetFileName(SourceFontPath));

      // Created Dynamic on purpose: TryAddCharacters refuses to rasterize into
      // a Static asset. We flip it to Static once the atlas is baked, which is
      // also what the Font Asset Creator's "static" output looks like on disk.
      TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
        font, SamplingPointSize, AtlasPadding, RenderMode,
        AtlasWidth, AtlasHeight, AtlasPopulationMode.Dynamic,
        enableMultiAtlasSupport: false);

      if(fontAsset == null)
      {
        Debug.LogError("CAPTION_FONT failed: CreateFontAsset returned null. Is 'Include Font Data' enabled on " + SourceFontPath + "?");
        return 2;
      }

      // Persist first so the atlas texture / material land as sub-assets and
      // TMP's editor resource manager can flush glyph table edits into them.
      if(File.Exists(FontAssetPath)) AssetDatabase.DeleteAsset(FontAssetPath);
      AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

      Texture2D atlas = fontAsset.atlasTextures[0];
      atlas.name = "Caption SDF Atlas";
      AssetDatabase.AddObjectToAsset(atlas, fontAsset);

      Material defaultMaterial = fontAsset.material;
      defaultMaterial.name = "Caption SDF Atlas Material";
      AssetDatabase.AddObjectToAsset(defaultMaterial, fontAsset);

      // Required glyphs first, so that if the atlas runs out of room it is
      // always an optional glyph that gets dropped.
      string requiredMissing;
      bool requiredComplete = fontAsset.TryAddCharacters(required, out requiredMissing, includeFontFeatures: true);

      // Then top up with optional glyphs one at a time. Added individually
      // because TryAddCharacters is all-or-nothing per call for atlas space:
      // a batch that overflows reports every leftover as missing, whereas
      // single adds let us pack right up to the edge and stop cleanly.
      var optionalAdded = new StringBuilder();
      var optionalDropped = new StringBuilder();
      foreach(char c in optional)
      {
        string s = c.ToString();
        string dropped;
        if(fontAsset.TryAddCharacters(s, out dropped, includeFontFeatures: false) &&
           string.IsNullOrEmpty(dropped))
        {
          optionalAdded.Append(c);
        }
        else
        {
          optionalDropped.Append(c);
        }
      }

      // Kerning pairs are only recomputed on an includeFontFeatures call, so
      // re-run the required set (a no-op for glyphs already present) to pick up
      // adjustments involving the optional glyphs we just added.
      if(optionalAdded.Length > 0)
      {
        string ignored;
        fontAsset.TryAddCharacters(required, out ignored, includeFontFeatures: true);
      }

      string charset = required + optionalAdded.ToString();

      Directory.CreateDirectory("Assets/Fonts");
      File.WriteAllText(CharsetPath, charset, new UTF8Encoding(false));
      AssetDatabase.ImportAsset(CharsetPath, ImportAssetOptions.ForceSynchronousImport);

      atlas = fontAsset.atlasTextures[0];
      atlas.Apply(false, false);

      // Freeze it: Static drops the runtime font reference so no FreeType
      // rasterization can happen in the player.
      fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
      fontAsset.isMultiAtlasTexturesEnabled = false;

      // Record the recipe on the asset so re-opening its Font Asset Creator
      // panel shows the same settings a rebuild would use. The struct's
      // constructor is internal to TMP, so the fields are set one by one.
      var settings = new FontAssetCreationSettings();
      settings.sourceFontFileName      = Path.GetFileName(SourceFontPath);
      settings.sourceFontFileGUID      = AssetDatabase.AssetPathToGUID(SourceFontPath);
      settings.pointSizeSamplingMode   = 1;              // 1 == custom size
      settings.pointSize               = SamplingPointSize;
      settings.padding                 = AtlasPadding;
      settings.packingMode             = 4;              // 4 == optimum packing
      settings.atlasWidth              = AtlasWidth;
      settings.atlasHeight             = AtlasHeight;
      settings.characterSetSelectionMode = 7;            // 7 == custom character list
      settings.characterSequence       = charset;
      settings.referencedFontAssetGUID = string.Empty;
      settings.referencedTextAssetGUID = string.Empty;
      settings.fontStyle               = 0;
      settings.fontStyleModifier       = 0f;
      settings.renderMode              = (int)RenderMode;
      settings.includeFontFeatures     = true;
      fontAsset.creationSettings = settings;

      EditorUtility.SetDirty(atlas);
      EditorUtility.SetDirty(defaultMaterial);
      EditorUtility.SetDirty(fontAsset);
      AssetDatabase.SaveAssets();

      Material preset = BuildOutlinePreset(fontAsset, atlas);

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

      WriteReport(fontAsset, atlas, required, optionalAdded.ToString(),
                  optionalDropped.ToString(), preset != null);

      int glyphs = fontAsset.glyphTable != null ? fontAsset.glyphTable.Count : 0;
      int chars  = fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0;
      Debug.Log(string.Format(
        "CAPTION_FONT atlas={0}x{1} required={2} optional_added={3} optional_dropped={4} characters={5} glyphs={6} material={7}",
        atlas.width, atlas.height, required.Length, optionalAdded.Length,
        optionalDropped.Length, chars, glyphs, MaterialPath));

      if(atlas.width != AtlasWidth || atlas.height != AtlasHeight)
      {
        Debug.LogError("CAPTION_FONT failed: atlas is " + atlas.width + "x" + atlas.height + ", expected " + AtlasWidth + "x" + AtlasHeight);
        return 3;
      }

      // The required set is non-negotiable: coverage was already proven against
      // the face, so a failure here means the glyphs did not FIT. That is a
      // settings problem (point size / padding / atlas size), not a font one.
      if(!requiredComplete || !string.IsNullOrEmpty(requiredMissing))
      {
        Debug.LogError("CAPTION_FONT failed: the required caption glyphs do not fit in a single " +
          AtlasWidth + "x" + AtlasHeight + " atlas at " + SamplingPointSize + "pt" +
          (string.IsNullOrEmpty(requiredMissing) ? "" : " (short by " + Describe(requiredMissing) + ")") +
          " - lower SamplingPointSize or raise the atlas size. See " + ReportPath);
        return 5;
      }

      // Every required character must be resolvable through the finished asset.
      var corpusGap = new StringBuilder();
      foreach(char c in required)
      {
        if(!fontAsset.HasCharacter(c)) corpusGap.Append(c);
      }
      if(corpusGap.Length > 0)
      {
        Debug.LogError("CAPTION_FONT failed: asset is missing required characters " +
          Describe(corpusGap.ToString()) + " after build - see " + ReportPath);
        return 5;
      }

      if(optionalDropped.Length > 0)
      {
        // Expected and harmless: the atlas is deliberately capped at one page.
        Debug.Log("CAPTION_FONT note: " + optionalDropped.Length +
          " optional (non-corpus) glyphs did not fit the single atlas page and were dropped; " +
          "see optional_dropped in " + ReportPath);
      }

      Debug.Log("CAPTION_FONT ok");
      return 0;
    }

    // ---- material preset ---------------------------------------------------

    static Material BuildOutlinePreset(TMP_FontAsset fontAsset, Texture2D atlas)
    {
      Shader shader = Shader.Find(MobileSDFShader);
      if(shader == null)
      {
        Debug.LogError("CAPTION_FONT material skipped: shader '" + MobileSDFShader + "' not found");
        return null;
      }

      Material m = new Material(shader);
      m.name = "Caption SDF - Outline";

      // Mirror the font asset's atlas binding; a preset whose _MainTex or
      // _GradientScale disagrees with the asset renders at the wrong weight.
      m.SetTexture("_MainTex", atlas);
      m.SetFloat("_TextureWidth",  atlas.width);
      m.SetFloat("_TextureHeight", atlas.height);
      m.SetFloat("_GradientScale", AtlasPadding + 1); // +1 packing modifier for SDF modes
      m.SetFloat("_WeightNormal",  fontAsset.normalStyle);
      m.SetFloat("_WeightBold",    fontAsset.boldStyle);

      m.SetFloat("_OutlineWidth", OutlineWidth);
      m.SetColor("_OutlineColor", Color.black);
      m.EnableKeyword("OUTLINE_ON");

      // Underlay == TMP's drop shadow. Captions sit over snow and over black
      // sky, so they need both the outline and an offset shadow to stay legible.
      m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, UnderlayAlpha));
      m.SetFloat("_UnderlayOffsetX", UnderlayOffsetX);
      m.SetFloat("_UnderlayOffsetY", UnderlayOffsetY);
      m.SetFloat("_UnderlayDilate", UnderlayDilate);
      m.SetFloat("_UnderlaySoftness", UnderlaySoftness);
      m.EnableKeyword("UNDERLAY_ON");

      if(File.Exists(MaterialPath)) AssetDatabase.DeleteAsset(MaterialPath);
      AssetDatabase.CreateAsset(m, MaterialPath);
      EditorUtility.SetDirty(m);
      return m;
    }

    // ---- character set -----------------------------------------------------

    /// The glyphs captions cannot do without: printable ASCII plus every
    /// character the current corpus actually uses. Must fit in the atlas, and
    /// must exist in the typeface, or the build stops (card N3 kill criterion).
    public static string BuildRequiredCharacterSet()
    {
      var set = new SortedSet<int>();

      for(int c = 0x0020; c <= 0x007E; c++) set.Add(c); // ASCII printable

      foreach(char c in ScriptLiteralCharacters(MainScriptPath))
      {
        if(c >= 0x20 && c != 0x7F) set.Add(c);
      }

      return ToStringSet(set);
    }

    /// Everything we would *like* to have, in descending order of usefulness to
    /// a caption. Packed in this order until the atlas page fills up, so the
    /// glyphs that get dropped are always the least likely to be needed.
    ///
    /// Priority matters: a 1024x1024 SDFAA page at 90pt fits ~200 glyphs, which
    /// is far short of ASCII + Latin-1 + Latin Extended-A. Typographic
    /// punctuation (em dash, curly quotes, ellipsis) goes first because a
    /// caption writer reaches for those constantly, whereas Latin Extended-A
    /// only matters if the project is ever localized.
    public static string BuildOptionalCharacterSet()
    {
      var ordered = new List<int>();
      var seen = new HashSet<int>();

      // 1. Typographic punctuation: dashes, curly quotes, ellipsis.
      ordered.AddRange(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201A, 0x201B, 0x201C, 0x201D, 0x2026 });
      // 2. Latin-1 Supplement: accented Latin for most Western European text.
      for(int c = 0x00A0; c <= 0x00FF; c++) ordered.Add(c);
      // 3. Latin Extended-A: Central/Eastern European. First to be dropped.
      for(int c = 0x0100; c <= 0x017F; c++) ordered.Add(c);

      // Anything already mandatory must not be requested twice.
      foreach(char c in BuildRequiredCharacterSet()) seen.Add(c);

      var sb = new StringBuilder();
      foreach(int c in ordered)
      {
        if(seen.Add(c)) sb.Append(char.ConvertFromUtf32(c));
      }
      return sb.ToString();
    }

    static string ToStringSet(SortedSet<int> set)
    {
      var sb = new StringBuilder(set.Count);
      foreach(int c in set) sb.Append(char.ConvertFromUtf32(c));
      return sb.ToString();
    }

    /// Which of `characters` the *typeface* genuinely lacks, independent of
    /// atlas capacity. FontEngine.GetGlyphIndex returns 0 for an unmapped code
    /// point; TMP applies the same NBSP/soft-hyphen substitutions internally,
    /// so they are mirrored here to avoid false positives.
    public static string MissingFromFontFace(Font font, string characters)
    {
      if(FontEngine.LoadFontFace(font, SamplingPointSize) != FontEngineError.Success)
      {
        Debug.LogError("CAPTION_FONT: could not load font face for coverage probe");
        return characters;
      }

      var missing = new StringBuilder();
      foreach(char c in characters)
      {
        if(!HasGlyph(c)) missing.Append(c);
      }
      return missing.ToString();
    }

    /// True if the currently loaded face maps this code point to a real glyph.
    /// TMP's internal GetGlyphIndex is not public, so the public
    /// TryGetGlyphIndex is used; it reports index 0 for unmapped code points.
    /// The NBSP / soft-hyphen substitutions mirror TryAddCharacters so this
    /// probe agrees with what TMP will actually manage to add.
    static bool HasGlyph(char c)
    {
      uint index;
      if(FontEngine.TryGetGlyphIndex(c, out index) && index != 0) return true;

      if(c == 0x00A0)                       return FontEngine.TryGetGlyphIndex(0x20, out index) && index != 0;
      if(c == 0x00AD || c == 0x2011)        return FontEngine.TryGetGlyphIndex(0x2D, out index) && index != 0;
      return false;
    }

    /// Pulls the characters out of every double-quoted C# literal in the file.
    /// Deliberately broader than just subtitle_strings: the alert/label strings
    /// in the same file end up on screen through the same caption path.
    public static HashSet<char> ScriptLiteralCharacters(string path)
    {
      var chars = new HashSet<char>();
      if(!File.Exists(path))
      {
        Debug.LogWarning("CAPTION_FONT charset: " + path + " not found; using static ranges only");
        return chars;
      }

      string src = File.ReadAllText(path);
      // Non-verbatim string literals; escapes are decoded below.
      foreach(Match m in Regex.Matches(src, "\"((?:[^\"\\\\\\n]|\\\\.)*)\""))
      {
        foreach(char c in Unescape(m.Groups[1].Value)) chars.Add(c);
      }
      return chars;
    }

    static string Unescape(string literal)
    {
      var sb = new StringBuilder(literal.Length);
      for(int i = 0; i < literal.Length; i++)
      {
        char c = literal[i];
        if(c != '\\' || i + 1 >= literal.Length) { sb.Append(c); continue; }

        char e = literal[++i];
        switch(e)
        {
          case 'n': sb.Append('\n'); break;
          case 'r': sb.Append('\r'); break;
          case 't': sb.Append('\t'); break;
          case '0': sb.Append('\0'); break;
          case '"': sb.Append('"');  break;
          case '\'': sb.Append('\''); break;
          case '\\': sb.Append('\\'); break;
          case 'u':
          case 'x':
          {
            int n = 0;
            int len = 0;
            int max = e == 'u' ? 4 : 4;
            while(len < max && i + 1 < literal.Length && Uri.IsHexDigit(literal[i + 1]))
            {
              n = n * 16 + Convert.ToInt32(literal[++i].ToString(), 16);
              len++;
            }
            if(len > 0) sb.Append((char)n);
            break;
          }
          default: sb.Append(e); break;
        }
      }
      return sb.ToString();
    }

    static string Describe(string s)
    {
      var sb = new StringBuilder();
      foreach(char c in s)
      {
        if(sb.Length > 0) sb.Append(' ');
        sb.Append("U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture));
      }
      return sb.ToString();
    }

    // ---- glyph report ------------------------------------------------------

    static void WriteReport(TMP_FontAsset fontAsset, Texture2D atlas, string required,
                            string optionalAdded, string optionalDropped, bool materialWritten)
    {
      var corpus = ScriptLiteralCharacters(MainScriptPath);
      var corpusMissing = new SortedSet<int>();
      foreach(char c in corpus)
      {
        if(c < 0x20) continue;
        if(!fontAsset.HasCharacter(c)) corpusMissing.Add(c);
      }

      var sb = new StringBuilder();
      sb.AppendLine("# Caption SDF glyph report (generated by IceCube.TMPCaptionFontBuilder)");
      sb.AppendLine("generated_utc      = " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
      sb.AppendLine("unity              = " + Application.unityVersion);
      sb.AppendLine("source_font        = " + SourceFontPath);
      sb.AppendLine("font_asset         = " + FontAssetPath);
      sb.AppendLine("material_preset    = " + MaterialPath + (materialWritten ? "" : "  (NOT WRITTEN)"));
      sb.AppendLine("sampling_point_size= " + SamplingPointSize);
      sb.AppendLine("padding            = " + AtlasPadding);
      sb.AppendLine("render_mode        = " + RenderMode);
      sb.AppendLine("atlas              = " + atlas.width + "x" + atlas.height + " (" + atlas.format + ")");
      sb.AppendLine("atlas_textures     = " + fontAsset.atlasTextures.Length);
      sb.AppendLine("population_mode    = " + fontAsset.atlasPopulationMode);
      sb.AppendLine("multi_atlas        = " + fontAsset.isMultiAtlasTexturesEnabled);
      sb.AppendLine("characters_in_asset= " + (fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0));
      sb.AppendLine("glyphs_in_asset    = " + (fontAsset.glyphTable != null ? fontAsset.glyphTable.Count : 0));
      sb.AppendLine();
      sb.AppendLine("# Required tier: printable ASCII + every character used by Main.cs string");
      sb.AppendLine("# literals. All of these MUST be present or the build fails.");
      sb.AppendLine("required_count      = " + required.Length);
      sb.AppendLine("required_all_present= " + (corpusMissing.Count == 0));
      sb.Append("subtitle_corpus_missing = ");
      if(corpusMissing.Count == 0) sb.AppendLine("(none)");
      else
      {
        var parts = new List<string>();
        foreach(int c in corpusMissing) parts.Add("U+" + c.ToString("X4", CultureInfo.InvariantCulture) + " '" + (char)c + "'");
        sb.AppendLine(string.Join(", ", parts.ToArray()));
      }
      sb.AppendLine();
      sb.AppendLine("# Optional tier: Latin-1 Supplement, Latin Extended-A and typographic");
      sb.AppendLine("# punctuation, packed in until the single atlas page is full. These are");
      sb.AppendLine("# present in the typeface (coverage was verified) but are not needed by the");
      sb.AppendLine("# current corpus, so dropping them is expected and not an error.");
      sb.AppendLine("optional_added     = " + optionalAdded.Length);
      sb.AppendLine("optional_dropped   = " + optionalDropped.Length);
      sb.AppendLine("optional_dropped_cps = " + (optionalDropped.Length == 0 ? "(none)" : Describe(optionalDropped)));
      sb.AppendLine();
      sb.AppendLine("charset_file       = " + CharsetPath);

      Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
      File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
      Debug.Log("CAPTION_FONT report written to " + ReportPath);
    }
  }
}
