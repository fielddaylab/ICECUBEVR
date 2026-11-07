#!/usr/bin/env python3
"""acceptance_stats.py - N18 final-acceptance analyzer for the icecube WebGL build.

Turns the N1 raw artifacts (PERFPROBE env/frame log lines, sharpness.py JSON
output, the .data file, time-to-first-frame, and the hand-filled gaze-marker
list) into pass/fail verdicts against the N18 thresholds. Every miss names
the owning card so the failure can be filed directly as a new card.

This tool only *measures and reports*. It never tunes the build.

Subcommands
  env        <perfprobe.log>                        hardware guard + 1:1 buffer
  frames     <perfprobe.log> --phase P --window N   60 s frame stats per phase
  sharpness  <sharpness_json> [--role R]            text sharpness thresholds
  payload    <path-to-.data>                        data-file size cap
  ttff       <ms>                                   time-to-first-frame (INFO)
  gaze       <gaze.json>                            marker visibility check
  report     --env E --frames intro.json warp.json spectrum.json
             --sharpness caption.json reticle.json marker.json
             --payload P --ttff T --gaze G --out tools/ACCEPTANCE.md
             builds the final report; exit 0 all-pass, 1 any-fail, 2 parse err

PERFPROBE log format (emitted by Assets/Plugins/WebGL/perfprobe.jslib, N1):

  PERFPROBE env {"renderer":..., "vendor":..., "tier":"HARDWARE"|"SOFTWARE",
                 "devicePixelRatio":..., "canvas_width":..., "canvas_height":...,
                 "canvas_client_width":..., "canvas_client_height":...,
                 "buffer_scale_x":..., "one_to_one":..., "webgl_version":...}

  PERFPROBE frame {"window_s":10, "n":<deltas in window>, "p50_ms":...,
                   "p95_ms":..., "mean_ms":..., "max_ms":...,
                   "jitter_pct":..., "skipped":..., "tier":...}

The N1 jslib dumps a *sorted-window aggregate* every 10 s, not raw per-frame
deltas. Two capture modes are therefore supported:

  EXACT  - if the log also carries raw per-frame deltas (a "deltas" or
           "frame_ms" array inside a frame record, or `PERFPROBE delta <ms>`
           lines), the stats are computed from the raw samples. This is the
           only mode that can measure "<= 2 frames > 66 ms per minute"
           exactly, so it is the preferred capture (see tools/ACCEPTANCE.md
           step 2 for the DevTools snippet that enables it).
  AGGREGATE - 10 s dump records only. p50/p95/jitter are combined across
           dumps by frame-count weighting, and the long-frame count is a
           LOWER BOUND (one long frame per dump whose max_ms > 66). Because
           the bound can only under-count, an AGGREGATE window that already
           fails long_per_minute is a true FAIL, but an AGGREGATE window that
           passes long_per_minute is reported as PASS(bounded) and flagged in
           `exactness`; the report marks the whole run NOT-SIGN-OFF-GRADE
           unless every phase was captured EXACT.

Phase windows are bounded by operator marker lines containing a phase name
(e.g. "PHASE intro" / "phase: warp"), typed into the console during the run.
"""
import argparse
import json
import math
import re
import statistics
import sys
from pathlib import Path

# ---------------------------------------------------------------- thresholds
# From the card N18 acceptance line. Each entry: (key, op, limit).
FRAME_THRESHOLDS = [
    ("p50_ms", "<=", 33.4),
    ("p95_ms", "<=", 35.0),
    ("long_per_minute", "<=", 2.0),
    ("jitter_pct", "<", 5.0),
]
SHARPNESS_THRESHOLDS = [
    ("median_transition_px", "<=", 1.5),
    ("p90_transition_px", "<=", 2.0),
    ("cap_height_px", ">=", 22.0),
    ("contrast_ratio", ">=", 7.0),
]
PAYLOAD_LIMIT_BYTES = 30 * 1024 * 1024  # 30 MiB

LONG_FRAME_MS = 66.0
MIN_WINDOW_S = 55.0
DUMP_WINDOW_S = 10.0  # N1 jslib DUMP_INTERVAL_MS

# Owning cards for report failures (the kill criterion must name the owner).
OWNERS = {
    "env.software_renderer": "N1 / N16",
    "env.buffer_1to1": "N6 / N16",
    "frames": "N6",
    "sharpness.caption": "N4",
    "sharpness.reticle": "N9",
    "sharpness.marker": "N10",
    "sharpness.generic": "N4 / N9 / N10",
    "payload": "N13 / N14",
    "gaze": "N10",
    "capture": "N18 (capture procedure)",
}

# Guard from the card: "the PERFPROBE env line must not contain SwiftShader
# or llvmpipe". Softpipe / Mesa offscreen / generic "software" are the same
# class of failure, so they are caught too.
SOFT_RENDERER_RE = re.compile(
    r"swiftshader|llvmpipe|softpipe|mesa\s+offscreen|software\s*rasteriz|"
    r"\bsoftware\b", re.I)

PHASES = ("intro", "warp", "spectrum")
SHARP_ROLES = ("caption", "reticle", "marker")

EXACT = "EXACT"
AGGREGATE = "AGGREGATE"


# ---------------------------------------------------------------- helpers
def fail(msg):
    """Parse error -> exit code 2 (distinct from a threshold FAIL = 1)."""
    sys.stderr.write("parse error: " + msg + "\n")
    sys.exit(2)


def _nearest_rank(sorted_vals, q):
    """Nearest-rank percentile: smallest value whose rank >= q*n (1-based)."""
    n = len(sorted_vals)
    if n == 0:
        return None
    k = int(math.ceil(q * n)) - 1
    k = max(0, min(n - 1, k))
    return sorted_vals[k]


def _num(val, default=None):
    if val is None:
        return default
    try:
        f = float(val)
    except (TypeError, ValueError):
        return default
    if math.isnan(f) or math.isinf(f):
        return default
    return f


def _check(name, value, op, limit, owner, reason=None):
    if value is None:
        ok = False
        reason = reason or "metric missing from capture"
    elif op == "<=":
        ok = value <= limit
    elif op == ">=":
        ok = value >= limit
    elif op == "<":
        ok = value < limit
    elif op == ">":
        ok = value > limit
    else:
        raise ValueError("bad op " + op)
    return {
        "name": name,
        "value": round(value, 3) if isinstance(value, float) else value,
        "op": op,
        "limit": limit,
        "pass": bool(ok),
        "owner": owner,
        "reason": reason,
    }


def _verdict(ok, owner=None, note=None):
    if ok:
        return "PASS" + (" ({})".format(note) if note else "")
    return "**FAIL**" + (" (owner: {})".format(owner) if owner else "")


# ------------------------------------------------------------- perfprobe log
PHASE_MARK_RE = re.compile(
    r"(?i)\bphase\s*[:=]?\s*[\"']?(intro|warp|spectrum)\b")
ENV_RE = re.compile(r"PERFPROBE\s+env\s+(\{.*\})")
FRAME_RE = re.compile(r"PERFPROBE\s+frame\s+(\{.*\})")
DELTA_RE = re.compile(r"PERFPROBE\s+delta\s+([0-9]+(?:\.[0-9]+)?)")


def parse_perfprobe_lines(text):
    """Parse an N1 PERFPROBE console log.

    Returns (envs, frames, deltas, marks) where
      envs   : [(line_index, dict)]   PERFPROBE env records
      frames : [(line_index, dict)]   PERFPROBE frame 10 s dump records
      deltas : [(line_index, ms)]     raw per-frame deltas, if the capture
                                      emitted them (PERFPROBE delta lines or
                                      a deltas[]/frame_ms[] array in a dump)
      marks  : [(line_index, phase)]  operator phase marker lines
    """
    envs, frames, deltas, marks = [], [], [], []
    for i, line in enumerate(text.splitlines()):
        s = line.strip()
        if not s:
            continue

        m = ENV_RE.search(s)
        if m:
            try:
                envs.append((i, json.loads(m.group(1))))
            except json.JSONDecodeError:
                fail("bad JSON in PERFPROBE env line " + str(i + 1) + ": " + s[:140])
            continue

        m = FRAME_RE.search(s)
        if m:
            try:
                rec = json.loads(m.group(1))
            except json.JSONDecodeError:
                fail("bad JSON in PERFPROBE frame line " + str(i + 1) + ": " + s[:140])
            frames.append((i, rec))
            raw = rec.get("deltas")
            if raw is None:
                raw = rec.get("frame_ms")
            if isinstance(raw, list):
                for v in raw:
                    f = _num(v)
                    if f is not None:
                        deltas.append((i, f))
            continue

        m = DELTA_RE.search(s)
        if m:
            f = _num(m.group(1))
            if f is not None:
                deltas.append((i, f))
            continue

        # Operator phase marker. Checked last so that a frame/env record that
        # happens to mention a phase name is never mistaken for a marker.
        m = PHASE_MARK_RE.search(s)
        if m and "PERFPROBE" not in s.upper():
            marks.append((i, m.group(1).lower()))

    return envs, frames, deltas, marks


def pick_env(envs):
    if not envs:
        fail("no 'PERFPROBE env {...}' line in log (is PerfProbe.cs in the build?)")
    # Last env line wins: the canvas may be resized after Unity boots.
    for _, env in reversed(envs):
        if "error" not in env:
            return env
    fail("every PERFPROBE env line reports an error: " + json.dumps(envs[-1][1]))


def phase_span(marks, phase, n_lines):
    """Line range [start, end) of a phase, bounded by operator markers."""
    hits = [i for i, p in marks if p == phase]
    if not hits:
        return None
    start = hits[0] + 1
    end = n_lines
    for i, p in marks:
        if i > hits[0] and p != phase:
            end = i
            break
    return (start, end)


# ------------------------------------------------------------------- env cmd
def env_report(env):
    checks = []
    renderer = env.get("renderer") or ""
    tier = (env.get("tier") or "").upper()

    soft = bool(SOFT_RENDERER_RE.search(renderer)) or tier == "SOFTWARE"
    unknown = (not renderer) or tier == "UNKNOWN"
    checks.append(_check(
        "hardware_renderer",
        0.0 if soft or unknown else 1.0, ">=", 1.0,
        OWNERS["env.software_renderer"],
        reason=("software rasterizer in GL_RENDERER (kill criterion: hardware "
                "GPU only)" if soft else
                ("GL_RENDERER unknown - paste the chrome://gpu string into the "
                 "env line before accepting" if unknown else
                 "hardware renderer confirmed"))))
    checks[-1]["value"] = renderer or None

    dpr = _num(env.get("devicePixelRatio"), None)
    bw = _num(env.get("canvas_width"))
    bh = _num(env.get("canvas_height"))
    cw = _num(env.get("canvas_client_width"))
    chh = _num(env.get("canvas_client_height"))

    one_to_one = None
    reason = None
    if None not in (bw, bh, cw, chh, dpr) and cw > 0 and chh > 0:
        want_w, want_h = round(cw * dpr), round(chh * dpr)
        # Allow 1 px of rounding slop per axis (CSS sizes are fractional).
        one_to_one = (abs(bw - want_w) <= 1) and (abs(bh - want_h) <= 1)
        reason = ("1:1 buffer confirmed" if one_to_one else
                  "drawing buffer {}x{} != CSS*DPR {}x{} - the browser "
                  "rescales, which blurs text".format(
                      int(bw), int(bh), int(want_w), int(want_h)))
    elif env.get("one_to_one") is not None:
        one_to_one = bool(env.get("one_to_one"))
        reason = "from jslib one_to_one flag (canvas sizes incomplete)"
    else:
        reason = "canvas sizes and DPR missing from env line"

    checks.append(_check(
        "buffer_1to1",
        1.0 if one_to_one else 0.0, ">=", 1.0,
        OWNERS["env.buffer_1to1"], reason=reason))
    checks[-1]["value"] = "buffer={}x{} css={}x{} dpr={}".format(
        _fmt(bw), _fmt(bh), _fmt(cw), _fmt(chh), _fmt(dpr))

    all_pass = all(c["pass"] for c in checks)
    return {
        "gl_renderer": renderer or None,
        "gl_vendor": env.get("vendor"),
        "tier": tier or None,
        "debug_renderer_info": env.get("debug_renderer_info"),
        "devicePixelRatio": dpr,
        "canvas_buffer": [_fmt(bw), _fmt(bh)],
        "canvas_css": [_fmt(cw), _fmt(chh)],
        "window_inner": [env.get("window_inner_width"),
                         env.get("window_inner_height")],
        "webgl_version": env.get("webgl_version"),
        "s3tc": env.get("s3tc"),
        "one_to_one": one_to_one,
        "user_agent": env.get("userAgent"),
        "checks": checks,
        "pass": all_pass,
    }


def _fmt(v):
    if v is None:
        return None
    if isinstance(v, float) and v == int(v):
        return int(v)
    return v


def cmd_env(args):
    envs, _, _, _ = parse_perfprobe_lines(_read(args.log))
    out = env_report(pick_env(envs))
    print(json.dumps(out, indent=2))
    return 0 if out["pass"] else 1


def _read(path):
    try:
        return Path(path).read_text(errors="replace")
    except OSError as e:
        fail("cannot read {}: {}".format(path, e))


# ---------------------------------------------------------------- frames cmd
def frames_from_deltas(deltas, window_s):
    """EXACT mode: stats from raw per-frame deltas (ms), first window_s only."""
    kept, total = [], 0.0
    for d in deltas:
        if total >= window_s * 1000.0:
            break
        kept.append(d)
        total += d
    if len(kept) < 2:
        return None
    srt = sorted(kept)
    mean = statistics.fmean(kept)
    stdev = statistics.pstdev(kept) if len(kept) > 1 else 0.0
    span_s = total / 1000.0
    long_frames = sum(1 for d in kept if d > LONG_FRAME_MS)
    return {
        "exactness": EXACT,
        "window_s": round(span_s, 2),
        "n_frames": len(kept),
        "n_dumps": None,
        "p50_ms": _nearest_rank(srt, 0.50),
        "p95_ms": _nearest_rank(srt, 0.95),
        "mean_ms": mean,
        "max_ms": srt[-1],
        "jitter_pct": (stdev / mean * 100.0) if mean > 0 else None,
        "long_frames": long_frames,
        "long_frames_exact": True,
        "long_per_minute": (long_frames * 60.0 / span_s) if span_s > 0 else None,
    }


def frames_from_dumps(recs, window_s):
    """AGGREGATE mode: combine N1 10 s dump records.

    p50/p95/mean/jitter are frame-count-weighted across dumps. The long-frame
    count is a LOWER BOUND: a dump only reports max_ms, so a dump with
    max_ms > 66 proves at least one long frame but cannot prove how many.
    """
    recs = [r for r in recs if "error" not in r and (_num(r.get("n"), 0) or 0) > 0]
    if not recs:
        return None
    n_dumps = len(recs)
    span_s = min(n_dumps * DUMP_WINDOW_S, window_s)
    # Only the dumps inside the window contribute.
    keep = recs[:int(math.ceil(window_s / DUMP_WINDOW_S))]
    n_total = sum(int(_num(r.get("n"), 0) or 0) for r in keep)

    def wmean(key):
        pairs = [(_num(r.get(key)), int(_num(r.get("n"), 0) or 0)) for r in keep]
        pairs = [(v, n) for v, n in pairs if v is not None and n > 0]
        if not pairs:
            return None
        tot = sum(n for _, n in pairs)
        return sum(v * n for v, n in pairs) / tot

    long_frames = sum(1 for r in keep
                      if (_num(r.get("max_ms"), 0.0) or 0.0) > LONG_FRAME_MS)
    maxes = [_num(r.get("max_ms"), 0.0) or 0.0 for r in keep]
    return {
        "exactness": AGGREGATE,
        "window_s": round(span_s, 2),
        "n_frames": n_total,
        "n_dumps": len(keep),
        "p50_ms": wmean("p50_ms"),
        "p95_ms": wmean("p95_ms"),
        "mean_ms": wmean("mean_ms"),
        "max_ms": max(maxes) if maxes else None,
        "jitter_pct": wmean("jitter_pct"),
        "long_frames": long_frames,
        "long_frames_exact": False,
        "long_per_minute": (long_frames * 60.0 / span_s) if span_s > 0 else None,
    }


def cmd_frames(args):
    text = _read(args.log)
    _, frames, deltas, marks = parse_perfprobe_lines(text)
    n_lines = len(text.splitlines())
    phase, window_s = args.phase, float(args.window)

    span = phase_span(marks, phase, n_lines)
    if span is None:
        fail("no phase marker for '{}' in {} - type a line containing "
             "'phase: {}' into the DevTools console at the start of the "
             "phase window (see tools/ACCEPTANCE.md step 2)".format(
                 phase, args.log, phase))
    lo, hi = span

    phase_deltas = [d for i, d in deltas if lo <= i < hi]
    phase_dumps = [r for i, r in frames if lo <= i < hi]

    stats = None
    if len(phase_deltas) >= 2:
        stats = frames_from_deltas(phase_deltas, window_s)
    if stats is None:
        stats = frames_from_dumps(phase_dumps, window_s)
    if stats is None:
        fail("no usable PERFPROBE frame data in phase '{}' (lines {}-{}); "
             "found {} dump records and {} raw deltas".format(
                 phase, lo + 1, hi, len(phase_dumps), len(phase_deltas)))

    owner = OWNERS["frames"]
    checks = []
    for key, op, limit in FRAME_THRESHOLDS:
        c = _check(key, stats.get(key), op, limit, owner)
        if key == "long_per_minute" and not stats["long_frames_exact"]:
            # The count is a lower bound: a pass is not provable from dumps.
            c["bounded"] = True
            if c["pass"]:
                c["reason"] = ("lower bound from 10 s dumps (max_ms only); "
                               "capture raw deltas to prove this threshold")
            else:
                c["reason"] = ("lower bound already exceeds the limit - "
                               "this is a true FAIL")
        checks.append(c)

    checks.append(_check("capture_window_s", stats.get("window_s"),
                         ">=", MIN_WINDOW_S, OWNERS["capture"],
                         reason=None if (stats.get("window_s") or 0) >= MIN_WINDOW_S
                         else "short capture - re-run this phase for a full 60 s"))

    all_pass = all(c["pass"] for c in checks)
    out = {"phase": phase, "requested_window_s": window_s}
    for k in ("exactness", "window_s", "n_frames", "n_dumps", "long_frames",
              "long_frames_exact"):
        out[k] = stats.get(k)
    for k in ("p50_ms", "p95_ms", "mean_ms", "max_ms", "jitter_pct",
              "long_per_minute"):
        v = stats.get(k)
        out[k] = round(v, 3) if isinstance(v, float) else v
    out["checks"] = checks
    out["pass"] = all_pass
    print(json.dumps(out, indent=2))
    return 0 if all_pass else 1


# ------------------------------------------------------------ sharpness cmd
def load_sharpness(path):
    raw = _read(path)
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        fail("not valid JSON (expected tools/sharpness.py output): " + path)
    if not isinstance(data, dict):
        fail("sharpness JSON must be an object: " + path)
    return {
        "median_transition_px": _num(data.get("median_px",
                                              data.get("median_transition_px"))),
        "p90_transition_px": _num(data.get("p90_px",
                                           data.get("p90_transition_px"))),
        "cap_height_px": _num(data.get("cap_height_px")),
        "contrast_ratio": _num(data.get("contrast_ratio")),
        "min_stroke_px": data.get("min_stroke_px"),
        "edges": data.get("edges"),
        "image": data.get("image"),
        "image_size": data.get("image_size"),
        "rect": data.get("rect"),
        "note": data.get("note"),
    }


def cmd_sharpness(args):
    data = load_sharpness(args.sharpness_json)
    role = args.role or "generic"
    owner = OWNERS.get("sharpness." + role, OWNERS["sharpness.generic"])
    # Cap-height and contrast only mean something for text; the reticle and
    # the gaze marker are glyph-free, so those two rows are N/A for them and
    # only the edge-transition thresholds gate.
    keys = SHARPNESS_THRESHOLDS
    if role in ("reticle", "marker"):
        keys = [k for k in SHARPNESS_THRESHOLDS
                if k[0] not in ("cap_height_px",)]
    checks = []
    for key, op, limit in keys:
        c = _check(key, data.get(key), op, limit, owner)
        if data.get(key) is None:
            c["reason"] = ("metric absent from sharpness.py output - check the "
                           "--rect actually covers the element and that the "
                           "screenshot is physical-pixel (no OS scaling)")
        checks.append(c)
    if data.get("edges") in (0, None) and data.get("note"):
        checks.append(_check("edges_found", 0.0, ">=", 1.0, OWNERS["capture"],
                             reason=data.get("note")))
    all_pass = all(c["pass"] for c in checks)
    out = dict(data)
    out["role"] = role
    out["checks"] = checks
    out["pass"] = all_pass
    print(json.dumps(out, indent=2))
    return 0 if all_pass else 1


# ------------------------------------------------------------ payload / ttff
def cmd_payload(args):
    p = Path(args.payload)
    if not p.exists():
        fail("no such file: " + str(p))
    size = p.stat().st_size
    ok = size <= PAYLOAD_LIMIT_BYTES
    check = _check("data_file_bytes", float(size), "<=",
                   float(PAYLOAD_LIMIT_BYTES), OWNERS["payload"],
                   reason=None if ok else
                   "data file over the 30 MiB cap - move payload out or "
                   "re-compress (do not tune during the run; file a card)")
    out = {
        "path": str(p),
        "size_bytes": size,
        "size_mib": round(size / (1024 * 1024), 3),
        "size_mb": round(size / (1000 * 1000), 3),
        "limit_bytes": PAYLOAD_LIMIT_BYTES,
        "limit_mib": 30,
        "checks": [check],
        "pass": ok,
        "owner": None if ok else OWNERS["payload"],
    }
    print(json.dumps(out, indent=2))
    return 0 if ok else 1


def cmd_ttff(args):
    ms = _num(args.ms)
    if ms is None:
        fail("time-to-first-frame must be a number of milliseconds")
    out = {
        "ttff_ms": round(ms, 1),
        "ttff_s": round(ms / 1000.0, 3),
        "verdict": "INFO",
        "pass": True,
        "note": "recorded, not gating: card N18 sets no TTFF threshold",
    }
    print(json.dumps(out, indent=2))
    return 0


# ----------------------------------------------------------------- gaze cmd
def cmd_gaze(args):
    raw = _read(args.gaze)
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        fail("gaze JSON invalid: " + args.gaze)
    if isinstance(data, dict) and isinstance(data.get("prompts"), list):
        data = data["prompts"]
    if not isinstance(data, list) or not data:
        fail("gaze JSON must be a non-empty list of "
             "{prompt_id, marker_visible, screenshot}")
    checks = []
    for entry in data:
        if not isinstance(entry, dict):
            fail("gaze entries must be objects: " + repr(entry)[:80])
        vis = entry.get("marker_visible")
        ok = vis is True
        if vis is None:
            reason = "UNFILLED - the operator must confirm the marker on the " \
                     "physical-pixel screenshot (not a vision-model verdict)"
        elif vis is False:
            reason = "no visible marker / edge arrow for this gaze prompt"
        else:
            reason = None
        checks.append({
            "prompt_id": entry.get("prompt_id"),
            "marker_visible": vis,
            "screenshot": entry.get("screenshot"),
            "pass": ok,
            "owner": None if ok else OWNERS["gaze"],
            "reason": reason,
        })
    all_pass = all(c["pass"] for c in checks)
    out = {
        "prompts": checks,
        "n_prompts": len(checks),
        "n_visible": sum(1 for c in checks if c["pass"]),
        "pass": all_pass,
    }
    print(json.dumps(out, indent=2))
    return 0 if all_pass else 1


# ------------------------------------------------------------- report cmd
def _load_json(path, label):
    try:
        return json.loads(Path(path).read_text())
    except OSError as e:
        fail("{} unreadable: {} ({})".format(label, path, e))
    except json.JSONDecodeError as e:
        fail("{} is not valid JSON: {} ({})".format(label, path, e))


def _all_pass(env, frames, sharp, payload, gaze):
    if not env.get("pass"):
        return False
    if any(not frames[p].get("pass") for p in PHASES):
        return False
    if any(not sharp[r].get("pass") for r in SHARP_ROLES):
        return False
    if not payload.get("pass"):
        return False
    if not gaze.get("pass"):
        return False
    return True


def collect_failures(env, frames, sharp, payload, gaze):
    """Every failing check, with the owning card named (kill criterion)."""
    out = []
    for c in env.get("checks", []):
        if not c.get("pass"):
            out.append(("environment", c.get("name"), c.get("value"),
                        c.get("owner"), c.get("reason")))
    for phase in PHASES:
        for c in frames[phase].get("checks", []):
            if not c.get("pass"):
                out.append(("frames/" + phase, c.get("name"), c.get("value"),
                            c.get("owner"), c.get("reason")))
    for role in SHARP_ROLES:
        for c in sharp[role].get("checks", []):
            if not c.get("pass"):
                out.append(("sharpness/" + role, c.get("name"), c.get("value"),
                            c.get("owner"), c.get("reason")))
    for c in payload.get("checks", []):
        if not c.get("pass"):
            out.append(("payload", c.get("name"), c.get("value"),
                        c.get("owner"), c.get("reason")))
    for c in gaze.get("prompts", []):
        if not c.get("pass"):
            out.append(("gaze", c.get("prompt_id"), c.get("marker_visible"),
                        c.get("owner"), c.get("reason")))
    return out


def render_report(env, frames, sharp, payload, ttff, gaze):
    overall = _all_pass(env, frames, sharp, payload, gaze)
    failures = collect_failures(env, frames, sharp, payload, gaze)
    bounded = [p for p in PHASES
               if frames[p].get("exactness") == AGGREGATE]

    L = []
    add = L.append
    add("# N18 - Final acceptance report (owner's Chromebook, N1 protocol)")
    add("")
    if overall and bounded:
        status = ("PASS (BOUNDED) - long-frame counts in {} came from 10 s "
                  "aggregates, not raw deltas; re-capture those phases with "
                  "raw deltas for a sign-off-grade number"
                  .format(", ".join(bounded)))
    elif overall:
        status = "PASS - all thresholds met"
    else:
        status = ("FAIL - {} threshold miss(es); sign-off is blocked"
                  .format(len(failures)))
    add("STATUS: **" + status + "**")
    add("")
    add("_Generated by `tools/acceptance_stats.py report`. Every number below "
        "was compared against its threshold by the tool - not by impression "
        "and not by a vision model's verdict._")
    add("")

    if failures:
        add("## 0. Kill criterion - blocking misses")
        add("")
        add("| Section | Metric | Measured | Owning card | Note |")
        add("| --- | --- | --- | --- | --- |")
        for section, name, value, owner, reason in failures:
            add("| {} | {} | {} | **{}** | {} |".format(
                section, name, value, owner or "?", reason or ""))
        add("")
        add("File each row above as a new card against the owning card's area. "
            "Do **not** tune anything in this run.")
        add("")

    add("## 1. Environment (hardware guard + 1:1 buffer)")
    add("")
    add("| Item | Value | Threshold | Verdict |")
    add("| --- | --- | --- | --- |")
    echecks = {c.get("name"): c for c in env.get("checks", [])}
    hw = echecks.get("hardware_renderer", {})
    b11 = echecks.get("buffer_1to1", {})
    add("| GL_RENDERER | `{}` | no SwiftShader / llvmpipe / softpipe | {} |".format(
        env.get("gl_renderer"), _verdict(hw.get("pass"), hw.get("owner"))))
    add("| GL_VENDOR | `{}` | recorded | - |".format(env.get("gl_vendor")))
    add("| tier | {} | HARDWARE | {} |".format(
        env.get("tier"), _verdict(env.get("tier") == "HARDWARE",
                                  OWNERS["env.software_renderer"])))
    add("| devicePixelRatio | {} | recorded | - |".format(
        env.get("devicePixelRatio")))
    add("| Canvas CSS size | {}x{} | recorded | - |".format(
        *(env.get("canvas_css") or [None, None])))
    add("| Drawing buffer size | {}x{} | buffer == CSS x DPR (1:1) | {} |".format(
        *(env.get("canvas_buffer") or [None, None]),
        _verdict(b11.get("pass"), b11.get("owner"))))
    add("| WebGL version | {} | recorded | - |".format(env.get("webgl_version")))
    add("| s3tc | {} | recorded (see N16) | - |".format(env.get("s3tc")))
    add("")
    add("- user agent: `{}`".format(env.get("user_agent")))
    if env.get("debug_renderer_info") is False:
        add("- `WEBGL_debug_renderer_info` unavailable: the GL_RENDERER above "
            "was pasted from `chrome://gpu` per the N1 fallback.")
    add("")

    add("## 2. Frame timing - 60 s per phase")
    add("")
    add("Thresholds: **p50 <= 33.4 ms, p95 <= 35 ms, <= 2 frames > 66 ms per "
        "minute, jitter < 5 %** in *all three* phases. Each window must hold "
        ">= {:.0f} s of valid capture.".format(MIN_WINDOW_S))
    add("")
    add("| Phase | mode | window s | n frames | p50 ms | p95 ms | max ms | "
        ">66 ms /min | jitter % | Verdict |")
    add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for phase in PHASES:
        f = frames[phase]
        add("| {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |".format(
            phase, f.get("exactness"), f.get("window_s"), f.get("n_frames"),
            f.get("p50_ms"), f.get("p95_ms"), f.get("max_ms"),
            f.get("long_per_minute"), f.get("jitter_pct"),
            _verdict(f.get("pass"), OWNERS["frames"],
                     note="bounded" if f.get("exactness") == AGGREGATE else None)))
    add("")
    for phase in PHASES:
        f = frames[phase]
        rows = ["{}={} ({} {})".format(
            c.get("name"), c.get("value"), c.get("op"), c.get("limit"))
            for c in f.get("checks", [])]
        add("- **{}**: {}".format(phase, "; ".join(rows)))
    add("")
    if bounded:
        add("> Phases captured in AGGREGATE mode ({}) report a *lower bound* on "
            "frames > 66 ms, because the N1 10 s dump exposes only `max_ms`. A "
            "bounded pass is not sign-off-grade for that one threshold."
            .format(", ".join(bounded)))
        add("")
    add("Owning card on any miss: **{}** (pacing governor / render budget)."
        .format(OWNERS["frames"]))
    add("")

    add("## 3. Sharpness (physical-pixel DevTools screenshots, no OS scaling)")
    add("")
    add("Thresholds: **median transition <= 1.5 px, p90 <= 2.0 px, cap-height "
        ">= 22 px, contrast >= 7:1**. Cap-height applies to the caption text; "
        "the reticle and gaze marker are glyph-free, so only the edge and "
        "contrast thresholds gate them.")
    add("")
    add("| Element | median px | p90 px | cap-height px | contrast | rect | "
        "screenshot | Verdict |")
    add("| --- | --- | --- | --- | --- | --- | --- | --- |")
    labels = {"caption": "caption", "reticle": "reticle", "marker": "gaze marker"}
    for role in SHARP_ROLES:
        s = sharp[role]
        add("| {} | {} | {} | {} | {} | {} | `{}` | {} |".format(
            labels[role], s.get("median_transition_px"),
            s.get("p90_transition_px"),
            s.get("cap_height_px") if role == "caption"
            else "n/a ({})".format(s.get("cap_height_px")),
            s.get("contrast_ratio"), s.get("rect"), s.get("image"),
            _verdict(s.get("pass"), OWNERS["sharpness." + role])))
    add("")
    add("Owning cards: caption -> **{}**, reticle -> **{}**, gaze marker -> "
        "**{}**.".format(OWNERS["sharpness.caption"],
                         OWNERS["sharpness.reticle"],
                         OWNERS["sharpness.marker"]))
    add("")

    add("## 4. Payload & first frame")
    add("")
    add("| Item | Value | Threshold | Verdict |")
    add("| --- | --- | --- | --- |")
    add("| `{}` size | {} bytes ({} MiB / {} MB) | <= 30 MiB | {} |".format(
        payload.get("path"), payload.get("size_bytes"), payload.get("size_mib"),
        payload.get("size_mb"),
        _verdict(payload.get("pass"), OWNERS["payload"])))
    add("| Time to first frame | {} ms ({} s) | INFO - not gating | recorded |"
        .format(ttff.get("ttff_ms"), ttff.get("ttff_s")))
    add("")
    add("Owning cards on a payload miss: **{}** (skybox payload / music out of "
        "the data file).".format(OWNERS["payload"]))
    add("")

    add("## 5. Gaze markers")
    add("")
    add("Every gaze prompt must show a visible 2D marker (or an off-screen edge "
        "arrow) in a physical-pixel screenshot. {} of {} prompts confirmed."
        .format(gaze.get("n_visible"), gaze.get("n_prompts")))
    add("")
    add("| Prompt | marker visible | screenshot | Verdict |")
    add("| --- | --- | --- | --- |")
    for c in gaze.get("prompts", []):
        add("| {} | {} | `{}` | {} |".format(
            c.get("prompt_id"), c.get("marker_visible"), c.get("screenshot"),
            _verdict(c.get("pass"), OWNERS["gaze"])))
    add("")
    add("Owning card on any miss: **{}**.".format(OWNERS["gaze"]))
    add("")

    add("## 6. Sign-off")
    add("")
    add("- [{}] Hardware GPU confirmed in the `PERFPROBE env` line (no "
        "SwiftShader / llvmpipe).".format("x" if env.get("pass") else " "))
    add("- [{}] 1:1 drawing buffer (no browser or OS rescale).".format(
        "x" if (echecks.get("buffer_1to1", {}).get("pass")) else " "))
    add("- [{}] Frame timing within thresholds in intro, warp and spectrum."
        .format("x" if all(frames[p].get("pass") for p in PHASES) else " "))
    add("- [{}] Caption, reticle and gaze-marker sharpness within thresholds."
        .format("x" if all(sharp[r].get("pass") for r in SHARP_ROLES) else " "))
    add("- [{}] Data file <= 30 MiB.".format("x" if payload.get("pass") else " "))
    add("- [{}] Every gaze prompt has a visible marker.".format(
        "x" if gaze.get("pass") else " "))
    add("- [ ] Operator sign-off: ______________  date: ____________")
    add("")
    add("**Kill criterion:** any threshold miss blocks sign-off and names the "
        "owning card (section 0). File each miss as a new card; do **not** "
        "tune anything during the acceptance run.")
    return "\n".join(L) + "\n"


def cmd_report(args):
    env = _load_json(args.env, "env")
    frames = {p: _load_json(getattr(args.frames, p), p + " frames")
              for p in PHASES}
    sharp = {r: _load_json(getattr(args.sharpness, r), r + " sharpness")
             for r in SHARP_ROLES}
    payload = _load_json(args.payload, "payload")
    ttff = _load_json(args.ttff, "ttff")
    gaze = _load_json(args.gaze, "gaze")

    text = render_report(env, frames, sharp, payload, ttff, gaze)
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(text)

    overall = _all_pass(env, frames, sharp, payload, gaze)
    sys.stderr.write("report written to {} ({})\n".format(
        out_path, "PASS" if overall else "FAIL"))
    if not overall:
        for section, name, value, owner, _reason in collect_failures(
                env, frames, sharp, payload, gaze):
            sys.stderr.write("  FAIL {}/{} = {}  -> owning card {}\n".format(
                section, name, value, owner))
    return 0 if overall else 1


# ------------------------------------------------------------------ main
def build_parser():
    ap = argparse.ArgumentParser(
        prog="acceptance_stats.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("env", help="hardware guard + 1:1 buffer check")
    p.add_argument("log", help="PERFPROBE console log")
    p.set_defaults(func=cmd_env)

    p = sub.add_parser("frames", help="60 s frame stats for one phase")
    p.add_argument("log", help="PERFPROBE console log")
    p.add_argument("--phase", required=True, choices=PHASES)
    p.add_argument("--window", type=float, default=60.0,
                   help="window length in seconds (default 60)")
    p.set_defaults(func=cmd_frames)

    p = sub.add_parser("sharpness", help="text/overlay sharpness thresholds")
    p.add_argument("sharpness_json", help="tools/sharpness.py output (JSON)")
    p.add_argument("--role", choices=list(SHARP_ROLES) + ["generic"],
                   default="generic")
    p.set_defaults(func=cmd_sharpness)

    p = sub.add_parser("payload", help="data-file size cap (30 MiB)")
    p.add_argument("payload", help="path to the Unity .data file")
    p.set_defaults(func=cmd_payload)

    p = sub.add_parser("ttff", help="time to first frame (INFO only)")
    p.add_argument("ms", help="milliseconds")
    p.set_defaults(func=cmd_ttff)

    p = sub.add_parser("gaze", help="gaze-marker visibility list")
    p.add_argument("gaze", help="hand-filled gaze.json")
    p.set_defaults(func=cmd_gaze)

    p = sub.add_parser("report", help="build tools/ACCEPTANCE.md")
    p.add_argument("--env", required=True)
    p.add_argument("--frames", required=True, nargs=3,
                   metavar=("INTRO", "WARP", "SPECTRUM"))
    p.add_argument("--sharpness", required=True, nargs=3,
                   metavar=("CAPTION", "RETICLE", "MARKER"))
    p.add_argument("--payload", required=True)
    p.add_argument("--ttff", required=True)
    p.add_argument("--gaze", required=True)
    p.add_argument("--out", default="tools/ACCEPTANCE.md")
    p.set_defaults(func=cmd_report)
    return ap


def main(argv=None):
    ap = build_parser()
    args = ap.parse_args(argv)
    if args.cmd == "report":
        ns = argparse.Namespace()
        for name, pth in zip(PHASES, args.frames):
            setattr(ns, name, pth)
        args.frames = ns
        ns = argparse.Namespace()
        for name, pth in zip(SHARP_ROLES, args.sharpness):
            setattr(ns, name, pth)
        args.sharpness = ns
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
