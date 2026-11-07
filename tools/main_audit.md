# N7 — Main.cs audit: frame-rate-dependent movement and per-frame allocations

Audit of `Assets/Scripts/Main.cs` (2,929 lines before this card, 2,999 after,
one `Main : MonoBehaviour`)
for the script-side causes of unsmooth motion — the ones that no amount of
resolution or buffer work can cure.

Scope guards honoured:

- No `.bak` files under `Assets/` or `ProjectSettings/`. The pre-edit file
  was copied to `/home/watson/icecube-backups/Main.cs.20261104-210834`.
- No refactor. Every change is an in-place one-liner or a few-line block on a
  camera/gaze/warp path, plus the cache fields those lines read.
- **Gaze timing constants untouched.** `gaze_trigger.t_max`,
  `t_max_numb`, `range`, the per-trigger overrides in `Start`
  (`warp_trigger.t_max = …`, `blackhole_trigger.range = 0.8f`, …),
  `subtitle_cues_absolute`, `subtitle_strings` and `credit_strings` are
  byte-identical. `gaze_trigger.tick(ptr, dt)` was already correct: it takes
  `Time.deltaTime` from every call site, so a gaze that needs 1.0 s of dwell
  still needs 1.0 s at any frame rate.

## Reproducing the table

```bash
python3 tools/main_audit.py                      # human summary + update-path map
python3 tools/main_audit.py --markdown           # the findings table below
python3 tools/main_audit.py --json               # machine readable
python3 tools/main_audit.py --fail-on-violations # regression guard: exit 1 if any remain
```

`tools/main_audit.py` is a static scanner (Python 3 stdlib only, report-only).
It finds the bodies of `Update`/`LateUpdate`/`FixedUpdate` by brace-depth
tracking, resolves one level of calls out of them into same-file methods, and
then flags lines by category. A line can be cleared two ways: automatically,
when the interpolation factor derives from a value that is itself accumulated
with `Time.deltaTime`; or explicitly, with a trailing `// N7-OK: <reason>`
comment that records a human verdict. After this card the scanner reports
**0 violations, 18 accepted, 6 informational**.

## Update paths found (what actually runs every frame)

There is **no `LateUpdate` and no `FixedUpdate`** in the file. Everything
runs from one `Update`:

| Method | Lines | Reached from |
| --- | --- | --- |
| `Update` | 2517–2997 | (entry point) |
| `UpdateScene` | 2179–2487 | `Update` |
| `gaze_trigger.tick` | 33–61 | `Update`, `UpdateScene` |
| `getEuler` / `getCamEuler` / `rotationFromEuler` | 582–595 | `Update`, `UpdateScene` |
| `MapVols` | 435–484 | `Update` (tail) |
| `PlaySFX` | 424–432 | `Update`, `UpdateScene` (event-driven) |
| `SetSpec` | 2490–2512 | `Update`, `UpdateScene` (event-driven) |
| `SetupScene` | 1877–2176 | `Update` (scene transition only) |
| `PreSetupNextScene` | 1841–1874 | `Update` (warp trigger only) |
| `reStart` | 611–707 | `Update` (void fade only) |
| `HandleHMDMounted` / `HandleHMDUnmounted` | 599–608 | `Update` (keypress) |

The scanner reports the event-driven ones too, which is why several findings
below are "accepted": they are inside an `Update` path but gated behind a
scene transition, so they run once per scene, not once per frame.

## Category A — frame-rate-dependent movement

Line numbers are **pre-edit** (the `/home/watson/icecube-backups/Main.cs.20261104-210834` copy); the post-edit line is
given where it moved.

| # | Pre-edit line | Code | Verdict | Fix |
| --- | --- | --- | --- | --- |
| A-1 | 2449–2450 | `if(void_t < target_void_t) void_t += 0.05f;` / `else if(void_t > target_void_t) void_t -= 0.05f;` | **Real.** The HMD-unmount / restart-query fade-to-black advanced 0.05 per *frame*: 0.33 s at 60 fps, 0.67 s at 30 fps, and visibly uneven while the frame time wobbles. | Fixed (now 2519–2520): `void_t ± void_t_rate * Time.deltaTime` with `void_t_rate = 3.0f` (= 0.05 × 60), so the fade is 0.333 s at any rate. |
| A-2 | 2492 | `intro_t += 0.002f;` | **Real.** The opening dim-to-clear ramp took 500 frames — 8.3 s at 60 fps, 16.7 s at 30. | Fixed (now 2562): `intro_t += intro_t_rate * Time.deltaTime` with `intro_t_rate = 0.12f` (= 0.002 × 60). |
| A-3 | 2663–2664 | `lazy_look_ahead = Vector3.Lerp(lazy_look_ahead, look_ahead, 0.1f);` / `very_lazy_look_ahead = Vector3.Lerp(very_lazy_look_ahead, look_ahead, 0.002f);` | **Real, and the one that is actually felt.** These are the exponential smoothers that drive the helmet rotation (line 2669 pre-edit) and the spectrum-projection rotation (2674, via `spec_euler` at 2673), i.e. they follow the camera. A fixed per-frame Lerp factor means the smoothing time constant is measured in *frames*: the helmet lags ~10 frames behind the camera whether those frames are 16 ms or 33 ms apart, so every frame-time hiccup changes the lag and the helmet swims relative to the view. | Fixed (now 2733–2734) with the frame-rate-independent form `alpha = 1 - Mathf.Exp(-k*dt)`: `lazy_look_ahead_k = 6.3216309f` (= −60·ln(1−0.1)) and `very_lazy_look_ahead_k = 0.12012016f` (= −60·ln(1−0.002)). At exactly 60 fps this reproduces 0.1 and 0.002 to 8 decimal places, so the tuned feel is preserved. |
| A-4 | 2181–2184 | `grid_yvel += grid_yacc; grid_yoff += grid_yvel; … grid_yvel *= 0.96f;` | **Real but out of scope.** A per-frame Euler integrator with a per-frame 0.96 damping factor — the ICE grid's drop-in bounce really does land at a different height and settle in a different time at 30 vs 60 fps. It is a prop animation on a `GameObject` that is not the camera, not a gaze target and not the warp; converting it changes the look of a hand-tuned bounce. Left alone and annotated `// N7-OK`; if the grid bounce ever needs to match across frame rates it should be a separate card. | Not fixed (deliberate). |
| A-5 | 2834 | `jitter += Random.Range(-0.1f, 0.1f);` | **Real but intended.** A per-frame random walk feeding the `jitter` shader global in the EXTREME scene — this *is* the glitch effect, and its character is supposed to be per-frame noise. Not camera/gaze/warp motion. Annotated `// N7-OK`. | Not fixed (deliberate). |
| A-6 | 2696 | `flash_alpha *= 1.1f;` | **False positive.** `flash_alpha` is recomputed from the portal/fail timers in the `if/else` chain immediately above every frame, so the 1.1 is a gain on a fresh value, not an accumulator. Annotated. | None needed. |
| A-7 | 2812, 2814 | `transform.Translate(scene_centers[…])` / `transform.Translate(-scene_centers[…])` | **False positive.** The two lines above reset the transform to position 0 and identity rotation, so this is an absolute rebuild (translate → rotate → translate back), not an accumulation. Annotated. | None needed. |
| A-8 | 2813 | `transform.Rotate(0f, Mathf.Rad2Deg * scene_rots[cur_scene_i], 0f);` | **Already correct.** The angle comes from `scene_rots`, integrated at 2804/2806 as `scene_rots[i] += scene_rot_deltas[i] * Time.deltaTime`. Annotated. | None needed. |
| A-9 | 2564, 2896 | `Mathf.Lerp(default_portal_position.z, 0, t*t*t*t*t)`, `Vector3.Lerp(gaze_pt, anti_gaze_pt, ball_t)` | **Already correct** (scanner reports these as informational, not violations). `t = in_portal_motion / max_portal_motion` (2563) and `ball_t = (nwave_t_10 % 2f)/2f` (2895) are both parametric in values advanced by `Time.deltaTime` (2543, 2501), so the warp engulf and the gaze ball travel at a wall-clock rate. **The warp animation itself was already frame-rate independent** — the warp-phase judder is therefore an allocation/pacing problem, not a stepping problem. | None needed. |
| A-10 | 2502–2503, 2805, 2807 | `while(nwave_t_1 > 1) nwave_t_1 -= 1f;` etc. | **Already correct.** Wrap-around of `Time.deltaTime`-accumulated phases. | None needed. |

Also already correct and worth recording: `subtitle_t += Time.deltaTime`
(2601), `ta[…] += Time.deltaTime` (2130), `in_portal_motion += Time.deltaTime
* 0.8f` (2543), `out_portal_motion` (2558), `in_fail_motion` (2577),
`out_fail_motion` (2593), `alert_t`/`timer_t` (2307–2308), `credits_t`
(2399), `time_mod_twelve_pi` (2817), `jitter_countdown` (2819),
`dumb_delay_t` (2869) and every `gaze_trigger.tick(…, Time.deltaTime)` call.

## Category B — per-frame allocations and native lookups

| # | Pre-edit line | Code | Verdict | Fix |
| --- | --- | --- | --- | --- |
| B-1 | 2505–2512 | eight `GetComponent<Camera>()` calls syncing `aspect` and `fieldOfView` across four cameras | **Real.** Eight native component lookups every single frame, unconditionally, for values that change only when the canvas resizes. | Fixed (now 2575–2582): `main_camera_cam`, `ar_camera_project_cam`, `ar_camera_static_cam`, `portal_camera_next_cam` are resolved once in `Start` and the eight calls become eight field accesses. |
| B-2 | 2436–2437 | `main_camera.GetComponent<Camera>().cullingMask = …` / `portal_camera_next.GetComponent<Camera>().cullingMask = …` in `SetSpec` | **Real on gaze.** `SetSpec` fires from the spectrum gaze trigger, so these sit on the gaze path. | Fixed (now 2506–2507) using the same cached `Camera` references. |
| B-3 | 2382–2387 | `foreach(Transform child_transform in blackhole[i].transform)` × 3 spectra, every frame of the EXTREME scene | **Real.** `Transform`'s enumerator is a struct but is reached through `IEnumerator`, so each `foreach` boxes an enumerator: 3 garbage objects per frame for the whole 60-second blackhole sequence, which on WebGL is exactly the kind of steady drip that produces periodic GC frames. The `Quaternion.Euler` was also recomputed per child. | Fixed (now 2446–2456): `blackhole_children` (`Transform[][]`) is built once in `Start` and walked by index; the rotation is computed once per spectrum and assigned to each child. Zero allocation. |
| B-4 | 2340 | `ar_timer_text.text = "00:" + Mathf.Floor(seconds_left) + ":" + Mathf.Floor((seconds_left - Mathf.Floor(seconds_left)) * 100);` | **Real, and the largest single source.** Every frame of the EXTREME countdown: two `float.ToString` conversions (each allocating), three concatenations (each allocating an intermediate `string`), and then a `TextMesh.text` setter which re-tessellates the mesh *even when the text is unchanged*. ~5 allocations/frame plus a mesh rebuild. | Fixed (now 2391–2405): integer seconds/hundredths, digits from a prebuilt `small_number_strings[0..100]` table, and the assignment guarded by a change check so the label is rewritten ~100×/s worst case instead of every frame, and the mesh is not rebuilt on identical text. Output format is byte-identical to the original (`"00:" + unpadded seconds + ":" + unpadded hundredths`, same truncating `Mathf.Floor` semantics). |
| B-5 | 2815 | `main_camera_skybox.material.SetFloat("_Rotation", …)` | **Real (minor).** The string property name is hashed on every call. | Fixed (now 2885): `skybox_rotation_id = Shader.PropertyToID("_Rotation")` cached in `Start`, matching what the file already does for `alpha`, `time_mod_twelve_pi` and `jitter`. |
| B-6 | 1809–1813, 2036–2040, 2078–2082 | `foreach(Transform …)` + `GetComponent<ParticleSystem>()` in `PreSetupNextScene` / `SetupScene` | **Accepted.** Reachable from `Update` but gated behind a scene transition, so they run once per scene, not per frame. Annotated `// N7-OK` so the scanner stays at zero violations. | None. |
| B-7 | 1860, 1976, 1995, 2014 | `curve = new AnimationCurve();` in `SetupScene` | **Accepted.** Same reasoning as B-6: once per scene transition. | None. |
| B-8 | 2901–2923 | the 10×10×10 grid bulb loops (`new Vector3(s,s,s)` × 1000 per frame) | **Not an allocation.** `Vector3` is a struct; `new Vector3` is a stack value, not heap garbage. It is 1000 `Vector3.Distance` calls and 1000 `localScale` setters per frame, which is a CPU cost (and only while the ICE grid is active), but it is not a GC source and not camera/gaze/warp motion. Out of scope for this card. | None. |

No `new List<>`/`new Dictionary<>`, no LINQ, no `.ToArray()`/`.ToList()`,
no `GameObject.Find`/`FindObjectOfType`, no `Resources.Load` and no
`Debug.Log` on any per-frame path. All 60-odd `GameObject.Find` calls and
the `Resources.Load<Material>` skybox loads are in `Start`, where they
belong.

## Category C — physics and camera placement

| Question | Answer |
| --- | --- |
| Any `Rigidbody` in the file? | **No.** `grep` for `Rigidbody` in `Main.cs` is empty: no `.velocity =`, no `.MovePosition`, no `.AddForce`. |
| Is `Rigidbody.interpolation` ever set? | Not applicable — there is no rigidbody to interpolate. Nothing moved by this script goes through the physics step, so the interpolation lever does not exist here and cannot be the cause of the judder. |
| Is there a `FixedUpdate`? | **No.** The file has exactly one Unity update hook. |
| Where does camera motion run? | In `Update` — **correct.** `camera_house.transform.localPosition` / `.rotation` are written at 2656/2658/2660 (pre-edit) from mouse input, which is vsync-paced and the right place for a view transform. Running it in `FixedUpdate` would be the bug; it is not. |
| Other transform writes in `Update` | `helmet` (2666/2668/2669), `spec_projection` (2674), `cam_spinner` (2710–2711), `gazeball` (2896), the scene group rotation (2809–2814) and the 1000 grid bulbs (2914–2923). All in `Update`, none in `FixedUpdate`. |

Conclusion for category C: **there is no physics-interpolation lever and no
Update/FixedUpdate mismatch.** Camera motion is already in the right hook.

## Measurements (N1 harness, warp phase)

Method: `tools/MEASURE.md` §1 on the owner's Chromebook, Chrome DevTools
Console, `PERFPROBE frame` lines over a full narration run, counting frames
longer than 66 ms per minute during the warp phase (the `in_portal_motion` /
`out_portal_motion` window). Baseline = the N2 build (governor defused,
`targetFrameRate = -1`, no `Screen.SetResolution`).

| Metric | N2 baseline | After N7 | Pass |
| --- | --- | --- | --- |
| frames > 66 ms per minute, warp phase | _PENDING MEASUREMENT_ | _PENDING MEASUREMENT_ | reduced vs baseline |
| p50 frame time, warp phase | _PENDING MEASUREMENT_ | _PENDING MEASUREMENT_ | ≤ 33.4 ms |
| p95 frame time, warp phase | _PENDING MEASUREMENT_ | _PENDING MEASUREMENT_ | ≤ 35 ms |
| `jitter_pct` | _PENDING MEASUREMENT_ | _PENDING MEASUREMENT_ | < 5 % |
| Full narration run, no gameplay regression | — | _PENDING OBSERVATION_ | all gazes trigger, all captions in sync, all scenes advance |

> **Owner instruction (exactly once, on the Chromebook):** play the N2 build
> and the N7 build back to back. For each, let the EXTREME scene run to its
> warp and copy every `PERFPROBE frame {...}` line emitted during the warp
> into the table above, plus the count of `>66 ms` frames. Also confirm
> during the N7 run that (a) every gaze target still triggers in the same
> dwell time, (b) the countdown label still reads `00:SS:CC` and counts down
> at 1 s/s, (c) the helmet still tracks the view without visible lag change,
> and (d) the fade-to-black on HMD-unmount still takes about a third of a
> second. SwiftShader/headless numbers are **not** acceptance evidence
> (`tools/MEASURE.md` §4).

### Expected direction

The three fixes that remove per-frame garbage (B-3 boxed enumerators ×3/frame,
B-4 ~5 string allocations/frame plus an unnecessary `TextMesh` mesh rebuild,
B-5 a string hash) all sit in the EXTREME scene, which is the scene that leads
into the warp — so the GC pressure that produces > 66 ms frames in the warp
phase is being removed from exactly the right place. The A-1/A-2/A-3 fixes do
not change the frame-time distribution; they make the motion that *is* drawn
advance by wall-clock time, so a 33 ms frame now moves the smoothers twice as
far as a 16 ms frame instead of the same distance, which is what removes the
perceived swim.

### Kill criterion

**Not triggered.** Frame-rate-dependent code was found (A-1, A-2, A-3 on the
camera-following smoothers and the fades) and real per-frame allocations were
found (B-1 through B-5), so this card had work to do. The remaining smoothness
lever is still N6 (pacing governor): nothing here changes the *rate* at which
frames are presented, only the per-frame cost and the correctness of the
stepping.
