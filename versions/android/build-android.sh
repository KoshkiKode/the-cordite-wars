#!/usr/bin/env bash
# build-android.sh — Export Android APK and sign it
# Version strings are kept in sync by bump-version.py.
#
# Prerequisites:
#   - $GODOT4 points to Godot 4.6 (Mono/C#) headless binary
#   - Android export templates + Android build template installed:
#       godot4 --install-android-build-template
#   - ANDROID_HOME set (Android SDK)
#   - Java 21 available
#   - Signing keystore: set KEYSTORE_PATH, KEYSTORE_PASS, KEY_ALIAS, KEY_PASS

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/build/android"
DIST_DIR="$PROJECT_ROOT/dist/android"
GODOT="${GODOT4:-godot4}"
APP_VERSION="$(python3 -c "import json; d=json.load(open('$SCRIPT_DIR/../shared/version.json')); print(f\"{d['major']}.{d['minor']}.{d['patch']}\")")"

echo "=== Cordite Wars: Android Build ==="

mkdir -p "$BUILD_DIR" "$DIST_DIR"

# ── Step 1: Godot export ──────────────────────────────────────────────────
echo "--- Exporting Android project via Godot..."
cd "$PROJECT_ROOT"
"$GODOT" --headless --export-release "Android" "$BUILD_DIR/CorditeWars.apk"

# If using Gradle build, Godot populates build/android/gradle/ instead.
if [ -d "$BUILD_DIR/gradle" ]; then
  echo "--- Gradle project detected — building with Gradle..."
  cd "$BUILD_DIR/gradle"

  export KEYSTORE_PATH="${KEYSTORE_PATH:-$PROJECT_ROOT/keystore.jks}"
  export KEYSTORE_PASS="${KEYSTORE_PASS:?KEYSTORE_PASS not set}"
  export KEY_ALIAS="${KEY_ALIAS:-cordite-wars-key}"
  export KEY_PASS="${KEY_PASS:?KEY_PASS not set}"

  ./gradlew assembleRelease

  UNSIGNED_APK="app/build/outputs/apk/release/app-release-unsigned.apk"
  SIGNED_APK="$DIST_DIR/CorditeWars_${APP_VERSION}.apk"

  # ── Step 2: Sign ─────────────────────────────────────────────────────────
  echo "--- Signing APK..."
  jarsigner -verbose \
    -sigalg SHA256withRSA \
    -digestalg SHA-256 \
    -keystore "$KEYSTORE_PATH" \
    -storepass "$KEYSTORE_PASS" \
    -keypass "$KEY_PASS" \
    "$UNSIGNED_APK" \
    "$KEY_ALIAS"

  # ── Step 3: Align ─────────────────────────────────────────────────────────
  echo "--- Aligning APK..."
  zipalign -v 4 "$UNSIGNED_APK" "$SIGNED_APK"

  echo "=== Android build complete ==="
  echo "APK: $SIGNED_APK"
else
  # Direct APK export path (without Gradle build)
  if [ -f "$BUILD_DIR/CorditeWars.apk" ]; then
    cp "$BUILD_DIR/CorditeWars.apk" "$DIST_DIR/CorditeWars_${APP_VERSION}_unsigned.apk"
    echo "NOTE: APK exported but not signed via Gradle. Use jarsigner manually."
  else
    echo "ERROR: No APK or Gradle project found."
    exit 1
  fi
fi
