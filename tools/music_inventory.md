# N14 — Music inventory and re-encode record

Source of truth for the music streaming migration (card N14).

## 1. Clip inventory and reference sites

All 16 music clips lived in `Assets/Resources/Music/<Spec>/<Scene>.mp3`.
Every source clip was **MP3, 44.1 kHz, stereo**, 160–320 kbps.

`Main.cs` references them **only** by `Resources.Load`, built from the
scene/spec name tables — there are no serialized AudioClip references:

```csharp
// Main.cs:1333  music_files[i,j] = "Music/"+spec_names[j]+"/"+scene_names[i];
// Main.cs:1342  musics[i,j] = Resources.Load<AudioClip>(music_files[i,j]);
```

Consumers of the loaded clips (all via the single `music_audiosource`):

| Main.cs line | What it does |
|---|---|
| 363 | `AudioSource music_audiosource;` — the one music channel |
| 366 | `AudioClip[,] musics;` — `[SCENE.COUNT, SPEC.COUNT]` clip table |
| 455 | sets `music_audiosource.volume` from `music_vols` |
| 594 | `music_audiosource.Stop()` on reset |
| 1594 | creates the AudioSource on the `Script` GameObject |
| 2120-2123 | credits: stop, assign VIZ clip, set volume, play |
| 2789-2798 | spec switch: preserves `music_audiosource.time` across the swap |
| 2886-2890 | restarts music when it is not playing |

**Scene/prefab GUID scan:** no `.unity`, `.prefab`, `.asset` or `.mat` file
references any music clip GUID, so removing them from `Resources` breaks no
serialized reference.

**Null checks (feeds N15):** none of the call sites null-check `musics[i,j]`.
`Resources.Load` now returns `null` for these names, so N15 must route these
eight sites through `Html5Music` rather than the AudioSource.

**Importer settings of the originals** (identical for all 16 clips):
`loadType: 1` (compressed in memory), `compressionFormat: 1` (Vorbis),
`quality: 0.4`, `preloadAudioData: 1`, **`forceToMono: 1`**, `normalize: 1`, `3D: 1`.

> The `forceToMono: 1` flag is the key finding: Unity already downmixed every
> music clip to mono at import time, so the shipped build never contained
> stereo music. Encoding the OGGs as mono is therefore **not** a quality
> reduction relative to the current build — it reproduces it exactly, and it
> is what brings the payload inside the 25 MB budget (see §3).

## 2. Re-encode command

```sh
ffmpeg -y -i <src>.mp3 -vn -map_metadata -1 -ac 1 -ar 44100 \
       -c:a libvorbis -q:a 3 Assets/StreamingAssets/music/<Spec>/<Scene>.ogg
```
`-q:a 3` per owner decision C4; `-ac 1` mirrors the originals' `forceToMono: 1`;
`-map_metadata -1` strips tags so output is reproducible.

## 3. Size and fidelity table

| Track | Dur (s) | Src ch | Src MP3 | OGG q3 mono | Saved | OGG dur | OGG ch | Original GUID |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| `Viz/Ice` | 213.2 | 2 | 4.89 MB | 1.42 MB | 71% | 213.5 | 1 | `1e0efdf1bdd004d01aa64c43323e0a5a` |
| `Viz/Voyager` | 192.4 | 2 | 4.41 MB | 1.56 MB | 65% | 192.7 | 1 | `8b735f3e35f204e04ba4f48e6afb6e17` |
| `Viz/Nothing` | 198.3 | 2 | 4.55 MB | 1.01 MB | 78% | 198.6 | 1 | `d8c1a74a8424349d69e94b765d182127` |
| `Viz/Extreme` | 224.7 | 2 | 5.15 MB | 1.67 MB | 68% | 225.0 | 1 | `43327b76d8cfa42c78b6d035a0c021bf` |
| `Viz/Earth` | 187.9 | 2 | 4.31 MB | 1.57 MB | 64% | 188.2 | 1 | `ac7a48b77ca5e4e32be329d0d07d6b5f` |
| `Viz/Credits` | 187.9 | 2 | 4.31 MB | 1.58 MB | 63% | 188.2 | 1 | `dc9fa574fcd3d4268aaaffbaafff08a0` |
| `Gam/Ice` | 66.0 | 2 | 2.52 MB | 0.45 MB | 82% | 66.0 | 1 | `fa92d52baa78c4f478ea92053c5ce61b` |
| `Gam/Voyager` | 192.4 | 2 | 4.41 MB | 1.61 MB | 63% | 192.7 | 1 | `e7d2abacdf03a43a7b0e765af42ef04d` |
| `Gam/Nothing` | 198.3 | 2 | 4.55 MB | 0.75 MB | 83% | 198.6 | 1 | `b4c54b14b51b1415390acbd3b2180d7e` |
| `Gam/Extreme` | 224.7 | 2 | 5.15 MB | 1.67 MB | 68% | 225.0 | 1 | `1b98711f463ed41df827516f959d14b6` |
| `Gam/Earth` | 0.5 | 2 | 0.01 MB | 0.01 MB | 47% | 0.5 | 1 | `603df148818b24b5c9d3e461d986c419` |
| `Neu/Ice` | 66.0 | 2 | 2.52 MB | 0.46 MB | 82% | 66.0 | 1 | `622b6a7a1f3b548a8bb1c524abeab839` |
| `Neu/Voyager` | 192.4 | 2 | 4.41 MB | 1.69 MB | 62% | 192.7 | 1 | `9c0a6527352ae4cdc84f1d84188a6bc4` |
| `Neu/Nothing` | 198.3 | 2 | 4.55 MB | 1.08 MB | 76% | 198.6 | 1 | `50d8a578ac1f144cf80a141e48e5cdac` |
| `Neu/Extreme` | 224.7 | 2 | 5.15 MB | 1.72 MB | 67% | 225.0 | 1 | `01e589ec5bae54d2a9c65b4b2e571891` |
| `Neu/Earth` | 0.5 | 2 | 0.01 MB | 0.01 MB | 47% | 0.5 | 1 | `ab6f8ad3ffe7b4700aa8c3103418fd50` |
| **total (16)** | | | **60.9 MB** | **18.3 MB** | **70%** | | | |

**Payload: 18.3 MB — within the 25 MB acceptance budget.**
Every output verified: `OggS` magic, codec `vorbis`, 44100 Hz,
duration within 0.5 s of its source (max drift 0.33 s, from MP3 encoder padding).

Reference points, for the record (same q3 setting):
- stereo q3 would be 30.9 MB — over budget, and stereo the build never shipped.
- mono q3 (shipped here): 18.3 MB.

## 4. Originals

Originals (clips **and** their `.meta` files, plus the folder metas) are preserved at:

```
/home/watson/icecube-backups/music-src/{Gam,Neu,Viz}/<Scene>.mp3[.meta]
```
Verified bit-identical by `md5sum` (32 files) **before** deletion from
`Assets/Resources/Music/`. Nothing was deleted outside the repo. No `.bak`
files were created in `Assets/` or `ProjectSettings/`.

## 5. Kill criterion

Not triggered. No music track is sample-synced to narration: music and
voiceover run on two independent `AudioSource`s with no shared clock, and the
only timing logic is `music_audiosource.time` being carried across a spec
switch (Main.cs:2789-2798) — a music-to-music continuity concern, not
narration sync. All 16 tracks therefore stream; none is kept as a Unity clip.

## 6. Not in this card

`Main.cs` is untouched: wiring the eight call sites above to `Html5Music` is
card **N15**. SFX and Voiceover are untouched and still load from `Resources`.
