# TextMesh -> TextMeshPro migration (card N12)

Every legacy `TextMesh` in `Assets/Scenes/main.unity` and the three AR label
prefabs Main.cs instantiates is now a `TextMeshPro` (3D / world-space SDF)
component on the **same GameObject**. Nothing was moved, renamed or deleted:
transform, parent, colliders and gaze triggers are untouched, only the
`TextMesh` + its `MeshRenderer`/`MeshFilter` went and TMP's own came in.

Tooling:

| What | Where |
|------|-------|
| Migration script (editor, batch-safe) | `Assets/Editor/TextMeshMigrator.cs` — `icecube > TMP > 3.` |
| World material (outline only) | `Assets/Fonts/Caption SDF - WorldOutline.mat` |
| Font asset | `Assets/Fonts/Caption SDF.asset` (N3, static atlas, ASCII 32-126 + Latin-1) |
| Lint (no Unity needed) | `python3 tools/check_textmesh.py` |
| Per-label measurements | `tools/textmesh_migration_report.txt` (regenerated on each run) |

## Routing decision (card step 1 / plan step 2)

The plan's rule was: parent chain includes the camera rig -> HUD text on N4's
Overlay canvas, everything else -> TMP 3D on the same object.

Measured parent chains:

| Object | Parent chain | Route |
|--------|-------------|-------|
| Timer `0:00` | `AR/Timer` | **3D** (AR is a world group, not the rig) |
| Alert `ALERT` | `AR/Alert` | **3D** |
| Subtitles | `AR/Subtitles` | 3D (N4 rebinds captions separately) |
| Credits_0 / Credits_1 (the two "empty" fontSize-150 ones; actual fs=60) | `CameraHouse/OVRCameraRig/TrackingSpace/CenterEyeAnchor/...` | 3D — see below |
| everything else | `AR/...` | 3D |

Neither Timer nor Alert hangs off the camera rig (both are children of the `AR`
world group, which Main.cs positions in front of the player), so the HUD branch
of the rule did not fire for them and they stay world-space. That also
preserves the `ar_timer.SetActive()` / `ar_alert.SetActive()` toggling in
Main.cs unchanged. The only rig-parented labels are `Credits_0/1`.

**Credits_0/1 (owner decision C7):** the two "empty" labels are not dead —
Main.cs writes `credit_strings[...]` into them during the credits roll
(`credits_text_0.text = ...`). Per plan step 6 ("if an assignment exists,
migrate them like the others") they were migrated, not disabled/deleted. If C7
lands as "delete", drop the GameObjects *and* the four `credits_*` references
in Main.cs together.

## Sizing (card step 2 / plan step 4)

Start size = `fontSize * characterSize * 0.1`, then the migrator renders both
the legacy mesh and the TMP mesh, and scales the TMP font size so the rendered
**width** matches the old renderer bounds (±10% tolerance; all 17 landed at
ratio 1.000). Empty labels are measured with the probe string `00:00:00 SAMPLE`.
VISIBLE / X-RAY / NEUTRINO then get the card's additional 1.5x.

| Object | Text | legacy fs×cs | start | fitted | bump | **final TMP size** | width (world) |
|--------|------|-------------|-------|--------|------|------------|-------|
| Restart_Text | RESTART | 40×2 | 8 | 79.13 | 1 | **79.13** | 36.2 |
| Resume_Text | RESUME | 40×2 | 8 | 78.07 | 1 | **78.07** | 33.4 |
| Begin_Text | BEGIN | 40×2 | 8 | 80.34 | 1 | **80.34** | 25.0 |
| Alert | ALERT | 30×1 | 3 | 29.95 | 1 | **29.95** | 0.096 |
| Timer | 0:00 | 20×1 | 2 | 23.64 | 1 | **23.64** | 0.23 |
| map0/text | Solar System | 30×1 | 3 | 33.13 | 1 | **33.13** | 0.246 |
| map1/text | Local Group | 30×1 | 3 | 32.86 | 1 | **32.86** | 0.224 |
| map2/text | ??? | 30×1 | 3 | 28.77 | 1 | **28.77** | 0.060 |
| Text_Viz | VISIBLE | 40×0.3 | 1.2 | 11.91 | 1.5 | **17.86** | 0.216 -> 0.324 |
| Text_Gam | X-RAY | 40×0.3 | 1.2 | 12.07 | 1.5 | **18.11** | 0.173 -> 0.259 |
| Text_Neu | NEUTRINO | 40×0.3 | 1.2 | 12.01 | 1.5 | **18.01** | 0.301 -> 0.451 |
| Credits_0 / _1 | (runtime) | 60×1 | 6 | 63.82 | 1 | **63.82** | 2.625 (probe) |
| Subtitles | (runtime) | 20×1 | 2 | 21.64 | 1 | **21.64** | 0.356 (probe) |
| AR*Label.prefab/Text | (runtime) | 40×0.5 | 2 | 21.09 | 1 | **21.09** | 17.35 (probe) |

Why "fitted" ≈ 10× "start": TextMesh at characterSize 1 renders a glyph of
`fontSize/10` world units per em... roughly; TMP's `fontSize` is in points at
`1 unit = 10 pt` for the 3D component. The card's `×0.1` heuristic was off by
that factor, which is exactly why the script measures instead of trusting it.

Alignment: TextMesh `anchor` -> TMP `alignment` with a zero-size rect and
centre pivot, so the transform origin sits at the same point on the text block
(LowerCenter -> Bottom, MiddleRight -> Right, etc.). `x/y` of the old
`m_LocalPosition` now serialize as `m_AnchoredPosition` on the RectTransform;
with anchors at (0.5,0.5) under a non-canvas parent this is the same local
position.

## Code (card step 3)

`Assets/Scripts/Main.cs`: `using TMPro;`, every `TextMesh` field/array ->
`TMP_Text`, every `GetComponent<TextMesh>()` -> `GetComponent<TMP_Text>()`.
`TMP_Text` is the shared base of `TextMeshPro` and `TextMeshProUGUI`, so the
`.text = ...` sites (timer, alert, credits, subtitles, AR labels) compile and
behave unchanged, and N4 can rebind subtitles to a UGUI caption without
touching this file again. No other logic changed.

## Guards

* No `.bak` under `Assets/` or `ProjectSettings/`; pre-edit copies of
  `main.unity`, `Main.cs` and the three prefabs are in
  `/home/watson/icecube-backups/<UTC timestamp>-N12-impl/`.
* `tools/check_textmesh.py` parses the YAML and fails on any enabled
  `!u!102` document, any `TextMesh` token left in Main.cs, or zero TMP
  components. Current: 17 TextMeshPro, 0 legacy.
* Glyph coverage: all label strings are ASCII (`?`, `-`, `:`, `'`), all present
  in the static atlas (`Assets/Fonts/caption_charset.txt`).

## Re-running

```sh
OUT=/home/watson/icecube-out-n12
nohup docker run --rm --name icecube-unity-migrate-n12 -v "$PWD":/project -v "$OUT":/out \
  -v icecube-unity-license-a:/root/.local/share/unity3d unityci/editor:ubuntu-2019.4.40f1-webgl-3 \
  unity-editor -quit -batchmode -nographics -silent-crashes -projectPath /project \
  -executeMethod IceCube.TextMeshMigrator.Migrate -logFile /out/migrate.log >"$OUT/migrate.out" 2>&1 &
grep "TEXTMESH_MIGRATE done" $OUT/migrate.log   # migrated=17 out_of_tolerance=0
python3 tools/check_textmesh.py
```

The script is idempotent in the sense that a second run finds no `TextMesh`
and does nothing. Gotcha fixed during this card: `OpenScene(Single)` unloads
assets only referenced from C#, so the font asset / material are re-resolved
after the scene load — the first run silently fell back to LiberationSans SDF.

## Build / hardware

Build is the standard `WebGLBuilder.Build` run (see below for the command); the
log must contain `BUILD_RESULT result=Succeeded errors=0`. Screenshots on the
Chromebook are pending the human-gated release step.

Build evidence for this run: `/home/watson/icecube-out-n12/build.log` line
`BUILD_RESULT result=Succeeded errors=0 warnings=25 size=364307102`; the data
manifest lists `Assets/Fonts/Caption SDF.asset` (1.0 MB) and
`Assets/Fonts/Caption SDF - WorldOutline.mat`. A headless-Chrome screenshot
was attempted but this host's headless Chrome exposes no WebGL context
(`canvas.getContext('webgl')` returns null even with SwiftShader flags), so the
per-label legibility screenshots are deferred to the Chromebook check in the
release stage.
