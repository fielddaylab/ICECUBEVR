# N15 — Music cue rewire (Main.cs -> Html5Music)

Card N15. Depends on N14, which moved the 16 music tracks out of
`Assets/Resources/Music` (MP3, decoded to float PCM by Unity WebGL) into
`Assets/StreamingAssets/music/<Spec>/<Scene>.ogg` and added the
`Html5Music` wrapper over `Assets/Plugins/WebGL/html5audio.jslib`.

This card rewires every music call site in `Main.cs` to that wrapper and adds the
autoplay gate. **SFX and voiceover are untouched** — they are still ordinary
`AudioSource`s loading from `Resources`, and no VO timing or caption cue moved.

## 1. Track ids

The old clip path and the new track id differ only by the `Music/` prefix, so the
map is mechanical and total — all 16 former clips have an entry:

| old `Resources.Load` path | new Html5Music track id | streamed file |
|---|---|---|
| `Music/Viz/Ice` | `Viz/Ice` | `StreamingAssets/music/Viz/Ice.ogg` |
| `Music/Viz/Voyager` | `Viz/Voyager` | …`/Viz/Voyager.ogg` |
| `Music/Viz/Nothing` | `Viz/Nothing` | …`/Viz/Nothing.ogg` |
| `Music/Viz/Extreme` | `Viz/Extreme` | …`/Viz/Extreme.ogg` |
| `Music/Viz/Earth` | `Viz/Earth` | …`/Viz/Earth.ogg` |
| `Music/Viz/Credits` | `Viz/Credits` | …`/Viz/Credits.ogg` |
| `Music/Gam/{Ice,Voyager,Nothing,Extreme,Earth}` | `Gam/<Scene>` | …`/Gam/<Scene>.ogg` |
| `Music/Neu/{Ice,Voyager,Nothing,Extreme,Earth}` | `Neu/<Scene>` | …`/Neu/<Scene>.ogg` |

Built in `Main.cs` by the same loop that built the old paths:

```csharp
music_files[i,j] = spec_names[j]+"/"+scene_names[i];   // was "Music/"+…
```

`Gam/Credits` and `Neu/Credits` never existed as clips (the credits scene only has
a VIZ bed) — `Resources.Load` returned null for them before and `Html5Music` will
404 for them now. Same behaviour, no new failure mode: the credits cue explicitly
asks for `SPEC.VIZ`.

## 2. Call-site rewrite

Every cue now goes through one helper, `Main.PlayMusic(scene, spec, start_time)`,
which is the only place in the project that calls `Html5Music.Play`.

| Cue | Trigger in Main.cs | Old call | New call | loop | volume |
|---|---|---|---|---|---|
| **scene bed** | `SetupScene()` — end of the per-scene setup, fires on load, on every portal transition and on both restart/resume paths | `music_audiosource.Stop(); .clip = musics[scene,VIZ]; .volume = music_vols[scene,VIZ]; .Play()` | `PlayMusic(cur_scene_i, SPEC.VIZ, 0f)` | false | `music_vols[scene,VIZ]` |
| **spec switch** | `UpdateScene()`, `spec_trigger.just_triggered && old_spec != cur_spec_i` | read `music_audiosource.time`, `Stop()`, swap clip, restore `.time`, `Play()` | `old_time = Html5Music.Time; Html5Music.Stop(); PlayMusic(cur_scene_i, cur_spec_i, old_time)` | false | `music_vols[scene,spec]` |
| **track ran out** | `UpdateScene()`, `music_was_playing && !IsPlaying()` | re-assign clip + `Play()` every frame it was idle | same, but polled on a 0.5 s timer (see §4) | false | `music_vols[scene,spec]` |
| **volume remap** | `MapVols()` (inspector volume edit / scene-spec change) | `music_audiosource.volume = music_vols[…]` | `Html5Music.SetTrackVolume(music_vols[…])` | — | — |
| **reset** | `reStart()` | `music_audiosource.Stop()` | `Html5Music.Stop()` | — | — |
| **channel creation** | `Start()` | `AddComponent<AudioSource>()` for music | removed; `Html5Music` owns the channel | — | — |

No cue moved relative to the voiceover or the subtitles: in `SetupScene()` and in
the spec-switch block the music statements sit in exactly the same position
relative to the `voiceover_audiosource` / `subtitle_*` statements as before.

`Pause`/`UnPause` and fades: the old code never used them on the music source, so
no fade API was added. `Html5Music.Stop(fadeMs)` already existed from N14 and is
called with the default `0`, i.e. a hard stop, matching `AudioSource.Stop()`.

### Volume semantics

The old code had a single `AudioSource.volume`. `Html5Music` keeps two factors —
`SetTrackVolume` (the per-cue level, what the call sites set) and `SetVolume`
(a master) — and pushes `clamp01(track * master)` to the element. With the master
left at its default 1.0 this is bit-identical in behaviour to the old single
`volume`, and it leaves a master lever for later work.

## 3. Autoplay gate

Chrome will not start audio before a user gesture. The old build did not care,
because Unity's own audio context had the same problem and the first music cue
happened to be inaudible. Here the gate is explicit, in C#, so it is testable:

- `Html5Music.unlocked` starts `false`. While it is false, `Play()` **does not call
  into JS at all** — not even `Music_SetBasePath` — it records
  `{track, loop, volume, startTime}` as a single pending request (latest wins).
- `Stop()` while locked clears the pending request and returns without touching JS.
- `SetVolume`/`SetTrackVolume` while locked only update the C# values.
- `IsPlaying()` while locked returns "is something pending", so the
  "track ran out, restart it" poll does not fight the gate.
- `Html5Music.Unlock()` is called from **exactly one** place: inside
  `advance_trigger.just_triggered` in `UpdateScene()` — the BEGIN gaze-dwell/click
  confirm that also runs `begin_text.SetActive(false)` and `SetSpec(SPEC.VIZ)`.
  It is in the same call stack as the user's input event, so the resulting
  `audio.play()` is treated by the browser as gesture-initiated. `Unlock()` pushes
  the recorded volume and then flushes the pending cue.

Consequence for the first cue: `Start()` -> `SetupScene()` queues `Viz/Ice` at
load, nothing is fetched or played, and that same track starts the instant BEGIN
is confirmed. `html5audio.jslib` keeps its own `NotAllowedError` retry as a second
line of defence, but on the gated path it should never trigger.

`tools/check_music_calls.py` enforces all of this statically: exactly one
`Html5Music.Play` (inside `PlayMusic`), exactly one `Html5Music.Unlock()` and it
must be inside the BEGIN confirm block, the gate check must precede any JS call in
`Play`, no `music_audiosource`/`musics` identifier may reappear in `Main.cs`, and
no `Resources.Load` of a music clip.

## 4. The restart poll (deliberate behaviour change)

The old code re-issued `clip = …; Play()` on **every frame** in which
`music_audiosource.isPlaying` was false. With an AudioSource that was cheap and
self-limiting. With a streamed `<audio>` element `IsPlaying()` is also false while
the element is still buffering, so a per-frame retry would call `play()` 30–60×
a second and the track would never get going. The poll is therefore throttled to
once per 0.5 s (`music_restart_interval`). Worst-case added gap at a track
boundary is 0.5 s on a bed that is otherwise a 3-minute ambient loop — inaudible,
and the same code path is what restarts the bed, so no cue is lost.

## 5. Payload effect

Music is no longer in `webgl.data.unityweb`; it is 16 separate `.ogg` files served
from `StreamingAssets/music/` (18.3 MB total, fetched lazily, one track at a time).
`Main.cs` holds no `AudioClip[,]` for music and performs no `Resources.Load` for
it, so nothing pulls the bytes back in. See `tools/music_inventory.md` §3 for the
per-track size table and `tools/check_music_payload.py` for the payload guard.

## 6. Verification (build `icecube-out-n15`, 2026-11-15)

Build: `unityci/editor:ubuntu-2019.4.40f1-webgl-3`, `WebGLBuilder.Build`, nohup + poll.
Log line: `BUILD_RESULT result=Succeeded errors=0 warnings=25 size=360148409`.
Static guards: `python3 tools/check_music_calls.py` and
`python3 tools/check_music_payload.py` both exit 0.

### Payload

| artifact | before (N13/N17 builds, music bundled) | this build |
|---|---:|---:|
| `Build/webgl.data.unityweb` | 136,976,806 B (130.6 MB) | 96,165,950 B (91.7 MB) |
| `StreamingAssets/music/**/*.ogg` | — | 16 files, 18.3 MB, fetched lazily one at a time |
| Unity build report "Sounds" | — | 9.0 MB (SFX + voiceover only) |

`webgl.data.unityweb` dropped 40.8 MB (gzipped; the 60.9 MB of MP3 was already
compressed) and the build report lists no `Assets/Resources/Music/*` entry. The
remaining ~92 MB is textures (156 MB uncompressed, 70% of user assets) — the
`<= 30 MB` target in the card belongs to the texture cards (N13), not to music.

### Autoplay gate — headless Chrome, CDP (`tools/music_gate_probe.js`, run 4)

Served same-origin by `tools/serve_webgl.js` (`audio/ogg`, Range support). Times are
seconds after `Page.navigate`.

```
[13.0] MUSICCUE track=Viz/Ice vol=0.5 t=0 unlocked=False      <- SetupScene at load: queued only
[18.9] PRE-GESTURE   cues=1 gate=0 ogg=0 audio=[]              <- no JS call, no fetch, no <audio>
[68.9] PRE-GESTURE(2) cues=1 gate=0 ogg=0 audio=[]             <- still nothing 56 s later
[70.0] GESTURE space+click sent                                 <- hmd_mounted + mouse capture
[72.7] MUSICGATE unlocked pending=Viz/Ice t=69.1               <- BEGIN dwell confirmed -> Unlock()
[73.5] NET  .../StreamingAssets/music/Viz/Ice.ogg               <- first and only .ogg request
[74.3] RESP 206 audio/ogg .../Viz/Ice.ogg                       <- same origin, correct mime
[83.1] AUDIO+10s [{src:Viz/Ice.ogg, paused:false, t:8.79, rs:4, vol:0.5}]
[93.1] PLAYCALLS [{src:.../Viz/Ice.ogg, ok:true}]               <- play() promise resolved, no NotAllowedError
[93.1] POST audio=[{src:Viz/Ice.ogg, paused:false, t:18.8, rs:4, vol:0.5}]
```

- Exactly one `HTMLMediaElement.play()` call for the whole run and it happened
  *after* the BEGIN confirm; its promise resolved (`ok:true`), so the jslib's
  `NotAllowedError` retry path was never needed.
- Element volume `0.5` == `ice_viz_music_vol` from the inspector, i.e. the per-cue
  volume recorded behind the gate was pushed correctly on flush.
- Playhead advanced 8.79 s -> 18.8 s over 10 s of wall time: the stream is playing
  in real time, not stalling on buffering (`readyState 4`).
- Kill criterion (CORS/mime block) not triggered: 206 `audio/ogg` from the same origin.

### Load-time stall

`PERFPROBE` is not in this branch (it belongs to the perf card), so the stall
evidence is the console timeline: first `GOVERNOR init` (first Unity frame) at
+4.1 s after navigate against the 136.9 MB data file's ~+10 s in the N13/N17 runs
on the same machine, and no music decode happens at start-up at all — the
`AudioClip` decode-to-PCM of 16 tracks that used to run inside `Resources.Load`
in `Start()` is gone. `MUSICCUE … unlocked=False` at +13.0 s marks the end of
`Start()`.

### Later cues

The spec-switch, track-ran-out and credits cues all route through the same
`PlayMusic()` -> `Html5Music.Play()` path exercised above (the static guard proves
there is no other path). With the gate open they call `Music_Play` directly, which
is the N14-tested jslib entry point. They were not individually driven in headless
Chrome in this run: reaching them needs a scripted gaze walk through the whole
narration (the full VO runs ~15 min); the mechanism they share with the first cue
is what was at risk and is what the probe verifies.

### Reproduce

```sh
OUT=/home/watson/icecube-out-n15
nohup docker run --rm --name icecube-unity-build-n15 -v "$PWD":/project -v "$OUT":/out \
  -v icecube-unity-license-a:/root/.local/share/unity3d unityci/editor:ubuntu-2019.4.40f1-webgl-3 \
  unity-editor -quit -batchmode -nographics -silent-crashes -projectPath /project \
  -buildTarget WebGL -executeMethod WebGLBuilder.Build -logFile /out/build.log >"$OUT/build.out" 2>&1 &
until grep -q BUILD_RESULT "$OUT/build.log" 2>/dev/null; do sleep 15; done; grep BUILD_RESULT "$OUT/build.log"
node tools/serve_webgl.js /home/watson/icecube-out-n15/webgl 8047 &
google-chrome --headless=new --remote-debugging-port=9233 --remote-allow-origins='*' \
  --no-sandbox --disable-gpu --window-size=1280,720 --user-data-dir=/tmp/n15prof about:blank &
node tools/music_gate_probe.js 9233 http://127.0.0.1:8047/ /tmp/n15_gate.json
```
