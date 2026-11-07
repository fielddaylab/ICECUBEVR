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
/// Browser autoplay rules: the first Play() may be deferred by the browser until the
/// user interacts with the page. The jslib queues it and retries on the next gesture,
/// so callers do not need to care -- but audio will not be heard before a click.
///
/// NOTE: nothing wires this into Main.cs yet; that is card N15.
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
    if (string.IsNullOrEmpty(name)) return;
    Init();
    volume = Mathf.Clamp01(volume);
    currentTrack = name;
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_Play(name, loop ? 1 : 0, volume);
#else
    PlayFallback(name, loop, volume);
#endif
  }

  /// <summary>Stops the current track, optionally fading out over fadeMs milliseconds.</summary>
  public static void Stop(int fadeMs = 0)
  {
    Init();
    currentTrack = null;
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_Stop(fadeMs);
#else
    StopFallback();
#endif
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
    Init();
    masterVolume = Mathf.Clamp01(v);
#if UNITY_WEBGL && !UNITY_EDITOR
    Music_SetVolume(masterVolume);
#else
    if (fallbackSource != null) fallbackSource.volume = fallbackTrackVolume * masterVolume;
#endif
  }

  /// <summary>
  /// True while a track is sounding, or while one is queued waiting for the user
  /// gesture that unlocks audio.
  /// </summary>
  public static bool IsPlaying()
  {
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
      Init();
#if UNITY_WEBGL && !UNITY_EDITOR
      return Music_GetTime();
#else
      return fallbackSource != null ? fallbackSource.time : 0f;
#endif
    }
    set
    {
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

  private static void PlayFallback(string name, bool loop, float volume)
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
      StartFallbackClip(cached, name, loop, volume);
      return;
    }

    // If the original clips are still importable (they are moved out of Resources by
    // this card) prefer them -- zero latency, no file IO.
    AudioClip res = Resources.Load<AudioClip>("Music/" + name);
    if (res == null) res = Resources.Load<AudioClip>(name);
    if (res != null)
    {
      fallbackClips[name] = res;
      StartFallbackClip(res, name, loop, volume);
      return;
    }

    // Otherwise decode the same OGG the browser would stream.
    fallbackRunner.StartCoroutine(LoadAndPlayFallback(name, loop, volume));
  }

  private static void StartFallbackClip(AudioClip clip, string name, bool loop, float volume)
  {
    // A newer Play() may have superseded this request while a load was in flight.
    if (currentTrack != name) return;
    fallbackSource.clip   = clip;
    fallbackSource.loop   = loop;
    fallbackSource.volume = volume * masterVolume;
    fallbackSource.time   = 0f;
    fallbackSource.Play();
  }

  private static IEnumerator LoadAndPlayFallback(string name, bool loop, float volume)
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
      StartFallbackClip(clip, name, loop, volume);
    }
  }

  private static void StopFallback()
  {
    if (fallbackSource != null && fallbackSource.isPlaying) fallbackSource.Stop();
  }
#endif
}
