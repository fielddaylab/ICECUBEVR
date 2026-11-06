using TMPro;
using UnityEngine;

/// Adapter that lets Main.cs keep writing captions through a single `.text`
/// property while the actual renderer is a Screen Space Overlay
/// TextMeshProUGUI instead of the old world-space TextMesh (card N4).
///
/// The point of this class is that Main.cs changes in exactly two places: the
/// field type and the one binding line. Nothing in the cue/pause machinery
/// (subtitle_t / subtitle_i / subtitle_cues_absolute /
/// subtitle_pause_i_* / advance_passed_*) needs to know the renderer changed.
///
/// If Main.cs ever needs another TextMesh member, add a pass-through here
/// rather than editing the call site, and record it in tools/CAPTIONS.md.
public class CaptionSink : MonoBehaviour
{
    [Tooltip("The TMP component that actually draws the caption. Defaults to one on this GameObject.")]
    [SerializeField] TMP_Text target;

    [Header("Caption style (owner decision C1 defaults)")]
    [Tooltip("Font size in reference pixels. The CanvasScaler maps the 1280x720 reference onto the real buffer, so this is 28 px at 720p and scales from there.")]
    [SerializeField] float fontSizePx = 28f;
    [Tooltip("Hard cap on visible lines; combined with Truncate overflow this is the 2-line budget.")]
    [SerializeField] int maxLines = 2;
    [SerializeField] Color textColor = Color.white;
    [Tooltip("Re-assert the fields above on Awake so a scene tweak and this component can never disagree at runtime. Turn off to let the scene win.")]
    [SerializeField] bool applyStyleOnAwake = true;

    /// The TMP component captions are written to. Null only if the scene is broken.
    public TMP_Text Target { get { return target; } }

    /// Drop-in replacement for TextMesh.text.
    public string text
    {
        get { return target != null ? target.text : string.Empty; }
        set
        {
            if (target == null)
            {
                Debug.LogError("CaptionSink: target not assigned; caption dropped", this);
                return;
            }
            target.text = value ?? string.Empty;
        }
    }

    void Awake()
    {
        if (target == null) target = GetComponent<TMP_Text>();
        if (target == null)
        {
            Debug.LogError("CaptionSink: no TMP_Text target on '" + name + "'; captions will not render", this);
            return;
        }
        if (applyStyleOnAwake) ApplyStyle();
    }

    /// Writes the serialized style onto the TMP component. Also called by the
    /// editor setup script so the baked scene and these fields always match.
    public void ApplyStyle()
    {
        if (target == null) return;
        target.fontSize = fontSizePx;
        target.enableAutoSizing = false;
        target.richText = true;                     // subtitle corpus may carry markup
        target.enableWordWrapping = true;
        target.overflowMode = TextOverflowModes.Truncate;
        target.maxVisibleLines = maxLines;
        target.alignment = TextAlignmentOptions.Center;
        target.color = textColor;
        target.raycastTarget = false;               // captions must never eat input
    }
}
