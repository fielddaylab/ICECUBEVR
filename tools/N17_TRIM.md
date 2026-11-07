# N17 — Shader and light trim (Mali/UHD fragment cost)

Two independently revertable changes, so the card's kill criterion can drop
the light changes alone:

| commit | touches | tool | revert |
|---|---|---|---|
| A. material swaps | `Assets/**/*.mat` (4 files), `tools/shader_trim_report.md` | `ShaderTrim` | `tools/restore_materials.sh` (copies the backup set back) |
| B. light demotion | `Assets/Scenes/main.unity` only | `LightTrim` | `git revert <commit B>` |

Reports: `tools/shader_trim_report.md`, `tools/light_trim_report.md`.
Backups (never under `Assets/`): `/home/watson/icecube-backups/materials/<UTC>-N17/`.

## What was swapped (part A)

Only materials the build actually draws, and only where the swap is
look-neutral by construction:

* **CLASS_UNLIT → Unlit/Transparent** — emissive-only Standard materials
  (emission colour luminance ≥ albedo luminance, white tint, no normal/
  metallic/parallax/occlusion map). The light loop contributed nothing the
  viewer could see. `Black hole overlay`, `Black hole stars`, `bf`.
* **CLASS_DIFFUSE_TINT → Legacy Shaders/Diffuse** — metallic 0, smoothness
  0.5 (Unity defaults), opaque, no maps, but a non-white `_Color`.
  `Mobile/Diffuse` has no `_Color`, so the tint would be lost (door quads
  could go flat white); Legacy Diffuse is the same Lambert cost class and
  keeps it. `Materials/White tex`.
* **CLASS_DIFFUSE → Mobile/Diffuse** — defaults + white tint. None in scene.
* **UI-layer rule** — a Standard material whose scene renderers all sit on
  the UI layer (5) is treated as visually unlit. None in scene today; the
  rule is there so a future UI quad on Standard gets caught by the audit.

Everything else on Standard is **CLASS_REVIEW** (31 materials, 15 in scene):
authored metallic/smoothness, normal maps, tinted emissive textures. Per
owner decision C10 they are **not** swapped; the tool produces before/after
screenshot pairs with a temporary in-memory swap instead (below).

## Lights (part B)

`QualitySettings.pixelLightCount` is already 0 for the WebGL quality level, so
no light was per-pixel; the cost is per-object light-list culling and vertex
lighting for 18 enabled realtime lights spread across three art-variant
layers (Viz/Gam/Neu). `LightTrim.Apply`:

* keeps one directional light per variant layer at **Auto** (the scene's
  "Important" key light — *not* ForcePixel, which would override
  pixelLightCount and add cost);
* sets every other enabled light to **Not Important (ForceVertex)**;
* **disables** a light only when no renderer admitted by its culling mask lies
  within its range (`lit = 0`). The audit found none — every point light
  reaches ≥ 2 renderers — so nothing was switched off and nothing can turn
  black from this step;
* never touches `Helmet_Light` beyond mode (Main.cs dereferences it).

## Commands (Unity 2019.4.40f1 container, headless)

```sh
OUT=/home/watson/icecube-out-n17; mkdir -p $OUT
RUN="docker run --rm --name icecube-unity-n17 \
  -v $PWD:/project -v $OUT:/out \
  -v /home/watson/icecube-backups:/home/watson/icecube-backups \
  -v icecube-unity-license-b:/root/.local/share/unity3d \
  unityci/editor:ubuntu-2019.4.40f1-webgl-3 unity-editor \
  -quit -batchmode -silent-crashes -projectPath /project -buildTarget WebGL"

# audit only (writes tools/shader_trim_report.md)
nohup $RUN -nographics -executeMethod ShaderTrim.Audit -logFile /out/audit.log &
# backup + apply the look-neutral swaps
nohup $RUN -nographics -executeMethod ShaderTrim.Apply -logFile /out/shader_apply.log &
# light audit / apply (writes tools/light_trim_report.md; Apply saves main.unity)
nohup $RUN -nographics -executeMethod LightTrim.Audit -logFile /out/light_audit.log &
nohup $RUN -nographics -executeMethod LightTrim.Apply -logFile /out/light_apply.log &
# C10 review shots: needs a GPU/GL context, so NO -nographics, under xvfb
nohup docker run --rm --name icecube-unity-n17 -v $PWD:/project -v $OUT:/out \
  -v /home/watson/icecube-backups:/home/watson/icecube-backups \
  -v icecube-unity-license-b:/root/.local/share/unity3d \
  unityci/editor:ubuntu-2019.4.40f1-webgl-3 \
  xvfb-run -a unity-editor -quit -batchmode -silent-crashes -projectPath /project \
  -buildTarget WebGL -executeMethod ShaderTrim.ReviewShots -logFile /out/review.log &
# build
nohup $RUN -nographics -executeMethod WebGLBuilder.Build -logFile /out/build.log &
grep -m1 'BUILD_RESULT result=Succeeded errors=0' $OUT/build.log
```

Headed editor: **Tools ▸ Icecube ▸ Shader Trim ▸ {Audit, Backup + Apply,
C10 Review Shots}** and **Tools ▸ Icecube ▸ Light Trim ▸ {Audit, Apply}**.

## Measuring (acceptance + kill criterion)

Serve the baseline build (`3eda0fc`, worktree `icecube-worktrees/n17-baseline`)
and the trimmed build side by side, then:

```sh
python3 tools/n17_capture.py --url http://127.0.0.1:8042/ --out /tmp/n17/before
python3 tools/n17_capture.py --url http://127.0.0.1:8046/ --out /tmp/n17/after
python3 tools/n17_diff.py /tmp/n17/before /tmp/n17/after --md tools/n17_results.md
python3 tools/n17_diff.py --review-shots /home/watson/icecube-backups/materials/review-shots/<set> \
        --md tools/n17_review_shots.md
```

`n17_capture.py` needs `websocket-client` and a Chrome with
`--remote-debugging-port`. Headless SwiftShader numbers are a regression
signal only — the acceptance p50 must come from the Chromebook via the N1
`PERFPROBE frame` console line (`tools/MEASURE.md`). If the Chromebook p50
gain is **< 1 ms**, `git revert` commit B (lights) and keep commit A.

Acceptance check for "no object turns black": `n17_diff.py` flags any phase
or review pair whose mean luma collapses (`BLACK`).
