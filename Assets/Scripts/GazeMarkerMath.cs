using UnityEngine;

/// Pure screen-space math for the 2D gaze markers (card N10).
///
/// Everything here is static and depends only on Vector2/Vector3/Rect, so it is
/// testable in an EditMode test without a Canvas, a Camera, or a scene. The
/// view (GazeMarkerView) owns all Unity plumbing; this file owns all of the
/// arithmetic that is easy to get wrong.
///
/// Conventions:
/// * "screen point" is what Camera.WorldToScreenPoint returns: x/y in pixels
///   with the origin at the bottom-left of the buffer, z = distance in front of
///   the camera in world units. z <= 0 means the anchor is behind the eye.
/// * "screen rect" is (0, 0, width, height) in the same pixel space.
public static class GazeMarkerMath
{
    /// Default inset, in reference pixels, of the off-screen arrow track from
    /// the buffer edge. Card N10 fixes this at 40 px.
    public const float DefaultInsetPx = 40f;

    /// True when the anchor is in front of the camera *and* its projection
    /// lands inside the buffer. Anything else is an off-screen case and must be
    /// drawn as a clamped arrow instead of a ring.
    public static bool IsOnScreen(Vector3 screenPoint, Rect screenRect)
    {
        if (screenPoint.z <= 0f) return false;              // behind the eye
        return screenRect.Contains(new Vector2(screenPoint.x, screenPoint.y));
    }

    /// Mirrors a projected point through the centre of the screen.
    ///
    /// WorldToScreenPoint of a point behind the camera is a *reflected*
    /// projection: an anchor behind-and-to-the-left reports as right-of-centre.
    /// Mirroring undoes that so the arrow points the short way round instead of
    /// confidently pointing away from the target.
    public static Vector2 ProjectBehindCamera(Vector2 screenPoint, Rect screenRect)
    {
        Vector2 c = screenRect.center;
        return new Vector2(2f * c.x - screenPoint.x, 2f * c.y - screenPoint.y);
    }

    /// Clamps a point into the screen rect shrunk by `inset` on every side, so
    /// a marker parked on the edge is fully visible rather than half-cropped.
    ///
    /// Degenerate buffers (very small, or an inset larger than half the buffer)
    /// collapse to the centre rather than producing an inverted rect.
    public static Vector2 ClampToInset(Vector2 screenPoint, Rect screenRect, float inset)
    {
        if (inset < 0f) inset = 0f;
        float minX = screenRect.xMin + inset;
        float maxX = screenRect.xMax - inset;
        float minY = screenRect.yMin + inset;
        float maxY = screenRect.yMax - inset;

        if (minX > maxX) minX = maxX = screenRect.center.x;
        if (minY > maxY) minY = maxY = screenRect.center.y;

        return new Vector2(Mathf.Clamp(screenPoint.x, minX, maxX),
                           Mathf.Clamp(screenPoint.y, minY, maxY));
    }

    /// Z rotation, in degrees, for an arrow sprite whose untransformed tip
    /// points along +Y, so that it points from `from` towards `to`.
    ///
    /// Returns 0 for a zero-length delta (arrow keeps whatever it had, which is
    /// better than NaN).
    public static float ArrowAngleDeg(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        if (d.sqrMagnitude <= Mathf.Epsilon) return 0f;
        // atan2(y, x) measures from +X; the sprite points along +Y, hence -90.
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
    }

    /// Breathing scale for the on-screen ring: 1 +/- amp at `hz` cycles/second.
    /// Card N10 asks for 0.9..1.1 at 1 Hz, i.e. amp 0.1, hz 1.
    public static float PulseScale(float t, float hz, float amp)
    {
        return 1f + amp * Mathf.Sin(2f * Mathf.PI * hz * t);
    }

    /// Converts a reference-pixel length into buffer pixels for a CanvasScaler
    /// in ScaleWithScreenSize / MatchWidthOrHeight mode.
    ///
    /// Only used for diagnostics and for sizing the inset on buffers that are
    /// not the 1280x720 reference; the Canvas itself does the real scaling.
    public static float ScaleFactor(Vector2 referenceResolution, Vector2 screenSize, float matchWidthOrHeight)
    {
        if (referenceResolution.x <= 0f || referenceResolution.y <= 0f) return 1f;
        float logWidth = Mathf.Log(Mathf.Max(screenSize.x, 1f) / referenceResolution.x, 2f);
        float logHeight = Mathf.Log(Mathf.Max(screenSize.y, 1f) / referenceResolution.y, 2f);
        float logWeighted = Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(matchWidthOrHeight));
        return Mathf.Pow(2f, logWeighted);
    }
}
