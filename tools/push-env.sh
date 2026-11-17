#!/bin/sh
# Push the project's .env (API keys) to the connected Quest, into the app's private folder where HypaConfig reads it.
# The keys are never built into the APK. Unity does this automatically after every Android build (HypaEnvBuild);
# use this script when you install an APK by hand.  Usage: tools/push-env.sh [package]   (default ai.hypa.lens)
set -e
cd "$(dirname "$0")/.."
PKG="${1:-ai.hypa.lens}"
[ -f .env ] || { echo "No .env in $(pwd). Copy .env.example to .env and fill it in."; exit 1; }
command -v adb >/dev/null 2>&1 || { echo "adb not found on PATH."; exit 1; }
adb get-state >/dev/null 2>&1 || { echo "No headset connected (adb devices). Plug it in and allow USB debugging."; exit 1; }
adb shell mkdir -p "/sdcard/Android/data/$PKG/files"
adb push .env "/sdcard/Android/data/$PKG/files/.env"
echo "Keys on the headset: $(adb shell cat /sdcard/Android/data/$PKG/files/.env | grep -v '^#' | cut -d= -f1 | grep . | tr '\n' ' ')"
echo "Restart the app if it was running."
