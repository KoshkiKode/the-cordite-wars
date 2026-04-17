#!/usr/bin/env bash
# build-windows.sh — Export Windows build and compile Inno Setup installer
# Version strings are kept in sync by bump-version.py.
#
# Prerequisites:
#   - $GODOT4 points to Godot 4.6 headless binary
#   - iscc (Inno Setup compiler) available on PATH
#   - Windows export templates installed

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/build/windows"
DIST_DIR="$PROJECT_ROOT/dist/windows"
GODOT="${GODOT4:-godot4}"
APP_VERSION="$(python3 -c "import json; d=json.load(open('$SCRIPT_DIR/../shared/version.json')); print(f\"{d['major']}.{d['minor']}.{d['patch']}\")")"

echo "=== Cordite Wars: Windows Build ==="
echo "Project root: $PROJECT_ROOT"
echo "Godot binary: $GODOT"

# 1. Ensure build directory exists
mkdir -p "$BUILD_DIR" "$DIST_DIR"

# 2. Export from Godot (headless)
echo "--- Exporting Windows binary..."
cd "$PROJECT_ROOT"
"$GODOT" --headless --export-release "Windows Desktop" "$BUILD_DIR/CorditeWars.exe"

if [ ! -f "$BUILD_DIR/CorditeWars.exe" ]; then
  echo "ERROR: Godot export failed. Check that the 'Windows Desktop' export preset is configured."
  exit 1
fi

echo "--- Export complete: $BUILD_DIR/CorditeWars.exe"

# 3. Compile Inno Setup installer
echo "--- Compiling Inno Setup installer..."
iscc "$PROJECT_ROOT/versions/windows/inno-setup.iss"

echo "=== Windows build complete ==="
echo "Installer: $DIST_DIR/CorditeWars_Setup_${APP_VERSION}.exe"
