"""N2 regression tests: the WebGL perf governor must stay defused.

Card N2 removed the two mechanisms that produced blur and judder in the WebGL
build:

  1. ``Application.targetFrameRate = 30`` -- on WebGL a positive
     targetFrameRate makes Unity drive its main loop from ``setTimeout``
     instead of ``requestAnimationFrame``, so frames land off-vsync.  The only
     vsync-aligned value on WebGL is ``-1``, which is what the active path must
     use.  (The hard 30fps lock is delivered later by N6's half-rate rAF shim,
     not by targetFrameRate, so no positive literal may come back here.)
  2. The one-way ``Screen.SetResolution`` ratchet (down to 1024x576, with a
     maxWidth/maxHeight cap of 1280x720 and a 0.75 scale floor).  The active
     path must not touch the drawing buffer at all, so Unity's default canvas
     matching keeps it 1:1 with physical pixels
     (canvas.width = round(clientWidth * devicePixelRatio)).

This project has no Unity test assembly (no .asmdef, and the package manifest
pulls in no com.unity.test-framework), and Unity cannot be driven in CI here,
so these are static checks over the committed C# source, the serialized scene
and the WebGL template configuration -- the same things a reviewer would grep
for.  They are deliberately strict about *reachability*: the legacy ratchet may
remain in the file, but only below the ``legacyResolutionScaling`` guard, and
that flag must be serialized off in the scene.  Build/ is never inspected (it
is gitignored and not committed).
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
GOVERNOR = REPO / "Assets" / "Scripts" / "WebGLPerfGovernor.cs"
GOVERNOR_META = REPO / "Assets" / "Scripts" / "WebGLPerfGovernor.cs.meta"
SCENE = REPO / "Assets" / "Scenes" / "main.unity"
PROJECT_SETTINGS = REPO / "ProjectSettings" / "ProjectSettings.asset"
TEMPLATES = REPO / "Assets" / "WebGLTemplates"

GUARD_RE = re.compile(r"if\s*\(\s*!\s*legacyResolutionScaling\s*\)\s*return\s*;")
UNITY_HOOKS = (
    "Awake",
    "OnEnable",
    "Start",
    "Update",
    "LateUpdate",
    "FixedUpdate",
    "OnValidate",
)
STARTUP_HOOKS = ("Awake", "OnEnable", "Start")


# ---------------------------------------------------------------- helpers
def strip_comments(src: str) -> str:
    """Blank out // and /* */ comments, preserving line structure."""
    src = re.sub(r"/\*.*?\*/", lambda m: re.sub(r"[^\n]", " ", m.group(0)), src, flags=re.S)
    return re.sub(r"//[^\n]*", "", src)


def code_lines(path: Path) -> list:
    return strip_comments(path.read_text(encoding="utf-8")).splitlines()


METHOD_RE = re.compile(
    r"^[ \t]*(?:(?:public|private|protected|internal|static|override|virtual|sealed)[ \t]+)*"
    r"(?:void|int|float|bool|string|[A-Z][\w<>.\[\]]*)[ \t]+"
    r"(?P<name>\w+)[ \t]*\([^;{)]*\)[ \t]*\{?[ \t]*$",
    re.M,
)


def method_bodies(src: str) -> dict:
    """Map method name -> brace-matched body text (comment-stripped source)."""
    bodies = {}
    for m in METHOD_RE.finditer(src):
        start = src.find("{", m.end() - 1)
        if start == -1:
            continue
        depth = 0
        for i in range(start, len(src)):
            if src[i] == "{":
                depth += 1
            elif src[i] == "}":
                depth -= 1
                if depth == 0:
                    bodies[m.group("name")] = src[start + 1 : i]
                    break
    return bodies


def statements(body: str) -> list:
    return [ln.strip() for ln in body.splitlines() if ln.strip()]


def logs(src: str) -> list:
    """Argument text of every Debug.Log* call in ``src``."""
    out = []
    for m in re.finditer(r"Debug\.Log(?:Warning|Error|Format)?\s*\(", src):
        depth = 0
        for i in range(m.end() - 1, len(src)):
            if src[i] == "(":
                depth += 1
            elif src[i] == ")":
                depth -= 1
                if depth == 0:
                    out.append(src[m.end() : i])
                    break
    return out


def cs_sources() -> list:
    return sorted((REPO / "Assets").rglob("*.cs"))


@pytest.fixture(scope="module")
def governor_src() -> str:
    assert GOVERNOR.is_file(), "governor script not found at %s" % GOVERNOR
    return strip_comments(GOVERNOR.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def methods(governor_src):
    bodies = method_bodies(governor_src)
    for required in ("Start", "ApplyQuality", "Update"):
        assert required in bodies, (
            "could not parse %s() out of the governor; parsed: %s" % (required, sorted(bodies))
        )
    return bodies


# ------------------------------------------------- 1. frame pacing (rAF)
def test_apply_quality_sets_target_frame_rate_to_minus_one(methods):
    assert re.search(r"Application\.targetFrameRate\s*=\s*-\s*1\s*;", methods["ApplyQuality"]), (
        "ApplyQuality must set Application.targetFrameRate = -1 (rAF, vsync-aligned on WebGL)"
    )


def test_target_frame_rate_is_never_assigned_anything_but_minus_one():
    offenders = []
    for path in cs_sources():
        for lineno, line in enumerate(code_lines(path), 1):
            for m in re.finditer(r"targetFrameRate\s*=\s*([^;]+);", line):
                if m.group(1).replace(" ", "") != "-1":
                    offenders.append("%s:%d: %s" % (path.relative_to(REPO), lineno, line.strip()))
    assert not offenders, (
        "targetFrameRate may only ever be assigned -1; any positive value switches Unity's "
        "WebGL main loop to setTimeout:\n" + "\n".join(offenders)
    )


def test_thirty_fps_lock_is_not_reintroduced(governor_src):
    assert not re.search(r"targetFrameRate\s*=\s*\d", governor_src), (
        "the hard 30fps lock must not come back via targetFrameRate; it is delivered by "
        "N6's half-rate rAF shim"
    )


def test_vsynccount_is_not_assigned_anywhere():
    offenders = []
    for path in cs_sources():
        for lineno, line in enumerate(code_lines(path), 1):
            if re.search(r"vSyncCount\s*=[^=]", line):
                offenders.append("%s:%d: %s" % (path.relative_to(REPO), lineno, line.strip()))
    assert not offenders, (
        "QualitySettings.vSyncCount must not be assigned (removed by N2):\n" + "\n".join(offenders)
    )


def test_quality_settings_block_is_preserved(methods):
    body = methods["ApplyQuality"]
    for expected in (
        "QualitySettings.SetQualityLevel",
        "QualitySettings.shadows",
        "QualitySettings.shadowDistance",
        "QualitySettings.antiAliasing",
        "QualitySettings.pixelLightCount",
        "QualitySettings.softParticles",
        "QualitySettings.realtimeReflectionProbes",
        "QualitySettings.masterTextureLimit",
    ):
        assert expected in body, "%s must still be applied by ApplyQuality" % expected


# ----------------------------------------------- 2. legacy flag + guard
def test_legacy_flag_is_public_serialized_and_defaults_to_false(governor_src):
    assert re.search(r"public\s+bool\s+legacyResolutionScaling\s*=\s*false\s*;", governor_src), (
        "expected `public bool legacyResolutionScaling = false;` on the governor"
    )
    assert not re.search(
        r"\[\s*(?:System\.)?NonSerialized\s*\]\s*(?:\[[^\]]*\]\s*)*public\s+bool\s+"
        r"legacyResolutionScaling",
        governor_src,
    ), "legacyResolutionScaling must stay serialized so the scene can pin it off"


def test_update_returns_immediately_on_its_first_line(methods):
    first = statements(methods["Update"])[0]
    assert GUARD_RE.fullmatch(first), (
        "Update's first statement must be `if (!legacyResolutionScaling) return;`, got %r" % first
    )


def test_nothing_executes_in_update_before_the_guard(methods):
    body = methods["Update"]
    guard = GUARD_RE.search(body)
    assert guard, "legacy guard not found in Update"
    above = body[: guard.start()]
    assert not re.search(r"[A-Za-z_]", above), (
        "no code may run in Update ahead of the guard, found %r" % above.strip()
    )


# -------------------------- 3. drawing buffer untouched on the active path
def test_set_resolution_never_sits_in_a_unity_lifecycle_hook(methods):
    callers = set(name for name, body in methods.items() if "Screen.SetResolution" in body)
    assert callers, "no Screen.SetResolution found at all -- governor source looks unexpected"
    assert not callers.intersection(UNITY_HOOKS), (
        "Screen.SetResolution must not sit directly in a Unity lifecycle hook: %s"
        % sorted(callers)
    )


def test_set_resolution_helper_is_only_reachable_below_the_guard(methods):
    helpers = [name for name, body in methods.items() if "Screen.SetResolution" in body]
    reachable = []
    for helper in helpers:
        for caller, body in methods.items():
            if caller == helper:
                continue
            guard = GUARD_RE.search(body)
            for call in re.finditer(r"\b%s\s*\(" % re.escape(helper), body):
                if caller != "Update" or guard is None or call.start() < guard.end():
                    reachable.append("%s() called from %s()" % (helper, caller))
    assert not reachable, (
        "the resolution ratchet must be unreachable while legacyResolutionScaling is false: "
        + "; ".join(reachable)
    )


def test_startup_does_not_touch_the_drawing_buffer(methods):
    scale_helpers = [n for n, b in methods.items() if "Screen.SetResolution" in b]
    for hook in STARTUP_HOOKS:
        body = methods.get(hook)
        if body is None:
            continue
        assert "Screen.SetResolution" not in body, "%s must not call Screen.SetResolution" % hook
        for helper in scale_helpers:
            assert not re.search(r"\b%s\s*\(" % re.escape(helper), body), (
                "%s must not apply an initial render scale via %s()" % (hook, helper)
            )


def test_no_set_resolution_anywhere_else_in_the_project():
    offenders = []
    for path in cs_sources():
        if path == GOVERNOR:
            continue
        for lineno, line in enumerate(code_lines(path), 1):
            if "Screen.SetResolution" in line:
                offenders.append("%s:%d: %s" % (path.relative_to(REPO), lineno, line.strip()))
    assert not offenders, (
        "Screen.SetResolution must not reappear outside the governor:\n" + "\n".join(offenders)
    )


def test_ratchet_fields_caps_and_floor_are_gone(governor_src):
    for banned in ("maxWidth", "maxHeight", "0.75", "1024", "576", "1280", "720"):
        assert banned not in governor_src, (
            "%r belongs to the removed 1024x576 ratchet / 1280x720 cap / 0.75 scale floor and "
            "must be gone from the governor source" % banned
        )


def test_active_path_derives_no_resolution_from_screen_size(methods):
    active = "".join(methods.get(hook, "") for hook in STARTUP_HOOKS) + methods["ApplyQuality"]
    assert "Screen.SetResolution" not in active, (
        "the active path must never call Screen.SetResolution"
    )
    assert not re.search(r"Mathf\.(?:Min|Clamp)\s*\(\s*Screen\.", active), (
        "the active path must not clamp the drawing buffer against Screen dimensions"
    )


# ------------------------------------------------------------ 4. logging
def test_start_logs_the_drawing_buffer_exactly_once(methods):
    startup = "".join(methods.get(hook, "") for hook in STARTUP_HOOKS)
    found = logs(startup)
    assert len(found) == 1, "expected exactly one startup log line, got %s" % (found,)
    assert re.search(
        r"\[WebGLPerfGovernor\]\s+drawing buffer\s*\{Screen\.width\}x\{Screen\.height\}", found[0]
    ), "startup log must report the drawing buffer size, got %r" % found[0]


def test_start_logs_after_applying_quality(methods):
    body = methods["Start"]
    apply_at = body.find("ApplyQuality(")
    log_at = body.find("Debug.Log")
    assert apply_at != -1, "Start must still call ApplyQuality()"
    assert log_at != -1 and apply_at < log_at, (
        "the drawing-buffer log must come after ApplyQuality() so it reports the final size"
    )


def test_no_ratchet_logging_remains(governor_src):
    offenders = [
        log
        for log in logs(governor_src)
        for banned in ("scale=", "fps=", "maxWidth", "maxHeight")
        if banned in log
    ]
    assert not offenders, "ratchet logging must be gone:\n" + "\n".join(offenders)


def test_legacy_branch_logs_only_the_applied_size(methods):
    checked = 0
    for name, body in methods.items():
        if "Screen.SetResolution" not in body:
            continue
        for log in logs(body):
            checked += 1
            assert re.search(r"\[WebGLPerfGovernor\]\s+applied\s*\{", log), (
                "the legacy log in %s() should print only the applied size, got %r" % (name, log)
            )
    assert checked >= 1, "expected the retained legacy branch to log the applied size"


# -------------------------------------------------- 5. serialized scene
def governor_guid() -> str:
    m = re.search(r"guid:\s*([0-9a-f]{32})", GOVERNOR_META.read_text(encoding="utf-8"))
    assert m, "could not read the governor script guid from its .meta file"
    return m.group(1)


def governor_components() -> list:
    guid = governor_guid()
    text = SCENE.read_text(encoding="utf-8", errors="replace")
    return [b for b in re.split(r"^--- ", text, flags=re.M) if "guid: %s" % guid in b]


def test_scene_still_has_exactly_one_governor_component():
    blocks = governor_components()
    assert len(blocks) == 1, (
        "expected exactly one serialized WebGLPerfGovernor in main.unity, found %d" % len(blocks)
    )


def test_scene_serializes_legacy_scaling_off():
    block = governor_components()[0]
    m = re.search(r"^\s*legacyResolutionScaling:\s*(\S+)\s*$", block, flags=re.M)
    assert m, "serialized governor is missing the legacyResolutionScaling field"
    assert m.group(1) == "0", (
        "legacyResolutionScaling must be serialized 0, got %r" % m.group(1)
    )


def test_scene_no_longer_serializes_the_removed_ratchet_fields():
    block = governor_components()[0]
    for stale in ("maxWidth", "maxHeight", "minScale", "targetFps", "renderScale"):
        assert not re.search(r"^\s*%s:" % stale, block, flags=re.M), (
            "stale serialized field %r must be gone from the governor component" % stale
        )


def test_no_scene_or_prefab_enables_legacy_scaling():
    offenders = []
    assets = REPO / "Assets"
    for path in list(assets.rglob("*.unity")) + list(assets.rglob("*.prefab")):
        text = path.read_text(encoding="utf-8", errors="replace")
        for lineno, line in enumerate(text.splitlines(), 1):
            m = re.match(r"^\s*legacyResolutionScaling:\s*(\S+)\s*$", line)
            if m and m.group(1) != "0":
                offenders.append("%s:%d: %s" % (path.relative_to(REPO), lineno, line.strip()))
    assert not offenders, "legacy resolution scaling must stay off:\n" + "\n".join(offenders)


# ----------------------------------- 6. WebGL template / canvas matching
def test_project_settings_uses_a_stock_or_checked_in_template():
    text = PROJECT_SETTINGS.read_text(encoding="utf-8", errors="replace")
    m = re.search(r"^\s*webGLTemplate:\s*(\S+)\s*$", text, flags=re.M)
    assert m, "ProjectSettings must record a webGLTemplate"
    template = m.group(1)
    if not template.endswith(":Default"):
        assert TEMPLATES.is_dir(), (
            "webGLTemplate %r is custom but Assets/WebGLTemplates/ is missing" % template
        )


def test_webgl_template_does_not_resize_the_drawing_buffer():
    if not TEMPLATES.is_dir():
        pytest.skip("stock WebGL template: Unity's default canvas matching is used")
    offenders = []
    patterns = (
        r"devicePixelRatio\s*[*/]",
        r"devicePixelRatio\s*=",
        r"canvas\.(?:width|height)\s*=",
        r"[sS]etResolution",
        r"matchWebGLToCanvasSize\s*[:=]\s*false",
    )
    for path in list(TEMPLATES.rglob("*.html")) + list(TEMPLATES.rglob("*.js")):
        text = path.read_text(encoding="utf-8", errors="replace")
        for lineno, line in enumerate(text.splitlines(), 1):
            if any(re.search(p, line) for p in patterns):
                offenders.append("%s:%d: %s" % (path.relative_to(REPO), lineno, line.strip()))
    assert not offenders, (
        "the template must leave Unity's default canvas matching alone so the drawing buffer "
        "stays 1:1 with physical pixels:\n" + "\n".join(offenders)
    )
