#!/usr/bin/env bash
# N17 rollback: copy a material backup set back into the project.
#
# Backups are written OUTSIDE the Unity project by ShaderTrim.Backup
# (/home/watson/icecube-backups/materials/<UTC>-N17/...), preserving the
# original Assets/ relative paths. Never keep .bak copies inside Assets/ -
# Unity imports them as real assets.
#
# Usage:
#   tools/restore_materials.sh                       # newest backup set, into CWD project
#   tools/restore_materials.sh <backup-dir>          # a specific set
#   tools/restore_materials.sh <backup-dir> <project-root>
#   DRY_RUN=1 tools/restore_materials.sh ...         # show what would be copied
set -euo pipefail

BACKUP_ROOT="${ICECUBE_MATERIAL_BACKUP_ROOT:-/home/watson/icecube-backups/materials}"
SET_DIR="${1:-}"
PROJECT="${2:-$(pwd)}"

if [ -z "$SET_DIR" ]; then
  SET_DIR="$(find "$BACKUP_ROOT" -maxdepth 1 -mindepth 1 -type d -name '*-N17' | sort | tail -n1)"
  [ -n "$SET_DIR" ] || { echo "no *-N17 backup set under $BACKUP_ROOT" >&2; exit 1; }
fi

[ -d "$SET_DIR/Assets" ] || { echo "$SET_DIR has no Assets/ tree" >&2; exit 1; }
[ -d "$PROJECT/Assets" ] || { echo "$PROJECT is not a Unity project root" >&2; exit 1; }

echo "restoring from: $SET_DIR"
echo "into project  : $PROJECT"
[ -f "$SET_DIR/manifest.json" ] && echo "manifest      : $SET_DIR/manifest.json"

n=0
while IFS= read -r -d '' src; do
  rel="${src#$SET_DIR/}"
  dst="$PROJECT/$rel"
  if [ "${DRY_RUN:-0}" = "1" ]; then
    echo "would restore $rel"
  else
    mkdir -p "$(dirname "$dst")"
    cp -p "$src" "$dst"
    echo "restored $rel"
  fi
  n=$((n+1))
done < <(find "$SET_DIR/Assets" -type f \( -name '*.mat' -o -name '*.mat.meta' \) -print0)

echo "files: $n"
echo "Reopen the project (or let Unity reimport) so the AssetDatabase picks the materials back up."
