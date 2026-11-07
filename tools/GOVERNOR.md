# N6 — Pacing governor (half-rate rAF shim + work-time ladder)

## Pieces

| Piece | File | Role |
| --- | --- | --- |
| rAF shim | `Assets/WebGLTemplates/Icecube/index.html` | Wraps `window.requestAnimationFrame` **before** `UnityLoader.js`. With `halfRate` on (default, owner decision C8 = hard lock 30; no query-string override) a queued callback is forwarded on every second native vsync → 30 vsync-aligned ticks per 60 vsyncs. Brackets each forwarded tick with `performance.now()` and exposes `window.icecubeWorkMs()`. Console-only diagnostics: `window.icecubeSetHalfRate(bool)`, `window.icecubeRaf`. Logs `[icecube] rAF shim active halfRate=true` once at startup. |
| jslib | `Assets/Plugins/WebGL/govworktime.jslib` | `GovWorkTime()` = previous main-loop tick work ms (0 on first frames / hidden tab / Editor). `GovShimPresent()`, `GovHalfRate()` for the init log line. Installs a measure-only fallback and warns if the template shim is missing. |
| policy | `Assets/Scripts/GovernorPolicy.cs` | Pure C#. EWMA α=0.1, rolling 3 s p90, 1 s verdict windows. Ignore first 10 s after load and 2 s after any scale change / `NotifySceneSwap()`. Step DOWN after p90 > 28 ms for 3 consecutive seconds, step UP after p90 < 20 ms for 8 consecutive seconds, 5 s cooldown after any change. Ladder `{1.0, 0.85, 0.71, 0.6, 0.5}`, clamped, no oscillation at either end. |
| plumbing | `Assets/Scripts/WebGLPerfGovernor.cs`, `GovernorScaleSink.cs` | Samples `GovWorkTime()` each frame, feeds the policy, applies `SceneCompositor.SetScale(index)` (N5; reflection so the governor stays inert if N5 is absent). Logs `GOV ladder=<i> rt=<w>x<h>` on change only. Never `Screen.SetResolution`, never `smoothDeltaTime`, `targetFrameRate` stays `-1`. |
| swap hook | `Assets/Scripts/Main.cs` | `WebGLPerfGovernor.NotifySceneSwap()` after both skybox swap sites (`SetupScene`, portal transition). |
| guards | `tools/build_webgl.sh`, `tools/verify_template.sh` | nohup build, poll for `BUILD_RESULT result=Succeeded errors=0`, then fail hard if the built `index.html` lacks the shim. |

## Build

```sh
tools/build_webgl.sh /home/watson/icecube-out-n6 icecube-unity-license-a
# -> BUILD_RESULT result=Succeeded errors=0 ...
# -> VERIFY_TEMPLATE result=PASS
```

## Hardware capture (per N1 `tools/MEASURE.md`)

Serve `/home/watson/icecube-out-n6/webgl`, open on the Chromebook, DevTools console. Three 60 s phases:

1. **Warmup** 0–60 s: expect `[icecube] rAF shim active halfRate=true`, `[GOV] init shim=present halfRate=1`, `[GOV] compositor found ...`. No `GOV ladder=` lines before 10 s.
2. **Steady** 60–120 s: collect `PERFPROBE frame {...}` lines; count `GOV ladder=` lines.
3. **Scene swap** 120–180 s: trigger a portal transition; the 2 s post-swap window must not produce a ladder change.

Acceptance (hardware only): p50 ≤ 33.4 ms, p95 ≤ 35 ms, ≤ 2 frames > 66 ms per minute, jitter (|Δ−median| > 8 ms) < 5 %, ≤ 2 ladder changes per minute after warmup, captions at N4 sharpness (`tools/sharpness.py`) at every ladder step.

Force a step for the sharpness check: `unityInstance.SendMessage('SceneCompositor','SetScaleFromJs','4')` — the governor adopts an external change (2 s ignore + 5 s cooldown) instead of fighting it.

## Kill criterion

If jitter stays ≥ 5 % with `GOV ladder=4` (scale 0.5), the bottleneck is CPU/script, not fill: stop tuning here and escalate to N7 / N8 / N17.
