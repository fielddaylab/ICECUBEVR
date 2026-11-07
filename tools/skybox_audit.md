# N13 — Skybox: cap faces at 2048, DXT1 + Crunch

Baseline git sha `3eda0fc087223baefd72db30e73d093afa0c4807` (branch
`card-1901016801358120722-n13-…`, base `webgl-chromebook`).
Pristine sources + metas copied to
`/home/watson/icecube-backups/skybox-src/` (`cp -a`, 177 files, 132,787,984 B)
before any change. **No `.bak` files exist under `Assets/` or
`ProjectSettings/`** and no source image was modified or resized — Unity
downsamples at import, so only `.meta` importer settings changed.

Owner decision **C5 is resolved to 2048 per face** by binding default #5
("Skybox: cap 2048/face", approved djgagnon 2026-11-04). No question raised.

---

## 1. What was actually wrong (the real finding)

The card's premise was that the skybox was uncapped. It was not. Commit
`a5ace81` ("WebGL: compress Resources/Textures") already set, on all 60
textures:

```
buildTarget: WebGL   maxTextureSize: 2048   textureCompression: 3 (CompressedLQ)
compressionQuality: 50   crunchedCompression: 1   overridden: 1
```

…but left `textureFormat: -1` (**Automatic**) and `alphaUsage: 1`
(From Input). Measured facts about what Automatic resolved to:

* **36 of the 60 textures are 32-bit RGBA PNGs** (all `Credits/*` and
  `Earth/*` faces, 2048×2048, colortype 6).
* **Every one of those alpha channels is fully opaque**
  (`alpha_min == alpha_max == 255`, PIL `getextrema()` over all 36 files).
* Unity's automatic picker treats a fully-opaque alpha as "no alpha", so on
  this exact source set Automatic **already resolved to DXT1 Crunched** — the
  per-texture sizes in the baseline build report
  (`555.4 kb` for `SkyboxZ-.png`, etc.) are byte-identical before and after
  pinning the format. The payload is therefore **not** going to shrink by
  100 MB from this card: the skybox share of the compressed
  `webgl.data.unityweb` is ~34 MB, not 128 MB (see §5).

What pinning `textureFormat: 28` (DXT1Crunched) + `alphaUsage: 0` does buy
is a guarantee: the day an artist re-exports one of those faces with a single
stray non-255 alpha pixel, Automatic would silently flip it to DXT5 Crunched
and **double** the GPU footprint (16 MB vs 8 MB per cubemap) — that cannot
happen any more, and `tools/verify_skybox_meta.py` fails if it is attempted.

Secondary gaps closed: `filterMode` was `1` (Bilinear) on all 60 — the card
asks for **trilinear**, which matters because a skybox face fills the frame at
the horizon and minifies hard toward the poles, so the mip chain is what a
Chromebook actually samples. `textureCompression` moved from `3`
(CompressedLQ) to `1` (Compressed) because the format is now explicit.

### Why there is no per-texture "crunch fallback" (dead end, reverted)

An earlier draft of this card compared the size of the imported artifact in
`Library/metadata/<guid>` against the theoretical DXT1 size and switched
any texture where "crunch came out larger" to plain DXT1. That reasoning was
wrong: Unity pads the serialized crunched texture to the uncompressed DXT
size with `0xCD` bytes (measured: `Neu/Voyager` artifact 10.9 MB of which
9.1 MB is `0xCD` padding, real crunch data 1.84 MB). The one texture it
"fixed" — `Gam/Voyager/Voyager.png` — went from ~1.9 MB to **9.8 MB**
LZ4-compressed in the build and grew `webgl.data.unityweb` by **5.4 MB**
(136,977,415 → 142,342,650 B). That change is reverted here; all 60 textures
are DXT1Crunched, and the only sanctioned fallback is the card's kill
criterion, applied to the whole tree via `SkyboxImportSettings.UseCrunch`.

---

## 2. Inventory

60 textures, 18 materials, 3 spectrum variants (`Gam` / `Neu` / `Viz`) × 6
scenes. Loaded at runtime by name, not by scene reference —
`Assets/Scripts/Main.cs:1366` builds
`"Skybox/" + spec + "/" + scene + "/" + scene` and
`Main.cs:1642` does `Resources.Load<Material>(...)`. `Resources/` is
force-included in WebGL builds, so **nothing here can be stripped, only
recompressed**.

### Classification

| Class | Count | Files | Shape |
|---|---:|---|---|
| `SIX_SIDED_FACE` | 36 | `{Gam,Neu,Viz}/{Credits,Earth}/Skybox{X,Y,Z}{+,-}.png` 2048² RGBA | 2D |
| `SIX_SIDED_FACE` | 18 | `{Gam,Neu,Viz}/Ice/Skybox{X,Y,Z}{+,-}.jpg` 2048² RGB | 2D |
| `EQUIRECT → CUBEMAP` | 6 | `{Gam,Neu,Viz}/{Nothing,Voyager}/*.png` 8192×4096 RGB | Cube (`textureShape: 2`, `generateCubemap: 6`) |

The six 8192×4096 equirects are **not** left as 2:1 panoramas: their importers
already carry `textureShape: 2`, so Unity generates a real cubemap from the
latlong source. Capping `maxTextureSize` to 2048 therefore yields a 2048/face
cubemap, which is exactly what the card asks for — the `4096x2048 crunched`
equirect branch of step 3 does not apply. Confirmed in the apply log:
`SKYBOX_TEX … Nothing.png src=2048x2048 capped=2048x2048 shape=Cube`.

### Material → texture bindings

`Skybox/Cubemap` is shader `fileID: 104`, `Skybox/6 Sided` is `fileID: 103`
(both built-in, guid `0000000000000000f000000000000000`).

| Material | Shader | Slots → textures |
|---|---|---|
| `{Gam,Neu,Viz}/Credits/Credits.mat` | Skybox/Cubemap | `_FrontTex`→`SkyboxZ+`, `_BackTex`→`SkyboxZ-`, `_LeftTex`→`SkyboxX+`, `_RightTex`→`SkyboxX-`, `_UpTex`→`SkyboxY+`, `_DownTex`→`SkyboxY-` (same dir) |
| `{Gam,Neu,Viz}/Earth/Earth.mat` | Skybox/Cubemap | same 6 slots, own dir |
| `{Gam,Neu,Viz}/Ice/Ice.mat` | Skybox/Cubemap | all three point at **`Viz/Ice/*.jpg`** — the `Gam/Ice` and `Neu/Ice` jpgs on disk are byte-identical duplicates that no material binds |
| `{Gam,Neu,Viz}/Nothing/Nothing.mat` | Skybox/6 Sided | `_Tex` → own `Nothing.png` cubemap; its six `_*Tex` slots point at unrelated textures outside `Resources/Skybox` |
| `{Gam,Neu,Viz}/Voyager/Voyager.mat` | Skybox/6 Sided | `_Tex` → own `Voyager.png` cubemap |
| `Gam/Extreme/Extreme.mat` | Skybox/6 Sided | `_Tex` → `Gam/Voyager/Voyager.png` (shared) |
| `Neu/Extreme/Extreme.mat` | Skybox/6 Sided | `_Tex` → `Neu/Voyager/Voyager.png` (shared) |
| `Viz/Extreme/Extreme.mat` | Skybox/6 Sided | `_Tex` → `Viz/Voyager/Voyager.png` (shared) |

**Flagged for the owner (no action taken under this card):**

* **12 unreferenced duplicate faces.** `Gam/Ice/*.jpg` and `Neu/Ice/*.jpg`
  are byte-identical to `Viz/Ice/*.jpg`, and all three `Ice.mat` files bind
  the `Viz` copies. Unity dedupes identical *imported* texture data poorly
  across distinct assets, so these 12 files are very likely pure payload.
  Deleting them is a behaviour-visible asset change and belongs on its own
  card, not here.
* **No HDR sources.** No `.exr`/`.hdr`/16-bit files anywhere in the tree; the
  heaviest sources are 8-bit PNG. There is no HDR-block-format question to
  answer for WebGL.
* **No meaningful alpha.** 36 RGBA faces, all uniformly opaque (see §1). The
  LDR DXT1 treatment is lossless with respect to alpha.
* **Two `.png` files are actually JPEG payloads** with a `.png` extension
  (`Gam/Nothing/Nothing.png`, `Gam/Voyager/Voyager.png`). Unity reads them
  fine; worth knowing if anyone ever scripts over this tree by magic bytes.

---

## 3. Settings applied

`Assets/Editor/SkyboxImportSettings.cs` — `SkyboxImportSettings.Apply()`,
idempotent, batchmode-safe, exits non-zero on failure. Per texture:

| Setting | Value | Why |
|---|---|---|
| WebGL `overridden` | `1` | platform override, not the default platform |
| WebGL `maxTextureSize` | `2048` | owner decision C5 / binding default #5 |
| WebGL `textureFormat` | `28` = `DXT1Crunched` | RGB Crunched DXT1, pinned (was Automatic) |
| WebGL `textureCompression` | `1` = `Compressed` | normal quality, not LQ, since the format is now explicit |
| WebGL `crunchedCompression` | `1` | Crunch transcode on top of DXT1 |
| WebGL `compressionQuality` | `50` | crunch quality 50 per the card |
| `enableMipMap` | `1` | already on; asserted |
| `filterMode` | `2` = Trilinear | was `1` (Bilinear) on all 60 |
| `alphaUsage` | `0` = None | stops Automatic ever re-picking DXT5 |

The kill criterion is a single switch: `SkyboxImportSettings.UseCrunch`
(`const bool`, default `true`). Setting it to `false` and re-running Apply
moves the whole tree to plain `DXT1` (`textureFormat: 10`,
`crunchedCompression: 0`); `Verify()` and `verify_skybox_meta.py
--expect-format dxt1` then accept only that state. Mixed trees fail both.

`SkyboxImportSettings.Verify()` re-reads every importer through the Unity
API, asserts the imported texture does not exceed the cap, and reports the
theoretical DXT1+mip GPU footprint. Final run (pass 3, after the dead-end
fallback was removed; `changed=0` because the metas were already in the
target state, which is the idempotency check):

```
SKYBOX_IMPORT result=Applied textures=60 changed=0 unchanged=60 \
  maxSize=2048 format=DXT1Crunched quality=50
SKYBOX_VERIFY result=Succeeded textures=60 failures=0 format=DXT1Crunched \
  cubemaps=6 faces=54 gpu_dxt1_mipped_bytes=251658180 gpu_mb=240.0
```

240.0 MB is the *whole tree* resident at once — 18 cubemaps' worth. Only one
spectrum × one scene is bound to the camera at a time, so the live skybox cost
is **8 MB** (one 2048 cubemap, DXT1, full mip chain: 6 × 2048² × 0.5 × 4/3)
on any device exposing `WEBGL_compressed_texture_s3tc`. On an ARM Chromebook
without S3TC the DXT data software-decompresses to RGBA32 — 128 MB resident
for one cubemap — which is the N16 card's territory, not this one's.

### Verifying the metas persisted (Unity-free)

`tools/verify_skybox_meta.py` parses the serialized YAML on disk rather than
trusting an editor API call, so it proves the settings survived the reimport.
It is the guard the card asks for ("Verify .meta importer settings
persisted"):

```
$ python3 tools/verify_skybox_meta.py
SKYBOX_META_VERIFY result=Succeeded failures=0 textures=60 expect=crunched \
  maxTextureSize=2048 quality=50 crunched=60 dxt1=0
```

Exit 0 on pass, 1 on any mismatch, with one `FAIL <path>: <key>=<got>
expected <want>` line per problem. It also fails on any `.bak` file under the
scanned root (card guard). Against the pristine backup tree
(`--root /home/watson/icecube-backups/skybox-src`) it reports
`failures=240` — exactly the four real deltas (`textureFormat=-1`,
`textureCompression=3`, `filterMode=1`, `alphaUsage=1`) × 60 textures.

Representative diff (`Gam/Earth/SkyboxZ-.png.meta`); **sources untouched**,
`git status --short Assets/Resources/Skybox` lists only `.meta` files:

```diff
-    filterMode: 1
+    filterMode: 2
-  alphaUsage: 1
+  alphaUsage: 0
     buildTarget: WebGL
-    textureFormat: -1
-    textureCompression: 3
+    textureFormat: 28
+    textureCompression: 1
```

---

## 4. Commands

Unity 2019.4.40f1 headless, same container and `WebGLBuilder.Build`
entrypoint every other card uses. Logs land in
`/home/watson/icecube-out-n13/`, never in the repo.

```sh
docker run --rm --name icecube-unity-n13 \
  -v /home/watson/icecube-worktrees/1901016801358120722:/project \
  -v /home/watson/icecube-out-n13:/out \
  -v icecube-unity-license-b:/root/.local/share/unity3d \
  unityci/editor:ubuntu-2019.4.40f1-webgl-3 \
  unity-editor -quit -batchmode -nographics -silent-crashes \
  -projectPath /project -buildTarget WebGL \
  -executeMethod SkyboxImportSettings.Apply -logFile /out/n13_apply3.log
# then -executeMethod WebGLBuilder.Build -logFile /out/n13_build4.log
```

Wrapper script: `/home/watson/icecube-backups/skybox-src/logs/n13_apply3.sh`,
launched under `nohup` and polled for `BUILD_RESULT result=Succeeded errors=0`
(final: `BUILD_RESULT result=Succeeded errors=0 warnings=25 size=423588587`). In a headed editor the same two
steps are on the menu under **icecube > Skybox**.

---

## 5. Results

### Payload (`webgl.data.unityweb`, gzip, bytes)

| Build | Bytes | Δ vs baseline | Note |
|---|---:|---:|---|
| Baseline (`3eda0fc`, card's measured state) | 136,987,233 | — | `/home/watson/icecube-out-a`, pre-N13 |
| Baseline re-measured (`icecube-out-b`, N17 pre-change) | 137,104,244 | +117,011 | same tree, different sibling-card state |
| N13 pass 2 (per-texture artifact-size fallback, **reverted**) | 142,342,650 | **+5,355,417** | `Gam/Voyager` forced to uncrunched DXT1 |
| **N13 pass 3 (this commit: 60/60 DXT1Crunched, pinned)** | **136,976,806** | **−10,427** | `n13_build4.log` |

Build-report texture category: 156.4 MB uncompressed, unchanged from
baseline. Per-asset uncompressed sizes for all 60 skybox textures are
**byte-identical** to the baseline build report (`diff` of the two
"Used Assets" tables is empty). That is the proof that Automatic had already
chosen DXT1Crunched on this source set; pinning it is a guard, not a diet.

### Why the ">= 100 MB" acceptance clause cannot be met by any importer setting

The card's `128 MB source` figure is the size of the PNG/JPG **sources** on
disk, which are never shipped. What ships is the imported, crunched,
LZ4-blocked data inside `data.unity3d`. Measured by LZ4HC-compressing each
imported artifact (padding stripped) exactly as the bundle does:

| Group | Count | Compressed in payload |
|---|---:|---:|
| `Credits`/`Earth` faces (36 × 2048² DXT1Crunched) | 36 | ~15.2 MB |
| `Ice` faces (18 × 2048² DXT1Crunched, 12 are unreferenced dupes) | 18 | ~3.5 MB |
| `Nothing` cubemaps (3 × 2048/face) | 3 | ~1.5 MB |
| `Voyager` cubemaps (3 × 2048/face) | 3 | ~7.5 MB |
| **Whole skybox tree** | **60** | **~27.7 MB** |

Deleting the entire skybox would save ~28 MB; no compression setting can
save 100 MB from it. The dominant payload is elsewhere
(`sharedassets0.assets` ≈ 61 MB compressed: `Earth_*` 21 MB textures,
`icl.obj` 22 MB mesh; `resources.resource` 51 MB of audio — see cards
N14/N15). This card's **measured** deliverables are therefore:

* payload: **holds** (−10 KB), with the +5.4 MB regression from the earlier
  N13 draft removed;
* GPU: **8 MB per live cubemap pinned** (DXT1); the DXT5 doubling path is
  closed by `alphaUsage: 0` + explicit format and is verifier-enforced;
* sampling: trilinear on all 60 (was bilinear);
* real payload wins still available in this tree, flagged for the owner:
  the 12 unreferenced duplicate `Ice` faces (~2.3 MB) and the
  `Gam/Voyager` source, whose crunched size (3.6 MB) is 2× its `Neu`/`Viz`
  siblings because the `.png` is a re-saved JPEG with block noise.

### Frame time

PERFPROBE p50 is not measurable headless (SwiftShader). The shader, draw
count and GPU format of the live skybox are unchanged versus the baseline
build, so the expected delta is zero; the trilinear switch adds one mip
fetch per sample on the skybox pass only. To be read on hardware with the N1
harness — see §6.

---

## 6. Deferred to hardware (not verifiable headless)

The container renders through SwiftShader on CPU, so neither remaining
acceptance clause can be settled here. Both are for the human staging check at
1366×768 on the Chromebook:

1. **No visible banding/blocking in the skybox.** Look specifically at the
   smooth gradients: the `Nothing` and `Voyager` starfield/nebula cubemaps and
   the sky gradient near the horizon on `Earth`. If blocking is visible on
   those gradients, the card's **kill criterion** applies — set
   `SkyboxImportSettings.UseCrunch = false`, rerun Apply, verify with
   `python3 tools/verify_skybox_meta.py --expect-format dxt1`, and accept
   ~12 MB of raw DXT1 per cubemap in the payload (LZ4 recovers some of it;
   `Gam/Voyager` measured 9.8 MB compressed in that state). Note the crunch
   step predates this card, so a blocking verdict is evidence against
   `a5ace81`, not against the DXT1 switch.
2. **Frame-time p50 improves or holds.** Read PERFPROBE p50 from the N1
   harness on the same machine and canvas size as the pre-change build. The
   expected mechanism is less texture upload and half the sampler bandwidth on
   Credits/Earth; the draw-call count and shader are unchanged, so a
   regression would be surprising and would point elsewhere.

## 7. Out of scope / not done

* `textureQuality` and `masterTextureLimit` are untouched (explicit DO NOT).
  They remain forced to 0 at runtime by other code.
* No `ProjectSettings/` change, no scene change, no runtime code change, no
  `.jslib` change, no `Packages/manifest.json` change.
* The 12 duplicate `Ice` faces and the `.png`-named-JPEG oddity are reported
  above, not fixed.
