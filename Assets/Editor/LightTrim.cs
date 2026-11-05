using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// N17 part B: cut the realtime light count for the WebGL/Chromebook build.
///
/// What the N8 audit measured: 13 Light components live in main.unity (15
/// counting the two that only exist on prefabs), all realtime, and the scene
/// holds three art variants (Viz/Gam/Neu) on separate layers with a light rig
/// duplicated per variant. The active camera's culling mask admits exactly one
/// variant at a time, so most of those lights can never light anything the
/// player sees - but Unity still evaluates them per renderer (light culling,
/// SH probe setup, per-object light lists) every frame.
///
/// QualitySettings.pixelLightCount is already 0, so none of these are per-pixel
/// today. Marking them renderMode = ForceVertex makes that explicit and
/// deterministic (it also survives someone raising pixelLightCount later), and
/// disabling lights whose range intersects no renderer in their own culling
/// mask removes them from the per-object light lists entirely.
///
/// The 13 scene lights all sit inside per-variant art sets (Set_*_Viz/Gam/Neu)
/// and Maintenance.setAllLights has already narrowed each culling mask to its
/// own layer, so "disable when it reaches nothing" is evaluated per layer.
///
/// Safety rule, from the card's "no object turns black" acceptance: we only
/// DISABLE a light when no renderer admitted by its culling mask intersects its
/// range. A light that reaches geometry is demoted, never switched off. One
/// directional light per art variant stays at Important so every set keeps a
/// primary key light.
public static class LightTrim
{
    const string ScenePath = "Assets/Scenes/main.unity";
    const string ReportPath = "tools/light_trim_report.md";

    class Row
    {
        public string name;
        public LightType type;
        public bool enabledBefore, enabledAfter;
        public float intensity, range;
        public int mask, layer;
        public LightRenderMode modeBefore, modeAfter;
        public int litRenderers;
        public string action = "unchanged";
        public bool mainCsReferenced;
    }

    // Main.cs resolves these by name and dereferences them. Never disable the
    // GameObject or remove the component - only the lighting contribution,
    // and only when the audit says it reaches nothing.
    static readonly string[] MainCsOwned = { "Helmet_Light" };

    [MenuItem("Tools/Icecube/Light Trim/Audit")]
    public static void AuditMenu() { Run(false); }

    [MenuItem("Tools/Icecube/Light Trim/Apply")]
    public static void ApplyMenu() { Run(true); }

    /// -executeMethod LightTrim.Audit  (read only, writes the report)
    public static void Audit() { Run(false); EditorApplication.Exit(0); }

    /// -executeMethod LightTrim.Apply  (writes the scene)
    public static void Apply() { Run(true); EditorApplication.Exit(0); }

    static void Run(bool write)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        // --- geometry the lights could possibly hit, with world bounds ---
        var rends = new List<Renderer>();
        foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer))
                    rends.Add(r);

        var lights = new List<Light>();
        foreach (var root in roots)
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                lights.Add(l);

        var rows = new List<Row>();
        foreach (var l in lights)
        {
            var row = new Row
            {
                name = l.gameObject.name,
                type = l.type,
                enabledBefore = l.enabled && l.gameObject.activeInHierarchy,
                intensity = l.intensity,
                range = l.range,
                mask = l.cullingMask,
                layer = l.gameObject.layer,
                modeBefore = l.renderMode,
                mainCsReferenced = MainCsOwned.Contains(l.gameObject.name),
            };
            row.litRenderers = CountLit(l, rends);
            rows.Add(row);
        }

        // One directional light per art-variant layer keeps "Important" so each
        // set retains a primary key light; everything else goes vertex.
        var keepImportant = new HashSet<Light>();
        foreach (var grp in lights.Where(l => l.type == LightType.Directional &&
                                              l.enabled && l.gameObject.activeInHierarchy)
                                  .GroupBy(l => l.gameObject.layer))
            keepImportant.Add(grp.OrderByDescending(l => l.intensity).First());

        int demoted = 0, disabled = 0, kept = 0;
        for (int i = 0; i < lights.Count; i++)
        {
            var l = lights[i];
            var row = rows[i];

            if (!row.enabledBefore)
            {
                row.action = "already off";
                row.enabledAfter = false;
                row.modeAfter = row.modeBefore;
                continue;
            }

            // Reaches no geometry its own culling mask admits -> pure overhead.
            if (row.litRenderers == 0 && l.type != LightType.Directional)
            {
                row.action = "disabled (range intersects no renderer in mask)";
                row.enabledAfter = false;
                row.modeAfter = LightRenderMode.ForceVertex;
                if (write) { l.enabled = false; l.renderMode = LightRenderMode.ForceVertex; }
                disabled++;
                continue;
            }

            row.enabledAfter = true;
            if (keepImportant.Contains(l))
            {
                // Auto, NOT ForcePixel: ForcePixel would override the
                // pixelLightCount=0 quality setting and ADD a per-pixel light
                // to every Standard forward-base pass. Auto keeps the light as
                // the scene's primary ("Important") directional while still
                // honouring the Low quality level, so this change can only
                // reduce cost, never raise it.
                row.action = "kept primary directional for layer " + l.gameObject.layer + " (Auto)";
                row.modeAfter = LightRenderMode.Auto;
                if (write) l.renderMode = LightRenderMode.Auto;
                kept++;
            }
            else
            {
                row.action = "Not Important (ForceVertex)";
                row.modeAfter = LightRenderMode.ForceVertex;
                if (write) l.renderMode = LightRenderMode.ForceVertex;
                demoted++;
            }
        }

        foreach (var r in rows)
            Debug.Log(string.Format("LIGHTROW {0}|{1}|{2}|int={3}|range={4}|mask={5}|lit={6}|{7}->{8}|{9}",
                r.name, r.type, r.enabledBefore ? "ON " : "off", r.intensity, r.range,
                r.mask, r.litRenderers, r.modeBefore, r.modeAfter, r.action));

        Debug.Log("LIGHTTRIM lights=" + rows.Count + " demoted=" + demoted +
                  " disabled=" + disabled + " kept_important=" + kept +
                  " enabled_after=" + rows.Count(r => r.enabledAfter));

        WriteReport(rows, demoted, disabled, kept);

        if (write)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("LIGHTTRIM_DONE scene saved");
        }
        else Debug.Log("LIGHTTRIM_AUDIT_DONE (no writes)");
    }

    /// Renderers that this light can actually affect: layer admitted by the
    /// light's culling mask AND bounds within range (directional = infinite).
    static int CountLit(Light l, List<Renderer> rends)
    {
        int n = 0;
        var pos = l.transform.position;
        foreach (var r in rends)
        {
            if ((l.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (l.type == LightType.Directional) { n++; continue; }
            var b = r.bounds;
            // Distance from the light to the renderer's AABB.
            var c = b.ClosestPoint(pos);
            if ((c - pos).sqrMagnitude <= l.range * l.range) n++;
        }
        return n;
    }

    static void WriteReport(List<Row> rows, int demoted, int disabled, int kept)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# N17 light trim report");
        sb.AppendLine();
        sb.AppendLine("Generated by `LightTrim.Audit` / `LightTrim.Apply` (Unity batchmode).");
        sb.AppendLine("`lit` = renderers whose layer the light's culling mask admits AND whose");
        sb.AppendLine("bounds fall inside the light's range. `lit = 0` on a point light means the");
        sb.AppendLine("light costs per-frame work and lights nothing, so it is disabled.");
        sb.AppendLine();
        sb.AppendLine("UTC: " + DateTime.UtcNow.ToString("o"));
        sb.AppendLine();
        sb.AppendLine("Totals: lights=" + rows.Count +
                      " demoted_to_vertex=" + demoted +
                      " disabled=" + disabled +
                      " kept_important=" + kept +
                      " enabled_after=" + rows.Count(r => r.enabledAfter));
        sb.AppendLine();
        sb.AppendLine("| light | type | layer | was | intensity | range | mask | lit | render mode | action |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine("| " + r.name + " | " + r.type + " | " + r.layer + " | " +
                          (r.enabledBefore ? "on" : "off") + " | " + r.intensity + " | " +
                          r.range + " | " + r.mask + " | " + r.litRenderers + " | " +
                          r.modeBefore + " -> " + r.modeAfter + " | " + r.action +
                          (r.mainCsReferenced ? " (Main.cs holds this object)" : "") + " |");
        sb.AppendLine();
        sb.AppendLine("## Revert");
        sb.AppendLine();
        sb.AppendLine("Light changes live in a single commit touching only `Assets/Scenes/main.unity`.");
        sb.AppendLine("If the measured frame-time gain is < 1 ms (card kill criterion), revert that");
        sb.AppendLine("commit and keep the material swaps, which are a separate commit.");

        Directory.CreateDirectory("tools");
        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log("LIGHTTRIM_REPORT " + ReportPath);
    }
}
