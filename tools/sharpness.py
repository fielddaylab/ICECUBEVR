#!/usr/bin/env python3
"""sharpness.py — per-scanline luminance-transition edge metric (card N1).

Given a physical-pixel PNG and a caption-strip rectangle, compute the width
of 20%-80% luminance transitions across vertical stems (text strokes) on
every scanline inside the rect, then report median, p90, and the min stroke
width. A 1:1 (non-upscaled) render shows transitions of ~1 px; a blurry
upscaled render shows 2+ px.

Also reports, for the N18 acceptance gate:
    cap_height_px  - rendered glyph-body height in physical pixels
    contrast_ratio - WCAG contrast between the text ink and the background,
                     computed on sRGB-linearized relative luminance

Both are polarity-aware: light-on-dark captions (the usual case here) are
measured the same way as dark-on-light.

Pass thresholds (see tools/MEASURE.md and tools/ACCEPTANCE.md):
    median transition <= 1.5 px, p90 <= 2.0 px,
    cap-height >= 22 px, contrast >= 7:1

Usage:
    python3 tools/sharpness.py screenshot.png --rect x,y,w,h
"""
import argparse
import json
import sys

import numpy as np
from PIL import Image

def _lum(img):
    """Grayscale luminance, 0..1 float array (H, W)."""
    if img.mode != "L":
        img = img.convert("L")
    a = np.asarray(img, dtype=np.float64) / 255.0
    return a

def _level_crossings(row, level):
    """Sub-pixel positions where the scanline crosses `level`."""
    out = []
    for i in range(len(row) - 1):
        a, b = float(row[i]), float(row[i + 1])
        if (a < level <= b) or (b < level <= a):
            if abs(b - a) < 1e-12:
                out.append(float(i))
            else:
                out.append(i + (level - a) / (b - a))
    return out


def _transitions_in_row(row):
    """Return (widths, min_run) for one scanline.

    widths: per-edge 20%->80% luminance transition width in pixels, measured
    as the distance between each 80 % level crossing and the nearest 20 %
    crossing. This is the standard edge-width metric and it scales with the
    real ramp: an ideal hard (1:1, unfiltered) edge measures ~0.6 px, a 2x
    browser upscale spreads the same edge to ~1.2 px, and heavier filtering
    grows from there. That separation is the whole point of this card, so the
    measurement must not be normalized inside a single pixel pair.

    min_run: minimum stroke run length in the row (min stroke width).
    """
    n = len(row)
    if n < 3:
        return [], 0
    lo, hi = float(np.min(row)), float(np.max(row))
    if hi - lo < 0.2:
        return [], 0  # no stem on this scanline

    # Polarity-free stem mask: the minority population by area is the stroke,
    # so light-on-dark captions behave like dark-on-light text.
    mid = lo + 0.5 * (hi - lo)
    dark = row < mid
    if dark.sum() * 2 > n:
        dark = ~dark
    if not dark.any():
        return [], 0

    lvl20 = lo + 0.20 * (hi - lo)
    lvl80 = lo + 0.80 * (hi - lo)
    x20 = _level_crossings(row, lvl20)
    x80 = _level_crossings(row, lvl80)

    widths = []
    if x20 and x80:
        for b in x80:
            d = min(abs(b - a) for a in x20)
            widths.append(max(d, 1e-3))

    # min stroke run length in the row
    runs = []
    run = 0
    for d in dark:
        if d:
            run += 1
        else:
            if run:
                runs.append(run)
                run = 0
    if run:
        runs.append(run)
    min_run = min(runs) if runs else 0
    return widths, min_run


def sharpness(img, rect):
    """Compute metrics over rect (x, y, w, h) in physical pixels."""
    lum = _lum(img)
    H, W = lum.shape
    x, y, w, h = [int(v) for v in rect]
    x = max(0, min(x, W - 1))
    y = max(0, min(y, H - 1))
    w = max(1, min(w, W - x))
    h = max(1, min(h, H - y))
    region = lum[y:y + h, x:x + w]

    all_widths = []
    min_stroke = None
    scanlines_with_edges = 0
    for row in region:
        widths, min_run = _transitions_in_row(row)
        if widths:
            all_widths.extend(widths)
            scanlines_with_edges += 1
            if min_run > 0:
                min_stroke = min_run if min_stroke is None else min(min_stroke, min_run)

    # Cap height and contrast are measured independently of the per-scanline
    # edge walk: a low-contrast caption yields no 20-80% transitions, and it
    # must still fail the acceptance gate on a *number* rather than go silent.
    extra = _cap_height_and_contrast(region)

    if not all_widths:
        return {
            "rect": [x, y, w, h],
            "scanlines_with_edges": 0,
            "edges": 0,
            "median_px": None,
            "p90_px": None,
            "min_stroke_px": None,
            "cap_height_px": extra["cap_height_px"],
            "contrast_ratio": extra["contrast_ratio"],
            "ink_polarity": extra["ink_polarity"],
            "ink_luminance": extra["ink_luminance"],
            "bg_luminance": extra["bg_luminance"],
            "ink_coverage": extra["ink_coverage"],
            "note": "no 20-80% luminance transitions found in rect "
                    "(too little contrast, or the rect misses the element)",
        }

    a = np.asarray(all_widths)
    return {
        "rect": [x, y, w, h],
        "scanlines_with_edges": scanlines_with_edges,
        "edges": int(a.size),
        "median_px": round(float(np.median(a)), 3),
        "p90_px": round(float(np.percentile(a, 90)), 3),
        "min_stroke_px": int(min_stroke) if min_stroke is not None else None,
        "cap_height_px": extra["cap_height_px"],
        "contrast_ratio": extra["contrast_ratio"],
        "ink_polarity": extra["ink_polarity"],
        "ink_luminance": extra["ink_luminance"],
        "bg_luminance": extra["bg_luminance"],
        "ink_coverage": extra["ink_coverage"],
    }


def _srgb_to_linear(c):
    """sRGB 0..1 -> linear relative luminance component (WCAG 2.x)."""
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def _cap_height_and_contrast(region):
    """Cap height (px) and WCAG contrast ratio for a text/overlay region.

    Polarity-aware and area-independent. Text covers only a small fraction of
    a caption strip's pixels (a few percent), so a fixed percentile cannot
    separate ink from background: both the 1st and 99th percentile of a thin
    caption are background. Instead the background is the region median
    (background dominates by area) and the ink side is whichever extreme sits
    further from it; pixels past the midpoint between the two are the ink
    mask, and the reported levels are the *medians* of the two populations,
    which is robust to hot pixels and to antialiased edge pixels.

    cap_height_px: the longest run of consecutive scanlines carrying real ink,
    where "real ink" means a row whose ink coverage is at least a quarter of
    the strongest row's coverage (scale-free, so it does not depend on how
    much blank strip was included in --rect). For a 1:1 render this is the
    rendered glyph-body height in physical pixels.

    contrast_ratio: (L_light + 0.05) / (L_dark + 0.05) on sRGB-linearized
    relative luminance of the ink core versus the background.
    """
    none = {"cap_height_px": None, "contrast_ratio": None,
            "ink_polarity": None, "ink_luminance": None, "bg_luminance": None,
            "ink_coverage": None}
    if region.size == 0:
        return none

    flat = region.reshape(-1)
    bg_seed = float(np.median(flat))
    d_dark = bg_seed - float(flat.min())
    d_light = float(flat.max()) - bg_seed

    if d_dark >= d_light:
        polarity = "dark_on_light"
        extreme = float(flat.min())
    else:
        polarity = "light_on_dark"
        extreme = float(flat.max())

    # No usable ink/background separation anywhere in this rect.
    if abs(extreme - bg_seed) < 0.10:
        out = dict(none)
        out["ink_polarity"] = polarity
        return out

    mid = (extreme + bg_seed) / 2.0
    ink = (region < mid) if polarity == "dark_on_light" else (region > mid)
    n_ink = int(ink.sum())
    if n_ink == 0:
        out = dict(none)
        out["ink_polarity"] = polarity
        return out

    ink_level = float(np.median(region[ink]))
    bg_vals = region[~ink]
    bg_level = float(np.median(bg_vals)) if bg_vals.size else bg_seed

    # Cap height: longest run of scanlines with real ink coverage.
    cover = ink.mean(axis=1)
    peak = float(cover.max())
    thresh = max(peak * 0.25, 1.0 / region.shape[1])
    max_run = run = 0
    for c in cover:
        if c >= thresh:
            run += 1
            if run > max_run:
                max_run = run
        else:
            run = 0

    lin_ink = _srgb_to_linear(ink_level)
    lin_bg = _srgb_to_linear(bg_level)
    lo, hi = min(lin_ink, lin_bg), max(lin_ink, lin_bg)
    contrast = round((hi + 0.05) / (lo + 0.05), 3)

    return {
        "cap_height_px": int(max_run) if max_run > 0 else None,
        "contrast_ratio": contrast,
        "ink_polarity": polarity,
        "ink_luminance": round(ink_level, 4),
        "bg_luminance": round(bg_level, 4),
        "ink_coverage": round(n_ink / float(region.size), 5),
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("png", help="physical-pixel screenshot PNG")
    ap.add_argument("--rect", default=None,
                    help="caption strip rect as x,y,w,h (physical px); "
                         "default: full image")
    args = ap.parse_args(argv)

    img = Image.open(args.png)
    if args.rect:
        try:
            rect = tuple(int(v) for v in args.rect.split(","))
            assert len(rect) == 4
        except Exception:
            print("error: --rect must be x,y,w,h", file=sys.stderr)
            return 2
    else:
        rect = (0, 0, img.width, img.height)

    result = sharpness(img, rect)
    result["image"] = args.png
    result["image_size"] = [img.width, img.height]
    print(json.dumps(result, indent=2))
    return 0

if __name__ == "__main__":
    sys.exit(main())
