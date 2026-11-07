using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Streams music through a browser &lt;audio&gt; element on WebGL instead of letting
/// Unity decode every music clip into float PCM in browser memory.
///
/// Track names are the StreamingAssets-relative path without extension, matching the
/// existing Resources layout, e.g. "Viz/Ice" -> StreamingAssets/music/Viz/Ice.ogg.
///
/// On WebGL the calls go to Assets/Plugins/WebGL/html5audio.jslib. Everywhere else
/// (Editor, standalone) they fall back to a plain AudioSource playing an AudioClip,
/// so existing editor workflows keep working untouched.
///
/// Browser autoplay rules are handled in two layers:
///  1. C# gate (this file). Nothing is handed to the browser until <see cref="Unlock"/>
///     is called from the first user gesture that starts the experience (the BEGIN
///     gaze/click confirm in Main.cs). A Play() before that is remembered as a single
///     pending request (latest wins); Stop() before that clears it. Unlock() then
///     fires the pending request. This is what guarantees the *first* play happens
///     after a gesture rather than relying on the browser rejecting it.
///  2. jslib retry (html5audio.jslib). If a play() is still rejected with
///     NotAllowedError it is queued there and retried on the next DOM gesture.
/// </summary>
public static class Html5Music
{
#if UNITY_WEBGL && !UNITY_EDITOR
  [DllImport("__Internal")] private static extern void  Music_SetBasePath(string url);
  [DllImport("__Internal")] private static extern void  Music_Play(string name, int loop, float volume);
  [DllImport("__Internal")] private static extern void  Music_Stop(int fadeMs);
  [DllImport("__Internal")] private static extern void  Music_SetVolume(float v);
  [DllImport("__Internal")] private static extern int   Music_IsPlaying();
  [DllImport("__Internal")] private static extern float Music_GetTime();
  [DllImport("__Internal")] private static extern void  Music_SetTime(float seconds);
#endif

  /// <summary>Folder under StreamingAssets holding the re-encoded OGG tracks.</summary>
  public const string MusicFolder = "music";

  /// <summary>File extension of the streamed tracks.</summary>
  public const string Extension = ".ogg";

  private static bool   initialized;
  private static float  masterVolume = 1f;
  private static string currentTrack;

  // ---- autoplay gate ------------------------------------------------------------
  // No browser call happens before Unlock(). Exactly one request is remembered while
  // locked, because music is a single channel: the latest cue is the only one that
  // would still be audible once audio is allowed.
  private static float trackVolume = 1f;
  private static bool  unlocked;
  private static bool  hasPending;
  private static string pendingTrack;
  private static bool  pendingLoop;
  private static float pendingVolume;
  private static float pendingStartTime;

  // ---- non-WebGL fallback state -------------------------------------------------
#if !UNITY_WEBGL || UNITY_EDITOR
  private static AudioSource fallbackSource;
  private static float       fallbackTrackVolume = 1f;
  private static FallbackRunner fallbackRunner;
  private static readonly Dictionary<string, AudioClip> fallbackClips =
    new Dictionary<string, AudioClip>();

  /// <summary>Coroutine host for the Editor/standalone fallback loader.</summary>
  private class FallbackRunner : MonoBehaviour { }
#endif

  /// <summary>
  /// Base URL (WebGL) / path (elsewhere) that track names are resolved against.
  /// Always ends with a '/'.
  /// </summary>
  public static string BasePath
  {
    get { return Application.streamingAssetsPath + "/" + MusicFolder + "/"; }
  }

  /// <summary>Full URL/path of a track, e.g. ".../music/Viz/Ice.ogg".</summary>
  public static string UrlFor(string name)
  {
    if (string.IsNullOrEmpty(name)) return null;
    return BasePath + name + Extension;
  }

  /// <summary>
  /// Hands the base path to the JS side. Safe to call repeatedly; called lazily by
  /// Play/Stop/SetVolume so callers never have to remember it.
  /// </summary>
  public static void Init()
  {
    if (initialized) return;
    initialized = true;
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_SetBasePath(BasePath);
#endif
  }

  /// <summary>
  /// Starts a track, stopping whatever was playing. On WebGL the first call may be
  /// deferred until the user clicks (browser autoplay policy).
  /// </summary>
  /// <param name="name">StreamingAssets-relative name without extension, e.g. "Viz/Ice".</param>
  /// <param name="loop">Loop the track.</param>
  /// <param name="volume">Track volume 0..1, multiplied by the master volume.</param>
  public static void Play(string name, bool loop = true, float volume = 1f)
  {
    Play(name, loop, volume, 0f);
  }

  /// <summary>
  /// Starts a track at <paramref name="startTime"/> seconds in. Used for the
  /// spec-switch continuity in Main.cs, which carries the playhead across the swap.
  /// </summary>
  public static void Play(string name, bool loop, float volume, float startTime)
  {
    if (string.IsNullOrEmpty(name)) return;
    volume = Mathf.Clamp01(volume);
    if (startTime < 0f) startTime = 0f;

    if (!unlocked)
    {
      // Autoplay gate: remember it, touch nothing in the browser.
      hasPending       = true;
      trackVolume      = volume;
      pendingTrack     = name;
      pendingLoop      = loop;
      pendingVolume    = volume;
      pendingStartTime = startTime;
      currentTrack     = name;
      return;
    }

    Init();
    currentTrack = name;
    trackVolume  = volume;
#if UNITY_WEBGL && !UNITY_EDITOR
    // The element's own gain stays at 1 and the effective level is pushed through
    // Music_SetVolume, so SetVolume/SetTrackVolume afterwards are a single multiply.
    Music_Play(name, loop ? 1 : 0, 1f);
    Music_SetVolume(Mathf.Clamp01(trackVolume * masterVolume));
    if (startTime > 0f) Music_SetTime(startTime);
#else
    PlayFallback(name, loop, volume, startTime);
#endif
  }

  /// <summary>Stops the current track, optionally fading out over fadeMs milliseconds.</summary>
  public static void Stop(int fadeMs = 0)
  {
    currentTrack = null;
    ClearPending();
    if (!unlocked) return;   // nothing was ever handed to the browser
    Init();
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_Stop(fadeMs);
#else
    StopFallback();
#endif
  }

  /// <summary>True once <see cref="Unlock"/> has run, i.e. music may reach the browser.</summary>
  public static bool IsUnlocked
  {
    get { return unlocked; }
  }

  /// <summary>True while a cue is waiting behind the autoplay gate.</summary>
  public static bool HasPending
  {
    get { return hasPending; }
  }

  /// <summary>Track remembered behind the gate, or null.</summary>
  public static string PendingTrack
  {
    get { return hasPending ? pendingTrack : null; }
  }

  /// <summary>
  /// Opens the gate. Call this from the first user gesture that starts the experience
  /// (the BEGIN confirm in Main.cs) so the browser's autoplay policy is already
  /// satisfied, then flush whatever cue was waiting. Idempotent.
  /// </summary>
  public static void Unlock()
  {
    if (unlocked) return;
    unlocked = true;
    // Evidence line for the autoplay-gate check: this must appear in the console
    // before the first "MUSICCUE ... unlocked=True", never after a cue that reached JS.
    Debug.Log("MUSICGATE unlocked pending=" + (hasPending ? pendingTrack : "none") +
              " t=" + Time.realtimeSinceStartup.ToString("F1"));
    Init();
    // Push the volume that was only recorded in C# while the gate was shut, so the
    // flushed cue starts at the level its caller asked for.
    SetVolume(masterVolume);
    if (!hasPending) return;

    string name  = pendingTrack;
    bool   loop  = pendingLoop;
    float  vol   = pendingVolume;
    float  start = pendingStartTime;
    ClearPending();
    Play(name, loop, vol, start);
  }

  private static void ClearPending()
  {
    hasPending       = false;
    pendingTrack     = null;
    pendingStartTime = 0f;
  }

  /// <summary>Master music volume 0..1, applied on top of each track's own volume.</summary>
  public static float Volume
  {
    get { return masterVolume; }
    set { SetVolume(value); }
  }

  /// <summary>Sets the master music volume (0..1).</summary>
  public static void SetVolume(float v)
  {
    masterVolume = Mathf.Clamp01(v);
    ApplyVolume();
  }

  /// <summary>
  /// Volume of the current track (0..1), the direct replacement for setting
  /// <c>AudioSource.volume</c> on the old music source. Multiplied by the master
  /// volume. Also applies to the cue queued behind the autoplay gate.
  /// </summary>
  public static void SetTrackVolume(float v)
  {
    trackVolume = Mathf.Clamp01(v);
    if (hasPending) pendingVolume = trackVolume;
    ApplyVolume();
  }

  /// <summary>Volume of the current/queued track, before the master multiply.</summary>
  public static float TrackVolume
  {
    get { return trackVolume; }
  }

  private static void ApplyVolume()
  {
    if (!unlocked) return;   // pushed for real by Unlock() -> Play()
    Init();
    float effective = Mathf.Clamp01(trackVolume * masterVolume);
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_SetVolume(effective);
#else
    fallbackTrackVolume = trackVolume;
    if (fallbackSource != null) fallbackSource.volume = effective;
#endif
  }

  /// <summary>
  /// True while a track is sounding, or while one is queued waiting for the user
  /// gesture that unlocks audio.
  /// </summary>
  public static bool IsPlaying()
  {
    if (!unlocked) return hasPending;   // a gated cue counts as "will be playing"
    Init();
#if UNITY_WEBGL && !UNITY_EDITOR
    return Music_IsPlaying() != 0;
#else
    return fallbackSource != null && fallbackSource.isPlaying;
#endif
  }

  /// <summary>Playback position of the current track, in seconds.</summary>
  public static float Time
  {
    get
    {
      if (!unlocked) return hasPending ? pendingStartTime : 0f;
      Init();
#if UNITY_WEBGL && !UNITY_EDITOR
      return Music_GetTime();
#else
      return fallbackSource != null ? fallbackSource.time : 0f;
#endif
    }
    set
    {
      if (!unlocked)
      {
        if (hasPending) pendingStartTime = value < 0f ? 0f : value;
        return;
      }
      Init();
#if UNITY_WEBGL && !UNITY_EDITOR
      Music_SetTime(value < 0f ? 0f : value);
#else
      if (fallbackSource != null) fallbackSource.time = value < 0f ? 0f : value;
#endif
    }
  }

  /// <summary>Name of the track last asked to play, or null after Stop().</summary>
  public static string CurrentTrack
  {
    get { return currentTrack; }
  }

  // ---- Editor / standalone fallback ---------------------------------------------
#if !UNITY_WEBGL || UNITY_EDITOR
  private static void EnsureFallbackSource()
  {
    if (fallbackSource != null) return;
    GameObject go = new GameObject("Html5Music (fallback)");
    go.hideFlags = HideFlags.HideAndDontSave;
    Object.DontDestroyOnLoad(go);
    fallbackSource = go.AddComponent<AudioSource>();
    fallbackSource.playOnAwake = false;
    fallbackSource.spatialBlend = 0f;
    fallbackSource.priority = 2;
    fallbackRunner = go.AddComponent<FallbackRunner>();
  }

  private static void PlayFallback(string name, bool loop, float volume, float startTime)
  {
    EnsureFallbackSource();

    // Mirror the WebGL behaviour: starting a track stops whatever was playing.
    if (fallbackSource.isPlaying) fallbackSource.Stop();

    fallbackTrackVolume = volume;
    fallbackSource.loop   = loop;
    fallbackSource.volume = volume * masterVolume;

    AudioClip cached;
    if (fallbackClips.TryGetValue(name, out cached) && cached != null)
    {
      StartFallbackClip(cached, name, loop, volume, startTime);
      return;
    }

    // If the original clips are still importable (they are moved out of Resources by
    // this card) prefer them -- zero latency, no file IO.
    AudioClip res = Resources.Load<AudioClip>("Music/" + name);
    if (res == null) res = Resources.Load<AudioClip>(name);
    if (res != null)
    {
      fallbackClips[name] = res;
      StartFallbackClip(res, name, loop, volume, startTime);
      return;
    }

    // Otherwise decode the same OGG the browser would stream.
    fallbackRunner.StartCoroutine(LoadAndPlayFallback(name, loop, volume, startTime));
  }

  private static void StartFallbackClip(AudioClip clip, string name, bool loop,
                                        float volume, float startTime)
  {
    // A newer Play() may have superseded this request while a load was in flight.
    if (currentTrack != name) return;
    fallbackSource.clip   = clip;
    fallbackSource.loop   = loop;
    fallbackSource.volume = volume * masterVolume;
    // Carrying the playhead across a spec switch can land past a shorter track.
    if (startTime > 0f && clip.length > 0f) startTime %= clip.length;
    fallbackSource.time   = startTime > 0f ? startTime : 0f;
    fallbackSource.Play();
  }

  private static IEnumerator LoadAndPlayFallback(string name, bool loop, float volume,
                                                 float startTime)
  {
    string url = UrlFor(name);
    if (url.IndexOf("://") < 0) url = "file://" + url;

    using (UnityWebRequest req =
             UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS))
    {
      yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
      bool failed = req.result != UnityWebRequest.Result.Success;
#else
      bool failed = req.isNetworkError || req.isHttpError;
#endif
      if (failed)
      {
        Debug.LogWarning("[Html5Music] fallback could not load \"" + name + "\" from " +
                         url + ": " + req.error);
        yield break;
      }

      AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
      if (clip == null)
      {
        Debug.LogWarning("[Html5Music] fallback decoded no audio for \"" + name + "\"");
        yield break;
      }
      clip.name = name;
      fallbackClips[name] = clip;
      StartFallbackClip(clip, name, loop, volume, startTime);
    }
  }

  private static void StopFallback()
  {
    if (fallbackSource != null && fallbackSource.isPlaying) fallbackSource.Stop();
  }
#endif
}
