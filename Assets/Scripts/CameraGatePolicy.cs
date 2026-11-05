using UnityEngine;

/// Pure, side-effect-free decision logic for <see cref="CameraGate"/> (card N11).
///
/// Everything here is a static function over value types so the gating rules and
/// the render-texture sizing rules can be reasoned about (and unit tested)
/// without a Unity scene, a Camera, or a RenderTexture. CameraGate itself is a
/// thin adapter: it gathers facts from the scene and asks this class what to do.
public static class CameraGatePolicy
{
    /// How a gate decides whether its camera may render.
    public enum GateMode
    {
        /// Render only while the camera's output is actually in use (default).
        Gated = 0,

        /// Ignore the consumer/source conditions and render on every eligible
        /// frame. This is the card's kill-criterion fallback: if a shader reads
        /// the render texture every frame and gating produces a black surface,
        /// flip this one serialized field to AlwaysOn and keep only the
        /// frame-skip plus the smaller render texture.
        AlwaysOn = 1,
    }

    /// How the gate drives its camera.
    public enum DriveMode
    {
        /// Pick ManualRender for cameras that own a target texture, and
        /// ToggleEnabled for cameras that draw to the screen.
        Auto = 0,

        /// camera.enabled stays false and the gate calls camera.Render().
        /// Correct for render-to-texture cameras: the texture is refreshed
        /// before the on-screen cameras sample it, and the per-frame cost
        /// disappears completely when the gate is shut.
        ManualRender = 1,

        /// camera.enabled is toggled and Unity keeps the camera in its normal
        /// depth-sorted pass. Required for cameras that render to the screen:
        /// a manual Render() from LateUpdate would draw *before* the main
        /// camera's clear and be wiped out.
        ToggleEnabled = 2,
    }

    /// Resolve <see cref="DriveMode.Auto"/> against the one fact that matters.
    public static DriveMode ResolveDriveMode(DriveMode requested, bool hasTargetTexture)
    {
        if (requested != DriveMode.Auto) return requested;
        return hasTargetTexture ? DriveMode.ManualRender : DriveMode.ToggleEnabled;
    }

    /// Frame-skip divisor, normalised. 0 and negatives mean "every frame".
    public static int NormalizeEveryNth(int renderEveryNthFrame)
    {
        return renderEveryNthFrame < 1 ? 1 : renderEveryNthFrame;
    }

    /// True when this frame is one of the every-Nth frames for this gate.
    /// The offset exists so two gates on n=2 can be staggered onto opposite
    /// frames (or deliberately kept in phase when they share a texture).
    public static bool IsNthFrame(int renderEveryNthFrame, int frameIndex, int frameOffset)
    {
        int n = NormalizeEveryNth(renderEveryNthFrame);
        if (n == 1) return true;
        int i = frameIndex + frameOffset;
        int m = i % n;
        if (m < 0) m += n;   // Time.frameCount never goes negative, but be total
        return m == 0;
    }

    /// The card's gate decision.
    ///
    /// <paramref name="anyConsumerActive"/> is the caller's summary of "is this
    /// camera's output being looked at right now" — in CameraGate that is
    /// (a consumer of the texture is live) AND (something the culling mask
    /// admits is live), plus the trailing-frame grace period.
    public static bool ShouldRender(GateMode mode, bool anyConsumerActive,
                                    int renderEveryNthFrame, int frameIndex)
    {
        return ShouldRender(mode, anyConsumerActive, renderEveryNthFrame, frameIndex, 0);
    }

    /// Overload carrying the stagger offset.
    public static bool ShouldRender(GateMode mode, bool anyConsumerActive,
                                    int renderEveryNthFrame, int frameIndex,
                                    int frameOffset)
    {
        if (mode == GateMode.Gated && !anyConsumerActive) return false;
        return IsNthFrame(renderEveryNthFrame, frameIndex, frameOffset);
    }

    /// Size a render texture: halve it (owner default #6), then honour the
    /// card's 512 cap, then refuse to go below a floor, preserving aspect so a
    /// 16:9 portal lands on 512x288 rather than a squashed 512x512.
    ///
    /// Never returns larger than the original: this card only ever shrinks.
    public static (int w, int h) ScaledSize(int origW, int origH, float rtScale,
                                            int rtMaxDimension, int rtMinDimension)
    {
        if (origW < 1) origW = 1;
        if (origH < 1) origH = 1;
        if (rtScale <= 0f) rtScale = 1f;

        float w = origW * rtScale;
        float h = origH * rtScale;

        // Cap the long edge, uniformly, so the aspect ratio survives.
        if (rtMaxDimension > 0)
        {
            float longEdge = Mathf.Max(w, h);
            if (longEdge > rtMaxDimension)
            {
                float k = rtMaxDimension / longEdge;
                w *= k;
                h *= k;
            }
        }

        // Then raise the short edge off the floor, also uniformly.
        if (rtMinDimension > 0)
        {
            float shortEdge = Mathf.Min(w, h);
            if (shortEdge < rtMinDimension)
            {
                float k = rtMinDimension / shortEdge;
                w *= k;
                h *= k;
            }
        }

        int outW = RoundToEven(w);
        int outH = RoundToEven(h);

        // The floor must not inflate a texture that started out smaller.
        if (outW > origW) outW = origW;
        if (outH > origH) outH = origH;

        if (outW < 1) outW = 1;
        if (outH < 1) outH = 1;
        return (outW, outH);
    }

    static int RoundToEven(float v)
    {
        int i = Mathf.RoundToInt(v);
        if (i < 1) i = 1;
        if (i > 1 && (i & 1) == 1) i -= 1;
        return i;
    }
}
