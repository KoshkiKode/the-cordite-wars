#!/usr/bin/env python3
"""
Semantic versioning automation for multi-platform game exports.
Updates version across project.godot, export_presets.cfg, package manifests, etc.
"""

import sys
import re
import json
import argparse
from pathlib import Path
from typing import Tuple

def parse_version(version_str: str) -> Tuple[int, int, int]:
    """Parse semantic version string (e.g., '0.1.0') into (major, minor, patch)."""
    parts = version_str.strip('v').split('.')
    if len(parts) != 3:
        raise ValueError(f"Invalid version format: {version_str}. Expected X.Y.Z")
    return tuple(int(p) for p in parts)

def format_version(major: int, minor: int, patch: int) -> str:
    """Format version tuple as X.Y.Z string."""
    return f"{major}.{minor}.{patch}"

def bump_version(current: str, bump_type: str) -> str:
    """Bump version by type: 'major', 'minor', or 'patch'."""
    major, minor, patch = parse_version(current)
    
    if bump_type == "major":
        major += 1
        minor = 0
        patch = 0
    elif bump_type == "minor":
        minor += 1
        patch = 0
    elif bump_type == "patch":
        patch += 1
    else:
        raise ValueError(f"Invalid bump type: {bump_type}")
    
    return format_version(major, minor, patch)

def update_project_godot(project_root: Path, new_version: str) -> None:
    """Update version in project.godot."""
    godot_file = project_root / "project.godot"
    if not godot_file.exists():
        print(f"⚠ {godot_file} not found, skipping")
        return

    major, minor, patch = parse_version(new_version)
    content = godot_file.read_text()

    # project.godot stores version under [application] section as config/version="X.Y.Z"
    updated = re.sub(
        r'(config/version\s*=\s*)"[^"]*"',
        f'\\g<1>"{new_version}"',
        content
    )
    updated = re.sub(r'(config/version_major\s*=\s*)\d+', f'\\g<1>{major}', updated)
    updated = re.sub(r'(config/version_minor\s*=\s*)\d+', f'\\g<1>{minor}', updated)
    updated = re.sub(r'(config/version_patch\s*=\s*)\d+', f'\\g<1>{patch}', updated)

    if updated != content:
        godot_file.write_text(updated)
        print(f"✓ Updated project.godot → {new_version}")
    else:
        print(f"⚠ No version found in project.godot")

def update_export_presets(project_root: Path, new_version: str) -> None:
    """Update version in export_presets.cfg for all platforms."""
    presets_files = list(project_root.glob("**/export_presets.cfg"))
    
    for presets_file in presets_files:
        content = presets_file.read_text()
        
        # Windows: application/product_version
        updated = re.sub(
            r'(application/product_version\s*=\s*)"[^"]*"',
            f'\\g<1>"{new_version}.0"',
            content
        )

        # Android: package/version (integer)
        # For Android, use major*100 + minor*10 + patch (e.g., 0.1.0 → 10)
        major, minor, patch = parse_version(new_version)
        android_version = major * 100 + minor * 10 + patch
        updated = re.sub(
            r'(package/version\s*=\s*)(\d+)',
            f'\\g<1>{android_version}',
            updated
        )

        # iOS: application/short_version, application/version
        updated = re.sub(
            r'(application/short_version\s*=\s*)"[^"]*"',
            f'\\g<1>"{new_version}"',
            updated
        )
        updated = re.sub(
            r'(application/version\s*=\s*)"[^"]*"',
            f'\\g<1>"{android_version}"',  # Use same integer version for consistency
            updated
        )
        
        if updated != content:
            presets_file.write_text(updated)
            print(f"✓ Updated {presets_file.relative_to(project_root)} → {new_version}")


def update_android_gradle(project_root: Path, new_version: str) -> None:
    """Update version in Android build.gradle."""
    gradle_file = project_root / "versions" / "android" / "build.gradle"
    if not gradle_file.exists():
        print(f"⚠ {gradle_file} not found, skipping")
        return

    major, minor, patch = parse_version(new_version)
    android_version = major * 100 + minor * 10 + patch
    content = gradle_file.read_text()
    updated = re.sub(r'(versionCode\s+)\d+', f'\\g<1>{android_version}', content)
    updated = re.sub(r'(versionName\s+")[^"]*"', f'\\g<1>{new_version}"', updated)

    if updated != content:
        gradle_file.write_text(updated)
        print(f"✓ Updated versions/android/build.gradle → {new_version}")

def update_snapcraft_yaml(project_root: Path, new_version: str) -> None:
    """Update version in snapcraft.yaml."""
    snap_file = project_root / "versions" / "linux" / "snapcraft.yaml"
    if not snap_file.exists():
        print(f"⚠ {snap_file} not found, skipping")
        return
    
    content = snap_file.read_text()
    updated = re.sub(
        r'(version:\s*)[\'"]?[0-9.]+[\'"]?',
        f'\\g<1>{new_version}',
        content
    )
    
    if updated != content:
        snap_file.write_text(updated)
        print(f"✓ Updated snapcraft.yaml → {new_version}")

def update_plist(project_root: Path, new_version: str) -> None:
    """Update version in macOS Info.plist and iOS ios-info.plist."""
    major, minor, patch = parse_version(new_version)
    bundle_version = major * 100 + minor * 10 + patch

    plist_files = [
        project_root / "versions" / "macos" / "Info.plist",
        project_root / "versions" / "ios" / "ios-info.plist",
    ]

    for plist_file in plist_files:
        if not plist_file.exists():
            print(f"⚠ {plist_file} not found, skipping")
            continue

        content = plist_file.read_text()

        # CFBundleShortVersionString
        updated = re.sub(
            r'(<key>CFBundleShortVersionString</key>\s*<string>)[^<]*(</string>)',
            f'\\g<1>{new_version}\\g<2>',
            content,
            flags=re.DOTALL
        )

        # CFBundleVersion (integer)
        updated = re.sub(
            r'(<key>CFBundleVersion</key>\s*<string>)[^<]*(</string>)',
            f'\\g<1>{bundle_version}\\g<2>',
            updated,
            flags=re.DOTALL
        )

        if updated != content:
            plist_file.write_text(updated)
            print(f"✓ Updated {plist_file.relative_to(project_root)} → {new_version}")


def update_version_json(project_root: Path, new_version: str) -> None:
    """Update versions/shared/version.json."""
    version_file = project_root / "versions" / "shared" / "version.json"
    if not version_file.exists():
        print(f"⚠ {version_file} not found, skipping")
        return

    major, minor, patch = parse_version(new_version)
    data = json.loads(version_file.read_text())
    data["major"] = major
    data["minor"] = minor
    data["patch"] = patch
    version_file.write_text(json.dumps(data, indent=2) + "\n")
    print(f"✓ Updated versions/shared/version.json → {new_version}")

def main():
    parser = argparse.ArgumentParser(
        description="Bump semantic version across all platform export files"
    )
    parser.add_argument(
        "action",
        choices=["major", "minor", "patch", "set"],
        help="Version bump type or 'set' to specify exact version"
    )
    parser.add_argument(
        "version",
        nargs="?",
        help="Exact version for 'set' action (e.g., 1.0.0)"
    )
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path.cwd(),
        help="Project root directory (default: cwd)"
    )
    
    args = parser.parse_args()
    project_root = args.project_root.resolve()
    
    # Read current version from project.godot
    godot_file = project_root / "project.godot"
    if not godot_file.exists():
        print(f"✗ project.godot not found in {project_root}")
        sys.exit(1)
    
    content = godot_file.read_text()
    match = re.search(r'config/version\s*=\s*"([^"]*)"', content)
    if not match:
        print("✗ Could not find version in project.godot")
        sys.exit(1)
    
    current_version = match.group(1)
    print(f"Current version: {current_version}")
    
    # Determine new version
    if args.action == "set":
        if not args.version:
            print("✗ --version required for 'set' action")
            sys.exit(1)
        new_version = args.version
    else:
        new_version = bump_version(current_version, args.action)
    
    print(f"New version: {new_version}\n")
    
    # Update all files
    update_project_godot(project_root, new_version)
    update_export_presets(project_root, new_version)
    update_snapcraft_yaml(project_root, new_version)
    update_plist(project_root, new_version)
    update_android_gradle(project_root, new_version)
    update_version_json(project_root, new_version)
    
    print(f"\n✓ Version bumped to {new_version}")

if __name__ == "__main__":
    main()
