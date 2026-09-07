#!/usr/bin/env bash
# Builds AudioBridge as a single self-contained Windows .exe and drops it in the shared
# Google Drive folder for testing. Runs from macOS or Windows; no Windows machine needed
# to produce the build, only to run it.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRIVE="${AUDIOBRIDGE_DRIVE:-$HOME/Library/CloudStorage/GoogleDrive-<your-google-account>/My Drive/AudioBridge}"

echo "==> Testing"
dotnet test "$ROOT" --nologo -v quiet

echo "==> Publishing"
rm -rf "$ROOT/publish"
dotnet publish "$ROOT/src/AudioBridge.App" -c Release -r win-x64 --self-contained true -o "$ROOT/publish" --nologo -v quiet
rm -f "$ROOT/publish"/*.pdb

if [ -d "$DRIVE" ]; then
  cp "$ROOT/publish/AudioBridge.exe" "$DRIVE/AudioBridge.exe"
  echo "==> Copied to $DRIVE"
else
  echo "==> Google Drive folder not found at $DRIVE; skipping copy." >&2
fi

ls -lh "$ROOT/publish/AudioBridge.exe"
