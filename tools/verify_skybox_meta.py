#!/usr/bin/env python3
"""Verify the N13 WebGL importer overrides persisted into every skybox .meta.

Unity-free: reads the serialized YAML on disk, so it proves the settings
survived the reimport rather than only that an editor API call was made.

Checked, per texture under Assets/Resources/Skybox:

  * a ``buildTarget: WebGL`` platformSettings block exists with
    ``overridden: 1``
  * ``maxTextureSize: 2048``       (owner decision C5 -> 2048 per face)
  * ``compressionQuality: 50``
  * ``textureCompression: 1``      (Compressed, not CompressedLQ)
  * the format state selected by ``--expect-format`` (default ``crunched``):
      - ``crunched``: ``textureFormat: 28`` + ``crunchedCompression: 1``
        (RGB Crunched DXT1, the card's default)
      - ``dxt1``: ``textureFormat: 10`` + ``crunchedCompression: 0``
        (plain DXT1, the kill-criterion fallback; use it only after flipping
        ``SkyboxImportSettings.UseCrunch`` to false and re-running Apply)
    Every texture must be in the SAME state - a mixed tree is a failure.
  * top level ``enableMipMap: 1``, ``filterMode: 2`` (trilinear) and
    ``alphaUsage: 0`` (skybox needs no alpha; keeps Automatic off DXT5)
  * no ``.bak`` files anywhere under the scanned root

Exit 0 when every texture passes, 1 otherwise.

    python3 tools/verify_skybox_meta.py                       # terse
    python3 tools/verify_skybox_meta.py --verbose             # one line per texture
    python3 tools/verify_skybox_meta.py --expect-format dxt1  # kill-criterion state
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

SKYBOX_ROOT = Path("Assets/Resources/Skybox")
IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".psd"}

MAX_FACE_SIZE = 2048
CRUNCH_QUALITY = 50
DXT1 = 10  # TextureImporterFormat.DXT1
DXT1_CRUNCHED = 28  # TextureImporterFormat.DXT1Crunched
COMPRESSION_COMPRESSED = 1  # TextureImporterCompression.Compressed
FILTER_TRILINEAR = 2  # FilterMode.Trilinear
ALPHA_NONE = 0  # TextureImporterAlphaSource.None

EXPECTED_PLATFORM = {
    "overridden": 1,
    "maxTextureSize": MAX_FACE_SIZE,
    "compressionQuality": CRUNCH_QUALITY,
    "textureCompression": COMPRESSION_COMPRESSED,
}
# --expect-format name -> (textureFormat, crunchedCompression)
FORMAT_STATES = {
    "crunched": (DXT1_CRUNCHED, 1),  # RGB Crunched DXT1 (default)
    "dxt1": (DXT1, 0),               # plain DXT1 (kill-criterion fallback)
}
FORMAT_NAMES = {v: k for k, v in FORMAT_STATES.items()}
EXPECTED_TOP_LEVEL = {
    "enableMipMap": 1,
    "filterMode": FILTER_TRILINEAR,
    "alphaUsage": ALPHA_NONE,
}

_KV = re.compile(r"^(\s*)([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(-?\d+)\s*$")


def find_textures(root: Path) -> list[Path]:
    if not root.is_dir():
        return []
    return sorted(
        p for p in root.rglob("*")
        if p.is_file() and p.suffix.lower() in IMAGE_EXTS
    )


def parse_meta(meta_path: Path) -> tuple[dict[str, int], dict[str, int] | None]:
    """Return (top-level int scalars, the WebGL platformSettings block)."""
    top: dict[str, int] = {}
    webgl: dict[str, int] | None = None

    current: dict[str, int] | None = None
    current_target: str | None = None
    in_platform_settings = False

    for raw in meta_path.read_text(encoding="utf-8", errors="replace").splitlines():
        stripped = raw.strip()

        if stripped == "platformSettings:":
            in_platform_settings = True
            continue

        if in_platform_settings:
            # A new list item starts a fresh platform block.
            if stripped.startswith("- "):
                current = {}
                current_target = None
                stripped = stripped[2:].strip()
                raw = raw.replace("- ", "  ", 1)
            # Dedent back to a sibling key of platformSettings ends the list.
            elif raw and not raw.startswith("  ") and ":" in raw:
                in_platform_settings = False
                current = None

        if stripped.startswith("buildTarget:"):
            current_target = stripped.split(":", 1)[1].strip()
            if current_target == "WebGL" and current is not None:
                webgl = current
            continue

        m = _KV.match(raw)
        if not m:
            continue
        _, key, value = m.groups()
        if in_platform_settings and current is not None:
            current[key] = int(value)
            if current_target == "WebGL":
                webgl = current
        else:
            # Keep the first (outermost) occurrence; platform blocks repeat
            # maxTextureSize/compressionQuality and must not clobber it.
            top.setdefault(key, int(value))

    return top, webgl


def check(texture: Path, expect_format: str) -> tuple[list[str], str | None]:
    """Return (problems, observed format state name or None)."""
    meta = texture.with_suffix(texture.suffix + ".meta")
    if not meta.is_file():
        return [f"{texture}: missing .meta"], None

    top, webgl = parse_meta(meta)
    problems: list[str] = []

    if webgl is None:
        return [f"{texture}: no 'buildTarget: WebGL' platformSettings block"], None

    for key, want in EXPECTED_PLATFORM.items():
        got = webgl.get(key)
        if got != want:
            problems.append(f"{texture}: WebGL {key}={got!r} expected {want}")

    fmt_key = (webgl.get("textureFormat"), webgl.get("crunchedCompression"))
    state = FORMAT_NAMES.get(fmt_key)
    want_fmt, want_crunch = FORMAT_STATES[expect_format]
    if fmt_key != (want_fmt, want_crunch):
        problems.append(
            f"{texture}: WebGL textureFormat={fmt_key[0]!r} "
            f"crunchedCompression={fmt_key[1]!r} expected "
            f"textureFormat={want_fmt} crunchedCompression={want_crunch} "
            f"({expect_format})"
        )

    for key, want in EXPECTED_TOP_LEVEL.items():
        got = top.get(key)
        if got != want:
            problems.append(f"{texture}: {key}={got!r} expected {want}")

    return problems, state


def find_bak_files(root: Path) -> list[Path]:
    if not root.is_dir():
        return []
    return sorted(p for p in root.rglob("*.bak") if p.is_file())


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=str(SKYBOX_ROOT),
                    help="skybox directory to scan (default %(default)s)")
    ap.add_argument("--verbose", "-v", action="store_true",
                    help="print one line per texture")
    ap.add_argument("--expect-format", choices=sorted(FORMAT_STATES),
                    default="crunched",
                    help="format state every texture must be in "
                         "(default %(default)s; 'dxt1' is the kill-criterion fallback)")
    args = ap.parse_args(argv)

    root = Path(args.root)
    textures = find_textures(root)
    if not textures:
        print(f"FAIL no textures found under {root}", file=sys.stderr)
        return 1

    all_problems: list[str] = []
    counts = {name: 0 for name in FORMAT_STATES}
    for tex in textures:
        problems, state = check(tex, args.expect_format)
        all_problems.extend(problems)
        if state is not None:
            counts[state] += 1
        if args.verbose:
            print(("ok   " if not problems else "FAIL ")
                  + f"{tex}  [{state or '?'}]")

    for bak in find_bak_files(root):
        all_problems.append(f"{bak}: .bak file inside the asset tree (guard)")

    for p in all_problems:
        print("FAIL " + p, file=sys.stderr)

    summary = (f"textures={len(textures)} expect={args.expect_format} "
               f"maxTextureSize={MAX_FACE_SIZE} quality={CRUNCH_QUALITY} "
               f"crunched={counts['crunched']} dxt1={counts['dxt1']}")
    if all_problems:
        print(f"SKYBOX_META_VERIFY result=Failed failures={len(all_problems)} {summary}")
        return 1

    print(f"SKYBOX_META_VERIFY result=Succeeded failures=0 {summary}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
