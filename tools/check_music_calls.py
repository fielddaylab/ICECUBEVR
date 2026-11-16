#!/usr/bin/env python3
"""Static guard for card N15: music must go through Html5Music, not an AudioSource.

Checks Main.cs (and any other gameplay script) for the patterns the rewire removed:

  1. no music AudioSource field/variable survives (music_audiosource & friends),
  2. no Resources.Load of a music clip (that is what put 61 MB in webgl.data),
  3. Html5Music.Play is only reached through Main.PlayMusic / Html5Music.Unlock,
     i.e. no call site bypasses the single gated entry point,
  4. Html5Music.Unlock() is called exactly once, and from the BEGIN confirm block,
  5. the autoplay gate in Html5Music.cs still refuses to touch JS before Unlock.

Exit 0 = clean, 1 = violation. No Unity needed, so it can run in CI.
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAIN = os.path.join(ROOT, "Assets", "Scripts", "Main.cs")
WRAPPER = os.path.join(ROOT, "Assets", "Scripts", "Html5Music.cs")
JSLIB = os.path.join(ROOT, "Assets", "Plugins", "WebGL", "html5audio.jslib")

# Fields/locals that used to hold the music channel. Anything named like this
# reappearing in Main.cs means a call site went back to an AudioSource.
MUSIC_SOURCE_NAMES = ["music_audiosource", "musics", "bgm_audiosource", "music_source"]

failures = []
notes = []


def read(path):
    if not os.path.exists(path):
        failures.append("missing file: " + os.path.relpath(path, ROOT))
        return ""
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def code_lines(text):
    """(1-based lineno, line) for lines that are not pure // comments."""
    out = []
    for i, line in enumerate(text.splitlines(), 1):
        if line.strip().startswith("//"):
            continue
        out.append((i, line))
    return out


main_src = read(MAIN)
wrapper_src = read(WRAPPER)
jslib_src = read(JSLIB)

# ---- 1. no music AudioSource left in Main.cs ---------------------------------
for lineno, line in code_lines(main_src):
    for name in MUSIC_SOURCE_NAMES:
        if re.search(r"\b" + re.escape(name) + r"\b", line):
            failures.append(
                "Main.cs:%d still references music AudioSource '%s': %s"
                % (lineno, name, line.strip())
            )

# ---- 2. no Resources.Load of music ------------------------------------------
for lineno, line in code_lines(main_src):
    if "Resources.Load" in line and re.search(r"music|Music|bgm|BGM", line):
        failures.append(
            "Main.cs:%d Resources.Load of a music clip (belongs in StreamingAssets): %s"
            % (lineno, line.strip())
        )

# ---- 3. Html5Music.Play only inside the gated entry point -------------------
# PlayMusic() is the single wrapper; everything else must call PlayMusic().
play_lines = [
    (n, l) for n, l in code_lines(main_src) if "Html5Music.Play(" in l
]
if len(play_lines) != 1:
    failures.append(
        "expected exactly 1 Html5Music.Play call in Main.cs (inside PlayMusic), found %d"
        % len(play_lines)
    )
else:
    # the call must sit inside the body of PlayMusic
    m = re.search(r"void\s+PlayMusic\s*\(", main_src)
    if not m:
        failures.append("Main.cs has no PlayMusic() entry point")
    else:
        start = main_src.count("\n", 0, m.start()) + 1
        lineno = play_lines[0][0]
        if not (start < lineno < start + 30):
            failures.append(
                "Main.cs:%d Html5Music.Play outside PlayMusic() (declared at line %d)"
                % (lineno, start)
            )
        else:
            notes.append("Html5Music.Play reached only via PlayMusic() at line %d" % start)

# ---- 4. Unlock called once, from the BEGIN confirm --------------------------
unlock_lines = [
    (n, l) for n, l in code_lines(main_src) if "Html5Music.Unlock()" in l
]
if len(unlock_lines) != 1:
    failures.append(
        "expected exactly 1 Html5Music.Unlock() call in Main.cs (the BEGIN confirm), found %d"
        % len(unlock_lines)
    )
else:
    lineno = unlock_lines[0][0]
    # The BEGIN confirm is the advance_trigger.just_triggered block that hides
    # begin_text; require those markers within a few lines above the Unlock.
    window = "\n".join(main_src.splitlines()[max(0, lineno - 12):lineno])
    if "just_triggered" not in window or "begin_text.SetActive(false)" not in window:
        failures.append(
            "Main.cs:%d Html5Music.Unlock() is not inside the BEGIN confirm block "
            "(expected advance_trigger.just_triggered + begin_text.SetActive(false) above it)"
            % lineno
        )
    else:
        notes.append("Html5Music.Unlock() gated on the BEGIN confirm at line %d" % lineno)

# ---- 5. the gate itself ------------------------------------------------------
if wrapper_src:
    for needed in ["unlocked", "hasPending", "public static void Unlock()",
                   "public static bool IsUnlocked"]:
        if needed not in wrapper_src:
            failures.append("Html5Music.cs is missing the autoplay gate piece: " + needed)
    # Play() must bail out before Init()/the DllImports when locked.
    pm = re.search(
        r"public static void Play\(string name, bool loop, float volume, float startTime\)"
        r"(.*?)\n  \}", wrapper_src, re.S)
    if not pm:
        failures.append("Html5Music.cs has no Play(name, loop, volume, startTime) overload")
    else:
        body = pm.group(1)
        gate = body.find("if (!unlocked)")
        call = body.find("Music_Play(")
        if gate < 0:
            failures.append("Html5Music.Play does not check the autoplay gate")
        elif 0 <= call < gate:
            failures.append("Html5Music.Play calls into JS before checking the gate")
        else:
            notes.append("Html5Music.Play checks !unlocked before any JS call")

if jslib_src and "NotAllowedError" not in jslib_src:
    failures.append("html5audio.jslib lost its autoplay-blocked retry path")

# ---- report ------------------------------------------------------------------
for n in notes:
    print("ok: " + n)

if failures:
    print()
    for f in failures:
        print("FAIL: " + f)
    print("\n%d violation(s)" % len(failures))
    sys.exit(1)

print("\nmusic call sites clean: no music AudioSource, no music Resources.Load, "
      "single gated Html5Music.Play, Unlock on the BEGIN confirm")
sys.exit(0)
