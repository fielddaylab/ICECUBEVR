// TextMeshMigrator - replaces legacy TextMesh components with TextMeshPro (3D)
// SDF text on the SAME GameObject (card N12).
//
// Why a script and not hand-edited YAML: a TextMeshPro component serializes
// ~80 fields plus a RectTransform swap, and the sizing has to be fitted to the
// bounds the old TextMesh actually rendered. Doing it inside the editor lets us
// measure both renderers and keep every label's visual width within ±10% of
// what it was, without touching position/rotation/scale or any collider.
//
//   unity-editor -quit -batchmode -nographics -projectPath /project \
//                -executeMethod IceCube.TextMeshMigrator.Migrate
//
// Rules (tools/textmesh_migration.md has the rationale):
//   * Never delete a GameObject; only the TextMesh + its MeshRenderer go.
//   * Transform is untouched. TMP converts it to a RectTransform with a
//     zero-size rect and centre pivot, so the old TextMesh anchor point is
//     still exactly the transform origin.
//   * Font: Assets/Fonts/Caption SDF.asset (static atlas from N3).
//     Material: Assets/Fonts/Caption SDF - WorldOutline.mat (outline only; the
//     underlay/drop-shadow variant is for the 2D caption layer).
//   * Start size = fontSize*characterSize*0.1 (the card's heuristic), then
//     scale by measured_old_width/measured_new_width. VISIBLE/X-RAY/NEUTRINO
//     get a further 1.5x (card step 2).
//   * Empty labels (runtime-assigned) are measured with a probe string.
//
// Also migrates the three AR label prefabs that Main.cs instantiates, because
// Main.cs fetches their text component by type and we switch it to TMP_Text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using TMPro;

namespace IceCube
{
  public static class TextMeshMigrator
  {
    public const string ScenePath         = "Assets/Scenes/main.unity";
    public const string FontAssetPath     = TMPCaptionFontBuilder.FontAssetPath;
    public const string BaseMaterialPath  = TMPCaptionFontBuilder.MaterialPath;
    public const string WorldMaterialPath = "Assets/Fonts/Caption SDF - WorldOutline.mat";
    public const string ReportPath        = "tools/textmesh_migration_report.txt";

    public static readonly string[] PrefabPaths =
    {
      "Assets/ARLeftLabel.prefab",
      "Assets/ARRightLabel.prefab",
      "Assets/ARBHLabel.prefab",
    };

    // Labels that are read at the far end of the AR rig; card step 2 says 1.5x.
    static readonly HashSet<string> BumpNames = new HashSet<string> { "Text_Viz", "Text_Gam", "Text_Neu" };
    const float BumpFactor = 1.5f;

    const float OutlineWidth = 0.15f;
    const float WidthTolerance = 0.10f;
    const string ProbeText = "00:00:00 SAMPLE";

    static readonly StringBuilder s_report = new StringBuilder();
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [MenuItem("icecube/TMP/3. Migrate TextMesh -> TextMeshPro (scene + prefabs)")]
    public static void Migrate()
    {
      int code = MigrateInternal();
      if(Application.isBatchMode) EditorApplication.Exit(code);
    }

    static int MigrateInternal()
    {
      s_report.Length = 0;
      Log("TEXTMESH_MIGRATE start");

      TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
      if(font == null) { Debug.LogError("TEXTMESH_MIGRATE failed: font asset missing " + FontAssetPath); return 2; }

      Material mat = EnsureWorldMaterial(font);
      if(mat == null) { Debug.LogError("TEXTMESH_MIGRATE failed: could not create " + WorldMaterialPath); return 2; }

      int migrated = 0;
      int bad = 0;

      // --- scene -----------------------------------------------------------
      Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

      // OpenScene(Single) unloads "unused" assets, which includes anything we
      // loaded above and only hold from C#. A stale TMP_FontAsset reference
      // compares == null, and TMP then silently falls back to its default
      // LiberationSans SDF (first run of this script did exactly that).
      // Re-resolve both references now that the scene is the live one.
      font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
      mat  = AssetDatabase.LoadAssetAtPath<Material>(WorldMaterialPath);
      if(font == null || mat == null) { Debug.LogError("TEXTMESH_MIGRATE failed: font/material unloaded after OpenScene"); return 2; }

      foreach(GameObject root in scene.GetRootGameObjects())
        foreach(TextMesh tm in root.GetComponentsInChildren<TextMesh>(true))
        {
          if(!MigrateOne(tm, font, mat, "scene")) bad++;
          migrated++;
        }
      EditorSceneManager.MarkSceneDirty(scene);
      if(!EditorSceneManager.SaveScene(scene, ScenePath)) { Debug.LogError("TEXTMESH_MIGRATE failed: SaveScene"); return 3; }

      // --- prefabs ---------------------------------------------------------
      foreach(string path in PrefabPaths)
      {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        mat  = AssetDatabase.LoadAssetAtPath<Material>(WorldMaterialPath);
        if(font == null || mat == null) { Debug.LogError("TEXTMESH_MIGRATE failed: font/material unloaded before " + path); return 2; }
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
          foreach(TextMesh tm in contents.GetComponentsInChildren<TextMesh>(true))
          {
            if(!MigrateOne(tm, font, mat, Path.GetFileName(path))) bad++;
            migrated++;
          }
          PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
      }

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      Log("TEXTMESH_MIGRATE done migrated=" + migrated + " out_of_tolerance=" + bad);
      File.WriteAllText(ReportPath, s_report.ToString());
      return 0;
    }

    // Outline-only preset for world labels. Copied from the N3 caption preset
    // so the atlas texture / gradient scale / weights stay consistent, with the
    // underlay (drop shadow) switched off: the 3D labels are read against the
    // sky/portal and only need the dark edge, not a second pass worth of alpha.
    static Material EnsureWorldMaterial(TMP_FontAsset font)
    {
      Material existing = AssetDatabase.LoadAssetAtPath<Material>(WorldMaterialPath);
      if(existing != null) return existing;

      Material src = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
      if(src == null) src = font.material;
      if(src == null) return null;

      Material m = new Material(src);
      m.name = Path.GetFileNameWithoutExtension(WorldMaterialPath);
      m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
      m.SetFloat(ShaderUtilities.ID_TextureWidth,  font.atlasWidth);
      m.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);
      m.SetFloat(ShaderUtilities.ID_GradientScale, font.atlasPadding + 1);
      m.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
      m.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
      m.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
      m.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
      m.EnableKeyword(ShaderUtilities.Keyword_Outline);
      m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
      m.DisableKeyword(ShaderUtilities.Keyword_Bevel);
      m.DisableKeyword(ShaderUtilities.Keyword_Glow);
      AssetDatabase.CreateAsset(m, WorldMaterialPath);
      Log("TEXTMESH_MIGRATE created material " + WorldMaterialPath);
      return m;
    }

    static bool MigrateOne(TextMesh tm, TMP_FontAsset font, Material mat, string where)
    {
      GameObject go = tm.gameObject;
      string path = HierarchyPath(go.transform);

      // ---- snapshot of the legacy component ------------------------------
      string text      = tm.text;
      int    fontSize  = tm.fontSize == 0 ? 13 : tm.fontSize; // 0 means "font default" (13 for Arial)
      float  charSize  = tm.characterSize;
      float  lineSp    = tm.lineSpacing;
      TextAnchor anchor = tm.anchor;
      Color  color     = tm.color;
      FontStyle style  = tm.fontStyle;
      bool   richText  = tm.richText;
      float  offsetZ   = tm.offsetZ;

      Transform tr = go.transform;
      Vector3    lp = tr.localPosition;
      Quaternion lr = tr.localRotation;
      Vector3    ls = tr.localScale;

      MeshRenderer oldMr = go.GetComponent<MeshRenderer>();
      bool wasEnabled = oldMr == null || oldMr.enabled;

      // ---- measure the legacy render ---------------------------------------
      bool probed = string.IsNullOrEmpty(text.Trim());
      if(probed) tm.text = ProbeText;
      Vector3 oldSize = oldMr != null ? oldMr.bounds.size : Vector3.zero;
      float oldW = oldSize.x, oldH = oldSize.y;
      if(probed) tm.text = text;

      // ---- swap components -------------------------------------------------
      UnityEngine.Object.DestroyImmediate(tm, true);
      if(oldMr != null) UnityEngine.Object.DestroyImmediate(oldMr, true);
      MeshFilter oldMf = go.GetComponent<MeshFilter>();
      if(oldMf != null) UnityEngine.Object.DestroyImmediate(oldMf, true);

      TextMeshPro tmp = go.AddComponent<TextMeshPro>();

      // AddComponent swapped Transform -> RectTransform; pin the transform back
      // exactly and collapse the rect to a point so the anchor stays put.
      RectTransform rt = go.GetComponent<RectTransform>();
      rt.localPosition = lp;
      rt.localRotation = lr;
      rt.localScale    = ls;
      rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
      rt.pivot = new Vector2(0.5f, 0.5f);
      rt.sizeDelta = Vector2.zero;
      rt.anchoredPosition3D = lp;

      tmp.font = font;
      tmp.fontSharedMaterial = mat;
      if(tmp.font != font || tmp.fontSharedMaterial != mat)
      {
        // Never let a label ship with the default font + our atlas material:
        // the glyph UVs would index the wrong texture.
        Debug.LogError("TEXTMESH_MIGRATE font/material did not stick on " + path +
          " (font=" + (tmp.font ? tmp.font.name : "null") + ")");
        throw new InvalidOperationException("font assignment failed on " + path);
      }
      tmp.text = text;
      tmp.color = color;
      tmp.richText = richText;
      tmp.fontStyle = ToTmpStyle(style);
      tmp.alignment = ToTmpAlignment(anchor);
      tmp.enableWordWrapping = false;
      tmp.overflowMode = TextOverflowModes.Overflow;
      tmp.enableKerning = true;
      tmp.extraPadding = true;           // room for the outline in the quad
      tmp.isOrthographic = false;
      tmp.lineSpacing = (lineSp - 1f) * 100f; // TextMesh 1.0 == TMP 0
      tmp.enableAutoSizing = false;
      tmp.sortingOrder = 0;
      // TextMesh drew in front by m_OffsetZ; TMP has no equivalent, keep it on
      // the transform via a tiny z shift only if it was non-zero (never is here).
      if(Mathf.Abs(offsetZ) > 1e-6f) rt.localPosition = lp + new Vector3(0, 0, offsetZ);

      // ---- size: heuristic start, then fit to the measured width ----------
      float startSize = fontSize * charSize * 0.1f;
      tmp.fontSize = startSize;
      if(probed) tmp.text = ProbeText;
      tmp.ForceMeshUpdate(true, true);
      Vector2 newSizeLocal = tmp.GetRenderedValues(false);
      // NB: `tr` died when AddComponent swapped Transform -> RectTransform;
      // only `rt` is valid from here on.
      float newW = newSizeLocal.x * Mathf.Abs(rt.lossyScale.x);
      float newH = newSizeLocal.y * Mathf.Abs(rt.lossyScale.y);

      float fitted = startSize;
      string fitNote = "formula";
      if(oldW > 1e-5f && newW > 1e-5f)
      {
        fitted = startSize * (oldW / newW);
        fitNote = "fitted-to-width";
      }
      else if(oldH > 1e-5f && newH > 1e-5f)
      {
        fitted = startSize * (oldH / newH);
        fitNote = "fitted-to-height";
      }
      float bump = BumpNames.Contains(go.name) ? BumpFactor : 1f;
      tmp.fontSize = fitted * bump;
      tmp.ForceMeshUpdate(true, true);
      Vector2 finalLocal = tmp.GetRenderedValues(false);
      float finalW = finalLocal.x * Mathf.Abs(rt.lossyScale.x);
      float finalH = finalLocal.y * Mathf.Abs(rt.lossyScale.y);
      if(probed) tmp.text = text;

      // ---- renderer flags: same as the old TextMesh renderer -------------
      MeshRenderer mr = go.GetComponent<MeshRenderer>();
      if(mr != null)
      {
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        mr.allowOcclusionWhenDynamic = true;
        mr.enabled = wasEnabled;
      }

      float ratio = oldW > 1e-5f ? (finalW / bump) / oldW : 0f;
      bool ok = ratio == 0f || Mathf.Abs(ratio - 1f) <= WidthTolerance;

      Log(string.Format(Inv,
        "TEXTMESH_MIGRATE [{0}] {1} text={2} legacy(fs={3} cs={4} anchor={5} w={6:0.####} h={7:0.####}) " +
        "tmp(start={8:0.###} fitted={9:0.###} bump={10} final={11:0.###} w={12:0.####} h={13:0.####} align={14}) " +
        "fit={15} width_ratio={16:0.###} {17}{18}",
        where, path, Quote(text), fontSize, charSize, anchor, oldW, oldH,
        startSize, fitted, bump, tmp.fontSize, finalW, finalH, tmp.alignment,
        fitNote, ratio, ok ? "OK" : "OUT_OF_TOLERANCE", probed ? " (probe='" + ProbeText + "')" : ""));

      EditorUtility.SetDirty(go);
      return ok;
    }

    // TextMesh anchor == where the transform origin sits on the text block.
    // TMP alignment with a zero-size rect at the pivot does the same thing.
    static TextAlignmentOptions ToTmpAlignment(TextAnchor a)
    {
      switch(a)
      {
        case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
        case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
        case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
        case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
        case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
        case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
        case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
        case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
        case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
      }
      return TextAlignmentOptions.Center;
    }

    static FontStyles ToTmpStyle(FontStyle s)
    {
      switch(s)
      {
        case FontStyle.Bold:          return FontStyles.Bold;
        case FontStyle.Italic:        return FontStyles.Italic;
        case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
      }
      return FontStyles.Normal;
    }

    static string HierarchyPath(Transform t)
    {
      string p = t.name;
      while(t.parent != null) { t = t.parent; p = t.name + "/" + p; }
      return p;
    }

    static string Quote(string s)
    {
      return "'" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "'";
    }

    static void Log(string s)
    {
      Debug.Log(s);
      s_report.AppendLine(s);
    }
  }
}
