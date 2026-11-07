#!/usr/bin/env python3
"""texture_audit.py — N16: WebGL texture memory estimate (ARM Chromebook sanity).

Inventories every texture in Assets/ via its .meta TextureImporter settings and
estimates GPU texture memory under two hypotheses:

  * s3tc present  -> DXT1 (0.5 B/px) / DXT5 (1.0 B/px) compressed upload
  * s3tc absent   -> Unity software-decompresses to RGBA32 (4.0 B/px) in GPU
                     memory regardless of the compressed format

The script is report-only: it never writes under Assets/ or ProjectSettings/.

Usage:
    python3 tools/texture_audit.py [--assets Assets] [--budget-mb 150]
                                   [--markdown] [--json]
"""
import argparse
import json
import os
import re
import struct
import sys

MIP_FACTOR = 4.0 / 3.0  # sum of all mipmap levels for a POT texture


# ---------------------------------------------------------------------------
# Image header dimension readers
# ---------------------------------------------------------------------------

def read_png_dims(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    w, h = struct.unpack(">II", head[16:24])
    return (w, h)


def read_jpg_dims(path):
    with open(path, "rb") as f:
        data = f.read()
    i = 2
    while i + 9 < len(data):
        if data[i] != 0xFF:
            i += 1
            continue
        marker = data[i + 1]
        if marker in (0xC0, 0xC2):  # SOF0 / SOF2
            h, w = struct.unpack(">HH", data[i + 5:i + 9])
            return (w, h)
        if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
            i += 2
            continue
        seglen = struct.unpack(">H", data[i + 2:i + 4])[0]
        i += 2 + seglen
    return None


def read_tga_dims(path):
    with open(path, "rb") as f:
        head = f.read(18)
    if len(head) < 18:
        return None
    w, h = struct.unpack("<HH", head[12:18])
    return (w, h)


def read_tiff_dims(path):
    with open(path, "rb") as f:
        head = f.read(8)
    if len(head) < 8 or head[:2] not in (b"II", b"MM"):
        return None
    little = head[:2] == b"II"
    off = struct.unpack("<I" if little else ">I", head[4:8])[0]
    with open(path, "rb") as f:
        f.seek(off)
        n = struct.unpack("<H" if little else ">H", f.read(2))[0]
        w = h = None
        for _ in range(n):
            rec = f.read(12)
            if len(rec) < 12:
                break
            tag = struct.unpack("<H" if little else ">H", rec[0:2])[0]
            cnt = struct.unpack("<I" if little else ">I", rec[4:8])[0]
            val = struct.unpack("<I" if little else ">I", rec[8:12])[0]
            if tag == 256 and cnt == 1:
                w = val
            elif tag == 257 and cnt == 1:
                h = val
    if w and h:
        return (w, h)
    return None


def read_psd_dims(path):
    with open(path, "rb") as f:
        head = f.read(26)
    if len(head) < 26 or head[:4] != b"8BPS":
        return None
    h, w = struct.unpack(">II", head[14:22])
    return (w, h)


def read_hdr_dims(path):
    with open(path, "r", errors="replace") as f:
        first = f.readline().strip()
    m = re.match(r"#\s*EXR\s+\d+\s+\d+", first)
    if m:
        return (int(m.group(2)), int(m.group(1)))
    return None


def _sniff_dims(path):
    """Content-based dimension detection (extension may be a lie)."""
    with open(path, "rb") as f:
        head = f.read(12)
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return read_png_dims(path)
    if head[:3] == b"\xff\xd8\xff":
        return read_jpg_dims(path)
    if head[:2] in (b"II", b"MM"):
        return read_tiff_dims(path)
    if head[:4] == b"8BPS":
        return read_psd_dims(path)
    return None


def read_image_dims(path):
    ext = os.path.splitext(path)[1].lower()
    readers = {
        ".png": read_png_dims,
        ".jpg": read_jpg_dims,
        ".jpeg": read_jpg_dims,
        ".tga": read_tga_dims,
        ".tif": read_tiff_dims,
        ".tiff": read_tiff_dims,
        ".psd": read_psd_dims,
        ".hdr": read_hdr_dims,
        ".exr": read_hdr_dims,
    }
    reader = readers.get(ext)
    try:
        if reader is not None:
            dims = reader(path)
            if dims is not None:
                return dims
    except (OSError, struct.error):
        pass
    # fall back to content sniffing (misnamed files)
    return _sniff_dims(path)


# ---------------------------------------------------------------------------
# .meta TextureImporter parsing (plain-text, no PyYAML)
# ---------------------------------------------------------------------------

def _meta_int(text, key, default=None):
    m = re.search(r"^\s*%s:\s*(-?\d+)\s*$" % re.escape(key), text, re.M)
    if m:
        return int(m.group(1))
    return default


def parse_platform_overrides(text):
    """Return {buildTarget: {key: int}} for the platformSettings block."""
    out = {}
    m = re.search(r"^\s*platformSettings:\s*\n", text, re.M)
    if not m:
        return out
    lines = text[m.end():].splitlines()
    cur = None
    for line in lines:
        if not line.strip():
            continue
        bm = re.match(r"^  - buildTarget:\s*(\S+)", line)
        if bm:
            cur = bm.group(1)
            out[cur] = {}
            continue
        if line.startswith("    "):
            if cur is not None:
                kv = re.match(r"^\s*(\w+):\s*(-?\d+)\s*$", line)
                if kv:
                    out[cur][kv.group(1)] = int(kv.group(2))
            continue
        if re.match(r"^  - ", line):
            # an entry without buildTarget on the first line
            if cur is not None:
                kv = re.match(r"^  - (\w+):\s*(-?\d+)\s*$", line)
                if kv:
                    out[cur][kv.group(1)] = int(kv.group(2))
            continue
        if re.match(r"^  \S", line):
            break
    return out


def parse_texture_meta(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        return None
    if "TextureImporter:" not in text:
        return None

    tm = re.search(r"^TextureImporter:\s*\n", text, re.M)
    body = text[tm.end():] if tm else text
    stop = re.search(r"^\S", body, re.M)
    if stop:
        body = body[:stop.start()]

    max_size = _meta_int(body, "maxTextureSize", 2048)
    mip = _meta_int(body, "enableMipMap", 1)
    readable = _meta_int(body, "isReadable", 0)
    tex_type = _meta_int(body, "textureType", 0)
    tex_shape = _meta_int(body, "textureShape", 1)
    fmt = _meta_int(body, "textureFormat", -1)

    platform = parse_platform_overrides(text)
    webgl = platform.get("WebGL", {})
    eff_max = webgl.get("maxTextureSize", max_size)
    eff_comp = webgl.get("textureCompression", 1)

    is_normal = tex_type in (1, 4)
    return {
        "meta": path,
        "max_size": max_size,
        "webgl_max_size": eff_max,
        "compression": eff_comp,
        "mipmaps": bool(mip),
        "readable": bool(readable),
        "is_normal": is_normal,
        "texture_shape": tex_shape,
        "texture_format": fmt,
        "webgl_overridden": "WebGL" in platform,
        "crunched": bool(webgl.get("crunchedCompression", 0)),
    }


# ---------------------------------------------------------------------------
# Memory model
# ---------------------------------------------------------------------------

def _alpha_likely(path, info):
    """Heuristic: alpha-bearing formats are compressed to DXT5 / RGBA32."""
    ext = os.path.splitext(path)[1].lower()
    if info["is_normal"]:
        return True
    if ext in (".png", ".tif", ".tiff", ".tga", ".psd", ".hdr", ".exr"):
        return True
    return False


def estimate(info, src_path, dims, assumed):
    """Return dict with per-branch byte counts for one texture."""
    w, h = dims
    maxw = info["webgl_max_size"]
    scale = min(1.0, maxw / float(max(w, h)))
    ew, eh = max(1, int(round(w * scale))), max(1, int(round(h * scale)))

    mip = MIP_FACTOR if info["mipmaps"] else 1.0
    face_count = 6 if info["texture_shape"] == 2 else 1
    px = ew * eh * face_count * mip

    comp = info["compression"]
    alpha = _alpha_likely(src_path, info)
    if comp == 0:
        kind = "uncompressed-RGBA32"
        dxt = 4.0
        fallback = 4.0
    else:
        # 1=Alpha, 2=Quality, 3=Fast all land on DXT1/DXT5 for D3D/WebGL
        kind = "DXT5" if alpha else "DXT1"
        dxt = 1.0 if alpha else 0.5
        fallback = 4.0

    dxt_bytes = int(px * dxt)
    rgba_bytes = int(px * fallback)
    cpu_bytes = 2 * rgba_bytes if info["readable"] else 0
    return {
        "asset": os.path.relpath(src_path, os.path.dirname(info["meta"]) + "/..") ,
        "size": "%dx%d" % (ew, eh),
        "faces": face_count,
        "kind": kind,
        "dxt_bytes": dxt_bytes,
        "rgba_bytes": rgba_bytes,
        "cpu_bytes": cpu_bytes,
        "dims_assumed": assumed,
        "readable": info["readable"],
        "crunched": info["crunched"],
        "n13_scope": face_count > 1,
    }


def to_markdown(result):
    lines = []
    lines.append("| Texture | Size | Format | DXT bytes | RGBA32 bytes |")
    lines.append("| --- | --- | --- | --- | --- |")
    for t in result["textures"]:
        flag = ""
        if t["readable"]:
            flag += " readable"
        if t["n13_scope"]:
            flag += " N13"
        if t["dims_assumed"]:
            flag += " dims-assumed"
        lines.append("| %s%s | %s | %s | %s | %s |" % (
            t["asset"], flag, t["size"], t["kind"],
            format(t["dxt_bytes"], ","), format(t["rgba_bytes"], ",")))
    lines.append("")
    lines.append("Totals: DXT %.1f MB / RGBA32 %.1f MB (budget %.1f MB)" % (
        result["s3tc_present"]["total_mb"], result["s3tc_absent"]["total_mb"],
        result["budget_mb"]))
    return "\n".join(lines)


def scene_referenced_guids(scene_paths):
    """Transitive set of guids referenced by the given scene/prefab/material files."""
    seed = set()
    for sp in scene_paths:
        try:
            with open(sp, errors="replace") as f:
                seed.update(re.findall(r"guid: ([a-f0-9]{32})", f.read()))
        except OSError:
            continue
    # transitively expand through prefabs, materials, scenes, assets
    all_refs = {}
    for root, dirs, files in os.walk("Assets"):
        for f in files:
            if f.endswith((".prefab", ".mat", ".unity", ".asset")):
                pth = os.path.join(root, f)
                with open(pth, errors="replace") as fh:
                    all_refs[pth] = set(re.findall(r"guid: ([a-f0-9]{32})", fh.read()))
    frontier = set(seed)
    seen = set()
    while frontier:
        new_frontier = set()
        for guids in all_refs.values():
            hit = guids & frontier
            if hit and not (guids <= seen):
                new_frontier |= guids - seen
        seen |= new_frontier
        frontier = new_frontier
    seed |= seen
    return seed


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--assets", default="Assets")
    ap.add_argument("--budget-mb", type=float, default=150.0)
    ap.add_argument("--markdown", action="store_true")
    ap.add_argument("--json", action="store_true")
    ap.add_argument(
        "--scene",
        action="append",
        default=[],
        metavar="PATH",
        help="limit the report to textures (transitively) referenced by this "
             "scene/prefab/material file; may be repeated",
    )
    args = ap.parse_args(argv)

    used_guids = None
    if args.scene:
        used_guids = scene_referenced_guids(args.scene)

    textures = []
    for root, dirs, files in os.walk(args.assets):
        dirs.sort()
        for f in sorted(files):
            if not f.endswith(".meta"):
                continue
            meta = os.path.join(root, f)
            info = parse_texture_meta(meta)
            if info is None:
                continue
            src = meta[: -len(".meta")]
            if used_guids is not None:
                gm = re.search(r"guid: ([a-f0-9]{32})", open(meta, errors="replace").read())
                if not gm or gm.group(1) not in used_guids:
                    continue
            dims = read_image_dims(src)
            assumed = False
            if dims is None:
                dims = (info["webgl_max_size"], info["webgl_max_size"])
                assumed = True
            est = estimate(info, src, dims, assumed)
            est["asset"] = os.path.relpath(src, args.assets)
            textures.append(est)

    total_dxt = sum(t["dxt_bytes"] for t in textures)
    total_rgba = sum(t["rgba_bytes"] for t in textures)
    total_cpu = sum(t["cpu_bytes"] for t in textures)

    def mb(n):
        return n / (1024.0 * 1024.0)

    result = {
        "count": len(textures),
        "scene_filtered": bool(args.scene),
        "budget_mb": args.budget_mb,
        "s3tc_present": {
            "total_bytes": total_dxt,
            "total_mb": mb(total_dxt),
            "under_budget": total_dxt < args.budget_mb * 1024 * 1024,
        },
        "s3tc_absent": {
            "total_bytes": total_rgba,
            "total_mb": mb(total_rgba),
            "under_budget": total_rgba < args.budget_mb * 1024 * 1024,
        },
        "readable_cpu_mb": mb(total_cpu),
        "n13_scope_count": sum(1 for t in textures if t["n13_scope"]),
        "readable_count": sum(1 for t in textures if t["readable"]),
        "assumed_dims_count": sum(1 for t in textures if t["dims_assumed"]),
        "textures": sorted(textures, key=lambda t: -t["rgba_bytes"]),
    }

    if args.json:
        print(json.dumps(result, indent=2))
    elif args.markdown:
        print(to_markdown(result))
    else:
        print("Textures: %d%s" % (
            result["count"],
            " (scene-referenced)" if result["scene_filtered"] else ""))
        print("  s3tc present (DXT1/DXT5):      %8.1f MB  %s" % (
            result["s3tc_present"]["total_mb"],
            "OK" if result["s3tc_present"]["under_budget"] else "OVER budget"))
        print("  s3tc absent (RGBA32 fallback): %8.1f MB  %s" % (
            result["s3tc_absent"]["total_mb"],
            "OK" if result["s3tc_absent"]["under_budget"] else "OVER budget"))
        print("  readable CPU-side copies:      %8.1f MB (%d textures)" % (
            result["readable_cpu_mb"], result["readable_count"]))
        print("  cubemap/skybox faces (N13):    %d" % result["n13_scope_count"])
        print("  dims-assumed textures:         %d" % result["assumed_dims_count"])
        print("  budget: %.1f MB" % result["budget_mb"])

    return 0


if __name__ == "__main__":
    sys.exit(main())
