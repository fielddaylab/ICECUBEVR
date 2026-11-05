#!/usr/bin/env bash
# N6 guard: the half-rate rAF shim must survive into the built index.html.
#
# The shim lives in Assets/WebGLTemplates/Icecube/index.html and is only copied
# into the build when ProjectSettings webGLTemplate points at PROJECT:Icecube.
# A build made with the stock APPLICATION:Default template looks fine, logs
# nothing, and silently runs un-paced at 60 fps with a governor that reads work
# times from the jslib fallback — so this check is run after every build.
#
# Usage: tools/verify_template.sh [build-dir]        (default /home/watson/icecube-out/webgl)
set -uo pipefail

BUILD_DIR="${1:-/home/watson/icecube-out/webgl}"
INDEX="$BUILD_DIR/index.html"
fail=0

say()  { printf '%s\n' "$*"; }
ok()   { say "VERIFY_TEMPLATE ok    $*"; }
bad()  { say "VERIFY_TEMPLATE FAIL  $*"; fail=1; }

if [ ! -f "$INDEX" ]; then
  bad "no index.html at $INDEX"
  say "VERIFY_TEMPLATE result=FAIL"
  exit 1
fi

say "VERIFY_TEMPLATE index=$INDEX"

# 1. the shim is present, by its public API name
if grep -q 'icecubeSetHalfRate' "$INDEX"; then
  ok "icecubeSetHalfRate present"
else
  bad "icecubeSetHalfRate missing (template not applied?)"
fi

# 2. it logs a startup line
if grep -q "\[icecube\] rAF shim active halfRate=" "$INDEX"; then
  ok "startup log line present"
else
  bad "startup log line '[icecube] rAF shim active halfRate=' missing"
fi

# 3. halfRate defaults to true (C8: hard lock 30)
if grep -q 'halfRate: true' "$INDEX"; then
  ok "halfRate defaults to true"
else
  bad "halfRate does not default to true"
fi

# 4. the work-time sampler the governor jslib reads
if grep -q 'icecubeWorkMs' "$INDEX"; then
  ok "icecubeWorkMs sampler present"
else
  bad "icecubeWorkMs sampler missing"
fi

# 5. requestAnimationFrame is actually wrapped
if grep -q 'window.requestAnimationFrame = function' "$INDEX"; then
  ok "requestAnimationFrame wrapped"
else
  bad "requestAnimationFrame is not wrapped"
fi

# 6. ordering: the shim must come before the Unity loader, or Unity captures the
#    native rAF before the wrapper is installed.
shim_line=$(grep -n 'window.requestAnimationFrame = function' "$INDEX" | head -1 | cut -d: -f1)
loader_line=$(grep -nE '<script[^>]+src=("|'"'"')[^"'"'"']*(UnityLoader\.js|UNITY_WEBGL_LOADER_URL)' "$INDEX" | head -1 | cut -d: -f1)
if [ -n "${shim_line:-}" ] && [ -n "${loader_line:-}" ]; then
  if [ "$shim_line" -lt "$loader_line" ]; then
    ok "shim (line $shim_line) precedes UnityLoader.js (line $loader_line)"
  else
    bad "shim (line $shim_line) comes AFTER UnityLoader.js (line $loader_line)"
  fi
else
  bad "could not locate both the shim and UnityLoader.js for the ordering check"
fi

# 7. no query-string fps override: owner decision C8 is a hard lock at 30.
if grep -qE '\?fps|fps=60|searchParams' "$INDEX"; then
  bad "a query-string fps override is present; C8 binds a hard 30 lock"
else
  ok "no query-string fps override (C8 hard lock 30)"
fi

if [ "$fail" -eq 0 ]; then
  say "VERIFY_TEMPLATE result=PASS"
  exit 0
fi
say "VERIFY_TEMPLATE result=FAIL"
exit 1
