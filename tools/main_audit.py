#!/usr/bin/env python3
"""main_audit.py -- N7: static audit of Main.cs Update paths.

Finds the two script-side causes of unsmooth motion that resolution cannot
cure:

  * **Category A -- frame-rate dependence.**  Motion written as
    ``x += 0.05f`` or ``Lerp(a, b, 0.1f)`` inside ``Update`` advances once per
    *frame*, not once per *second*.  On a machine whose frame time wobbles
    between 16 ms and 33 ms the same code moves the camera at two different
    speeds, which reads as judder even at a perfectly steady resolution.
  * **Category B -- per-frame allocations.**  ``GetComponent``, string
    concatenation, ``new`` of reference types and boxed ``foreach`` enumerators
    inside ``Update`` feed the GC; on WebGL a collection is a visible frame
    spike (the > 66 ms frames the N1 harness counts).
  * **Category C -- physics / camera placement.**  Whether anything is moved
    through a ``Rigidbody`` (needs interpolation) and whether camera motion
    runs in ``Update`` (right, vsync-paced) or ``FixedUpdate`` (wrong, fixed
    step).

The script is report-only: it never writes to the sources it reads.

An "Update path" is the body of ``Update`` / ``LateUpdate`` / ``FixedUpdate``
plus the body of any method of the same file that those bodies call (one level
of transitive resolution -- enough for this file, where ``Update`` calls
``UpdateScene``, ``MapVols``, ``PlaySFX``, ``SetupScene`` and
``gaze_trigger.tick``).

A flagged line can be downgraded two ways:

  * automatically, when the interpolation factor is derived from a value that
    is itself accumulated with ``Time.deltaTime`` (``Lerp(a, b, t*t*t)`` where
    ``t`` comes from a ``+= Time.deltaTime`` counter is frame-rate
    independent); or
  * explicitly, with a trailing ``// N7-OK: <reason>`` comment, which records
    a human verdict and keeps the scanner usable as a regression guard.

Usage::

    python3 tools/main_audit.py                      # human summary
    python3 tools/main_audit.py --markdown           # findings table
    python3 tools/main_audit.py --json               # machine readable
    python3 tools/main_audit.py --fail-on-violations # exit 1 if any remain
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

DEFAULT_SOURCE = os.path.join("Assets", "Scripts", "Main.cs")

ENTRY_POINTS = ("Update", "LateUpdate", "FixedUpdate")

#: ``Time.*`` members that make a line frame-rate independent.
DELTA_TIME_RE = re.compile(
    r"Time\.(?:deltaTime|unscaledDeltaTime|smoothDeltaTime|fixedDeltaTime"
    r"|fixedUnscaledDeltaTime|unscaledTime|time)\b"
)

#: names that conventionally hold a per-frame delta
DELTA_NAMES = ("dt", "delta", "deltaTime", "dtime", "d_t")

ACK_RE = re.compile(r"//\s*N7-OK:?\s*(?P<reason>[^\n]*)")

# --------------------------------------------------------------------------
# Category A patterns
# --------------------------------------------------------------------------

INTERPOLATOR_RE = re.compile(
    r"\b(?:Mathf|Vector2|Vector3|Vector4|Quaternion|Color)\.(?:Lerp|LerpUnclamped"
    r"|Slerp|SlerpUnclamped|MoveTowards|MoveTowardsAngle|RotateTowards"
    r"|SmoothStep)\s*\("
)
TRANSFORM_MOTION_RE = re.compile(r"\.(?:Rotate|RotateAround|Translate)\s*\(")
BARE_MOVETOWARDS_RE = re.compile(r"(?<![.\w])(?:MoveTowards|RotateTowards)\s*\(")

FLOAT_LITERAL_RE = re.compile(r"(?<![\w.])(?:\d+\.\d*|\.\d+|\d+)[fF]\b|(?<![\w.])\d+\.\d+")
COMPOUND_ASSIGN_RE = re.compile(r"(?P<lhs>[A-Za-z_][\w.\[\], ]*?)\s*(?P<op>\+=|-=|\*=)\s*(?P<rhs>[^;]+);")
#: a multiplier that is a bare float literal, i.e. a per-frame decay factor
DAMPING_RE = re.compile(r"^\s*-?(?:\d+\.\d*|\.\d+|\d+)[fF]?\s*$")

# --------------------------------------------------------------------------
# Category B patterns  (name -> (regex, severity))
# --------------------------------------------------------------------------

ALLOC_PATTERNS = [
    ("GetComponent", re.compile(r"\.GetComponent(?:s)?(?:InChildren|InParent)?\s*[<(]"), "violation"),
    ("Find", re.compile(r"\b(?:GameObject|Transform)\.Find\s*\(|\bFindObjects?OfType\s*\(|\.Find\s*\("), "violation"),
    ("ResourcesLoad", re.compile(r"\bResources\.Load\b"), "violation"),
    ("StringConcat", re.compile(r'"[^"]*"\s*\+|\+\s*"[^"]*"|\.ToString\s*\(|string\.Format\s*\(|\$"'), "violation"),
    ("NewArray", re.compile(r"\bnew\s+[A-Za-z_][\w.<>, ]*\s*\["), "violation"),
    ("NewCollection", re.compile(r"\bnew\s+(?:List|Dictionary|HashSet|Queue|Stack|Array)\s*<"), "violation"),
    ("NewReference", re.compile(r"\bnew\s+(?:AnimationCurve|StringBuilder|Material|Texture2D|GameObject|Mesh)\b"), "violation"),
    ("ToArrayToList", re.compile(r"\.To(?:Array|List)\s*\(\s*\)"), "violation"),
    ("Linq", re.compile(r"\.(?:Where|Select|Any|All|First|FirstOrDefault|OrderBy|OrderByDescending|Sum|Count)\s*\("), "violation"),
    ("ForeachEnumerator", re.compile(r"\bforeach\s*\(\s*(?P<type>[A-Za-z_][\w.<>, ]*?)\s+\w+\s+in\s+(?P<coll>[^)]+)\)"), "informational"),
    ("MaterialStringProperty", re.compile(r"\.(?:SetFloat|SetColor|SetVector|SetInt|SetTexture|GetFloat)\s*\(\s*\""), "informational"),
    ("DebugLog", re.compile(r"\bDebug\.Log(?:Warning|Error|Format)?\s*\("), "informational"),
    ("Instantiate", re.compile(r"\bInstantiate\s*\("), "violation"),
]

# struct types whose `new` never touches the managed heap
VALUE_TYPES = (
    "Vector2", "Vector3", "Vector4", "Quaternion", "Color", "Color32",
    "Rect", "Bounds", "Matrix4x4", "Ray", "Plane", "Keyframe",
)
NEW_VALUE_TYPE_RE = re.compile(r"\bnew\s+(?:%s)\b" % "|".join(VALUE_TYPES))

# --------------------------------------------------------------------------
# Category C patterns
# --------------------------------------------------------------------------

RIGIDBODY_RE = re.compile(r"\bRigidbody2?D?\b")
RIGIDBODY_MOVE_RE = re.compile(
    r"\.(?:velocity|angularVelocity)\s*=|\.MovePosition\s*\(|\.MoveRotation\s*\("
    r"|\.AddForce\s*\(|\.AddTorque\s*\("
)
INTERPOLATION_RE = re.compile(r"\.interpolation\s*=")
CAMERA_WRITE_RE = re.compile(
    r"(?:camera_house|main_camera|Camera\.main|cam|camera)\w*\.transform\.(?:position"
    r"|localPosition|rotation|localRotation|eulerAngles|localEulerAngles)\s*="
)


# ==========================================================================
# source plumbing
# ==========================================================================

def strip_comments(src: str) -> str:
    """Blank out ``//`` and ``/* */`` comments, preserving line/column layout."""
    src = re.sub(r"/\*.*?\*/", lambda m: re.sub(r"[^\n]", " ", m.group(0)), src, flags=re.S)
    src = re.sub(r'"(?:\\.|[^"\\\n])*"', lambda m: '"' + " " * (len(m.group(0)) - 2) + '"', src)
    return re.sub(r"//[^\n]*", lambda m: " " * len(m.group(0)), src)


METHOD_DECL_RE = re.compile(
    r"^[ \t]*(?:public |private |protected |internal |static |virtual |override |sealed |new )*"
    r"(?P<ret>[A-Za-z_][\w.<>,\[\] ]*?)\s+(?P<name>[A-Za-z_]\w*)\s*\((?P<params>[^;{)]*)\)\s*$",
    re.M,
)


def find_methods(stripped: str):
    """Return ``{name: [method, ...]}`` with 1-based inclusive line ranges.

    Bodies are located by brace-depth tracking from the first ``{`` after the
    signature, so nested classes, ``switch`` blocks and lambdas are handled.
    """
    lines = stripped.split("\n")
    methods: dict[str, list] = {}
    for i, line in enumerate(lines):
        m = METHOD_DECL_RE.match(line + "\n")
        if not m:
            # allow "void Update() {" on one line
            m = re.match(
                r"^[ \t]*(?:public |private |protected |internal |static |virtual |override )*"
                r"(?P<ret>[A-Za-z_][\w.<>,\[\] ]*?)\s+(?P<name>[A-Za-z_]\w*)\s*\((?P<params>[^;{)]*)\)\s*\{",
                line,
            )
            if not m:
                continue
        name = m.group("name")
        if name in ("if", "for", "foreach", "while", "switch", "catch", "lock", "using", "return", "new"):
            continue
        # find the opening brace
        j = i
        while j < len(lines) and "{" not in lines[j]:
            if ";" in lines[j]:
                j = -1
                break
            j += 1
        if j == -1 or j >= len(lines) or j > i + 2:
            continue
        depth = 0
        start = j
        end = None
        for k in range(j, len(lines)):
            depth += lines[k].count("{") - lines[k].count("}")
            if depth <= 0:
                end = k
                break
        if end is None:
            continue
        methods.setdefault(name, []).append({
            "name": name,
            "returns": m.group("ret").strip(),
            "params": m.group("params").strip(),
            "decl_line": i + 1,
            "body_start": start + 1,
            "body_end": end + 1,
        })
    return methods


CALL_RE = re.compile(r"(?<![\w.])(?P<name>[A-Za-z_]\w*)\s*\(")


def called_names(stripped_lines, method):
    """Identifiers invoked like a method anywhere inside ``method``'s body."""
    names = set()
    for line in stripped_lines[method["body_start"] - 1: method["body_end"]]:
        for m in CALL_RE.finditer(line):
            names.add(m.group("name"))
    # `obj.tick(...)` -- the dot form is excluded above, so pick it up too
    for line in stripped_lines[method["body_start"] - 1: method["body_end"]]:
        for m in re.finditer(r"\.(?P<name>[A-Za-z_]\w*)\s*\(", line):
            names.add(m.group("name"))
    return names


KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "return", "new", "catch", "lock",
    "using", "typeof", "sizeof", "nameof", "else", "do",
}


def update_paths(stripped: str):
    """Every method that executes every frame, with its entry point recorded."""
    methods = find_methods(stripped)
    lines = stripped.split("\n")
    paths = []
    seen = set()
    for entry in ENTRY_POINTS:
        for m in methods.get(entry, []):
            key = (m["name"], m["body_start"])
            if key in seen:
                continue
            seen.add(key)
            rec = dict(m)
            rec["entry"] = entry
            rec["depth"] = 0
            paths.append(rec)
    # one level of transitive resolution
    for root in list(paths):
        for name in sorted(called_names(lines, root)):
            if name in KEYWORDS or name in ENTRY_POINTS:
                continue
            for m in methods.get(name, []):
                key = (m["name"], m["body_start"])
                if key in seen:
                    continue
                seen.add(key)
                rec = dict(m)
                rec["entry"] = root["entry"]
                rec["depth"] = 1
                paths.append(rec)
    return paths


# ==========================================================================
# delta-time provenance
# ==========================================================================

IDENT_RE = re.compile(r"[A-Za-z_]\w*")


def time_accumulated_names(stripped: str):
    """Fields/locals that are advanced by ``Time.deltaTime`` somewhere."""
    names = set()
    for m in re.finditer(r"(?P<lhs>[A-Za-z_][\w.]*)\s*(?:\[[^\]]*\])?\s*(?:\+=|-=)\s*(?P<rhs>[^;]*);", stripped):
        if DELTA_TIME_RE.search(m.group("rhs")):
            names.add(m.group("lhs").split(".")[-1])
    for m in re.finditer(r"(?P<lhs>[A-Za-z_][\w.]*)\s*(?:\[[^\]]*\])?\s*=\s*(?P<rhs>[^;]*);", stripped):
        rhs = m.group("rhs")
        lhs = m.group("lhs").split(".")[-1]
        if not DELTA_TIME_RE.search(rhs):
            continue
        # `x = x + dt` / `x = (x + dt) % k` is an accumulator; but
        # `x = Lerp(x, y, 1-exp(-k*dt))` is *not* -- it is the frame-rate
        # independent smoother, and treating its target as "accumulated" would
        # silence a genuinely broken Lerp on a neighbouring line.
        esc = re.escape(lhs)
        if re.search(r"(?<![\w.])%s\s*\+" % esc, rhs) or re.search(r"\+\s*%s(?![\w])" % esc, rhs):
            names.add(lhs)
    # one fix-point pass: locals seeded from an accumulated value
    for _ in range(3):
        grew = False
        for m in re.finditer(r"(?:float|double|var)\s+(?P<lhs>\w+)\s*=\s*(?P<rhs>[^;]*);", stripped):
            rhs_names = set(IDENT_RE.findall(m.group("rhs")))
            if rhs_names & names and m.group("lhs") not in names:
                names.add(m.group("lhs"))
                grew = True
        if not grew:
            break
    names.discard("")
    return names


def delta_safe_names(method):
    """Parameter names of ``method`` that carry a frame delta."""
    safe = set()
    for part in method["params"].split(","):
        toks = part.strip().split()
        if len(toks) >= 2 and toks[-1] in DELTA_NAMES:
            safe.add(toks[-1])
    return safe


def line_is_delta_scaled(raw_line, safe_names):
    if DELTA_TIME_RE.search(raw_line):
        return True
    for name in safe_names:
        if re.search(r"(?<![\w.])%s(?![\w])" % re.escape(name), raw_line):
            return True
    return False


# ==========================================================================
# scanning
# ==========================================================================

def last_call_argument(text, open_paren_index):
    """The last top-level argument of the call whose `(` is at the given index."""
    depth = 0
    start = open_paren_index + 1
    last = start
    for i in range(open_paren_index, len(text)):
        ch = text[i]
        if ch in "([":
            depth += 1
        elif ch in ")]":
            depth -= 1
            if depth == 0:
                return text[last:i]
        elif ch == "," and depth == 1:
            last = i + 1
    return text[last:]


def is_parametric(code, hits, accumulated):
    """True when the motion's *rate* comes from a Time.deltaTime-accumulated value.

    `Lerp(a, b, t*t)` where `t` is accumulated with Time.deltaTime is
    frame-rate independent: the factor is a function of wall-clock time, so the
    only thing a longer frame changes is how far along the curve we sample.
    The check is deliberately scoped to the factor argument -- looking at the
    whole line would let the Lerp *target* vouch for a hard-coded factor.
    """
    if "interpolator" in hits:
        m = INTERPOLATOR_RE.search(code) or BARE_MOVETOWARDS_RE.search(code)
        if m:
            factor = last_call_argument(code, m.end() - 1)
            return bool(set(IDENT_RE.findall(factor)) & accumulated)
        return False
    if "literal-step" in hits:
        cm = COMPOUND_ASSIGN_RE.search(code)
        if not cm:
            return False
        lhs = cm.group("lhs").split(".")[-1].split("[")[0].strip()
        # `while(phase > 2pi) phase -= 2pi;` wraps an accumulated phase around;
        # the subtraction is a modulo, not a per-frame advance.
        if lhs in accumulated and re.match(r"\s*while\s*\(", code):
            return True
        return bool(set(IDENT_RE.findall(cm.group("rhs"))) & accumulated)
    # .Rotate(...) / .Translate(...) / `vel *= 0.96f`
    if "transform-motion" in hits:
        m = TRANSFORM_MOTION_RE.search(code)
        if m:
            args = code[m.end() - 1:]
            return bool(set(IDENT_RE.findall(args)) & accumulated)
    return False


def _finding(category, code, severity, path, lineno, raw, reason):
    return {
        "category": category,
        "code": code,
        "severity": severity,
        "line": lineno,
        "method": path["name"],
        "entry": path["entry"],
        "transitive": bool(path["depth"]),
        "text": raw.strip(),
        "reason": reason,
    }


def scan_source(src: str):
    stripped = strip_comments(src)
    raw_lines = src.split("\n")
    stripped_lines = stripped.split("\n")
    paths = update_paths(stripped)
    accumulated = time_accumulated_names(stripped)

    findings = []
    covered = set()
    for path in paths:
        safe = delta_safe_names(path) | {"Time.deltaTime"}
        local_acc = set(accumulated)
        for lineno in range(path["body_start"], path["body_end"] + 1):
            if lineno in covered:
                continue
            code = stripped_lines[lineno - 1]
            raw = raw_lines[lineno - 1]
            if not code.strip():
                continue
            covered.add(lineno)

            # track locals seeded from accumulated values, in execution order
            lm = re.match(r"\s*(?:float|double|var)\s+(\w+)\s*=\s*(.*)", code)
            if lm and (set(IDENT_RE.findall(lm.group(2))) & local_acc or DELTA_TIME_RE.search(lm.group(2))):
                local_acc.add(lm.group(1))

            ack = ACK_RE.search(raw)
            ack_reason = ack.group("reason").strip() if ack else None

            delta_scaled = line_is_delta_scaled(code, safe)
            fixed_step = path["entry"] == "FixedUpdate"

            # ---------------- Category A
            hits = []
            if INTERPOLATOR_RE.search(code) or BARE_MOVETOWARDS_RE.search(code):
                hits.append("interpolator")
            if TRANSFORM_MOTION_RE.search(code):
                hits.append("transform-motion")
            cm = COMPOUND_ASSIGN_RE.search(code)
            if cm and FLOAT_LITERAL_RE.search(cm.group("rhs")):
                if cm.group("op") in ("+=", "-="):
                    hits.append("literal-step")
                elif DAMPING_RE.search(cm.group("rhs")):
                    # `vel *= 0.96f` is a per-frame decay: the half-life is
                    # measured in frames, so the motion changes shape with the
                    # frame rate even though nothing is "added" per frame.
                    hits.append("literal-damping")

            if hits:
                parametric = is_parametric(code, hits, local_acc)
                if delta_scaled:
                    sev, reason = "ok", "scaled by a frame delta on the same line"
                elif fixed_step:
                    sev, reason = "informational", "runs on the fixed step (frame-rate independent)"
                elif ack_reason:
                    sev, reason = "accepted", ack_reason
                elif parametric:
                    sev, reason = (
                        "informational",
                        "parametric: factor derives from a Time.deltaTime-accumulated value",
                    )
                else:
                    sev, reason = "violation", "advances once per frame, not once per second"
                if sev != "ok":
                    findings.append(_finding("A", "+".join(hits), sev, path, lineno, raw, reason))

            # ---------------- Category B
            for name, pattern, base_sev in ALLOC_PATTERNS:
                m = pattern.search(code)
                if not m:
                    continue
                if name == "NewArray" and NEW_VALUE_TYPE_RE.search(code):
                    continue
                if name == "ForeachEnumerator":
                    coll = m.group("coll")
                    if re.search(r"\.transform\b|\bTransform\b", coll) or "Transform" in m.group("type"):
                        reason = "Transform.GetEnumerator() boxes an enumerator every frame"
                        sev = "violation"
                    else:
                        reason = "foreach may box an enumerator depending on the collection type"
                        sev = "informational"
                elif name == "StringConcat":
                    reason = "builds a new string every frame (plus float->string formatting)"
                    sev = base_sev
                elif name == "MaterialStringProperty":
                    reason = "string property name is hashed on every call; cache Shader.PropertyToID"
                    sev = base_sev
                else:
                    reason = "managed allocation / native lookup on every frame"
                    sev = base_sev
                if ack_reason:
                    sev, reason = "accepted", ack_reason
                findings.append(_finding("B", name, sev, path, lineno, raw, reason))

    # ---------------- Category C (file-wide)
    camera_writes = []
    for path in paths:
        for lineno in range(path["body_start"], path["body_end"] + 1):
            if CAMERA_WRITE_RE.search(stripped_lines[lineno - 1]):
                camera_writes.append({"line": lineno, "entry": path["entry"], "method": path["name"]})
    physics = {
        "mentions_rigidbody": bool(RIGIDBODY_RE.search(stripped)),
        "moves_rigidbody": sorted(
            i + 1 for i, l in enumerate(stripped_lines) if RIGIDBODY_MOVE_RE.search(l)
        ),
        "sets_interpolation": sorted(
            i + 1 for i, l in enumerate(stripped_lines) if INTERPOLATION_RE.search(l)
        ),
        "has_fixed_update": any(p["entry"] == "FixedUpdate" for p in paths),
        "camera_writes": camera_writes,
        "camera_motion_in_fixed_update": any(c["entry"] == "FixedUpdate" for c in camera_writes),
    }

    severities = ("violation", "accepted", "informational")
    counts = {s: sum(1 for f in findings if f["severity"] == s) for s in severities}
    return {
        "update_paths": [
            {k: p[k] for k in ("name", "entry", "depth", "decl_line", "body_start", "body_end")}
            for p in paths
        ],
        "time_accumulated_names": sorted(accumulated),
        "findings": sorted(findings, key=lambda f: (f["line"], f["category"], f["code"])),
        "physics": physics,
        "counts": counts,
        "violations": [f for f in findings if f["severity"] == "violation"],
    }


def scan_file(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        result = scan_source(f.read())
    result["source"] = path
    return result


# ==========================================================================
# reporting
# ==========================================================================

SEV_ORDER = {"violation": 0, "accepted": 1, "informational": 2}


def to_markdown(result):
    out = []
    out.append("| Line | Cat | Finding | Severity | Update path | Code | Note |")
    out.append("| --- | --- | --- | --- | --- | --- | --- |")
    for f in sorted(result["findings"], key=lambda f: (SEV_ORDER[f["severity"]], f["line"])):
        code = f["text"].replace("|", "\\|")
        if len(code) > 90:
            code = code[:87] + "..."
        out.append("| %d | %s | %s | %s | `%s` (from `%s`) | `%s` | %s |" % (
            f["line"], f["category"], f["code"], f["severity"],
            f["method"], f["entry"], code, f["reason"]))
    out.append("")
    out.append("Counts: %s" % ", ".join("%s=%d" % kv for kv in sorted(result["counts"].items())))
    return "\n".join(out)


def to_text(result):
    out = []
    out.append("source: %s" % result.get("source", "<stdin>"))
    out.append("update paths (%d):" % len(result["update_paths"]))
    for p in result["update_paths"]:
        out.append("  %-22s lines %4d-%-4d  %s%s" % (
            p["name"], p["body_start"], p["body_end"], p["entry"],
            " (transitive)" if p["depth"] else ""))
    out.append("")
    for sev in ("violation", "accepted", "informational"):
        rows = [f for f in result["findings"] if f["severity"] == sev]
        out.append("%s (%d):" % (sev.upper(), len(rows)))
        for f in sorted(rows, key=lambda f: f["line"]):
            out.append("  L%-5d [%s/%s] %s" % (f["line"], f["category"], f["code"], f["text"][:100]))
            out.append("          -> %s" % f["reason"])
        out.append("")
    ph = result["physics"]
    out.append("physics / camera (category C):")
    out.append("  Rigidbody mentioned:          %s" % ph["mentions_rigidbody"])
    out.append("  Rigidbody moved at lines:     %s" % (ph["moves_rigidbody"] or "none"))
    out.append("  .interpolation assigned:      %s" % (ph["sets_interpolation"] or "never"))
    out.append("  FixedUpdate present:          %s" % ph["has_fixed_update"])
    out.append("  camera transform writes:      %d" % len(ph["camera_writes"]))
    out.append("  camera motion in FixedUpdate: %s" % ph["camera_motion_in_fixed_update"])
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description="N7 static audit of Main.cs Update paths")
    ap.add_argument("source", nargs="?", default=DEFAULT_SOURCE)
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--markdown", action="store_true")
    ap.add_argument("--fail-on-violations", action="store_true",
                    help="exit 1 when any unannotated violation remains")
    args = ap.parse_args(argv)

    if not os.path.exists(args.source):
        print("main_audit: no such source: %s" % args.source, file=sys.stderr)
        return 2
    result = scan_file(args.source)

    if args.json:
        print(json.dumps(result, indent=2))
    elif args.markdown:
        print(to_markdown(result))
    else:
        print(to_text(result))

    if args.fail_on_violations and result["counts"]["violation"]:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
