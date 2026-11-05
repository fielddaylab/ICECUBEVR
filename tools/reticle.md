# Overlay reticle with ring-fill dwell (card N9)

Replaces the two camera-locked world-space quads with one Screen Space Overlay
UI reticle whose ring fills as the dwell timer runs. Owner decision **C3 =
ring-fill**; the spinner art is kept but no longer drawn.

## Correction to the card text: these are quads, not cameras

The card (and the N8 brief) call `Cam_Reticle` and `Cam_Spinner_Reticle`
"overlay cameras". They are not. Measured in `Assets/Scenes/main.unity`:

| Object | Components | Layer | Parent chain |
|---|---|---|---|
| `Cam_Reticle` (fileID 830923160) | Transform, MeshFilter (built-in Quad 10210), MeshRenderer (`Star_Material`) | 31 `Set_AR_Project` | `CameraHouse > OVRCameraRig > TrackingSpace > CenterEyeAnchor` |
| `Cam_Spinner` (fileID 1744631586) | Transform only (pivot) | 31 | `… > Cam_Reticle` |
| `Cam_Spinner_Reticle` (fileID 1481464033) | Transform, MeshFilter (Quad), MeshRenderer (`Star_Material`) | 31 | `… > Cam_Reticle > Cam_Spinner` |

The scene holds exactly **9 Camera components**, none of them on these objects:

| Camera GameObject | enabled | depth | cullingMask | targets RT |
|---|---|---|---|---|
| `CenterEyeAnchor` | 1 | 1 | 257 | no |
| `LeftEyeAnchor` | 0 | 1 | 257 | no |
| `RightEyeAnchor` | 0 | 1 | 257 | no |
| `Main Camera` (GameObject inactive) | 1 | 1 | 257 | no |
| `Helmet_Camera` | 0 | 3 | 134217728 | no |
| `AR_Camera_Static` | 1 | 1 | 1073741824 | yes |
| `AR_Camera_Project` | 1 | 0 | 2147483648 | yes |
| `AR_Camera_Result` | 1 | 2 | 536870912 | no |
| `Portal_Camera_Next` | 1 | 0 | 512 | yes |

**Consequence for acceptance:** the "two fewer enabled cameras in PERFPROBE
camera count" criterion cannot be met by this card, because no camera exists to
disable — the reticle was two extra *draw calls* on the `CenterEyeAnchor`
camera (layer 31 is inside its mask via the Default/`Set_*` mask rotation), not
two extra camera passes. What this card actually removes is **2 transparent
quad draws plus 2 per-frame transform writes** from the scaled 3D buffer, and
it moves the reticle to the native-resolution overlay. Camera reduction belongs
to card N11 (portal/AR camera gating). The frame-time p50 criterion still
applies and should hold or improve.

## Kill criterion: not triggered

The card says: *if Main.cs reads the reticle cameras' projection for gaze math,
keep the cameras enabled with cullingMask 0 instead of disabling.* Every
reference in `Main.cs` was classified; none is a projection read.

| Line(s) | Reference | Class |
|---|---|---|
| 1403 | `cam_reticle = GameObject.Find("Cam_Reticle")` | bind |
| 1404 | `cam_spinner = GameObject.Find("Cam_Spinner")` | bind |
| 1405 | `reticle_d = cam_reticle.transform.position.z` | **transform read** |
| 1612, 2671 | `getCamEuler(cam_reticle.transform.position)` | **transform read** |
| 2137, 2146, 2204, 2221, 2236, 2272, 2287, 2322, 2719, 2762 | `*_trigger.tick(cam_reticle.transform.position, dt)` | **transform read** (gaze pointer for all six triggers) |
| 2752-2754 | `Vector3.Distance(spec_*_reticle…, cam_reticle.transform.position)` | **transform read** |
| 2710-2711 | `cam_spinner.transform.localScale / .localRotation` from `warp_trigger.shrink / .rot` | **visibility/look drive** (now commented out) |
| 658, 1685, 1693, 1846, 2093, 2202…2334, 2415-2416 | `gaze_reticle.SetActive(...)` | **visibility toggle** of the *world gaze target*, not the crosshair |
| 1555-1557, 2425-2431 | `spec_*_reticle.transform` | **transform read** (AR check parenting / selection marker) |

`cam_reticle.transform.position` is the hot path: it is the gaze pointer fed to
every `gaze_trigger.tick`. So the GameObjects **must stay alive** — only their
`MeshRenderer.enabled` is set to 0. No `WorldToScreenPoint`,
`ScreenPointToRay`, `ViewportToWorldPoint`, `projectionMatrix`, `fieldOfView`,
`aspect` or `pixelRect` is read off them anywhere. `ReticleCanvasSetup`
defensively also sets `cullingMask = 0` before `enabled = false` on any Camera
it happens to find on those objects, so the kill-criterion path costs nothing
if the scene ever changes.

## Progress variable: `shrink`

From `gaze_trigger.tick` in `Main.cs`:

```
this.t_in += dt;
if(this.t_in >= this.t_max) { this.just_triggered = true; this.t_numb = this.t_max_numb; this.t_in = this.t_max; }
...
this.shrink = (this.t_in/this.t_max);
this.rot    = (this.t_in/this.t_max)*5f*360.0f;
```

- `t_in` is the raw dwell accumulator, compared against `t_max` in the confirm
  branch, and clamped **to** `t_max` on the confirming tick.
- `shrink = t_in / t_max` is therefore monotonic `0 .. 1` over `t_max`, and
  equals exactly `1.0` on the same tick `just_triggered` is set.
- `rot` is the same ramp times `1800` degrees — monotonic but not 0..1.

**Formula used: `fillAmount = shrink`.** The fill visibly completes exactly at
confirm, as the acceptance criterion requires. (`shrink` is a misleading name —
it is a *growth* ratio; the old quad fed it to `localScale`, so the spinner grew
from 0 to full size.)

`t_max`, `t_max_numb` and `range` are untouched, as is the spacebar fallback
(`Input.GetKeyDown("space")` -> `HandleHMDUnmounted` in `Update`).

## Plumbing

Six `gaze_trigger` instances exist (`restart`, `resume`, `advance`, `spec`,
`warp`, `blackhole`) and they are created and ticked inside `Main`, not exposed
as components, so `ReticleView` cannot hold a reference to "the active one".
Instead `tick()` publishes into three **static** fields on `gaze_trigger`:

```
frame_progress       = max(shrink) over triggers that were in range this frame
frame_in_range       = any trigger in range this frame
frame_just_triggered = any trigger confirmed this frame
```

`ReticleView.LateUpdate` reads them and calls `gaze_trigger.ResetFrame()`, so
the values never leak across frames. Writing is one compare per tick and no
allocation. At most one trigger is in range at a time (they are gated by
scene/phase), and `max` is the correct reduction if that ever stops being true.

This keeps the dwell state machine entirely in `Main.cs`: the reticle is a pure
observer and cannot change when or whether a trigger fires.

## Scene objects

Built by `Assets/Editor/ReticleCanvasSetup.cs`, menu
`icecube/Reticle/Setup Scene Reticle` (and `ReticleCanvasSetup.Run` in batch).
Both entry points are idempotent — re-running finds the existing objects by
name and re-applies values.

```
UI_Canvas                       (Screen Space Overlay, from card N4)
  ...captions...
  Reticle                       anchors 0.5/0.5, pivot 0.5/0.5, pos 0,0, 48x48
                                CanvasGroup alpha 0.5, blocksRaycasts off
                                ReticleView
    Outline  Image  reticle_outline.png  48x48  black
    Ring     Image  reticle_ring.png     48x48  white
    Fill     Image  reticle_ring.png     48x48  Filled / Radial360 / Top / CW
    Dot      Image  reticle_dot.png       8x8   white
```

`Reticle` is forced to be the **last sibling** of the canvas so its draw order
relative to the captions is deterministic (they do not overlap spatially, but
ordering should not depend on insertion history).

If no Screen Space Overlay canvas exists (N4 not yet merged into the branch
being built), the setup script creates one named `UI_Canvas` with N4's
contract — overlay, `pixelPerfect`, `ScaleWithScreenSize`, 1280x720 reference,
`matchWidthOrHeight = 1` (match height, so 48 px stays 48 px at 720p). It never
creates a *second* overlay canvas: any existing one wins.

## Sprites

`icecube/Reticle/Generate Sprites` writes three RGBA PNGs under
`Assets/UI/Reticle/`, at 2x the on-screen size so the UI downsample is a clean
bilinear minification rather than a magnification:

| File | Texture | Geometry (in 48 px units) |
|---|---|---|
| `reticle_ring.png` | 96x96 | white annulus, outer r 21, thickness 3 |
| `reticle_outline.png` | 96x96 | black annulus, outer r 22.5, inner r 16.5 (1.5 px halo each side of the ring) |
| `reticle_dot.png` | 16x16 | white disc, diameter 8 |

The dot gets its own tight 16x16 texture rather than sharing the 96x96 sheet:
in an 8x8 rect a 96x96 sprite would be minified 12x and disappear.

Coverage is computed with a fixed 4x4 supersample per pixel and constant RGB
with varying alpha, so bilinear taps on the transparent side never bleed a
different hue into the edge. Generation is deterministic — fixed loops, no
randomness, no time or GUID input — so re-running produces byte-identical PNGs.

Importer settings applied by the script: `Sprite (2D and UI)`, Single,
`FullRect` mesh, mipmaps **off**, Bilinear, Clamp, `alphaIsTransparency`,
sRGB, **Uncompressed** (these are tiny; DXT crunch would eat the edge), 100
pixels-per-unit.

## Look

`ReticleView` drives a `CanvasGroup`, not per-Image alpha, so opacity is one
value:

| State | Alpha | Fill colour |
|---|---|---|
| no gaze target to confirm (`armed == false`) | 0.35 | — (fill disabled) |
| idle, target available, not dwelling | 0.5 | — (fill disabled) |
| dwelling (`in_range`) | 1.0 | `inRangeColor` cyan |
| confirming frame + 0.18 s hold | 1.0 | `confirmColor` green, fill pinned at 1.0 |

Alpha follows its target with `1 - exp(-rate * dt)`, which is frame-rate
independent (card N7's rule); the confirm flash is cosmetic and does not gate
confirmation. The `Fill` Image is `enabled = false` while `fillAmount == 0`, so
an idle reticle is 3 draws, not 4.

`armed` is set each frame by `Main.SyncUIReticle()` from
`gaze_reticle.activeSelf` — i.e. from the *existing* gaze-target logic. This
card does not change when gaze targets appear; that is card N10.

## Verify from the shell

```bash
# the reticle objects exist, under the overlay canvas
grep -n 'm_Name: Reticle$' Assets/Scenes/main.unity
grep -n 'm_Name: \(Outline\|Ring\|Fill\|Dot\)$' Assets/Scenes/main.unity

# legacy quads kept, renderers off (m_Enabled: 0 on their MeshRenderers)
python3 tools/reticle_verify.py
```

`tools/reticle_verify.py` parses the scene YAML and asserts: one overlay
canvas; `Reticle` + its four Images present under it; `Cam_Reticle` and
`Cam_Spinner_Reticle` GameObjects still present with `MeshRenderer.m_Enabled:
0`; and the camera inventory unchanged at 9 with 6 enabled.
