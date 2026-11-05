#!/usr/bin/env python3
"""Verify the N13 WebGL importer overrides persisted into every skybox .meta.

Unity-free: reads the serialized YAML on disk, so it proves the settings
survived the reimport rather than only that an editor API call was made.

Checked, per texture under Assets/Resources/Skybox:

  * a ``buildTarget: WebGL`` platformSettings block exists with
    ``overridden: 1``
  * ``maxTextureSize: 2048``       (owner decision C5 -> 2048 per face)
  * ``textureFormat: 28``          (TextureImporterFormat.DXT1Crunched)
  * ``crunchedCompression: 1``
  * ``compressionQuality: 50``
  * top level ``enableMipMap: 1`` and ``filterMode: 2`` (trilinear)

Exit 0 when every texture passes, 1 otherwise.

    python3 tools/verify_skybox_meta.py            # terse
    python3 tools/verify_skybox_meta.py --verbose  # one line per texture
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
DXT1_CRUNCHED = 28  # TextureImporterFormat.DXT1Crunched
FILTER_TRILINEAR = 2  # FilterMode.Trilinear

EXPECTED_PLATFORM = {
    "overridden": 1,
    "maxTextureSize": MAX_FACE_SIZE,
    "textureFormat": DXT1_CRUNCHED,
    "crunchedCompression": 1,
    "compressionQuality": CRUNCH_QUALITY,
}
EXPECTED_TOP_LEVEL = {
    "enableMipMap": 1,
    "filterMode": FILTER_TRILINEAR,
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


def check(texture: Path) -> list[str]:
    meta = texture.with_suffix(texture.suffix + ".meta")
    if not meta.is_file():
        return [f"{texture}: missing .meta"]

    top, webgl = parse_meta(meta)
    problems: list[str] = []

    if webgl is None:
        return [f"{texture}: no 'buildTarget: WebGL' platformSettings block"]

    for key, want in EXPECTED_PLATFORM.items():
        got = webgl.get(key)
        if got != want:
            problems.append(f"{texture}: WebGL {key}={got!r} expected {want}")

    for key, want in EXPECTED_TOP_LEVEL.items():
        got = top.get(key)
        if got != want:
            problems.append(f"{texture}: {key}={got!r} expected {want}")

    return problems


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=str(SKYBOX_ROOT),
                    help="skybox directory to scan (default %(default)s)")
    ap.add_argument("--verbose", "-v", action="store_true",
                    help="print one line per texture")
    args = ap.parse_args(argv)

    root = Path(args.root)
    textures = find_textures(root)
    if not textures:
        print(f"FAIL no textures found under {root}", file=sys.stderr)
        return 1

    all_problems: list[str] = []
    for tex in textures:
        problems = check(tex)
        all_problems.extend(problems)
        if args.verbose:
            print(("ok   " if not problems else "FAIL ") + str(tex))

    for p in all_problems:
        print("FAIL " + p, file=sys.stderr)

    if all_problems:
        print(f"SKYBOX_META_VERIFY result=Failed textures={len(textures)} "
              f"failures={len(all_problems)}")
        return 1

    print(f"SKYBOX_META_VERIFY result=Succeeded textures={len(textures)} "
          f"failures=0 maxTextureSize={MAX_FACE_SIZE} "
          f"format=DXT1Crunched quality={CRUNCH_QUALITY}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
