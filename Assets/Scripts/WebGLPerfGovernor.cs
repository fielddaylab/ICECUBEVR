using UnityEngine;
using Icecube.Perf;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// Pacing governor for the WebGL/Chromebook build (card N6).
///
/// The shape of the problem, in order:
///   1. Pacing.  Frame rate is locked to a *vsync-aligned* 30 fps by the rAF
///      shim in Assets/WebGLTemplates/Icecube/index.html, which forwards every
///      second native vsync to Unity's main loop. Nothing here touches
///      Application.targetFrameRate (it stays -1 per N2): a positive value
///      switches Unity's WebGL main loop to setTimeout and reintroduces exactly
///      the judder this card exists to remove.
///   2. Measurement.  GovWorkTime() returns the previous main-loop tick's work
///      duration in ms (performance.now() bracketed around the tick by the
///      shim). Time.deltaTime / Time.smoothDeltaTime are useless here: with the
///      30 fps lock the delta is ~33.3 ms no matter how much headroom the frame
///      had, and smoothDeltaTime additionally lags the signal it averages.
///   3. Actuation.  Only the 3D RenderTexture is scaled, through
///      SceneCompositor.SetScale (N5). Screen.SetResolution is never called, so
///      the drawing buffer stays 1:1 with physical pixels and the Overlay
///      canvas / captions keep their N4 sharpness at every ladder step.
///
/// The decision logic lives in GovernorPolicy (pure C#, no UnityEngine) so the
/// hysteresis and cooldown behaviour is testable off-device. This component is
/// only plumbing: sample -> policy -> SetScale -> one log line per change.
public class WebGLPerfGovernor : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
  [DllImport("__Internal")] private static extern float GovWorkTime();
  [DllImport("__Internal")] private static extern int GovShimPresent();
  [DllImport("__Internal")] private static extern int GovHalfRate();
#else
  // Editor / standalone: no browser main loop to measure, so the governor
  // no-ops rather than acting on a fabricated number.
  private static float GovWorkTime() { return 0f; }
  private static int GovShimPresent() { return 0; }
  private static int GovHalfRate() { return 0; }
#endif

  [Header("Governor")]
  [Tooltip("Master switch. Off = the 3D RT stays at whatever scale the " +
           "compositor was left on (index 0 = native) and nothing is measured.")]
  public bool governorEnabled = true;

  [Tooltip("Ladder of 3D RenderTexture scales. Index 0 must be 1.0 (native). " +
           "Pushed onto SceneCompositor.scaleLadder so both sides agree.")]
  public float[] ladder = new float[] { 1.0f, 0.85f, 0.71f, 0.6f, 0.5f };

  [Header("Startup quality (unchanged from N2)")]
  public float particleScale = 0.35f;

  [Header("Diagnostics")]
  [Tooltip("Log one GOV line per second with p90 / EWMA / counters. Off by " +
           "default: the card asks for the applied ladder index and RT size only.")]
  public bool verboseLog = false;

  GovernorPolicy policy;
  GovernorScaleSink sink;
  float nextVerboseLog;
  float nextSinkProbe;
  int lastLoggedIndex = -1;
  bool startupLogged;
  bool sinkReady;

  // Scene/skybox swaps are announced through this static hook, because the
  // swap sites (Main.cs) must not have to find the governor instance.
  static int s_swapRequests;

  /// Call when the skybox or scene content changes: the frames right after a
  /// swap are upload-bound, not fill-bound, and must not drive the ladder.
  /// Safe to call from anywhere, including before the governor exists.
  public static void NotifySceneSwap()
  {
    s_swapRequests++;
  }

  int consumedSwapRequests;

  void Awake()
  {
    ApplyQuality();
    TrimParticles();

    policy = new GovernorPolicy(ladder != null ? ladder.Length : 1);
    sink = new GovernorScaleSink(ladder);
  }

  void ApplyQuality()
  {
    QualitySettings.SetQualityLevel(1, true);   // "Low"
    QualitySettings.shadows = ShadowQuality.Disable;
    QualitySettings.shadowDistance = 0f;
    QualitySettings.pixelLightCount = 0;
    QualitySettings.realtimeReflectionProbes = false;
    QualitySettings.softParticles = false;
    QualitySettings.antiAliasing = 0;
    QualitySettings.lodBias = 0.4f;
    QualitySettings.skinWeights = SkinWeights.OneBone;
    // -1 on WebGL = requestAnimationFrame (vsync-aligned), which is what the
    // half-rate shim gates. Do NOT set a positive targetFrameRate: that selects
    // Unity's setTimeout main loop. See N2.
    Application.targetFrameRate = -1;
  }

  void Update()
  {
    if (!governorEnabled || policy == null) return;

    float now = Time.realtimeSinceStartup;

    if (!startupLogged)
    {
      startupLogged = true;
      bool shim = GovShimPresent() != 0;
      bool half = GovHalfRate() != 0;
      Debug.Log("[GOV] init shim=" + (shim ? "present" : "MISSING") +
                " halfRate=" + (half ? "1" : "0") +
                " ladder=" + LadderString());
    }

    // The governor has exactly one actuator: SceneCompositor.SetScale (N5).
    // Until that object exists there is nothing to apply, so do not even feed
    // the policy -- otherwise its index would drift away from the real RT.
    // FindObjectOfType is not free, so probe once a second, not every frame.
    if (!sinkReady)
    {
      if (now < nextSinkProbe) return;
      nextSinkProbe = now + 1f;
      if (!sink.Available) return;
      sinkReady = true;
      // Adopt whatever index the compositor booted with (defaultLadderIndex).
      int boot = sink.CurrentIndex;
      if (boot > 0) policy.NotifyExternalScaleChange(now, boot);
      Debug.Log("[GOV] compositor found; governing from ladder index " + policy.LadderIndex);
    }

    // Drain scene-swap notifications (they can arrive several per frame).
    if (consumedSwapRequests != s_swapRequests)
    {
      consumedSwapRequests = s_swapRequests;
      policy.NotifySceneSwap(now);
    }

    // Someone moved the scale behind our back (console SetScale, N5 A/B
    // toggle): adopt it instead of fighting it. Reflection read, so once a
    // second rather than per frame; a one-second lag here is harmless because
    // the external change already carries its own 2 s ignore window.
    if (now >= nextSinkProbe)
    {
      nextSinkProbe = now + 1f;
      int external = sink.CurrentIndex;
      if (external >= 0 && external != policy.LadderIndex)
      {
        policy.NotifyExternalScaleChange(now, external);
        lastLoggedIndex = external;
      }
    }

    // Previous main-loop tick's work time (ms). 0 = no sample yet / hidden
    // tab / Editor; the policy discards those. Never deltaTime, never
    // smoothDeltaTime: both read ~33 ms at a locked 30 regardless of headroom.
    float workMs = GovWorkTime();

    GovernorAction action = policy.Tick(now, workMs);

    if (action != GovernorAction.None)
    {
      ApplyLadder(policy.LadderIndex);
    }

    if (verboseLog && now >= nextVerboseLog)
    {
      nextVerboseLog = now + 1f;
      Debug.Log("[GOV] p90=" + policy.LastP90.ToString("0.0") +
                "ms ewma=" + policy.Ewma.ToString("0.0") +
                "ms n=" + policy.SampleCount +
                " hi=" + policy.HighSeconds + " lo=" + policy.LowSeconds +
                " idx=" + policy.LadderIndex + " state=" + policy.LastReason);
    }
  }

  void ApplyLadder(int index)
  {
    if (!sink.Apply(index)) return;
    if (index == lastLoggedIndex) return;   // no change applied, no log, no noise
    lastLoggedIndex = index;

    int w, h;
    sink.GetRenderSize(index, out w, out h);
    // The card asks for the applied ladder index and RT size only.
    Debug.Log("GOV ladder=" + index + " rt=" + w + "x" + h);
  }

  string LadderString()
  {
    if (ladder == null || ladder.Length == 0) return "[]";
    var sb = new System.Text.StringBuilder("[");
    for (int i = 0; i < ladder.Length; i++)
    {
      if (i > 0) sb.Append(',');
      sb.Append(ladder[i].ToString("0.00"));
    }
    return sb.Append(']').ToString();
  }

  // --- console entry points ------------------------------------------------

  ///   unityInstance.SendMessage('__WebGLPerfGovernor', 'SetGovernorEnabledFromJs', '0')
  public void SetGovernorEnabledFromJs(string onOff)
  {
    string s = onOff == null ? "" : onOff.Trim().ToLowerInvariant();
    if (s == "1" || s == "true" || s == "on") { governorEnabled = true; }
    else if (s == "0" || s == "false" || s == "off") { governorEnabled = false; }
    else { Debug.LogWarning("[GOV] SetGovernorEnabledFromJs ignored: '" + onOff + "'"); return; }
    Debug.Log("[GOV] governorEnabled=" + governorEnabled);
  }

  ///   unityInstance.SendMessage('__WebGLPerfGovernor', 'NotifySceneSwapFromJs', '')
  public void NotifySceneSwapFromJs(string _)
  {
    NotifySceneSwap();
  }

  // --- startup quality -----------------------------------------------------

  void TrimParticles()
  {
    int n = 0;
    foreach (var ps in Object.FindObjectsOfType<ParticleSystem>())
    {
      var main = ps.main;
      int cur = main.maxParticles;
      int want = Mathf.Max(40, Mathf.RoundToInt(cur * particleScale));
      if (want < cur) { main.maxParticles = want; n++; }
      var rend = ps.GetComponent<ParticleSystemRenderer>();
      if (rend != null)
      {
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
      }
    }
  }
}
