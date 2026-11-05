using UnityEngine;

/// Final stage of the N5 compositor: copy the scaled scene RenderTexture onto
/// the native drawing buffer.
///
/// Lives on the "Compositor" camera (cullingMask 0, clearFlags Nothing,
/// depth 100) so the blit is the only thing that camera does. A Screen Space
/// Overlay canvas still draws after every camera, straight to the backbuffer,
/// so UI/captions stay at native resolution regardless of the scene scale.
///
/// Note on cost: because this component declares OnRenderImage, Unity treats
/// the Compositor camera as having an image effect and renders it into a
/// throwaway temporary RenderTexture first, handing that to us as `src`. We
/// ignore `src` entirely and blit the shared scene RT to `dest` (null means
/// the backbuffer). The temp RT is why the compositor is not free even at
/// scale 1.0, and it is what the N1 on/off A/B measures. There is deliberately
/// no second blit path here: adding an OnPostRender method would not remove
/// the temp RT as long as OnRenderImage exists on the same component, so the
/// kill-criterion escape hatch is SceneCompositor.SetCompositorEnabled(false),
/// which detaches every camera and disables this one outright.
[RequireComponent(typeof(Camera))]
public class CompositorBlit : MonoBehaviour
{
  [HideInInspector] public RenderTexture source;
  [HideInInspector] public bool flipVertical;

  static readonly Vector2 FlipScale = new Vector2(1f, -1f);
  static readonly Vector2 FlipOffset = new Vector2(0f, 1f);

  void OnRenderImage(RenderTexture src, RenderTexture dest)
  {
    RenderTexture from = source;
    if (from == null)
    {
      // Nothing of ours to show (compositor disabled mid-frame, or the RT was
      // released): pass the camera's own target through so the frame is never
      // left undefined.
      if (src != dest) Graphics.Blit(src, dest);
      return;
    }

    if (flipVertical) Graphics.Blit(from, dest, FlipScale, FlipOffset);
    else Graphics.Blit(from, dest);
  }
}
