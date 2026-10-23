#!/usr/bin/env bash
# Fetch stream branches from agent clones and merge them into the current branch.
#   tools/integrate.sh <clone-root> <stream> [<stream> ...]
# Example: tools/integrate.sh /tmp/.../scratchpad/wt core-settings core-windows
# Each stream lives in <clone-root>/<stream> on branch bw/<stream>. Conflicts stop the script: resolve, commit, re-run for the rest.
set -euo pipefail
root="$1"; shift
for s in "$@"; do
  clone="$root/$s"
  [ -d "$clone/.git" ] || { echo "skip $s: no clone at $clone"; continue; }
  if [ -n "$(git -C "$clone" status --porcelain)" ]; then echo "WARNING: $s clone has uncommitted changes (not merged)"; fi
  git fetch -q "$clone" "bw/$s:refs/heads/bw/$s" --force
  if git merge-base --is-ancestor "bw/$s" HEAD; then echo "$s: already merged"; continue; fi
  echo "== merging bw/$s ($(git rev-parse --short bw/$s))"
  git -c user.name=Claude -c user.email=noreply@anthropic.com merge --no-ff -m "merge: stream $s" "bw/$s"
done
