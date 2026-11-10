#!/usr/bin/env bash
# Run once per clone: Git LFS, Unity Smart Merge (UnityYAMLMerge), and the secret-scan hooks.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

if command -v git-lfs >/dev/null 2>&1; then git lfs install --local; else
  echo "WARNING: git-lfs is not installed (brew install git-lfs / apt install git-lfs). Binary assets need it."; fi

# Find UnityYAMLMerge from the installed editor version.
ver=$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)
for c in \
  "/Applications/Unity/Hub/Editor/$ver/Unity.app/Contents/Tools/UnityYAMLMerge" \
  "$HOME/Applications/Unity/Hub/Editor/$ver/Unity.app/Contents/Tools/UnityYAMLMerge" \
  "C:/Program Files/Unity/Hub/Editor/$ver/Editor/Data/Tools/UnityYAMLMerge.exe" \
  "$HOME/Unity/Hub/Editor/$ver/Editor/Data/Tools/UnityYAMLMerge"; do
  if [ -x "$c" ]; then merge="$c"; break; fi
done
if [ -n "${merge:-}" ]; then
  git config merge.unityyamlmerge.name "Unity SmartMerge"
  git config merge.unityyamlmerge.driver "\"$merge\" merge -p %O %B %A %A"
  git config merge.unityyamlmerge.recursive binary
  echo "Smart Merge: $merge"
else
  echo "WARNING: UnityYAMLMerge for Unity $ver not found; install that editor and re-run."
fi

git config core.hooksPath tools/hooks
echo "Hooks: secret scan runs before every commit and push."
