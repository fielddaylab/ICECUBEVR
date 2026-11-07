using UnityEngine;

/// Runtime performance governor for the WebGL/Chromebook build.
///
/// N2 ("governor defuse"): the two mechanisms that guaranteed blur and
/// judder are gone from the active path:
///   1. Application.targetFrameRate=30 -- on WebGL a positive targetFrameRate
///      makes Unity drive the main loop with setTimeout instead of
///      requestAnimationFrame, which adds frame jitter. The active path now
///      uses targetFrameRate=-1 (rAF, vsync-aligned).
///   2. The one-way Screen.SetResolution ratchet to 1024x576 plus the
///      maxWidth/maxHeight cap. The active path no longer touches the
///      drawing buffer at all: Unity's default canvas matching sizes the
///      buffer at round(clientWidth * devicePixelRatio), i.e. 1:1 with
///      physical pixels.
///
/// The old ratchet is retained ONLY behind `legacyResolutionScaling`
/// (default false, no-op) so a regression can be A/B'd without archaeology.
/// No new scaling/pacing scheme is introduced here (that is N5/N6).
public class WebGLPerfGovernor : MonoBehaviour
{
    [Header("Active path")]
    [Tooltip("Legacy resolution ratchet. Default false: Update is a no-op and no Screen.SetResolution is ever called.")]
    public bool legacyResolutionScaling = false;

    [Header("Legacy ratchet (only used when legacyResolutionScaling is on)")]
    public int targetFps = 30;
    public int maxWidth = 1280;
    public int maxHeight = 720;
    public int minWidth = 640;
    public int minHeight = 360;
    [Tooltip("Seconds between resolution adjustments.")]
    public float interval = 2.0f;
    [Tooltip("Multiplicative step when scaling down/up.")]
    public float stepDown = 0.85f;
    public float stepUp = 1.08f;
    public float particleScale = 0.35f;

    float scale = 1.0f;
    float nextCheck;
    int lastAppliedW, lastAppliedH;
    int baseW, baseH;
    bool haveBase;

    void Awake()
    {
        ApplyQuality();
        TrimParticles();
        Debug.Log($"[WebGLPerfGovernor] drawing buffer {Screen.width}x{Screen.height}");
        if (legacyResolutionScaling)
        {
            nextCheck = Time.realtimeSinceStartup + 4f;   // let it finish loading
        }
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
        // -1 on WebGL = requestAnimationFrame (vsync-aligned). Do NOT set a
        // positive targetFrameRate: that switches the main loop to setTimeout.
        // Do NOT set vSyncCount either: it only affects the platform's own
        // vsync and is irrelevant on the rAF path.
        Application.targetFrameRate = -1;
    }

    void Update()
    {
        if (!legacyResolutionScaling) return;

        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + interval;

        // Establish the real canvas size once it is no longer the 300x150 default.
        if (!haveBase)
        {
            if (Screen.width <= 320 || Screen.height <= 200) return;
            baseW = Screen.width;
            baseH = Screen.height;
            haveBase = true;
        }

        // smoothDeltaTime is already averaged, which is what we want here.
        float dt = Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        float fps = 1f / dt;

        float target = targetFps;
        if (fps < target * 0.9f && scale > 0.34f)
        {
            scale *= stepDown;
            ApplyLegacy();
        }
        else if (fps > target * 1.25f && scale < 1.0f)
        {
            scale = Mathf.Min(1.0f, scale * stepUp);
            ApplyLegacy();
        }
    }

    void ApplyLegacy()
    {
        int capW = Mathf.Min(baseW, maxWidth);
        int capH = Mathf.Min(baseH, maxHeight);
        int w = Mathf.Max(minWidth, Mathf.RoundToInt(capW * scale));
        int h = Mathf.Max(minHeight, Mathf.RoundToInt(capH * scale));
        if (w == lastAppliedW && h == lastAppliedH) return;
        lastAppliedW = w;
        lastAppliedH = h;
        Screen.SetResolution(w, h, false);
        Debug.Log($"[WebGLPerfGovernor] applied {w}x{h}");
    }

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
