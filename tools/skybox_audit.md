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

…but left `textureFormat: -1` (**Automatic**). That one field is the whole
story:

* **36 of the 60 textures are 32-bit RGBA PNGs** (all `Credits/*` and
  `Earth/*` faces, 2048×2048, colortype 6).
* For an RGBA source, Unity's automatic WebGL pick is **DXT5 Crunched** —
  1.0 byte/px — not DXT1 Crunched at 0.5 byte/px.
* **Every one of those alpha channels was measured fully opaque**
  (`alpha_min == alpha_max == 255`, PIL `getextrema()` over all 36 files).

So half the GPU footprint and a large share of the payload of the dominant
asset group was being spent storing the constant value 255, 4.19 million times
per face, 36 times over. Forcing `RGB Crunched DXT1` is therefore **not a
quality trade at all** on these files — it is a lossless removal of a dead
channel. The kill criterion ("if crunch introduces visible blocking on
gradients, fall back to DXT1 uncrunched") is about the *crunch* step, which
was already in place before this card and is unchanged in quality terms.

Secondary gaps closed: `filterMode` was `1` (Bilinear) on all 60 — the card
asks for **trilinear**, which matters because a skybox face fills the frame at
the horizon and minifies hard toward the poles, so the mip chain is what a
Chromebook actually samples. `alphaUsage` was `1` (From Input) and is now `0`
(None) so the automatic path can never re-promote these to DXT5 on a future
reimport.

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
* **Three `.png` files are actually JPEG payloads** with a `.png` extension
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
| WebGL `textureFormat` | `28` = `DXT1Crunched` | RGB Crunched DXT1; halves the 36 RGBA faces |
| WebGL `textureCompression` | `1` = `Compressed` | normal quality, not LQ, since the format is now explicit |
| WebGL `crunchedCompression` | `1` | Crunch transcode on top of DXT1 |
| WebGL `compressionQuality` | `50` | crunch quality 50 per the card |
| `enableMipMap` | `1` | already on; asserted |
| `filterMode` | `2` = Trilinear | was `1` (Bilinear) on all 60 |
| `alphaUsage` | `0` = None | stops Automatic ever re-picking DXT5 |

`SkyboxImportSettings.Verify()` re-reads every importer through the Unity API
and additionally reports the theoretical DXT1+mip GPU footprint. Run result:

```
SKYBOX_IMPORT result=Applied textures=60 changed=60 unchanged=0 \
  maxSize=2048 format=DXT1Crunched quality=50
SKYBOX_VERIFY result=Succeeded textures=60 failures=0 \
  gpu_dxt1_mipped_bytes=251658180 gpu_mb=240.0
```

240.0 MB is the *whole tree* resident at once — 18 cubemaps' worth. Only one
spectrum × one scene is bound to the camera at a time, so the live skybox cost
is **8 MB** (one 2048 cubemap, DXT1, full mip chain: 6 × 2048² × 0.5 × 4/3).
Before this card the same cubemap cost **16 MB** on the Credits/Earth scenes
(DXT5). On an ARM Chromebook lacking `WEBGL_compressed_texture_s3tc` the DXT
data software-decompresses to RGBA32, where the before/after is 128 MB vs
128 MB resident — identical, because the decompressed form has no alpha
saving; the win there is purely download size and decode time.

### Verifying the metas persisted (Unity-free)

`tools/verify_skybox_meta.py` parses the serialized YAML on disk rather than
trusting an editor API call, so it proves the settings survived the reimport.
It is the guard the card asks for ("Verify .meta importer settings
persisted"):

```
$ python3 tools/verify_skybox_meta.py
SKYBOX_META_VERIFY result=Succeeded textures=60 failures=0 \
  maxTextureSize=2048 format=DXT1Crunched quality=50
```

Exit 0 on pass, 1 on any mismatch, with one `FAIL <path>: <key>=<got>
expected <want>` line per problem. Against the pre-change tree it correctly
reported `failures=120` — exactly the two real deltas (`textureFormat=-1`,
`filterMode=1`) × 60 textures.

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
  -executeMethod SkyboxImportSettings.Apply -logFile /out/n13_apply.log
# then -executeMethod WebGLBuilder.Build -logFile /out/n13_build.log
```

Both were launched under `nohup` and polled for
`BUILD_RESULT result=Succeeded errors=0`. In a headed editor the same two
steps are on the menu under **icecube > Skybox**.

---

## 5. Results

<!-- N13_RESULTS -->

---

## 6. Deferred to hardware (not verifiable headless)

The container renders through SwiftShader on CPU, so neither remaining
acceptance clause can be settled here. Both are for the human staging check at
1366×768 on the Chromebook:

1. **No visible banding/blocking in the skybox.** Look specifically at the
   smooth gradients: the `Nothing` and `Voyager` starfield/nebula cubemaps and
   the sky gradient near the horizon on `Earth`. If blocking is visible on
   those gradients, the card's **kill criterion** applies — set
   `s.format = TextureImporterFormat.DXT1` and
   `s.crunchedCompression = false` in `SkyboxImportSettings.Apply()`
   (two lines), rerun Apply, and accept ~12 MB per cubemap. Note the crunch
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
