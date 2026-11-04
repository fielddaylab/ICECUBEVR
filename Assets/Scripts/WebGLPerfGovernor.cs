using UnityEngine;

/// Runtime performance governor for the WebGL/Chromebook build.
///
/// Why adaptive and not just "set low settings": we cannot know the target
/// Chromebook's GPU. Fixed settings either leave performance on the table or
/// still miss 30fps on a weak Mali/UHD part. So this measures actual frame time
/// and scales render resolution to hit the target, which is the one lever that
/// reliably buys frame rate on fill-bound integrated GPUs.
///
/// Also note the Awake-timing trap: Unity's canvas is often still at its
/// default 300x150 during Awake, so reading Screen.width there and clamping
/// once does nothing. We re-evaluate every interval instead.
public class WebGLPerfGovernor : MonoBehaviour
{
    [Header("Target")]
    public int targetFps = 30;

    [Header("Resolution bounds")]
    public int maxWidth = 1280;
    public int maxHeight = 720;
    public int minWidth = 640;
    public int minHeight = 360;

    [Header("Adaptation")]
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
        nextCheck = Time.realtimeSinceStartup + 4f;   // let it finish loading
        Debug.Log("GOVERNOR init target=" + targetFps + "fps quality=" +
                  QualitySettings.names[QualitySettings.GetQualityLevel()]);
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
        QualitySettings.vSyncCount = 0;             // required for targetFrameRate
        Application.targetFrameRate = targetFps;
    }

    void Update()
    {
        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + interval;

        // Establish the real canvas size once it is no longer the 300x150 default.
        if (!haveBase)
        {
            if (Screen.width <= 320 || Screen.height <= 200) return;
            baseW = Screen.width;
            baseH = Screen.height;
            haveBase = true;
            Debug.Log("GOVERNOR base resolution " + baseW + "x" + baseH +
                      " dpr-adjusted cap " + maxWidth + "x" + maxHeight);
        }

        // smoothDeltaTime is already averaged, which is what we want here.
        float dt = Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        float fps = 1f / dt;

        float target = targetFps;
        if (fps < target * 0.9f && scale > 0.34f)
        {
            scale *= stepDown;
            Apply();
        }
        else if (fps > target * 1.25f && scale < 1.0f)
        {
            scale = Mathf.Min(1.0f, scale * stepUp);
            Apply();
        }
    }

    void Apply()
    {
        int capW = Mathf.Min(baseW, maxWidth);
        int capH = Mathf.Min(baseH, maxHeight);
        int w = Mathf.Max(minWidth, Mathf.RoundToInt(capW * scale));
        int h = Mathf.Max(minHeight, Mathf.RoundToInt(capH * scale));
        if (w == lastAppliedW && h == lastAppliedH) return;
        lastAppliedW = w;
        lastAppliedH = h;
        Screen.SetResolution(w, h, false);
        Debug.Log("GOVERNOR scale=" + scale.ToString("0.00") + " -> " + w + "x" + h);
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
        Debug.Log("GOVERNOR particle systems trimmed=" + n);
    }
}
