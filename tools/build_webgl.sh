#!/usr/bin/env bash
# N6: headless WebGL build + template guard, in one step.
#
#   tools/build_webgl.sh [out-dir] [license-volume]
#     out-dir          host dir for the build + log   (default /home/watson/icecube-out-n6)
#     license-volume   docker volume with the Unity license (default icecube-unity-license-a)
#
# Runs WebGLBuilder.Build in the 2019.4.40f1 unityci container under nohup,
# polls the log for 'BUILD_RESULT result=Succeeded errors=0', then runs
# tools/verify_template.sh against the produced index.html so a build made
# without the Icecube template (no rAF shim => un-paced 60 fps) can never pass
# silently. Exit code is non-zero on build failure, timeout, or guard failure.
set -uo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/home/watson/icecube-out-n6}"
LICENSE_VOL="${2:-icecube-unity-license-a}"
IMAGE="unityci/editor:ubuntu-2019.4.40f1-webgl-3"
NAME="icecube-unity-build-$$"
LOG="$OUT/build.log"
TIMEOUT_S="${BUILD_TIMEOUT_S:-5400}"

mkdir -p "$OUT"
rm -f "$LOG"

echo "BUILD start project=$HERE out=$OUT image=$IMAGE"
nohup docker run --rm --name "$NAME" \
  -v "$HERE":/project \
  -v "$OUT":/out \
  -v "$LICENSE_VOL":/root/.local/share/unity3d \
  "$IMAGE" \
  unity-editor -quit -batchmode -nographics -silent-crashes \
  -projectPath /project -buildTarget WebGL \
  -executeMethod WebGLBuilder.Build -logFile /out/build.log \
  > "$OUT/build.out" 2>&1 &

start=$(date +%s)
while :; do
  if [ -f "$LOG" ] && grep -q 'BUILD_RESULT' "$LOG"; then break; fi
  if ! docker ps --format '{{.Names}}' | grep -qx "$NAME"; then
    # container gone without a result line: compile error or crash
    sleep 2
    grep -q 'BUILD_RESULT' "$LOG" 2>/dev/null && break
    echo "BUILD container exited without BUILD_RESULT; compiler errors:"
    grep -n 'error CS' "$LOG" 2>/dev/null | head -20
    exit 2
  fi
  if [ $(( $(date +%s) - start )) -ge "$TIMEOUT_S" ]; then
    echo "BUILD timeout after ${TIMEOUT_S}s"; docker rm -f "$NAME" >/dev/null 2>&1; exit 3
  fi
  sleep 15
done

result=$(grep -m1 'BUILD_RESULT' "$LOG")
echo "$result"
if ! grep -q 'BUILD_RESULT result=Succeeded errors=0' "$LOG"; then
  grep -n 'error CS' "$LOG" | head -20
  exit 1
fi

# The shim must survive into the built page: this is the N6 guard.
"$HERE/tools/verify_template.sh" "$OUT/webgl"
