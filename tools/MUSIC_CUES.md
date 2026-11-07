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
