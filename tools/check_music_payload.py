#!/usr/bin/env python3
"""Verify the streamed music payload (card N14).

Checks, using only the standard library:
  * every expected Spec/Scene track exists as an .ogg in StreamingAssets/music
  * every .ogg really is an Ogg container ('OggS' magic) carrying Vorbis at 44100 Hz
  * the total payload is within the 25 MB budget
  * every asset file has a sibling .meta and all GUIDs are unique
  * no music clips remain under Assets/Resources/Music
  * no .bak files were left in Assets/ or ProjectSettings/

Exit status 0 = all good, 1 = at least one failure.
"""

import os
import re
import struct
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MUSIC_DIR = os.path.join(REPO, "Assets", "StreamingAssets", "music")
BUDGET_BYTES = 25 * 1024 * 1024

SPECS = ["Viz", "Gam", "Neu"]
# Credits music only exists for the Viz spec, matching the original Resources tree.
SCENES = {
    "Viz": ["Ice", "Voyager", "Nothing", "Extreme", "Earth", "Credits"],
    "Gam": ["Ice", "Voyager", "Nothing", "Extreme", "Earth"],
    "Neu": ["Ice", "Voyager", "Nothing", "Extreme", "Earth"],
}

failures = []
notes = []


def fail(msg):
    failures.append(msg)
    print("FAIL: " + msg)


def ok(msg):
    print("ok:   " + msg)


def read_vorbis_id_header(path):
    """Return (channels, sample_rate) from the first Vorbis identification header.

    Parses just enough of the Ogg page framing to find the packet; no deps.
    """
    with open(path, "rb") as f:
        head = f.read(8192)

    if head[:4] != b"OggS":
        return None, None, "missing OggS magic"

    # Ogg page header: 'OggS', version, flags, granule(8), serial(4), seq(4),
    # crc(4), n_segments(1), segment table(n_segments)
    if len(head) < 27:
        return None, None, "truncated first page"
    n_segs = head[26]
    body = 27 + n_segs
    packet = head[body:]

    # Vorbis identification packet: 0x01 'vorbis', version(4), channels(1),
    # rate(4), ...
    if packet[:7] != b"\x01vorbis":
        return None, None, "first packet is not a Vorbis identification header"
    if len(packet) < 16:
        return None, None, "truncated Vorbis identification header"
    channels = packet[11]
    rate = struct.unpack("<I", packet[12:16])[0]
    return channels, rate, None


def main():
    if not os.path.isdir(MUSIC_DIR):
        fail("missing directory Assets/StreamingAssets/music")
        return 1

    total = 0
    found = []

    for spec in SPECS:
        for scene in SCENES[spec]:
            rel = "%s/%s.ogg" % (spec, scene)
            path = os.path.join(MUSIC_DIR, spec, scene + ".ogg")
            if not os.path.isfile(path):
                fail("missing track %s" % rel)
                continue
            size = os.path.getsize(path)
            total += size
            found.append(rel)

            channels, rate, err = read_vorbis_id_header(path)
            if err:
                fail("%s: %s" % (rel, err))
                continue
            if rate != 44100:
                fail("%s: sample rate %d, expected 44100" % (rel, rate))
            if channels < 1:
                fail("%s: bogus channel count %d" % (rel, channels))
            if size == 0:
                fail("%s: zero bytes" % rel)

    expected = sum(len(v) for v in SCENES.values())
    if len(found) == expected:
        ok("all %d expected tracks present" % expected)

    # Nothing unexpected in the folder either.
    for root, _dirs, files in os.walk(MUSIC_DIR):
        for fn in files:
            if fn.endswith(".meta"):
                continue
            rel = os.path.relpath(os.path.join(root, fn), MUSIC_DIR).replace(os.sep, "/")
            if not fn.endswith(".ogg"):
                fail("unexpected non-ogg file in music payload: %s" % rel)
            elif rel not in found:
                fail("unexpected extra track: %s" % rel)

    mb = total / 1048576.0
    if total <= BUDGET_BYTES:
        ok("payload %.1f MB within the 25 MB budget" % mb)
    else:
        fail("payload %.1f MB exceeds the 25 MB budget" % mb)

    # Every asset needs a .meta, and GUIDs must be unique project-wide.
    for root, _dirs, files in os.walk(MUSIC_DIR):
        for fn in files:
            if fn.endswith(".meta"):
                continue
            if not os.path.isfile(os.path.join(root, fn) + ".meta"):
                fail("missing .meta for %s" % fn)

    for rel in ["Assets/Plugins/WebGL/html5audio.jslib",
                "Assets/Scripts/Html5Music.cs",
                "Assets/StreamingAssets/music"]:
        p = os.path.join(REPO, rel)
        if not os.path.exists(p):
            fail("missing %s" % rel)
        elif not os.path.exists(p + ".meta"):
            fail("missing .meta for %s" % rel)
    ok("plugin, wrapper and payload metas present")

    guids = {}
    dups = 0
    assets = os.path.join(REPO, "Assets")
    for root, _dirs, files in os.walk(assets):
        for fn in files:
            if not fn.endswith(".meta"):
                continue
            p = os.path.join(root, fn)
            try:
                with open(p, "r", errors="ignore") as f:
                    m = re.search(r"^guid: ([0-9a-f]{32})", f.read(), re.M)
            except OSError:
                continue
            if not m:
                continue
            g = m.group(1)
            if g in guids:
                fail("duplicate GUID %s in %s and %s" % (g, guids[g], p))
                dups += 1
            else:
                guids[g] = p
    if not dups:
        ok("all %d asset GUIDs unique" % len(guids))

    # Originals must be gone from Resources (that is the whole point of the card).
    old = os.path.join(REPO, "Assets", "Resources", "Music")
    if os.path.exists(old):
        fail("Assets/Resources/Music still exists; music would still be bundled")
    else:
        ok("Assets/Resources/Music removed from the build")

    # SFX and Voiceover must be untouched.
    for keep in ["SFX", "Voiceover"]:
        if os.path.isdir(os.path.join(REPO, "Assets", "Resources", keep)):
            ok("Assets/Resources/%s untouched" % keep)
        else:
            fail("Assets/Resources/%s is missing" % keep)

    # Guard: no .bak files committed into the project.
    bak = []
    for sub in ["Assets", "ProjectSettings"]:
        for root, _dirs, files in os.walk(os.path.join(REPO, sub)):
            for fn in files:
                if fn.endswith(".bak"):
                    bak.append(os.path.relpath(os.path.join(root, fn), REPO))
    if bak:
        for b in bak:
            fail("stray .bak file: %s" % b)
    else:
        ok("no .bak files in Assets/ or ProjectSettings/")

    print()
    if failures:
        print("%d check(s) FAILED" % len(failures))
        return 1
    print("all checks passed (%.1f MB streamed music payload)" % mb)
    return 0


if __name__ == "__main__":
    sys.exit(main())
