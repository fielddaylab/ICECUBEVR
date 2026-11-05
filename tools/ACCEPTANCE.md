# N18 — Final acceptance report (owner's Chromebook, N1 protocol)

STATUS: **UNFILLED — hardware run pending**

This file is the sign-off gate for the icecube WebGL build. It must be filled
from a run on **the owner's Chromebook** (real GPU), with physical-pixel
DevTools screenshots and no OS scaling.

Hard rules carried from the card:

- **Hardware only.** If the `PERFPROBE env` line contains `SwiftShader`,
  `llvmpipe` or `softpipe` (or `tier: "SOFTWARE"`), the capture is
  **non-acceptance evidence** (N1) — re-run on the hardware GPU.
- **Physical pixels.** Screenshots come from DevTools at the device's own
  pixel ratio. No OS zoom, no browser zoom, no image resampling.
- **No vision-model verdicts.** Every row below is a number compared against
  a threshold by `tools/acceptance_stats.py`, not an impression. The only
  human judgement allowed is "is a gaze marker drawn, yes/no" (§5), and that
  is recorded by the operator looking at the physical-pixel PNG.
- **Do not tune during the run.** Any threshold miss blocks sign-off and names
  its owning card (§0 of the generated report). File each miss as a **new
  card**; do not fix it inside this run.

Measured state going in: *all prior verdicts were taken on a 1280x720 DPR-1
SwiftShader box*, so none of them count here.

---

## 0. Thresholds and owning cards

| # | Metric | Threshold | Source | Owning card on a miss |
| --- | --- | --- | --- | --- |
| 1 | GL_RENDERER | no SwiftShader / llvmpipe / softpipe | `PERFPROBE env` | N1 / N16 |
| 2 | Drawing buffer | `buffer == CSS × DPR` on both axes (1:1) | `PERFPROBE env` | N6 / N16 |
| 3 | p50 frame time | ≤ 33.4 ms, all three phases | `PERFPROBE` frames | N6 |
| 4 | p95 frame time | ≤ 35 ms, all three phases | `PERFPROBE` frames | N6 |
| 5 | Long frames | ≤ 2 frames > 66 ms per minute, all three phases | `PERFPROBE` frames | N6 |
| 6 | Jitter | < 5 %, all three phases | `PERFPROBE` frames | N6 |
| 7 | Capture window | ≥ 55 s of valid capture per phase | `PERFPROBE` frames | N18 (procedure) |
| 8 | Caption median transition | ≤ 1.5 px | `sharpness.py` | N4 |
| 9 | Caption p90 transition | ≤ 2.0 px | `sharpness.py` | N4 |
| 10 | Caption cap-height | ≥ 22 px | `sharpness.py` | N4 |
| 11 | Caption contrast | ≥ 7:1 | `sharpness.py` | N4 |
| 12 | Reticle edges / contrast | ≤ 1.5 / ≤ 2.0 px, ≥ 7:1 | `sharpness.py` | N9 |
| 13 | Gaze-marker edges / contrast | ≤ 1.5 / ≤ 2.0 px, ≥ 7:1 | `sharpness.py` | N10 |
| 14 | Data file size | ≤ 30 MB (30 MiB) | `ls -l` on the `.data` | N13 / N14 |
| 15 | Gaze markers | every gaze prompt has a visible marker | screenshots | N10 |
| 16 | Time to first frame | recorded, **not gating** (INFO) | DevTools / console | — |

Cap-height is a text metric, so rows 10 applies to the caption only; the
reticle and the gaze marker are glyph-free and gate on edge width and
contrast.

---

## 1. Capture procedure (run this on the Chromebook)

```bash
pip install -r tools/requirements-measure.txt
```

### 1.1 Load the build and record the environment

Serve the build over HTTP (not `file://`) and open it in Chrome on the
Chromebook, fullscreen or at a fixed window size. In DevTools → Console the
N1 probe prints one `PERFPROBE env {...}` line at boot. Save the whole console
to `perfprobe-console.log` (right-click → *Save as...*).

If `WEBGL_debug_renderer_info` is blocked, the env line carries
`debug_renderer_info: false`; open `chrome://gpu`, copy the real GL_RENDERER
string, and paste it into the `renderer` field of the saved env line before
running the analyzer (N1 fallback).

```bash
python3 tools/acceptance_stats.py env perfprobe-console.log > env.json
```

This checks rows 1–2: the hardware guard and the 1:1 buffer.

### 1.2 Mark the three phases and capture 60 s each

The analyzer slices the log by operator marker lines. At the **start** of each
60 s window, type a marker into the DevTools console:

```js
console.log('phase: intro')      // then let it run 60 s
console.log('phase: warp')       // then let it run 60 s
console.log('phase: spectrum')   // then let it run 60 s
```

Run the **full narration** once, start to finish, and mark the three phases as
they come up. Do not reload between phases.

**Preferred (EXACT mode).** The N1 jslib dumps a 10 s aggregate, which can
only put a *lower bound* on "frames > 66 ms". To measure row 5 exactly, paste
this into the console *before* the first marker so raw per-frame deltas are
logged too:

```js
(function(){var p=performance.now();function t(){var n=performance.now();
console.log('PERFPROBE delta '+(n-p).toFixed(2));p=n;requestAnimationFrame(t);}
requestAnimationFrame(t);})();
```

**Fallback (AGGREGATE mode).** Without the snippet the analyzer falls back to
the 10 s `PERFPROBE frame` dumps, flags the phase `AGGREGATE`, and marks the
long-frame row as a bounded result. A bounded *failure* is a true failure; a
bounded *pass* is not sign-off-grade for row 5 and the report says so.

```bash
python3 tools/acceptance_stats.py frames perfprobe-console.log --phase intro    --window 60 > frames-intro.json
python3 tools/acceptance_stats.py frames perfprobe-console.log --phase warp     --window 60 > frames-warp.json
python3 tools/acceptance_stats.py frames perfprobe-console.log --phase spectrum --window 60 > frames-spectrum.json
```

### 1.3 Physical-pixel screenshots and sharpness

DevTools → **Rendering** panel → *Screenshot size: **Window size***, then
DevTools menu (⋮) → **Capture screenshot**. Confirm the PNG's pixel dimensions
equal `canvas_width × canvas_height` from the env line — if they are smaller,
something rescaled and the shot is invalid.

Capture one shot each with a caption on screen, with the reticle visible, and
with a gaze marker visible. Read the element's bounding box off the PNG in
physical pixels and pass it as `--rect x,y,w,h`:

```bash
python3 tools/sharpness.py caption.png --rect <x,y,w,h> > sharp-caption.json
python3 tools/sharpness.py reticle.png --rect <x,y,w,h> > sharp-reticle.json
python3 tools/sharpness.py marker.png  --rect <x,y,w,h> > sharp-marker.json

python3 tools/acceptance_stats.py sharpness sharp-caption.json --role caption > s-caption.json
python3 tools/acceptance_stats.py sharpness sharp-reticle.json --role reticle > s-reticle.json
python3 tools/acceptance_stats.py sharpness sharp-marker.json  --role marker  > s-marker.json
```

### 1.4 Data file size and time to first frame

```bash
python3 tools/acceptance_stats.py payload Build/Build.data > payload.json

# time-to-first-frame: DevTools → Network → the .data request's finish time,
# or the delta from navigationStart to the first rendered frame.
python3 tools/acceptance_stats.py ttff <ms> > ttff.json
```

### 1.5 Gaze markers

One entry per gaze prompt. `marker_visible` is `true` only if the 2D marker
(or its off-screen edge arrow) is visibly drawn in the physical-pixel
screenshot — the operator's own look at the PNG, not a vision model's verdict.
Leave it `null` and the row fails as UNFILLED.

```bash
cat > gaze.json <<'JSON'
[
  {"prompt_id": "begin",    "marker_visible": null, "screenshot": "gaze-begin.png"},
  {"prompt_id": "warp",     "marker_visible": null, "screenshot": "gaze-warp.png"},
  {"prompt_id": "spec_viz", "marker_visible": null, "screenshot": "gaze-spec-viz.png"},
  {"prompt_id": "spec_gam", "marker_visible": null, "screenshot": "gaze-spec-gam.png"},
  {"prompt_id": "spec_neu", "marker_visible": null, "screenshot": "gaze-spec-neu.png"},
  {"prompt_id": "resume",   "marker_visible": null, "screenshot": "gaze-resume.png"}
]
JSON
python3 tools/acceptance_stats.py gaze gaze.json > gaze-check.json
```

### 1.6 Generate the report (overwrites this file)

```bash
python3 tools/acceptance_stats.py report \
  --env env.json \
  --frames frames-intro.json frames-warp.json frames-spectrum.json \
  --sharpness s-caption.json s-reticle.json s-marker.json \
  --payload payload.json --ttff ttff.json --gaze gaze-check.json \
  --out tools/ACCEPTANCE.md
echo "exit=$?"   # 0 = all PASS (sign-off may proceed), 1 = a miss blocks
                 # sign-off, 2 = an artifact failed to parse
```

Keep the raw artifacts (`perfprobe-console.log`, the PNGs, and every
intermediate JSON) next to the report; they are the evidence behind it.

---

## 2. Result tables — UNFILLED until the hardware run

The tables below are what `report` fills in. Until then every value is
`UNFILLED` and the run has **not** been accepted.

### 2.1 Environment

| Item | Value | Threshold | Verdict |
| --- | --- | --- | --- |
| GL_RENDERER | UNFILLED | no SwiftShader / llvmpipe / softpipe | UNFILLED |
| GL_VENDOR | UNFILLED | recorded | — |
| tier | UNFILLED | HARDWARE | UNFILLED |
| devicePixelRatio | UNFILLED | recorded | — |
| Canvas CSS size | UNFILLED | recorded | — |
| Drawing buffer size | UNFILLED | buffer == CSS × DPR (1:1) | UNFILLED |

### 2.2 Frame timing — 60 s per phase

| Phase | mode | window s | n frames | p50 ms | p95 ms | >66 ms /min | jitter % | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| intro | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED |
| warp | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED |
| spectrum | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED |

### 2.3 Sharpness

| Element | median px | p90 px | cap-height px | contrast | Verdict |
| --- | --- | --- | --- | --- | --- |
| caption | UNFILLED | UNFILLED | UNFILLED | UNFILLED | UNFILLED |
| reticle | UNFILLED | UNFILLED | n/a | UNFILLED | UNFILLED |
| gaze marker | UNFILLED | UNFILLED | n/a | UNFILLED | UNFILLED |

### 2.4 Payload & first frame

| Item | Value | Threshold | Verdict |
| --- | --- | --- | --- |
| `.data` file size | UNFILLED | ≤ 30 MiB | UNFILLED |
| Time to first frame | UNFILLED | INFO — not gating | UNFILLED |

### 2.5 Gaze markers

| Prompt | marker visible | screenshot | Verdict |
| --- | --- | --- | --- |
| begin | UNFILLED | UNFILLED | UNFILLED |
| warp | UNFILLED | UNFILLED | UNFILLED |
| spec_viz | UNFILLED | UNFILLED | UNFILLED |
| spec_gam | UNFILLED | UNFILLED | UNFILLED |
| spec_neu | UNFILLED | UNFILLED | UNFILLED |
| resume | UNFILLED | UNFILLED | UNFILLED |

---

## 3. Sign-off

- [ ] Hardware GPU confirmed in the `PERFPROBE env` line (no SwiftShader /
      llvmpipe).
- [ ] 1:1 drawing buffer (no browser or OS rescale).
- [ ] Frame timing within thresholds in intro, warp and spectrum.
- [ ] Caption, reticle and gaze-marker sharpness within thresholds.
- [ ] Data file ≤ 30 MiB.
- [ ] Every gaze prompt has a visible marker.
- [ ] Operator sign-off: ______________  date: ____________

**Kill criterion:** any threshold miss blocks sign-off and names its owning
card. File each miss as a new card against that card's area; do **not** tune
anything during the acceptance run.
