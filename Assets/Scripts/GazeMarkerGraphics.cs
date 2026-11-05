using UnityEngine;

/// Runtime-generated sprites for the 2D gaze markers (card N10).
///
/// These are built in code rather than committed as PNGs for two reasons:
/// 1. No binary assets in the repo, so a change to the ring thickness is a
///    reviewable one-line diff instead of an opaque blob.
/// 2. The shapes are analytic, so they are generated with a signed-distance
///    coverage term and come out with exactly a 1 px transition - which is what
///    the N1 sharpness harness measures (median edge transition <= 1.5 px).
///
/// Everything is cached in statics: the textures are created once per player
/// session and shared by every marker.
public static class GazeMarkerGraphics
{
    // Textures are authored at 2x the reference-pixel size so that a 48 px
    // marker at the 1280x720 CanvasScaler reference still has pixels to spare
    // when the Chromebook runs the canvas above the reference height.
    public const int RingTexSize = 96;
    public const int ArrowTexSize = 48;

    static Sprite _ring;
    static Sprite _halo;
    static Sprite _arrow;

    /// White ring: outer radius 46 px, 6 px thick (in texture pixels).
    public static Sprite Ring
    {
        get
        {
            if (_ring == null) _ring = BuildRing("GazeMarkerRing", 46f, 6f, Color.white);
            return _ring;
        }
    }

    /// Black halo ring drawn *behind* the white ring: same centreline, thicker,
    /// so the white edge keeps contrast against both snow and starfield. This is
    /// the "double outline" the card asks for.
    public static Sprite Halo
    {
        get
        {
            if (_halo == null) _halo = BuildRing("GazeMarkerHalo", 47f, 12f, Color.black);
            return _halo;
        }
    }

    /// Triangle pointing along +Y with a baked black outline, so the off-screen
    /// arrow is one draw call and keeps contrast on any background.
    public static Sprite Arrow
    {
        get
        {
            if (_arrow == null) _arrow = BuildArrow("GazeMarkerArrow");
            return _arrow;
        }
    }

    /// Frees the cached textures. Only useful for tests that want a clean slate;
    /// the player never needs it.
    public static void ResetCache()
    {
        DestroySprite(ref _ring);
        DestroySprite(ref _halo);
        DestroySprite(ref _arrow);
    }

    static void DestroySprite(ref Sprite s)
    {
        if (s == null) return;
        Texture2D t = s.texture;
        if (Application.isPlaying) { Object.Destroy(s); if (t != null) Object.Destroy(t); }
        else { Object.DestroyImmediate(s); if (t != null) Object.DestroyImmediate(t); }
        s = null;
    }

    /// Annulus with `thickness` centred on `outerRadius - thickness/2`.
    /// Coverage is 1 px wide so the edge reads as sharp but not aliased.
    static Sprite BuildRing(string name, float outerRadius, float thickness, Color color)
    {
        int size = RingTexSize;
        var tex = NewTexture(name, size);
        var px = new Color32[size * size];

        float mid = outerRadius - thickness * 0.5f;
        float half = thickness * 0.5f;
        float c = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Distance to the annulus band (negative inside the band).
                float d = Mathf.Abs(r - mid) - half;
                float a = Coverage(d);
                px[y * size + x] = WithAlpha(color, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        return MakeSprite(tex);
    }

    static Sprite BuildArrow(string name)
    {
        int size = ArrowTexSize;
        var tex = NewTexture(name, size);
        var px = new Color32[size * size];

        float m = 3f;                                    // margin so the outline fits
        var tip = new Vector2((size - 1) * 0.5f, size - 1 - m);
        var left = new Vector2(m, m);
        var right = new Vector2(size - 1 - m, m);
        const float outline = 2f;                        // black border width, px

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x, y);
                float d = TriangleDistance(p, tip, right, left);
                float inner = Coverage(d + outline);     // white core
                float outer = Coverage(d);               // core + outline
                Color col = Color.Lerp(Color.black, Color.white, inner);
                px[y * size + x] = WithAlpha(col, outer);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        return MakeSprite(tex);
    }

    /// Signed distance to a triangle; negative inside. Vertices must be given
    /// in counter-clockwise order for the sign to come out right.
    public static float TriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d = Mathf.Min(Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)), SegmentDistance(p, c, a));
        return PointInTriangle(p, a, b, c) ? -d : d;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 <= Mathf.Epsilon ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a);
        float d2 = Cross(c - b, p - b);
        float d3 = Cross(a - c, p - c);
        bool neg = d1 <= 0f && d2 <= 0f && d3 <= 0f;
        bool pos = d1 >= 0f && d2 >= 0f && d3 >= 0f;
        return neg || pos;
    }

    static float Cross(Vector2 u, Vector2 v) { return u.x * v.y - u.y * v.x; }

    /// Maps a signed distance in pixels to 0..1 coverage across one pixel.
    static float Coverage(float signedDistance)
    {
        return Mathf.Clamp01(0.5f - signedDistance);
    }

    static Color32 WithAlpha(Color c, float a)
    {
        return new Color32((byte)Mathf.RoundToInt(c.r * 255f),
                           (byte)Mathf.RoundToInt(c.g * 255f),
                           (byte)Mathf.RoundToInt(c.b * 255f),
                           (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
    }

    static Texture2D NewTexture(string name, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = name;
        tex.filterMode = FilterMode.Bilinear;            // sharp at 1:1, clean when scaled
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.DontSave;
        return tex;
    }

    static Sprite MakeSprite(Texture2D tex)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                              0, SpriteMeshType.FullRect);
        s.name = tex.name + "_sprite";
        s.hideFlags = HideFlags.DontSave;
        return s;
    }
}
