# N1 — Real-hardware capture & measurement procedure

SwiftShader (headless) captures are **not acceptance evidence**. The headless
box is CPU-rendered at DPR 1, which hides the browser-upscale blur that real
hardware exhibits. Every verdict must be made from a real-hardware capture
following this procedure.

## 1. Hardware capture

1. On a real device (e.g. Chromebook with discrete/iGPU) open the WebGL build
   in Chrome at the target resolution, fullscreen or a fixed window size.
2. Open DevTools (F12) → **Rendering** panel → check **Screenshot size:
   Window size** (or "Capture node screenshot" for a specific node).
3. Capture the frame: DevTools menu (⋮) → **Capture screenshot**. This yields
   a physical-pixel PNG (no browser downscale). Save it as e.g.
   `Build-NN-hardware.png`.
4. Record the GPU string: open `chrome://gpu`, copy the **GL_RENDERER**
   value (also `GL_VERSION`) into the report. The `PERFPROBE env` console
   line from step 5 contains the same value and a `tier` tag
   (`HARDWARE` / `SOFTWARE`).
5. In DevTools → **Console**, let the page run ≥ 20 s and collect:
   - one `PERFPROBE env {...}` line (renderer, devicePixelRatio,
     canvas.width/height, canvas.clientWidth/Height, buffer_scale_x/y,
     one_to_one, webgl_version, s3tc), and
   - ≥ 1 `PERFPROBE frame {...}` line (p50_ms, p95_ms, jitter_pct, tier).

### Kill-criterion fallback
If `WEBGL_debug_renderer_info` is unavailable in the owner's Chrome, the env
line contains `debug_renderer_info: false`, the raw `gl.RENDERER`, and
`navigator.userAgent`; paste the `chrome://gpu` GL_RENDERER string manually
into the report. This never blocks other cards.

## 2. Pass thresholds

| Metric | Source | Pass |
| --- | --- | --- |
| Buffer 1:1 with DPR | `PERFPROBE env` `one_to_one`, `buffer_scale_x/y` ≈ devicePixelRatio | 1:1 buffer |
| Median edge transition | `sharpness.py` `median_px` | **≤ 1.5 px** |
| p90 edge transition | `sharpness.py` `p90_px` | **≤ 2.0 px** |
| p50 frame time | `PERFPROBE frame` `p50_ms` | **≤ 33.4 ms** |
| p95 frame time | `PERFPROBE frame` `p95_ms` | **≤ 35 ms** |
| Jitter | `PERFPROBE frame` `jitter_pct` | **< 5 %** |

## 3. Sharpness metric

```bash
pip install -r tools/requirements-measure.txt
# rect = x,y,w,h (physical px) of the caption strip region
python3 tools/sharpness.py Build-11.png --rect 0,600,1280,120
```

Prints JSON with `median_px` (median of per-scanline 20–80 % luminance
transition widths across vertical stems), `p90_px`, and `min_stroke_px`
(minimum dark-stroke run width). Pass: median ≤ 1.5 px, p90 ≤ 2.0 px.

## 4. Interpreting results

- `tier: "SOFTWARE"` (SwiftShader/llvmpipe/softpipe): **non-acceptance**
  evidence only — useful for regression comparison, never for a pass verdict.
- A 1:1 buffer (`buffer_scale ≈ devicePixelRatio`) with median transition
  ≤ 1.5 px rules out browser-upscale blur as the source of any perceived
  softness; the remaining cause is in-renderer filtering.
- Frame-time lines include the first-30-frames warm-up excluded and deltas
  > 1000 ms (hidden tab) skipped — see the `skipped` field.
