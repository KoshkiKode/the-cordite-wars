# versions/

This directory contains all platform-specific build configuration, packaging scripts, and shared export settings for Cordite Wars: Six Fronts.

Files here contain platform-specific build configuration and packaging scripts for Cordite Wars: Six Fronts. Version strings across these files are managed by `bump-version.py` at the project root. Re-run that script after changing the version number.

---

## Directory Structure

```
versions/
├── README.md                       ← This file
│
├── windows/
│   ├── inno-setup.iss              ← Inno Setup 6 installer script (produces CorditeWars_Setup.exe)
│   ├── CorditeWars.wxs             ← WiX v7 MSI installer script (produces CorditeWars.msi)
│   ├── AppxManifest.xml            ← MSIX package manifest (produces CorditeWars.msix)
│   └── build-windows.sh            ← Headless Godot export + Inno Setup compile
│
├── linux/
│   ├── snapcraft.yaml              ← Snap package manifest (strict confinement)
│   ├── cordite-wars.desktop        ← XDG desktop entry base (used by Snap, DEB, Flatpak)
│   ├── build-linux.sh              ← Headless Godot export + snapcraft build
│   ├── debian/
│   │   └── control                 ← Debian/Ubuntu DEB package control file
│   └── flatpak/
│       └── com.koshkikode.CorditeWars.yml  ← Flatpak manifest (org.freedesktop.Platform 24.08)
│
├── macos/
│   ├── Info.plist                  ← macOS app bundle metadata (CFBundle keys)
│   ├── entitlements.plist          ← Hardened runtime entitlements for codesign/notarize
│   └── build-macos.sh              ← Godot export + codesign + DMG creation + notarize
│
├── android/
│   ├── build.gradle                ← Android Gradle build config (minSdk 24, targetSdk 35)
│   ├── AndroidManifest.xml         ← Android permissions and activity configuration
│   └── build-android.sh            ← Godot export + gradle assemble + APK sign
│
├── ios/
│   ├── ios-info.plist              ← iOS/iPadOS app bundle metadata (CFBundle + UIKit keys)
│   └── ios-export-options.plist    ← Xcode export options for xcodebuild -exportArchive
│
└── shared/
    ├── export_presets.cfg          ← Godot export presets for all platforms (reference copy)
    └── version.json                ← Current version info (semver + protocol number)
```

---

## File Descriptions

### `windows/inno-setup.iss`

Inno Setup 6 installer script. Running `iscc versions/windows/inno-setup.iss` produces a single `CorditeWars_Setup.exe` installer that:

- Detects whether .NET 9 Desktop Runtime is installed; prompts to download if missing
- Installs the game to `%ProgramFiles%\Cordite Wars Six Fronts\` by default
- Creates a Start Menu group and optional desktop shortcut
- Registers an uninstaller
- Sets minimum Windows version to Windows 10 (build 17763, version 1809)

### `windows/AppxManifest.xml`

MSIX package manifest. Used with `makeappx pack` to produce a `CorditeWars.msix` package that can be:

- **Sideloaded** on developer machines using the CI-generated self-signed dev cert (`CN=CorditeWarsTeam`).
- **Distributed via the Microsoft Store** after re-signing with a real EV code-signing certificate (update `Publisher` to match your cert subject).

Key settings:

| Field | Value |
|-------|-------|
| `Identity/Name` | `KoshkiKode.CorditeWarsSixFronts` |
| `MinVersion` | `10.0.17763.0` (Windows 10 1809) |
| `EntryPoint` | `Windows.FullTrustApplication` |
| `Capabilities` | `internetClient`, `privateNetworkClientServer`, `runFullTrust` |

### `windows/build-windows.sh`

Bash script (run via Git Bash / WSL / CI). Calls `$GODOT4 --headless --export-release "Windows Desktop"`, then invokes `iscc` to produce the installer. Output goes to `dist/windows/`.

### `linux/snapcraft.yaml`

Snap package manifest using `core22` base and `strict` confinement. Plugs requested:

| Plug | Purpose |
|------|---------|
| `audio-playback` | OGG audio via PulseAudio/PipeWire |
| `opengl` | GPU rendering (Vulkan / GLES3) |
| `x11` / `wayland` | Display server |
| `network` + `network-bind` | Multiplayer |
| `home` | Save files in user home (fallback) |

### `linux/debian/control`

Debian/Ubuntu package control file. The CI job (`package-linux-deb`) injects the current version and computed `Installed-Size` at build time. Installing the produced `.deb` package places the game at `/opt/cordite-wars/` with a `/usr/bin/cordite-wars` launcher symlink.

### `linux/flatpak/com.koshkikode.CorditeWars.yml`

Flatpak manifest using `org.freedesktop.Platform//24.08` runtime. The CI job (`package-linux-flatpak`) stages the Linux export into `build/linux/` next to the manifest and runs `flatpak-builder` to produce a self-contained `.flatpak` bundle.

Finish-args granted:

| Arg | Purpose |
|-----|---------|
| `--share=ipc` | X11 MIT-SHM shared memory |
| `--share=network` | Multiplayer |
| `--socket=x11` / `--socket=wayland` | Display |
| `--socket=pulseaudio` | Audio |
| `--device=dri` | GPU rendering |
| `--filesystem=home` | Save files |

### `linux/cordite-wars.desktop`

XDG `.desktop` file base. Categories: `Game;StrategyGame;`. Used directly by Snap; patched by the DEB and Flatpak CI jobs to set the correct `Icon=` path for each format.

### `linux/build-linux.sh`

Exports the Linux binary headlessly from Godot, sets execute permissions, then runs `snapcraft --destructive-mode` (or `--use-lxd` in CI). Output: `cordite-wars_X.Y.Z_amd64.snap`.

### `macos/Info.plist`

Apple property list for the `.app` bundle. Key entries:

| Key | Value |
|-----|-------|
| `CFBundleIdentifier` | `com.koshkikode.corditewars` |
| `LSMinimumSystemVersion` | `12.0` (Monterey) |
| `NSHighResolutionCapable` | `true` (Retina support) |
| `CFBundleShortVersionString` | Matches `version.json` semver |

### `macos/entitlements.plist`

Hardened runtime entitlements required for Apple notarization:

- `com.apple.security.cs.allow-unsigned-executable-memory` — required by Mono/.NET JIT
- `com.apple.security.network.client` — multiplayer outbound connections
- `com.apple.security.network.server` — LAN hosting (listen server)

### `macos/build-macos.sh`

Full build pipeline: Godot export → `codesign` with Developer ID → DMG creation → `notarytool submit` → `stapler staple`. Requires environment variables `APPLE_ID`, `APPLE_TEAM_ID`, and `AC_PASSWORD` (App-Specific Password) to be set.

### `android/build.gradle`

Android Gradle configuration:

| Setting | Value |
|---------|-------|
| `applicationId` | `com.koshkikode.corditewars` |
| `minSdk` | 24 (Android 7.0) |
| `targetSdk` | 35 (Android 15) |
| ABI filters | `arm64-v8a` (primary), optional `armeabi-v7a` |

### `android/AndroidManifest.xml`

Required permissions: `INTERNET`, `ACCESS_WIFI_STATE`, `ACCESS_NETWORK_STATE`, `VIBRATE`. Activity is locked to `landscape` orientation. `configChanges` includes `orientation|keyboardHidden|screenSize` so Godot handles those itself.

### `android/build-android.sh`

Calls `$GODOT4 --headless --export-release "Android"`, then runs `./gradlew assembleRelease`, signs with `jarsigner`, and aligns with `zipalign`. Output: `dist/android/CorditeWars_X.Y.Z.apk`.

---

## `ios/`

### `ios/ios-info.plist`

iOS/iPadOS app bundle metadata. Key entries:

| Key | Value |
|-----|-------|
| `CFBundleIdentifier` | `com.koshkikode.corditewars` |
| `MinimumOSVersion` | `16.0` (iOS 16+) |
| `UIRequiredDeviceCapabilities` | `arm64`, `metal` |
| `UISupportedInterfaceOrientations` | Landscape only (RTS game) |
| `NSAllowsLocalNetworking` | `true` (LAN multiplayer) |

This file is merged into the Godot-generated Xcode project's `Info.plist` before signing.

### `ios/ios-export-options.plist`

Xcode export options file used with `xcodebuild -exportArchive`:

```bash
xcodebuild -exportArchive \
  -archivePath CorditeWars.xcarchive \
  -exportPath dist/ios \
  -exportOptionsPlist versions/ios/ios-export-options.plist
```

Supports `development`, `ad-hoc`, and `app-store` distribution methods. Set `signingStyle` to `automatic` for Xcode-managed signing, or `manual` with explicit provisioning profile UUIDs.

---

### `shared/export_presets.cfg`

Godot export presets file. Contains preset definitions for:
1. Windows Desktop (x86_64, embedded PCK)
2. Linux/X11 (x86_64, embedded PCK)
3. macOS (Universal Binary: x86_64 + arm64)
4. Android (arm64-v8a, Gradle build, minSdk 24)
5. iOS (arm64, Xcode project handoff)

### `shared/version.json`

Machine-readable version metadata:

```json
{
  "major": 0,
  "minor": 1,
  "patch": 0,
  "protocol": 1,
  "build": "alpha"
}
```

| Field | Meaning |
|-------|---------|
| `major` | Breaking change — incompatible multiplayer across different values |
| `minor` | New content — compatible if `major` matches |
| `patch` | Bug fixes — always compatible |
| `protocol` | Network packet format version — must match for multiplayer |
| `build` | Pre-release label (alpha, beta, rc1, stable, etc.) |

The game reads this file at startup to populate the version display in the main menu and the network handshake packet.

---

## Regenerating Files

If version numbers, bundle identifiers, or platform targets change:

```bash
cd /path/to/project
python3 bump-version.py [major|minor|patch]
```

The script will print a summary of all files written.

Do **not** hardcode version strings in these files. Always use `bump-version.py` to keep all platform files in sync.
