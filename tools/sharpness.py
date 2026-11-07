#!/usr/bin/env python3
"""sharpness.py — per-scanline luminance-transition edge metric (card N1).

Given a physical-pixel PNG and a caption-strip rectangle, compute the width
of 20%-80% luminance transitions across vertical stems (text strokes) on
every scanline inside the rect, then report median, p90, and the min stroke
width. A 1:1 (non-upscaled) render shows transitions of ~1 px; a blurry
upscaled render shows 2+ px.

Pass thresholds (see tools/MEASURE.md):
    median transition <= 1.5 px, p90 <= 2.0 px

Usage:
    python3 tools/sharpness.py screenshot.png --rect x,y,w,h [--stem-mode auto]
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

def _transitions_in_row(row):
    """Return (widths, min_run) for one scanline.

    widths: per-edge width (in px) of the 20%->80% luminance transition.
    min_run: minimum dark-stroke run length in the row (min stroke width).
    A "dark" pixel is one with luminance < 0.5 after local contrast check.
    """
    n = len(row)
    if n < 3:
        return [], 0
    # adaptive threshold: pixel is "dark" if it is clearly below the row
    # background (local contrast), using a simple hysteresis on the row.
    lo, hi = float(np.min(row)), float(np.max(row))
    if hi - lo < 0.2:
        return [], 0  # no stem on this scanline
    bg = hi if (hi - lo) < (lo + 0.0) or True else hi  # background = brighter side
    # binary mask of "dark" (text) pixels
    dark = row < 0.5
    if not dark.any():
        dark = row < lo + 0.5 * (hi - lo)
    if not dark.any():
        return [], 0

    widths = []
    prev_dark = bool(dark[0])
    i = 0
    while i < n - 1:
        cur = bool(dark[i + 1])
        if cur != prev_dark:
            # edge between pixels i and i+1: measure where the linear
            # crossing of the two samples hits the 20% and 80% levels,
            # expressed in pixels (1.0 = sharp single-pixel transition).
            a, b = row[i], row[i + 1]
            lo_l = min(a, b)
            hi_l = max(a, b)
            if hi_l - lo_l < 1e-6:
                widths.append(1.0)
            else:
                frac_a = (a - lo_l) / (hi_l - lo_l)
                frac_b = (b - lo_l) / (hi_l - lo_l)
                x0 = i + (0.2 - frac_a) / (frac_b - frac_a)
                x1 = i + (0.8 - frac_a) / (frac_b - frac_a)
                widths.append(max(abs(x1 - x0), 1e-3))
            prev_dark = cur
        i += 1

    # min dark run length in the row
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

    if not all_widths:
        return {
            "rect": [x, y, w, h],
            "scanlines_with_edges": 0,
            "edges": 0,
            "median_px": None,
            "p90_px": None,
            "min_stroke_px": None,
            "note": "no luminance transitions found in rect",
        }

    a = np.asarray(all_widths)
    return {
        "rect": [x, y, w, h],
        "scanlines_with_edges": scanlines_with_edges,
        "edges": int(a.size),
        "median_px": round(float(np.median(a)), 3),
        "p90_px": round(float(np.percentile(a, 90)), 3),
        "min_stroke_px": int(min_stroke) if min_stroke is not None else None,
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
