using System.Collections.Generic;
using UnityEngine;

/// 3D-to-scaled-RenderTexture compositor (card N5).
///
/// Every camera that currently renders to the backbuffer is redirected into one
/// shared RenderTexture sized native*scale. A final "Compositor" camera (depth
/// 100, cullingMask 0, clearFlags Nothing) blits that RT back to the backbuffer.
/// A Screen Space Overlay canvas keeps drawing at native resolution because
/// Unity renders Overlay canvases straight to the backbuffer after all cameras,
/// so UI/captions are untouched by the scene scale.
///
/// This card is deliberately behaviour-neutral: the default ladder index is 0,
/// i.e. scale 1.0, so the RT is exactly native size until N6 introduces the
/// automatic scaling policy. The only observable cost at scale 1.0 is the blit.
///
/// Notes on the 2019.4 WebGL target:
///  - There is no ScalableBufferManager / allowDynamicResolution here, which is
///    why we own an explicit RT instead of asking Unity to scale the buffer.
///  - RenderTexture MSAA is unsupported on this target, so antiAliasing stays 1.
///    QualitySettings.antiAliasing is already 0 on the quality levels this build
///    uses, so nothing is lost.
///  - Screen.width/Screen.height keep reporting the *native* drawing buffer even
///    while cameras render into the RT. Code that mixes Screen space with camera
///    pixel space must go through the helpers at the bottom of this file.
///
/// Main.cs screen-space audit (card step 5). Grepping Main.cs for
/// Screen.width / Screen.height / ScreenPointToRay / WorldToScreenPoint finds:
///   - no ScreenPointToRay, WorldToScreenPoint or ScreenToWorldPoint calls at
///     all, so no raycast or UI-placement math depends on a camera's pixel
///     size. The six gaze triggers compare world-space positions
///     (gaze_trigger.in_range -> Vector3.Distance), which the compositor
///     cannot affect.
///   - Screen.width/Screen.height appear only as the centre offset for the
///     look controls (mouse_x/mouse_y init, and the CameraHouse rotation
///     expressions). Those values are accumulated mouse *deltas* measured
///     against the native screen centre and converted straight to Euler
///     angles; they are never fed into a camera as pixels. Screen.width and
///     Screen.height keep reporting the native drawing buffer while cameras
///     render into the RT, so this math is unchanged by any scale.
///   - Main.cs also copies main_camera's aspect/fieldOfView onto the AR and
///     portal cameras (resize_ar_cameras). Reading .aspect from a camera that
///     now targets the RT returns the RT's aspect, and the RT is always
///     native*scale in *both* axes, so the value is the same ratio it was
///     before (worst case a <0.2% rounding drift at scale 0.5 on 1280x720).
///     The AR/portal cameras keep their own RenderTextures and are untouched.
/// Conclusion: no cam.pixelRect-relative fixes are needed in Main.cs. The
/// helpers at the bottom of this file exist for future call sites (and for N10
/// Overlay-UI placement), not because an existing one is broken.
///
/// The wiring is done at runtime (Awake) rather than by editing nine cameras in
/// main.unity: only this one GameObject appears in the scene YAML, which keeps
/// the scene diff small and avoids fighting the other cards over per-camera
/// properties. Cameras that already own a RenderTexture (portal / AR) are never
/// touched.
public class SceneCompositor : MonoBehaviour
{
  public const string GameObjectName = "SceneCompositor";

  [Header("Master switch (kill criterion)")]
  [Tooltip("Set false to ship native 3D with no compositor at all. " +
           "If the blit measures > 3 ms p50 on the Chromebook, this is the off switch.")]
  public bool compositorEnabled = true;

  [Header("Scale ladder (index 0 must be 1.0 = native)")]
  public float[] scaleLadder = new float[] { 1.0f, 0.8f, 0.66f, 0.5f };
  [Tooltip("Index into scaleLadder used at startup. 0 = native; N5 ships 0.")]
  public int defaultLadderIndex = 0;

  [Header("RenderTexture format")]
  [Tooltip("Try RGB565 (half the bandwidth of ARGB32). Falls back to Default " +
           "when the platform reports it unsupported. Conservative default is off.")]
  public bool useRgb565 = false;
  [Tooltip("Depth buffer bits for the shared RT. 24 keeps the existing " +
           "multi-camera depth-clear ordering intact.")]
  public int depthBits = 24;

  [Header("Blit")]
  [Tooltip("Card N5 specifies clearFlags Nothing on the Compositor camera. " +
           "Because that camera carries an image effect, 'Nothing' can make the " +
           "driver restore the previous buffer contents into the temporary RT " +
           "before OnRenderImage runs, which costs a full-screen copy we then " +
           "overwrite anyway. Flip this on to clear to solid black instead and " +
           "measure the difference with the N1 harness; leave it off for the " +
           "behaviour specified by the card.")]
  public bool clearCompositorCamera = false;
  [Tooltip("Escape hatch: flip the blit vertically. Unity normally hides the " +
           "GL bottom-up RenderTexture / top-down backbuffer difference, so this " +
           "should stay off; it exists so an upside-down WebGL build can be fixed " +
           "from the scene instead of a rebuild.")]
  public bool flipVertical = false;

  [Header("Rescan")]
  [Tooltip("Seconds between camera rescans, to adopt cameras created after " +
           "startup. 0 disables polling (one scan at wire time still happens). " +
           "A rescan allocates, so keep this coarse; N7 cares about per-frame " +
           "allocations and this is deliberately not per-frame.")]
  public float rescanIntervalSeconds = 5f;

  [Header("Diagnostics")]
  public bool verboseLog = false;

  // --- state ---------------------------------------------------------------

  static SceneCompositor instance;
  public static SceneCompositor Instance { get { return instance; } }

  RenderTexture rt;
  Camera compositorCamera;
  CompositorBlit blitter;

  readonly List<Camera> managed = new List<Camera>();
  System.Collections.ObjectModel.ReadOnlyCollection<Camera> managedView;
  int ladderIndex;
  int rtWidth, rtHeight;
  int lastScreenW, lastScreenH;
  bool wired;
  bool prevCompositorEnabled;
  float nextRescan;
  bool warnedAboutMsaa;
  bool loggedCameraSet;

  public RenderTexture Target { get { return rt; } }
  public IReadOnlyList<Camera> ManagedCameras
  {
    get
    {
      // Cached so that polling this from a per-frame HUD allocates nothing.
      if (managedView == null) managedView = managed.AsReadOnly();
      return managedView;
    }
  }
  public int CurrentLadderIndex { get { return ladderIndex; } }
  public float CurrentScale { get { return ScaleAt(ladderIndex); } }
  public bool IsActive { get { return compositorEnabled && rt != null; } }

  /// True when cam is one of the cameras we redirected into the shared RT.
  public bool IsManaged(Camera cam)
  {
    if (cam == null) return false;
    for (int i = 0; i < managed.Count; i++) if (managed[i] == cam) return true;
    return false;
  }

  // --- lifecycle -----------------------------------------------------------

  void Awake()
  {
    if (instance != null && instance != this)
    {
      Debug.LogWarning("[SceneCompositor] a second instance exists; disabling the duplicate on " + name);
      enabled = false;
      return;
    }
    instance = this;

    if (scaleLadder == null || scaleLadder.Length == 0)
      scaleLadder = new float[] { 1.0f };
    // Index 0 is contractually native so that defaultLadderIndex 0 is a no-op.
    scaleLadder[0] = 1.0f;
    for (int i = 0; i < scaleLadder.Length; i++)
      scaleLadder[i] = Mathf.Clamp(scaleLadder[i], 0.25f, 1.0f);

    ladderIndex = Mathf.Clamp(defaultLadderIndex, 0, scaleLadder.Length - 1);
    prevCompositorEnabled = compositorEnabled;

    EnsureCompositorCamera();
    if (compositorEnabled) TryWire();
  }

  void OnDestroy()
  {
    Unwire();
    ReleaseTarget();
    if (instance == this) instance = null;
  }

  void LateUpdate()
  {
    // compositorEnabled can also be flipped straight on the component (the
    // inspector during a profiling session, or a debug key) rather than through
    // SetCompositorEnabled. Honour that so the A/B toggle cannot leave the
    // cameras pointing at a released RT.
    if (compositorEnabled != prevCompositorEnabled)
    {
      bool want = compositorEnabled;
      compositorEnabled = prevCompositorEnabled;   // let the setter see a real change
      SetCompositorEnabled(want);
      prevCompositorEnabled = compositorEnabled;
    }

    if (!compositorEnabled) return;

    // The WebGL canvas is frequently still at its 300x150 default during Awake
    // and can be resized by the browser at any time, so the native size is
    // re-checked every frame rather than sampled once.
    if (!wired) { TryWire(); return; }

    if (Screen.width != lastScreenW || Screen.height != lastScreenH)
    {
      if (verboseLog)
        Debug.Log("[SceneCompositor] screen resize " + lastScreenW + "x" + lastScreenH +
                  " -> " + Screen.width + "x" + Screen.height);
      Reallocate();
      return;
    }

    // Adopt cameras that appeared after wiring. Without this, a camera created
    // later would render straight to the backbuffer and then be overwritten by
    // the blit, i.e. silently disappear.
    if (rescanIntervalSeconds > 0f && Time.unscaledTime >= nextRescan)
    {
      nextRescan = Time.unscaledTime + rescanIntervalSeconds;
      int before = managed.Count;
      Rescan();
      if (managed.Count != before)
      {
        ApplyTargetToManaged();
        if (verboseLog)
          Debug.Log("[SceneCompositor] rescan adopted " + (managed.Count - before) +
                    " camera(s); now " + managed.Count);
      }
    }
  }

  // --- public API ----------------------------------------------------------

  /// Select a scale from the discrete ladder. Reallocates only when the
  /// resulting RT size actually changes. No automatic policy lives here: N6
  /// owns the decision of *when* to call this.
  public void SetScale(int index)
  {
    if (scaleLadder == null || scaleLadder.Length == 0)
      scaleLadder = new float[] { 1.0f };

    int clamped = Mathf.Clamp(index, 0, scaleLadder.Length - 1);
    bool indexChanged = clamped != ladderIndex;
    ladderIndex = clamped;

    if (!compositorEnabled)
    {
      if (verboseLog) Debug.Log("[SceneCompositor] SetScale(" + clamped + ") while disabled; stored only");
      return;
    }
    if (!wired) { TryWire(); return; }

    int w, h;
    DesiredSize(out w, out h);
    if (!indexChanged && rt != null && rt.width == w && rt.height == h)
    {
      // Size already correct, so skip the (expensive) reallocation, but still
      // pick up cameras created since the last scan: a caller asking for a
      // scale is a natural, non-per-frame moment to re-sync the camera set.
      int before = managed.Count;
      Rescan();
      if (managed.Count != before) ApplyTargetToManaged();
      return;
    }

    Reallocate();
  }

  /// Browser-console entry point:
  ///   unityInstance.SendMessage('SceneCompositor', 'SetScaleFromJs', '2')
  public void SetScaleFromJs(string index)
  {
    int parsed;
    if (!int.TryParse(index == null ? "" : index.Trim(), out parsed))
    {
      Debug.LogWarning("[SceneCompositor] SetScaleFromJs ignored, not an int: '" + index + "'");
      return;
    }
    SetScale(parsed);
  }

  /// On/off switch used for the N1 blit-cost A/B, and the kill criterion.
  public void SetCompositorEnabled(bool on)
  {
    if (compositorEnabled == on && (on ? wired : !wired)) { prevCompositorEnabled = on; return; }
    compositorEnabled = on;
    prevCompositorEnabled = on;
    if (on)
    {
      TryWire();
    }
    else
    {
      Unwire();
      ReleaseTarget();
      Debug.Log("[SceneCompositor] disabled; cameras restored to the native drawing buffer");
    }
  }

  ///   unityInstance.SendMessage('SceneCompositor', 'SetCompositorEnabledFromJs', '0')
  public void SetCompositorEnabledFromJs(string onOff)
  {
    string s = onOff == null ? "" : onOff.Trim().ToLowerInvariant();
    if (s == "1" || s == "true" || s == "on") { SetCompositorEnabled(true); return; }
    if (s == "0" || s == "false" || s == "off") { SetCompositorEnabled(false); return; }
    Debug.LogWarning("[SceneCompositor] SetCompositorEnabledFromJs ignored: '" + onOff + "'");
  }

  /// Adopt a camera created after startup.
  public void Register(Camera cam)
  {
    if (cam == null || cam == compositorCamera) return;
    if (IsManaged(cam)) return;
    if (cam.targetTexture != null && cam.targetTexture != rt) return;  // portal/AR: leave alone
    managed.Add(cam);
    if (compositorEnabled && rt != null) cam.targetTexture = rt;
  }

  /// Release a camera back to the backbuffer.
  public void Unregister(Camera cam)
  {
    if (cam == null) return;
    for (int i = 0; i < managed.Count; i++)
    {
      if (managed[i] != cam) continue;
      if (cam.targetTexture == rt) cam.targetTexture = null;
      managed.RemoveAt(i);
      return;
    }
  }

  /// Find every camera that should be composited. Inactive and disabled
  /// cameras are included so that scripts enabling them later still render
  /// into the RT. A camera is managed iff it has no targetTexture of its own.
  public void Rescan()
  {
    // Drop destroyed entries first.
    for (int i = managed.Count - 1; i >= 0; i--)
      if (managed[i] == null) managed.RemoveAt(i);

    // Resources.FindObjectsOfTypeAll is the only variant available on 2019.4
    // that returns inactive objects, so results are filtered back down to
    // cameras that actually live in a loaded scene.
    Camera[] all = Resources.FindObjectsOfTypeAll<Camera>();
    for (int i = 0; i < all.Length; i++)
    {
      Camera cam = all[i];
      if (cam == null || cam == compositorCamera) continue;
      if (!cam.gameObject.scene.IsValid()) continue;                       // prefab/asset copy
      if ((cam.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      if (cam.targetTexture != null && cam.targetTexture != rt) continue;  // portal/AR RT: untouched
      if (!IsManaged(cam)) managed.Add(cam);
    }
  }

  // --- internals -----------------------------------------------------------

  float ScaleAt(int index)
  {
    if (scaleLadder == null || scaleLadder.Length == 0) return 1.0f;
    return scaleLadder[Mathf.Clamp(index, 0, scaleLadder.Length - 1)];
  }

  void DesiredSize(out int w, out int h)
  {
    float s = ScaleAt(ladderIndex);
    w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * s));
    h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * s));
  }

  bool ScreenIsReady()
  {
    // Unity's WebGL canvas reports the 300x150 default for the first frames.
    return Screen.width > 320 && Screen.height > 200;
  }

  void TryWire()
  {
    if (wired || !compositorEnabled) return;
    if (!ScreenIsReady()) return;
    EnsureCompositorCamera();
    Reallocate();
  }

  void Reallocate()
  {
    int w, h;
    DesiredSize(out w, out h);

    if (rt == null || rt.width != w || rt.height != h)
    {
      ReleaseTarget();
      rt = CreateTarget(w, h);
      if (rt == null)
      {
        Debug.LogWarning("[SceneCompositor] RenderTexture creation failed at " + w + "x" + h +
                         "; staying on the native drawing buffer");
        compositorEnabled = false;
        if (compositorCamera != null) compositorCamera.enabled = false;
        return;
      }
      rtWidth = w;
      rtHeight = h;
    }

    lastScreenW = Screen.width;
    lastScreenH = Screen.height;
    nextRescan = Time.unscaledTime + Mathf.Max(0f, rescanIntervalSeconds);

    Rescan();
    ApplyTargetToManaged();

    // Parity caveat worth surfacing rather than debugging later: the native
    // drawing buffer can be MSAA-resolved, but RenderTexture MSAA is
    // unsupported on this target, so a quality level with antiAliasing > 0
    // will look very slightly softer/aliased differently once composited.
    if (!warnedAboutMsaa && QualitySettings.antiAliasing > 0)
    {
      warnedAboutMsaa = true;
      Debug.LogWarning("[SceneCompositor] QualitySettings.antiAliasing=" +
                       QualitySettings.antiAliasing +
                       " but RenderTexture MSAA is unsupported here; the composited " +
                       "scene is not multisampled. Set antiAliasing=0 for exact parity.");
    }

    if (compositorCamera != null) compositorCamera.enabled = true;
    if (blitter != null) { blitter.source = rt; blitter.flipVertical = flipVertical; }
    wired = true;

    if (!loggedCameraSet)
    {
      loggedCameraSet = true;
      var sb = new System.Text.StringBuilder("[SceneCompositor] composited cameras:");
      for (int i = 0; i < managed.Count; i++)
      {
        Camera cam = managed[i];
        if (cam == null) continue;
        sb.Append(' ').Append(cam.name).Append("(d=").Append(cam.depth)
          .Append(cam.enabled && cam.gameObject.activeInHierarchy ? "" : ",off").Append(')');
      }
      Debug.Log(sb.ToString());
    }

    Debug.Log("[SceneCompositor] scale=" + ScaleAt(ladderIndex).ToString("0.###") +
              " idx=" + ladderIndex +
              " rt=" + rtWidth + "x" + rtHeight +
              " screen=" + Screen.width + "x" + Screen.height +
              " cams=" + managed.Count);
  }

  RenderTexture CreateTarget(int w, int h)
  {
    RenderTextureFormat fmt = RenderTextureFormat.Default;
    if (useRgb565 && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565))
      fmt = RenderTextureFormat.RGB565;

    var tex = new RenderTexture(w, h, Mathf.Max(0, depthBits), fmt, RenderTextureReadWrite.Default);
    tex.name = "SceneCompositorRT";
    tex.filterMode = FilterMode.Bilinear;     // scale < 1 is upsampled by the blit
    tex.wrapMode = TextureWrapMode.Clamp;
    tex.useMipMap = false;
    tex.autoGenerateMips = false;
    tex.antiAliasing = 1;                     // RT MSAA is unsupported on this target
    tex.anisoLevel = 0;
    if (!tex.Create())
    {
      if (Application.isPlaying) Object.Destroy(tex); else Object.DestroyImmediate(tex);
      return null;
    }
    return tex;
  }

  void ApplyTargetToManaged()
  {
    for (int i = managed.Count - 1; i >= 0; i--)
    {
      Camera cam = managed[i];
      if (cam == null) { managed.RemoveAt(i); continue; }
      if (cam == compositorCamera) { managed.RemoveAt(i); continue; }
      if (cam.targetTexture != null && cam.targetTexture != rt)
      {
        // Something gave this camera its own RenderTexture after we adopted it
        // (a portal/AR camera being re-pointed). The card says those are off
        // limits, so release it instead of overwriting the assignment.
        Debug.Log("[SceneCompositor] releasing " + cam.name +
                  "; it acquired its own RenderTexture");
        managed.RemoveAt(i);
        continue;
      }
      cam.targetTexture = rt;
      if (verboseLog) Debug.Log("[SceneCompositor] managed camera " + cam.name);
    }
  }

  void Unwire()
  {
    for (int i = 0; i < managed.Count; i++)
    {
      Camera cam = managed[i];
      if (cam == null) continue;
      if (cam.targetTexture == rt) cam.targetTexture = null;
    }
    managed.Clear();
    if (compositorCamera != null) compositorCamera.enabled = false;
    if (blitter != null) blitter.source = null;
    wired = false;
  }

  void ReleaseTarget()
  {
    if (rt == null) return;
    if (blitter != null) blitter.source = null;
    rt.Release();
    if (Application.isPlaying) Object.Destroy(rt); else Object.DestroyImmediate(rt);
    rt = null;
    rtWidth = rtHeight = 0;
  }

  void EnsureCompositorCamera()
  {
    if (compositorCamera != null) return;

    var go = new GameObject("Compositor");
    go.transform.SetParent(transform, false);
    compositorCamera = go.AddComponent<Camera>();
    // Card default: Nothing. The blit covers every pixel, so there is nothing
    // to clear; clearCompositorCamera exists only as a profiling comparison.
    compositorCamera.clearFlags = clearCompositorCamera
      ? CameraClearFlags.SolidColor
      : CameraClearFlags.Nothing;
    compositorCamera.backgroundColor = Color.black;
    compositorCamera.cullingMask = 0;                         // draws nothing itself
    compositorCamera.depth = 100f;                            // renders after every scene camera
    compositorCamera.useOcclusionCulling = false;
    compositorCamera.allowHDR = false;
    compositorCamera.allowMSAA = false;
    compositorCamera.orthographic = true;
    compositorCamera.orthographicSize = 1f;
    compositorCamera.nearClipPlane = 0.01f;
    compositorCamera.farClipPlane = 1f;
    compositorCamera.targetTexture = null;                    // always the backbuffer
    compositorCamera.depthTextureMode = DepthTextureMode.None;
    compositorCamera.enabled = false;                         // enabled once the RT exists

    blitter = go.AddComponent<CompositorBlit>();
    blitter.source = rt;
  }

  // --- coordinate helpers --------------------------------------------------
  //
  // Screen.width/Screen.height stay native while cameras render into the RT,
  // so any code that feeds a Screen-space pixel into a camera that now targets
  // the RT must rescale. These are identity at scale 1.0 and when the
  // compositor is off, so existing call sites keep their current behaviour.

  /// Native backbuffer pixel -> managed-camera (RT) pixel.
  public static Vector2 ScreenToRTPoint(Vector2 nativeScreenPoint)
  {
    SceneCompositor c = instance;
    if (c == null || c.rt == null || !c.compositorEnabled) return nativeScreenPoint;
    if (Screen.width <= 0 || Screen.height <= 0) return nativeScreenPoint;
    return new Vector2(nativeScreenPoint.x * ((float)c.rt.width / Screen.width),
                       nativeScreenPoint.y * ((float)c.rt.height / Screen.height));
  }

  /// Managed-camera (RT) pixel -> native backbuffer pixel.
  public static Vector2 RTToScreenPoint(Vector2 rtPoint)
  {
    SceneCompositor c = instance;
    if (c == null || c.rt == null || !c.compositorEnabled) return rtPoint;
    if (c.rt.width <= 0 || c.rt.height <= 0) return rtPoint;
    return new Vector2(rtPoint.x * ((float)Screen.width / c.rt.width),
                       rtPoint.y * ((float)Screen.height / c.rt.height));
  }

  /// Scale-invariant WorldToScreenPoint: the result is always in native
  /// backbuffer pixels, which is what Overlay-canvas UI placement needs.
  public static Vector3 WorldToNativeScreen(Camera cam, Vector3 world)
  {
    if (cam == null) return Vector3.zero;
    Vector3 v = cam.WorldToViewportPoint(world);
    return new Vector3(v.x * Screen.width, v.y * Screen.height, v.z);
  }

  /// ScreenPointToRay that accepts native backbuffer pixels regardless of
  /// whether cam renders to the RT or straight to the screen.
  public static Ray NativeScreenPointToRay(Camera cam, Vector2 nativeScreenPoint)
  {
    if (cam == null) return new Ray(Vector3.zero, Vector3.forward);
    SceneCompositor c = instance;
    if (c != null && c.IsManaged(cam))
      return cam.ScreenPointToRay(ScreenToRTPoint(nativeScreenPoint));
    return cam.ScreenPointToRay(nativeScreenPoint);
  }
}
