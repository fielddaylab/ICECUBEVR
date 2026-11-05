using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// N17 part A: swap Standard-shader materials to cheap, look-neutral shaders.
///
/// Why this is worth doing on a Chromebook: the Standard shader is a
/// metallic-workflow PBR fragment program (BRDF + GGX specular + indirect
/// specular + per-pixel light loop). On Mali/UHD class parts the scene is
/// fill-bound, so the per-pixel program is the cost, not the triangles. A
/// material that sits at Unity's DEFAULTS (metallic 0, smoothness 0.5, no
/// normal/metallic/parallax/occlusion map, no emission) is paying for a
/// specular lobe nobody authored. Swapping those to Mobile/Diffuse or
/// Legacy Diffuse keeps the albedo and the diffuse response but drops the
/// specular/IBL math. Materials that are *visually unlit* (emissive-only
/// glows, skybox-facing quads, UI quads) are swapped to Unlit/*, which also
/// removes the light loop entirely.
///
/// IMPORTANT guard rails (owner decision C10):
///   - ONLY default-lighting materials get the diffuse swap. Anything with an
///     authored metallic/smoothness, a normal map, or a cutout mode is left
///     alone and reported as CLASS_REVIEW for the owner.
///   - Nothing may turn black: for every swap we carry over _MainTex (with
///     scale/offset) and the albedo colour. When the target shader cannot
///     express the colour (Mobile/Diffuse has no _Color) we use the Legacy
///     Diffuse variant that can, instead of dropping the tint.
///   - Backups go OUTSIDE the project (/home/watson/icecube-backups/materials
///     by default). Writing *.bak under Assets/ would make Unity import the
///     backup as a second real asset.
public static class ShaderTrim
{
    const string ScenePath = "Assets/Scenes/main.unity";
    const string ReportPath = "tools/shader_trim_report.md";
    const string DefaultBackupRoot = "/home/watson/icecube-backups/materials";

    // Materials under these roots ship with third-party/VR packages that the
    // WebGL build never renders. Never touch them.
    static readonly string[] ExcludedRoots =
    {
        "Assets/Oculus/", "Assets/OSPNative/", "Assets/GoogleAnalytics/",
        "Assets/PlayServicesResolver/", "Assets/Plugins/",
    };

    const float Eps = 0.001f;

    public enum Cls { Skip, Diffuse, DiffuseTint, UnlitTex, UnlitColor, Review }

    public class Entry
    {
        public string path;
        public string guid;
        public string shader;
        public Cls cls;
        public string target;      // shader name we would move to
        public string reason;
        public float metallic, smoothness, mode;
        public bool mainTex, bumpMap, metallicGloss, parallax, occlusion, emission;
        public Color color = Color.white, emissionColor = Color.black;
        public int renderQueue;
        public List<string> users = new List<string>();
        public bool inScene;
    }

    // ---------------------------------------------------------------- menu

    [MenuItem("Tools/Icecube/Shader Trim/Audit")]
    public static void AuditMenu() { var e = Classify(); WriteReport(e); }

    [MenuItem("Tools/Icecube/Shader Trim/Backup + Apply")]
    public static void ApplyMenu()
    {
        var e = Classify();
        Backup(e);
        ApplySwaps(e);
        WriteReport(e);
    }

    // ------------------------------------------------------------- batch

    /// -executeMethod ShaderTrim.Audit  (read only)
    public static void Audit()
    {
        var entries = Classify();
        WriteReport(entries);
        LogSummary("TRIM_AUDIT", entries);
        EditorApplication.Exit(0);
    }

    /// -executeMethod ShaderTrim.Apply  (backs up first, then writes)
    public static void Apply()
    {
        var entries = Classify();
        string dir = Backup(entries);
        int swapped = ApplySwaps(entries);
        WriteReport(entries);
        Debug.Log("TRIM_BACKUP dir=" + dir);
        LogSummary("TRIM_APPLY", entries);
        Debug.Log("TRIM_DONE swapped=" + swapped);
        EditorApplication.Exit(0);
    }

    static void LogSummary(string tag, List<Entry> entries)
    {
        foreach (Cls c in Enum.GetValues(typeof(Cls)))
            Debug.Log(tag + " " + c + "=" + entries.Count(e => e.cls == c));
        Debug.Log(tag + " standard_materials=" + entries.Count +
                  " in_scene=" + entries.Count(e => e.inScene));
    }

    // --------------------------------------------------------- collection

    /// Every material actually rendered by the build: scene renderers,
    /// particle renderers, skybox + RenderSettings, plus prefabs that Main.cs
    /// instantiates at runtime (grid bulbs, labels, AR widgets).
    static Dictionary<Material, List<string>> CollectUsed()
    {
        var used = new Dictionary<Material, List<string>>();

        void Add(Material m, string who)
        {
            if (m == null) return;
            List<string> l;
            if (!used.TryGetValue(m, out l)) { l = new List<string>(); used[m] = l; }
            if (!l.Contains(who)) l.Add(who);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    Add(m, "scene:" + r.gameObject.name + "@L" + r.gameObject.layer);
            foreach (var sb in root.GetComponentsInChildren<Skybox>(true))
                Add(sb.material, "skybox:" + sb.gameObject.name);
        }
        Add(RenderSettings.skybox, "RenderSettings.skybox");

        // Prefabs: only those the scene actually references (prefab instances
        // or serialized prefab fields on Main.cs, e.g. the AR label / grid
        // bulb / star prefabs it Instantiates at runtime) count as "in scene".
        // Materials that live only on orphan prefabs (demo scenes, unused
        // art) are still reported, but never swapped - there is nothing to
        // gain and nothing to screenshot.
        string sceneText = File.ReadAllText(ScenePath);
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Excluded(p)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            bool referenced = sceneText.Contains(guid);
            string tag = (referenced ? "prefab:" : "prefab(unreferenced):") + Path.GetFileName(p);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    Add(m, tag);
        }
        return used;
    }

    static bool Excluded(string path)
    {
        foreach (var root in ExcludedRoots)
            if (path.StartsWith(root, StringComparison.Ordinal)) return true;
        return false;
    }

    static bool HasTex(Material m, string prop)
    {
        return m.HasProperty(prop) && m.GetTexture(prop) != null;
    }

    static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

    static bool IsWhite(Color c)
    {
        return Mathf.Abs(c.r - 1f) <= 0.02f && Mathf.Abs(c.g - 1f) <= 0.02f &&
               Mathf.Abs(c.b - 1f) <= 0.02f;
    }

    // ------------------------------------------------------ classification

    public static List<Entry> Classify()
    {
        var used = CollectUsed();
        var entries = new List<Entry>();

        foreach (var kv in used)
        {
            var m = kv.Key;
            string path = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat")) continue;
            if (Excluded(path)) continue;

            var e = new Entry
            {
                path = path,
                guid = AssetDatabase.AssetPathToGUID(path),
                shader = m.shader != null ? m.shader.name : "<null>",
                renderQueue = m.renderQueue,
                users = kv.Value,
                inScene = kv.Value.Any(u => u.StartsWith("scene:") || u.StartsWith("skybox:") ||
                                            u.StartsWith("prefab:") || u.StartsWith("RenderSettings")),
            };

            if (e.shader != "Standard" && e.shader != "Standard (Specular setup)")
            {
                e.cls = Cls.Skip;
                e.reason = "not a Standard material";
                entries.Add(e);
                continue;
            }

            e.metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            e.smoothness = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            e.mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
            e.mainTex = HasTex(m, "_MainTex");
            e.bumpMap = HasTex(m, "_BumpMap") || HasTex(m, "_DetailNormalMap");
            e.metallicGloss = HasTex(m, "_MetallicGlossMap") || HasTex(m, "_SpecGlossMap");
            e.parallax = HasTex(m, "_ParallaxMap");
            e.occlusion = HasTex(m, "_OcclusionMap");
            e.emission = m.IsKeywordEnabled("_EMISSION");
            if (m.HasProperty("_Color")) e.color = m.GetColor("_Color");
            if (m.HasProperty("_EmissionColor")) e.emissionColor = m.GetColor("_EmissionColor");

            bool extraMaps = e.bumpMap || e.metallicGloss || e.parallax || e.occlusion;
            bool transparentish = Mathf.Approximately(e.mode, 2f) || Mathf.Approximately(e.mode, 3f);

            // ---- visually unlit: emission dominates the albedo response ----
            // These read as self-lit glows (black holes, wisps, milky-way
            // planes, logo quads). The light loop contributes nothing the
            // viewer can see, so Unlit/* is look-neutral AND the cheapest
            // fragment program available.
            if (e.emission && !extraMaps &&
                Lum(e.emissionColor) >= Lum(e.color) - Eps && Lum(e.emissionColor) > 0f)
            {
                if (e.mainTex && IsWhite(e.color))
                {
                    e.cls = Cls.UnlitTex;
                    e.target = transparentish ? "Unlit/Transparent" : "Unlit/Texture";
                    e.reason = "emissive-only (emission lum >= albedo lum), white tint, _MainTex";
                    entries.Add(e);
                    continue;
                }
                if (!e.mainTex)
                {
                    e.cls = Cls.UnlitColor;
                    e.target = "Unlit/Color";
                    e.reason = "emissive-only, no albedo map -> flat colour";
                    entries.Add(e);
                    continue;
                }
                // Tinted + textured emissive: Unlit/Texture cannot express the
                // tint, so leave it for the owner rather than shifting colour.
                e.cls = Cls.Review;
                e.reason = "emissive but tinted albedo map; Unlit/* would drop the tint";
                entries.Add(e);
                continue;
            }

            // ---- default lighting params: specular lobe is unauthored ----
            bool defaults = Mathf.Abs(e.metallic) <= Eps &&
                            Mathf.Abs(e.smoothness - 0.5f) <= Eps &&
                            !extraMaps && !e.emission && Mathf.Approximately(e.mode, 0f);
            if (defaults)
            {
                if (IsWhite(e.color))
                {
                    e.cls = Cls.Diffuse;
                    e.target = "Mobile/Diffuse";
                    e.reason = "metallic 0, smoothness 0.5, no maps, opaque, white tint";
                }
                else
                {
                    // Mobile/Diffuse has no _Color property; using it would
                    // discard the tint and the object could read as flat white
                    // (or black, if the texture is a mask). Legacy Diffuse is
                    // the same Lambert cost class and keeps _Color.
                    e.cls = Cls.DiffuseTint;
                    e.target = "Legacy Shaders/Diffuse";
                    e.reason = "default lighting params but tinted albedo; keep _Color";
                }
                entries.Add(e);
                continue;
            }

            e.cls = Cls.Review;
            e.reason = "authored " + (extraMaps ? "maps" : "metallic/smoothness") +
                       " (metallic=" + e.metallic.ToString("0.###") +
                       " smoothness=" + e.smoothness.ToString("0.###") +
                       " mode=" + e.mode.ToString("0") + ")";
            entries.Add(e);
        }

        entries.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.Ordinal));
        return entries;
    }

    static bool Swappable(Entry e)
    {
        // Only materials the build can actually draw are rewritten.
        return e.inScene &&
               (e.cls == Cls.Diffuse || e.cls == Cls.DiffuseTint ||
                e.cls == Cls.UnlitTex || e.cls == Cls.UnlitColor);
    }

    // ------------------------------------------------------------ backup

    static string BackupRoot()
    {
        string env = Environment.GetEnvironmentVariable("ICECUBE_MATERIAL_BACKUP_ROOT");
        return string.IsNullOrEmpty(env) ? DefaultBackupRoot : env;
    }

    /// Copies every material we are about to rewrite (plus its .meta) outside
    /// the project, with a manifest. Returns the backup directory.
    public static string Backup(List<Entry> entries)
    {
        var targets = entries.Where(Swappable).ToList();
        string dir = Path.Combine(BackupRoot(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-N17");
        Directory.CreateDirectory(dir);

        var manifest = new StringBuilder();
        manifest.Append("{\n  \"card\": \"N17\",\n  \"utc\": \"")
                .Append(DateTime.UtcNow.ToString("o")).Append("\",\n  \"materials\": [\n");

        for (int i = 0; i < targets.Count; i++)
        {
            var e = targets[i];
            string dst = Path.Combine(dir, e.path);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(e.path, dst, true);
            if (File.Exists(e.path + ".meta")) File.Copy(e.path + ".meta", dst + ".meta", true);

            manifest.Append("    {\"path\": \"").Append(e.path.Replace("\"", "\\\""))
                    .Append("\", \"guid\": \"").Append(e.guid)
                    .Append("\", \"shader\": \"").Append(e.shader)
                    .Append("\", \"target\": \"").Append(e.target)
                    .Append("\", \"class\": \"").Append(e.cls)
                    .Append("\", \"bytes\": ").Append(new FileInfo(e.path).Length)
                    .Append("}").Append(i + 1 < targets.Count ? ",\n" : "\n");
        }
        manifest.Append("  ]\n}\n");
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest.ToString());
        Debug.Log("TRIM_BACKUP materials=" + targets.Count + " dir=" + dir);
        return dir;
    }

    // ------------------------------------------------------------- apply

    public static int ApplySwaps(List<Entry> entries)
    {
        int n = 0;
        foreach (var e in entries.Where(Swappable))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(e.path);
            if (m == null) { Debug.LogWarning("TRIM missing " + e.path); continue; }
            var shader = Shader.Find(e.target);
            if (shader == null)
            {
                Debug.LogWarning("TRIM shader not found: " + e.target + " for " + e.path);
                continue;
            }

            // Snapshot what has to survive the swap so nothing renders black.
            Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            Color albedo = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Color emis = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            int queue = m.renderQueue;

            m.shader = shader;
            m.shaderKeywords = new string[0];

            if (m.HasProperty("_MainTex") && tex != null)
            {
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_MainTex", scale);
                m.SetTextureOffset("_MainTex", offset);
            }
            if (m.HasProperty("_Color"))
            {
                // Unlit/Color on an emissive material: the visible colour was
                // the emission, not the albedo. Take the brighter of the two
                // so a black-albedo glow does not become a black quad.
                Color c = (e.cls == Cls.UnlitColor && Lum(emis) > Lum(albedo)) ? emis : albedo;
                c.a = albedo.a;
                m.SetColor("_Color", c);
            }
            // Transparent/fade materials carry an explicit queue; Unlit/*
            // declares Transparent anyway, but keep an authored override.
            m.renderQueue = queue >= 0 ? queue : -1;

            EditorUtility.SetDirty(m);
            n++;
            Debug.Log("TRIM_SWAP " + e.cls + " " + e.path + " -> " + e.target);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("TRIM_SWAPPED count=" + n);
        return n;
    }

    // ------------------------------------------------------------ report

    public static void WriteReport(List<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# N17 shader trim report");
        sb.AppendLine();
        sb.AppendLine("Generated by `ShaderTrim.Audit` (Unity batchmode). Scope: materials");
        sb.AppendLine("referenced by renderers/skybox in `Assets/Scenes/main.unity` plus the");
        sb.AppendLine("runtime-instantiated prefabs (third-party/VR package materials excluded).");
        sb.AppendLine();
        sb.AppendLine("UTC: " + DateTime.UtcNow.ToString("o"));
        sb.AppendLine();

        sb.AppendLine("## Shader histogram (materials in scope)");
        sb.AppendLine();
        sb.AppendLine("| shader | materials |");
        sb.AppendLine("|---|---|");
        foreach (var g in entries.GroupBy(e => e.shader).OrderByDescending(g => g.Count()))
            sb.AppendLine("| " + g.Key + " | " + g.Count() + " |");
        sb.AppendLine();

        sb.AppendLine("## Classification");
        sb.AppendLine();
        sb.AppendLine("| class | materials | action |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| CLASS_DIFFUSE | " + entries.Count(e => e.cls == Cls.Diffuse) + " | -> Mobile/Diffuse |");
        sb.AppendLine("| CLASS_DIFFUSE_TINT | " + entries.Count(e => e.cls == Cls.DiffuseTint) + " | -> Legacy Shaders/Diffuse (keeps _Color) |");
        sb.AppendLine("| CLASS_UNLIT | " + entries.Count(e => e.cls == Cls.UnlitTex || e.cls == Cls.UnlitColor) + " | -> Unlit/Texture, Unlit/Transparent, Unlit/Color |");
        sb.AppendLine("| CLASS_REVIEW | " + entries.Count(e => e.cls == Cls.Review) + " | NOT swapped - owner decision C10 |");
        sb.AppendLine("| CLASS_SKIP | " + entries.Count(e => e.cls == Cls.Skip) + " | already non-Standard |");
        sb.AppendLine();

        foreach (Cls c in new[] { Cls.Diffuse, Cls.DiffuseTint, Cls.UnlitTex, Cls.UnlitColor, Cls.Review })
        {
            var rows = entries.Where(e => e.cls == c).ToList();
            if (rows.Count == 0) continue;
            sb.AppendLine("### " + c + " (" + rows.Count + ")");
            sb.AppendLine();
            sb.AppendLine("| material | metallic | smoothness | mode | maintex | emission | target | reason | in scene |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var e in rows)
                sb.AppendLine("| " + e.path + " | " + e.metallic.ToString("0.###") + " | " +
                              e.smoothness.ToString("0.###") + " | " + e.mode.ToString("0") + " | " +
                              (e.mainTex ? "yes" : "no") + " | " + (e.emission ? "yes" : "no") + " | " +
                              (string.IsNullOrEmpty(e.target) ? "-" : e.target) + " | " + e.reason + " | " +
                              (e.inScene ? "yes" : "orphan prefab only (not swapped)") + " |");
            sb.AppendLine();
        }

        sb.AppendLine("## Users (swapped materials only)");
        sb.AppendLine();
        foreach (var e in entries.Where(Swappable))
            sb.AppendLine("- `" + e.path + "` -> " + e.target + ": " +
                          string.Join(", ", e.users.Take(8).ToArray()) +
                          (e.users.Count > 8 ? " (+" + (e.users.Count - 8) + " more)" : ""));
        sb.AppendLine();

        Directory.CreateDirectory("tools");
        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log("TRIM_REPORT " + ReportPath + " entries=" + entries.Count);
    }
}
