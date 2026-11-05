using UnityEngine;
using UnityEngine.UI;

/// Screen Space Overlay reticle with ring-fill dwell feedback (card N9).
///
/// Replaces the two world-space quads that used to be parented to the camera
/// (`Cam_Reticle` and `Cam_Spinner_Reticle`). Those drew into the scaled 3D
/// buffer, so every reticle edge inherited the buffer's upscale blur, and the
/// spinner communicated dwell only through scale+spin. This component lives on
/// the overlay canvas, so it is rasterised at native backbuffer resolution
/// (N1 sharpness), and it shows dwell as a radial ring fill (owner decision C3).
///
/// Progress source: `Main.gaze_trigger` publishes per-frame telemetry into
/// static fields during tick(). `shrink` (== t_in / t_max) is the monotonic
/// 0..1 ramp, and the confirm branch fires on the same tick that clamps
/// t_in to t_max, so fill reaches exactly 1.0 on the confirming frame. See
/// tools/reticle.md for the derivation.
///
/// The old quads keep their GameObjects (Main.cs reads
/// `cam_reticle.transform.position` as the gaze pointer for all six triggers,
/// and `reticle_d` from its z) — only their MeshRenderers are switched off.
public class ReticleView : MonoBehaviour
{
    [Header("Parts (assigned by ReticleCanvasSetup)")]
    [Tooltip("Black halo ring, drawn under the white ring.")]
    public Image outline;
    [Tooltip("White ring at rest; the un-filled remainder during dwell.")]
    public Image ring;
    [Tooltip("Same geometry as `ring`, Filled / Radial360 — this is the dwell gauge.")]
    public Image fill;
    [Tooltip("Centre dot, so the aim point is readable even at idle alpha.")]
    public Image dot;
    [Tooltip("Drives overall opacity without touching per-Image colours.")]
    public CanvasGroup group;

    [Header("Look")]
    [Tooltip("Opacity when no gaze target is in range.")]
    public float idleAlpha = 0.5f;
    [Tooltip("Opacity while dwelling on a target.")]
    public float activeAlpha = 1.0f;
    [Tooltip("Opacity when no gaze target exists at all (nothing to confirm).")]
    public float disarmedAlpha = 0.35f;
    [Tooltip("Ring colour at rest.")]
    public Color idleColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Fill colour while dwelling.")]
    public Color inRangeColor = new Color(0.45f, 0.85f, 1f, 1f);
    [Tooltip("Fill colour on the confirming frame and during the flash hold.")]
    public Color confirmColor = new Color(0.35f, 1f, 0.55f, 1f);
    [Tooltip("Seconds the full ring is held in confirmColor after a trigger fires. Purely cosmetic; does not gate confirmation.")]
    public float confirmFlash = 0.18f;
    [Tooltip("Exponential alpha follow rate (per second). Frame-rate independent; 0 disables smoothing.")]
    public float alphaLerp = 12f;

    [Header("State (read-only at runtime)")]
    [Tooltip("Set by Main.cs each frame: is there a gaze target to confirm right now?")]
    public bool armed = true;

    /// Last progress pushed to the ring, for tests/telemetry.
    public float progress { get { return _progress; } }
    /// Last in-range state pushed to the ring, for tests/telemetry.
    public bool inRange { get { return _inRange; } }

    float _progress;
    bool _inRange;
    float _flash;
    float _alpha = -1f;

    /// Singleton-ish handle so Main.cs can bind without a scene reference.
    public static ReticleView instance;

    void Awake()
    {
        instance = this;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        Apply(0f, false, 0f);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// Show/hide without disturbing the dwell state machine.
    public void SetVisible(bool v)
    {
        if (gameObject.activeSelf != v) gameObject.SetActive(v);
    }

    void LateUpdate()
    {
        // Runs after every Update(), so all six triggers have ticked.
        float p = Main.gaze_trigger.frame_progress;
        bool ir = Main.gaze_trigger.frame_in_range;
        bool fired = Main.gaze_trigger.frame_just_triggered;
        // Main.Update() clears these at the top of the next frame, so this is
        // a pure read: the reticle can never perturb the dwell state machine.

        if (fired) _flash = confirmFlash;
        else if (_flash > 0f) _flash -= Time.deltaTime;

        if (_flash > 0f)
        {
            p = 1f;
            ir = true;
        }

        _progress = Mathf.Clamp01(p);
        _inRange = ir;
        Apply(_progress, ir, _flash);
    }

    void Apply(float p, bool ir, float flash)
    {
        if (fill != null)
        {
            fill.fillAmount = p;
            fill.color = flash > 0f ? confirmColor : (ir ? inRangeColor : idleColor);
            // Keep the gauge off the draw list entirely when empty.
            if (fill.enabled != (p > 0f)) fill.enabled = p > 0f;
        }
        if (ring != null) ring.color = idleColor;

        float target = !armed ? disarmedAlpha : (ir ? activeAlpha : idleAlpha);
        if (_alpha < 0f || alphaLerp <= 0f) _alpha = target;
        else _alpha = Mathf.Lerp(_alpha, target, 1f - Mathf.Exp(-alphaLerp * Time.deltaTime));
        if (group != null) group.alpha = _alpha;
    }
}
