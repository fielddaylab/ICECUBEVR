using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Builds the pixel-crisp overlay reticle (card N9).
///
/// Two entry points, both idempotent and both runnable from batch mode:
///   icecube/Reticle/Generate Sprites       -> GenerateSprites()
///   icecube/Reticle/Setup Scene Reticle    -> SetupScene()
///   (batch)                                -> Run()  does both, then saves.
///
/// Why procedural sprites: the three shapes are exact circles with known radii,
/// so generating them from the radii in this file keeps the art and the
/// documented geometry from drifting, and the result is byte-identical on every
/// run (fixed loops, no randomness, no time/GUID input).
public class ReticleCanvasSetup
{
    // ---- geometry, in 48 px on-screen units (see tools/reticle.md) ----
    const int   RETICLE_PX       = 48;   // on-screen box for ring + outline
    const int   SS               = 2;    // sprite supersample: 96x96 texture
    const float RING_OUTER       = 21f;  // white ring outer radius
    const float RING_THICK       = 3f;   // white ring thickness
    const float OUTLINE_PAD      = 1.5f; // black halo on each side of the ring
    const int   DOT_PX           = 8;    // on-screen centre dot diameter
    const int   AA               = 4;    // coverage samples per axis per pixel

    const string DIR             = "Assets/UI/Reticle";
    const string RING_PATH       = DIR + "/reticle_ring.png";
    const string OUTLINE_PATH    = DIR + "/reticle_outline.png";
    const string DOT_PATH        = DIR + "/reticle_dot.png";

    const string SCENE_PATH      = "Assets/Scenes/main.unity";
    const string CANVAS_NAME     = "UI_Canvas";   // the N4 overlay canvas
    const string RETICLE_NAME    = "Reticle";

    // The old camera-locked quads. Kept in the scene (Main.cs reads their
    // transforms for gaze math) but no longer rendered.
    static readonly string[] LEGACY_QUADS = { "Cam_Reticle", "Cam_Spinner_Reticle" };

    [MenuItem("icecube/Reticle/Generate Sprites")]
    public static void GenerateSprites()
    {
        Directory.CreateDirectory(DIR);

        int size = RETICLE_PX * SS;
        float c = size * 0.5f;

        // White ring: annulus [outer-thick, outer].
        WriteRing(RING_PATH, size, c,
                  (RING_OUTER - RING_THICK) * SS, RING_OUTER * SS, Color.white);

        // Black outline: annulus that encloses the white ring with OUTLINE_PAD
        // on both sides, drawn underneath it.
        WriteRing(OUTLINE_PATH, size, c,
                  (RING_OUTER - RING_THICK - OUTLINE_PAD) * SS,
                  (RING_OUTER + OUTLINE_PAD) * SS, Color.black);

        // Centre dot gets its own tight texture: a 96x96 sheet shown in an
        // 8x8 rect would scale the disc down 12x and vanish.
        int dsize = DOT_PX * SS;
        WriteRing(DOT_PATH, dsize, dsize * 0.5f, -1f, dsize * 0.5f, Color.white);

        AssetDatabase.Refresh();
        Debug.Log("RETICLE sprites=3 dir=" + DIR + " size=" + size + "x" + size);
    }

    /// Writes an anti-aliased annulus (inner<0 => filled disc) as an RGBA PNG
    /// and applies the Sprite importer settings the UI needs.
    static void WriteRing(string path, int size, float c, float inner, float outer, Color rgb)
    {
        var px = new Color32[size * size];
        float step = 1f / AA;
        float half = step * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < AA; sy++)
                {
                    for (int sx = 0; sx < AA; sx++)
                    {
                        float fx = x + half + sx * step - c;
                        float fy = y + half + sy * step - c;
                        float r = Mathf.Sqrt(fx * fx + fy * fy);
                        if (r <= outer && (inner < 0f || r >= inner)) hits++;
                    }
                }
                byte a = (byte)Mathf.RoundToInt(255f * hits / (AA * AA));
                // Premultiply-safe: keep RGB constant so bilinear taps on the
                // transparent side never bleed a different hue into the edge.
                px[y * size + x] = new Color32((byte)(rgb.r * 255f),
                                               (byte)(rgb.g * 255f),
                                               (byte)(rgb.b * 255f), a);
            }
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false, false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null)
        {
            Debug.LogError("RETICLE importer missing for " + path);
            return;
        }
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 100f;
        imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.alphaIsTransparency = true;
        imp.sRGBTexture = true;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        imp.SetTextureSettings(settings);
        imp.SaveAndReimport();
    }

    [MenuItem("icecube/Reticle/Setup Scene Reticle")]
    public static void SetupSceneMenu()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != SCENE_PATH)
            scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
        SetupScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// Batch entry point: sprites, then scene, then save.
    public static void Run()
    {
        GenerateSprites();
        var scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
        SetupScene();
        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        Debug.Log("RETICLE_RESULT saved=" + ok);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void SetupScene()
    {
        var canvas = FindOverlayCanvas();
        var reticle = BuildReticle(canvas);
        MuteLegacyQuads();
        Debug.Log("RETICLE scene ok canvas=" + canvas.name + " reticle=" + reticle.name);
    }

    /// Finds the single Screen Space Overlay canvas. Never creates a second one:
    /// an existing overlay canvas (card N4's, whatever its name) always wins.
    static Canvas FindOverlayCanvas()
    {
        var overlays = new System.Collections.Generic.List<Canvas>();
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                if (c.renderMode == RenderMode.ScreenSpaceOverlay) overlays.Add(c);

        if (overlays.Count > 1) return Dedupe(overlays);
        if (overlays.Count == 1) return overlays[0];

        // N4 has not landed on this branch yet. Create the canonical canvas
        // under the N4 name and settings so the two cards converge on one
        // object instead of racing to add a second overlay canvas.
        Debug.LogWarning("RETICLE no overlay canvas found; creating '" + CANVAS_NAME +
                         "' with the N4 contract (1280x720 reference, ScaleWithScreenSize)");
        var go = new GameObject(CANVAS_NAME, typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        canvas.sortingOrder = 0;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;   // match height: 48 px stays 48 px at 720p
        scaler.referencePixelsPerUnit = 100f;
        return canvas;
    }


    /// Collapses several Screen Space Overlay canvases into one.
    ///
    /// This exists for the N4 merge: if this card is built on a branch where
    /// N4 has not landed, SetupScene() creates `UI_Canvas` itself (below), and
    /// after the merge the scene can briefly hold two overlay canvases. Two
    /// overlay canvases are two extra UI batches and a nondeterministic draw
    /// order, so re-running `icecube/Reticle/Setup Scene Reticle` folds them.
    ///
    /// Keeper is chosen deterministically: the canvas carrying the captions
    /// (anything with a `CaptionSink`) wins, else the one named `UI_Canvas`,
    /// else the one with the most children, else alphabetical. Children are
    /// moved with their world-space layout preserved off (worldPositionStays =
    /// false) because these are anchored RectTransforms.
    static Canvas Dedupe(System.Collections.Generic.List<Canvas> overlays)
    {
        overlays.Sort(delegate(Canvas a, Canvas b)
        {
            int ca = Score(a), cb = Score(b);
            if (ca != cb) return cb - ca;
            int ka = a.transform.childCount, kb = b.transform.childCount;
            if (ka != kb) return kb - ka;
            return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
        });
        var keep = overlays[0];
        for (int i = 1; i < overlays.Count; i++)
        {
            var dup = overlays[i];
            if (dup == keep) continue;
            var kids = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in dup.transform) kids.Add(t);
            foreach (var t in kids) t.SetParent(keep.transform, false);
            Debug.Log("RETICLE folded duplicate overlay canvas '" + dup.name +
                      "' (" + kids.Count + " children) into '" + keep.name + "'");
            Object.DestroyImmediate(dup.gameObject);
        }
        return keep;
    }

    /// 2 = holds the captions, 1 = canonical name, 0 = neither.
    static int Score(Canvas c)
    {
        foreach (var mb in c.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == "CaptionSink") return 2;
        return c.name == CANVAS_NAME ? 1 : 0;
    }

    static GameObject BuildReticle(Canvas canvas)
    {
        var existing = canvas.transform.Find(RETICLE_NAME);
        var root = existing != null ? existing.gameObject
                                    : new GameObject(RETICLE_NAME, typeof(RectTransform));
        if (existing == null) root.transform.SetParent(canvas.transform, false);
        root.layer = canvas.gameObject.layer;
        // Last sibling: deterministic draw order above the captions.
        root.transform.SetAsLastSibling();

        var rt = root.GetComponent<RectTransform>();
        Centre(rt, RETICLE_PX, RETICLE_PX);

        var group = Need<CanvasGroup>(root);
        group.alpha = 0.5f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var outline = Part(rt, "Outline", OUTLINE_PATH, RETICLE_PX, RETICLE_PX, Color.black);
        var ring    = Part(rt, "Ring",    RING_PATH,    RETICLE_PX, RETICLE_PX, Color.white);
        var fill    = Part(rt, "Fill",    RING_PATH,    RETICLE_PX, RETICLE_PX, Color.white);
        var dot     = Part(rt, "Dot",     DOT_PATH,     DOT_PX,     DOT_PX,     Color.white);

        // The dwell gauge: same ring geometry, radially wiped from the top.
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 0f;

        var view = Need<ReticleView>(root);
        view.outline = outline;
        view.ring = ring;
        view.fill = fill;
        view.dot = dot;
        view.group = group;

        // Draw order within the reticle: outline, ring, fill, dot.
        outline.transform.SetSiblingIndex(0);
        ring.transform.SetSiblingIndex(1);
        fill.transform.SetSiblingIndex(2);
        dot.transform.SetSiblingIndex(3);

        root.SetActive(true);
        return root;
    }

    static Image Part(RectTransform parent, string name, string spritePath,
                      int w, int h, Color color)
    {
        var t = parent.Find(name);
        var go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform));
        if (t == null) go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        Centre(go.GetComponent<RectTransform>(), w, h);

        var img = Need<Image>(go);
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (img.sprite == null) Debug.LogError("RETICLE missing sprite " + spritePath);
        img.color = color;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.maskable = false;
        go.SetActive(true);
        return img;
    }

    static void Centre(RectTransform rt, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    static T Need<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    /// Turn off the MeshRenderers on the legacy reticle quads.
    ///
    /// NOTE (measured, card N9): Cam_Reticle and Cam_Spinner_Reticle are
    /// MeshRenderer quads on layer 31 (Set_AR_Project), NOT Cameras — the card
    /// text calls them cameras but the scene says otherwise. There is therefore
    /// no camera to disable here and no camera-count saving from this card; see
    /// tools/reticle.md. The GameObjects stay alive because Main.cs uses
    /// cam_reticle.transform.position as the gaze pointer for all six triggers.
    static void MuteLegacyQuads()
    {
        foreach (var name in LEGACY_QUADS)
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.LogWarning("RETICLE legacy quad not found: " + name); continue; }
            foreach (var r in go.GetComponents<Renderer>())
            {
                r.enabled = false;
                Debug.Log("RETICLE muted renderer on " + name);
            }
            var cam = go.GetComponent<Camera>();
            if (cam != null)
            {
                // Kill criterion from the card: if anything reads this camera's
                // projection, neutralise with cullingMask 0 instead of
                // disabling so the matrices stay live. Main.cs reads only
                // transforms today (tools/reticle.md), so we also disable.
                cam.cullingMask = 0;
                cam.enabled = false;
                Debug.Log("RETICLE disabled camera on " + name);
            }
        }
    }
}
