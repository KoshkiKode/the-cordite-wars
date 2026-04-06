# Cordite Wars: Six Fronts — Build & Export Guide

This document covers everything needed to build, package, and export the game for all supported platforms. It also documents the quality tier system, network protocol versioning, and backwards compatibility design.

---

## Table of Contents

1. [Backwards Compatibility Design](#backwards-compatibility-design)
   - [Quality Tier System](#quality-tier-system)
   - [Simulation vs Rendering Split](#simulation-vs-rendering-split)
2. [Network Protocol Versioning](#network-protocol-versioning)
   - [Version Number Format](#version-number-format)
   - [Packet Header Layout](#packet-header-layout)
   - [Compatibility Matrix](#compatibility-matrix)
3. [Build Prerequisites](#build-prerequisites)
4. [Windows Build (MSI/EXE)](#windows-build-msiexe)
   - [Export Configuration](#windows-export-configuration)
   - [Inno Setup Installer Script](#inno-setup-installer-script)
   - [Build Steps](#windows-build-steps)
5. [Linux Build (Snap)](#linux-build-snap)
   - [Export Configuration](#linux-export-configuration)
   - [snapcraft.yaml](#snapcraftyaml)
   - [Build Steps](#linux-build-steps)
6. [macOS Build (Universal Binary)](#macos-build-universal-binary)
   - [Export Configuration](#macos-export-configuration)
   - [Xcode Project Setup](#xcode-project-setup)
   - [Code Signing & Notarization](#code-signing--notarization)
   - [Build Steps](#macos-build-steps)
7. [Android Build](#android-build)
   - [Export Configuration](#android-export-configuration)
   - [Gradle Configuration](#gradle-configuration)
   - [Android Quality Tier Detection](#android-quality-tier-detection)
   - [Touch Input Mapping](#touch-input-mapping)
   - [Build Steps](#android-build-steps)
8. [Automated Builds (CI/CD)](#automated-builds-cicd)
9. [Troubleshooting](#troubleshooting)

---

## Backwards Compatibility Design

### Quality Tier System

The game uses a four-tier quality system to target hardware ranging from 2015-era budget devices through to modern high-end gaming PCs. **Quality tier is a client-side rendering concern only** — it has no effect on simulation logic, game balance, or multiplayer compatibility.

#### Tier 0 — Potato Mode (Minimum Specification)

**Target hardware:** 2015-era integrated graphics (Intel HD 4000–5500, AMD Radeon R3/R5), budget Android phones (Snapdragon 400–600 series), 2 GB VRAM, 4 GB system RAM.

| Setting           | Value                                         |
|-------------------|-----------------------------------------------|
| Renderer          | Compatibility (OpenGL ES 3.0 / WebGL2)        |
| Shadows           | **OFF**                                       |
| Anti-aliasing     | **OFF**                                       |
| Texture max size  | 512 × 512                                     |
| Model LOD         | Lowest (simplified meshes, ≤ 500 triangles)   |
| Particles         | Minimal — count reduced by **75%**            |
| Post-processing   | **NONE** (no SSAO, no bloom, no tone mapping) |
| Draw distance     | **60%** of baseline                           |
| Anisotropic       | Off (bilinear only)                           |
| UI scale          | Auto — DPI-aware                              |
| Target FPS        | 30                                            |

This mode is the baseline for Android packaging. The Android exporter forces Tier 0 unless the device is detected as Tier 1/2 capable (see [Android Quality Tier Detection](#android-quality-tier-detection)).

---

#### Tier 1 — Low

**Target hardware:** 2018-era entry discrete GPU (NVIDIA GTX 1050, AMD RX 550), 4 GB VRAM, 8 GB system RAM.

| Setting           | Value                                              |
|-------------------|----------------------------------------------------|
| Renderer          | Compatibility (OpenGL ES 3.0)                      |
| Shadows           | Low — 1024 px atlas, directional light only        |
| Anti-aliasing     | FXAA                                               |
| Texture max size  | 1024 × 1024                                        |
| Model LOD         | Low (reduced polygon budget)                       |
| Particles         | Reduced — count at **50%**                         |
| Post-processing   | Basic — tone mapping only (Filmic)                 |
| Draw distance     | **80%** of baseline                                |
| Anisotropic       | Off                                                |
| Target FPS        | 30–60                                              |

---

#### Tier 2 — Medium (Default)

**Target hardware:** 2020-era mid-range GPU (NVIDIA RTX 3060, AMD RX 6600), 6 GB VRAM, 16 GB system RAM.

This is the default for new installations on any detected discrete GPU.

| Setting           | Value                                                       |
|-------------------|-------------------------------------------------------------|
| Renderer          | Forward+ (Godot default)                                    |
| Shadows           | Medium — 2048 px atlas, directional + up to 2 omni lights  |
| Anti-aliasing     | MSAA 2×                                                     |
| Texture max size  | Full (2048 × 2048 or as authored)                           |
| Model LOD         | Medium                                                      |
| Particles         | Full — **100%** emission                                    |
| Post-processing   | Full — SSAO, bloom (Filmic tone mapping)                    |
| Draw distance     | **100%** (baseline)                                         |
| Anisotropic       | 4× filtering                                                |
| Target FPS        | 60                                                          |

---

#### Tier 3 — High

**Target hardware:** 2022+ high-end GPU (NVIDIA RTX 4070/4080, AMD RX 7800/7900), 8 GB+ VRAM, 32 GB system RAM.

| Setting           | Value                                                              |
|-------------------|--------------------------------------------------------------------|
| Renderer          | Forward+                                                           |
| Shadows           | High — 4096 px atlas, all light types                              |
| Anti-aliasing     | MSAA 4×                                                            |
| Texture max size  | Full + anisotropic 16×                                             |
| Model LOD         | Full detail (maximum polygon budget)                               |
| Particles         | Full + extra VFX layers (shell casings, debris micro-particles)    |
| Post-processing   | Full — SSAO, SSR, bloom, volumetric fog (Filmic tone mapping)      |
| Draw distance     | **120%** extended (long-range unit visibility)                     |
| Anisotropic       | 16× filtering                                                      |
| Target FPS        | 60+ (unlocked above 60 if capable)                                 |

---

### Simulation vs Rendering Split

**This is the single most important compatibility rule:**

> The simulation layer **must never** read rendering state.

The game is structured around a hard boundary:

```
┌─────────────────────────────────────────────────────────────────┐
│  SIMULATION (deterministic, FixedPoint arithmetic, same on all  │
│  machines and quality tiers)                                     │
│                                                                  │
│  - Unit positions, HP, attack cooldowns                          │
│  - Economy (cordite counts, build queues)                        │
│  - Pathfinding results                                           │
│  - Combat resolution (damage, death)                             │
│  - Tech tree state                                               │
│  - Network tick synchronisation                                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ EventBus (read-only data transfer)
┌──────────────────────────▼──────────────────────────────────────┐
│  RENDERING / AUDIO (non-deterministic, float math OK)           │
│                                                                  │
│  - QualityManager tier selection                                 │
│  - Particle systems, LOD, shadows                                │
│  - AudioManager (SoundRegistry lookups, AudioStreamPlayer)      │
│  - UI animations, health bar interpolation                       │
│  - Camera, Fog of War visual layer                               │
└─────────────────────────────────────────────────────────────────┘
```

`float` is acceptable in `QualityManager`, `SoundRegistry`, all `AudioManager` code, all `MeshInstance3D` / shader code, and UI. Never in unit stats, combat formulas, or position storage.

---

## Network Protocol Versioning

### Version Number Format

Version strings follow Semantic Versioning (`MAJOR.MINOR.PATCH`):

```
0.1.0-alpha
│ │ │  └── pre-release tag (alpha, beta, rc1, etc.)
│ │ └──── PATCH: bug fixes, balance tweaks — always compatible
│ └───── MINOR: new units, buildings, map content — compatible within same MAJOR
└─────── MAJOR: breaking changes (simulation rule changes, save format) — incompatible
```

The `protocol` field in `versions/shared/version.json` is an independent integer that increments whenever the **network packet format** changes. It is possible for the protocol version to advance without the game version advancing (e.g. a hotfix that changes packet encoding).

### Packet Header Layout

Every network packet begins with a fixed-size header:

```
Offset  Size  Field
──────────────────────────────────────────────────────────────
0       4     Magic bytes: 0x43 0x57 0x53 0x46  ("CWSF")
4       2     Protocol version (uint16, big-endian)
6       2     Packet type ID (uint16)
8       4     Sender peer ID (uint32)
12      4     Simulation tick (uint32) — ignored for lobby packets
16      2     Payload length in bytes (uint16)
18      2     CRC-16 of the full packet including header (set to 0 during calculation)
──────────────────────────────────────────────────────────────
Total:  20 bytes fixed header, followed by `PayloadLength` bytes of payload
```

On connection handshake, both peers exchange their protocol version. A mismatch causes an immediate disconnect with reason code `PROTOCOL_MISMATCH`. The server logs both version numbers for diagnostics.

```
Handshake sequence:
  Client → Server: HELLO { protocol: 3, game_version: "0.2.0", player_name: "..." }
  Server → Client: HELLO_ACK { protocol: 3 } | REJECT { reason: PROTOCOL_MISMATCH, server_protocol: 4 }
```

### Compatibility Matrix

| Scenario                               | Multiplayer Compatible? | Notes                                          |
|----------------------------------------|-------------------------|------------------------------------------------|
| Same version (0.2.1 vs 0.2.1)         | ✅ Yes                  | Fully compatible                               |
| Same MAJOR+MINOR, different PATCH      | ✅ Yes                  | Patches are balance/bugfix only                |
| Same MAJOR, different MINOR            | ✅ Yes                  | If protocol version matches                    |
| Different MAJOR                        | ❌ No                   | Breaking simulation changes; auto-rejected     |
| Different quality tier (any)           | ✅ Yes                  | Tier is local-only; no simulation impact       |
| Different OS/platform (same version)   | ✅ Yes                  | Cross-platform multiplayer is fully supported  |
| Protocol mismatch (even same semver)   | ❌ No                   | Rare — only if hotfix changed packet format    |

**Version X (MAJOR):** Incompatible. A MAJOR bump means simulation rules changed — unit stats use different fixed-point representation, or a new core mechanic was added that older clients cannot simulate. Connection is rejected at handshake.

**Version Y (MINOR):** Compatible if MAJOR matches. A MINOR bump adds new content (units, buildings, maps). Clients without the new content simply cannot pick those units/maps in-lobby — they can still play on maps/factions they both have. Protocol version must also match.

**Version Z (PATCH):** Always compatible. Patches fix bugs and adjust balance values that must be identical across the session — all connected peers receive patch updates simultaneously via Steam or itch.io.

---

## Build Prerequisites

All platforms require:

- **Godot 4.6** with C# / .NET support (`godot4` available on PATH, or `GODOT4` env var set)
- **.NET 9 SDK** (`dotnet --version` ≥ 9.0)
- **Godot export templates** installed for each target platform (Editor → AssetLib → Export Templates, or download from `godotengine.org/download`)

Platform-specific:

| Platform | Additional Requirements |
|----------|-------------------------|
| Windows  | [Inno Setup 6](https://jrsoftware.org/isdl.php) (`iscc` on PATH), Windows 10+ build host (or Wine for cross-compile) |
| Linux    | [Snapcraft](https://snapcraft.io/docs/snapcraft-overview) (`snapcraft` CLI), LXD or Multipass for snap builds |
| macOS    | Xcode 14+, Apple Developer Program account (for notarization), `codesign` and `xcrun altool` / `notarytool` |
| Android  | Android Studio (Hedgehog or newer), Android NDK r23c, Java 17, `ANDROID_HOME` env var set, Godot Android build template installed |

### CI Signing Secrets Checklist (GitHub Actions)

Configure these repository secrets before release packaging:

| Secret | Used for |
|--------|----------|
| `ANDROID_RELEASE_KEYSTORE_B64` | Base64-encoded Android release keystore (`.jks` / `.keystore`) |
| `ANDROID_RELEASE_KEYSTORE_ALIAS` | Android release key alias |
| `ANDROID_RELEASE_KEYSTORE_PASSWORD` | Android keystore password |
| `ANDROID_RELEASE_KEY_PASSWORD` | Android key password |
| `APPLE_DEV_ID` | `Developer ID Application: Name (TEAMID)` signing identity |
| `APPLE_TEAM_ID` | Apple 10-character team identifier |
| `APPLE_ID` | Apple ID email for notarization |
| `AC_PASSWORD` | App-specific password for notarization |

Notes:
- The CI iOS/iPadOS **handoff export** (Xcode project files only) does not require Apple signing secrets.
- Apple signing secrets are required when you move from handoff files to signed/notarized distributables on your Mac build host.

---

## Windows Build (MSI/EXE)

### Windows Export Configuration

In `versions/shared/export_presets.cfg` the Windows preset includes:

```ini
[preset.0]
name="Windows Desktop"
platform="Windows Desktop"
runnable=true
export_filter="all_resources"
include_filter=""
exclude_filter=""

[preset.0.options]
custom_template/debug=""
custom_template/release=""
binary_format/64_bits=true
binary_format/embed_pck=true
texture_format/s3tc=true
texture_format/etc=false
texture_format/etc2=false
binary_format/architecture="x86_64"
```

### Inno Setup Installer Script

The `versions/windows/inno-setup.iss` file produces `CorditeWars_Setup.exe`. Key sections:

```iss
[Setup]
AppId={{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
AppName=Cordite Wars: Six Fronts
AppVersion=0.1.0
DefaultDirName={autopf}\Cordite Wars Six Fronts
DefaultGroupName=Cordite Wars Six Fronts
OutputBaseFilename=CorditeWars_Setup
SetupIconFile=assets\icons\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
MinVersion=10.0.17763

[Files]
Source: "build\windows\CorditeWars.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "build\windows\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs
Source: "build\windows\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Cordite Wars: Six Fronts"; Filename: "{app}\CorditeWars.exe"
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Cordite Wars: Six Fronts"; Filename: "{app}\CorditeWars.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons"

[Run]
Filename: "{app}\CorditeWars.exe"; Description: "Launch Cordite Wars: Six Fronts"; Flags: nowait postinstall skipifsilent
```

**.NET 9 Runtime Check:** The installer checks for the .NET 9 Desktop Runtime. If absent, it offers to download it:

```iss
[Code]
function IsDotNet9Installed: Boolean;
var
  Key: String;
begin
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App';
  Result := RegKeyExists(HKLM, Key);
end;

procedure InitializeWizard;
begin
  if not IsDotNet9Installed then
    MsgBox('The .NET 9 Runtime is required. It will be downloaded during installation.', mbInformation, MB_OK);
end;
```

### Windows Build Steps

```bash
# In versions/windows/build-windows.sh:
# 1. Export from Godot headlessly
$GODOT4 --headless --export-release "Windows Desktop" build/windows/CorditeWars.exe

# 2. Compile the Inno Setup installer
iscc versions/windows/inno-setup.iss /O"dist/windows"

# Output: dist/windows/CorditeWars_Setup.exe
```

---

## Linux Build (Snap)

### Linux Export Configuration

```ini
[preset.1]
name="Linux/X11"
platform="Linux/X11"
runnable=true
binary_format/64_bits=true
binary_format/architecture="x86_64"
binary_format/embed_pck=true
```

### snapcraft.yaml

```yaml
name: cordite-wars
base: core22
version: '0.1.0'
summary: Cordite Wars — Six Fronts RTS
description: |
  A cross-platform real-time strategy game inspired by Command & Conquer
  and Tempest Rising. Command six distinct factions across land, air, and sea.
grade: devel
confinement: strict

architectures:
  - build-on: amd64

apps:
  cordite-wars:
    command: bin/CorditeWars
    extensions: [gnome]
    plugs:
      - audio-playback
      - opengl
      - x11
      - wayland
      - network
      - network-bind
      - home

parts:
  game:
    plugin: dump
    source: build/linux/
    organize:
      CorditeWars: bin/CorditeWars
    stage-packages:
      - libgles2
      - libopenal1
```

### Linux Desktop Entry

`versions/linux/cordite-wars.desktop`:

```ini
[Desktop Entry]
Name=Cordite Wars: Six Fronts
Exec=cordite-wars
Icon=/snap/cordite-wars/current/assets/icons/icon.png
Type=Application
Categories=Game;StrategyGame;
StartupNotify=true
```

### Linux Build Steps

```bash
# In versions/linux/build-linux.sh:
$GODOT4 --headless --export-release "Linux/X11" build/linux/CorditeWars
chmod +x build/linux/CorditeWars
snapcraft --destructive-mode
# Output: cordite-wars_0.1.0_amd64.snap
```

---

## macOS Build (Universal Binary)

### macOS Export Configuration

Godot 4.6 produces a `.app` bundle. For App Store or notarization, it must be a Universal Binary (x86_64 + ARM64).

```ini
[preset.2]
name="macOS"
platform="macOS"
binary_format/architecture="universal"
codesign/enable=true
codesign/identity=""
codesign/entitlements_custom_file="versions/macos/entitlements.plist"
notarization/enable=true
notarization/apple_id_name=""
notarization/apple_id_password=""
notarization/api_uuid=""
notarization/api_key=""
```

### Xcode Project Setup

The `.app` bundle is embedded in an Xcode project for submission to the Mac App Store or for ad-hoc distribution. The Xcode project uses:

- **Deployment target:** macOS 12.0
- **Architectures:** arm64 + x86_64 (Universal)
- **Code sign style:** Manual (use your Developer ID Application certificate)
- **Hardened runtime:** Enabled (required for notarization)
- **Bundle identifier:** `com.corditewars.sixfronts`

`versions/macos/Info.plist` keys:

| Key | Value |
|-----|-------|
| `CFBundleIdentifier` | `com.corditewars.sixfronts` |
| `CFBundleName` | Cordite Wars: Six Fronts |
| `CFBundleShortVersionString` | `0.1.0` |
| `CFBundleVersion` | `1` |
| `LSMinimumSystemVersion` | `12.0` |
| `NSHighResolutionCapable` | `true` |
| `NSMicrophoneUsageDescription` | Not applicable (omit) |

### Code Signing & Notarization

#### Step 1 — Export from Godot

```bash
$GODOT4 --headless --export-release "macOS" build/macos/CorditeWars.app
```

#### Step 2 — Sign the app bundle

```bash
codesign --deep --force --options runtime \
  --entitlements versions/macos/entitlements.plist \
  --sign "Developer ID Application: Your Name (TEAMID)" \
  build/macos/CorditeWars.app
```

Verify the signature:

```bash
codesign --verify --deep --strict --verbose=2 build/macos/CorditeWars.app
spctl --assess --type execute -vvv build/macos/CorditeWars.app
```

#### Step 3 — Create DMG for distribution

```bash
hdiutil create -volname "Cordite Wars" -srcfolder build/macos/CorditeWars.app \
  -ov -format UDZO dist/macos/CorditeWars_0.1.0.dmg
codesign --sign "Developer ID Application: Your Name (TEAMID)" \
  dist/macos/CorditeWars_0.1.0.dmg
```

#### Step 4 — Submit for notarization

```bash
xcrun notarytool submit dist/macos/CorditeWars_0.1.0.dmg \
  --apple-id "your@email.com" \
  --team-id "TEAMID" \
  --password "@keychain:AC_PASSWORD" \
  --wait
```

#### Step 5 — Staple the notarization ticket

```bash
xcrun stapler staple dist/macos/CorditeWars_0.1.0.dmg
xcrun stapler validate dist/macos/CorditeWars_0.1.0.dmg
```

After stapling, the DMG can be distributed without requiring an internet connection for Gatekeeper to validate it.

#### Entitlements (entitlements.plist)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.network.client</key><true/>
  <key>com.apple.security.network.server</key><true/>
</dict>
</plist>
```

### macOS Build Steps

Full sequence is in `versions/macos/build-macos.sh`. See [Code Signing & Notarization](#code-signing--notarization) for the detailed sub-steps.

---

## Android Build

### Android Export Configuration

```ini
[preset.3]
name="Android"
platform="Android"
runnable=true
custom_template/debug=""
custom_template/release=""
gradle_build/use_gradle_build=true
gradle_build/min_sdk=24
gradle_build/target_sdk=34
package/unique_name="com.corditewars.sixfronts"
package/name="Cordite Wars"
package/signed=true
architectures/armeabi-v7a=false
architectures/arm64-v8a=true
architectures/x86=false
architectures/x86_64=false
```

### Gradle Configuration

`versions/android/build.gradle`:

```groovy
android {
    compileSdk 34
    defaultConfig {
        applicationId "com.corditewars.sixfronts"
        minSdk 24          // Android 7.0 Nougat
        targetSdk 34       // Android 14
        versionCode 1
        versionName "0.1.0"
        ndk {
            abiFilters "arm64-v8a"
        }
    }
    buildTypes {
        release {
            minifyEnabled false
            signingConfig signingConfigs.release
        }
    }
}
dependencies {
    implementation 'androidx.annotation:annotation:1.7.0'
}
```

### AndroidManifest.xml Key Elements

```xml
<uses-permission android:name="android.permission.INTERNET"/>
<uses-permission android:name="android.permission.ACCESS_WIFI_STATE"/>
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE"/>
<uses-permission android:name="android.permission.VIBRATE"/>

<activity
    android:screenOrientation="landscape"
    android:configChanges="orientation|keyboardHidden|screenSize">
</activity>
```

### Android Quality Tier Detection

On Android, `QualityManager.AutoDetect()` follows this decision tree:

```
Device booted
    │
    ├─ Compatibility renderer (no Vulkan) → Tier 0 (Potato)
    │
    └─ Vulkan available
        │
        ├─ GPU name contains "Adreno 7xx" or "Immortalis" → Tier 1 (Low)
        │
        ├─ GPU name contains "Adreno 6xx" or "Mali-G78/G710" → Tier 0–1
        │
        └─ All others → Tier 0 (Potato)
```

The settings menu always allows manual override, and the selected tier is persisted to `user://settings.cfg`.

**Performance guardrails on Android:** If the running average FPS drops below 25 for more than 5 continuous seconds, the game automatically offers to drop one tier. This is implemented in `PerformanceMonitor.cs` (see `src/Systems/Graphics/`).

### Touch Input Mapping

Android touch controls map to in-game actions via the `TouchInputHandler.cs` system:

| Gesture | Action |
|---------|--------|
| Single tap | Select unit / building |
| Long press (0.5s) | Context menu |
| Drag (no unit selected) | Camera pan |
| Drag (unit selected) | Move order |
| Two-finger pinch | Camera zoom |
| Two-finger rotate | Camera rotate |
| Double tap on unit | Select all of same type on screen |
| Swipe up on minimap | Jump to location |

The on-screen HUD on Android shows a compact version of the build panel (scrollable) and action buttons scaled for touch (minimum 48 × 48 dp tap targets per Android accessibility guidelines).

### Android Build Steps

```bash
# In versions/android/build-android.sh:

# 1. Godot exports the Gradle project
$GODOT4 --headless --export-release "Android" build/android/CorditeWars.apk

# 2. If using Gradle build (recommended for Play Store):
#    Godot populates the android/ directory with the Gradle project.
cd build/android/
./gradlew assembleRelease

# 3. Sign the APK
jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 \
  -keystore keystore.jks \
  -storepass $KEYSTORE_PASS \
  app/build/outputs/apk/release/app-release-unsigned.apk cordite-wars-key

zipalign -v 4 \
  app/build/outputs/apk/release/app-release-unsigned.apk \
  dist/android/CorditeWars_0.1.0.apk
```

---

## Automated Builds (CI/CD)

The project uses GitHub Actions. Three workflows are defined:

### `build-windows.yml`

Triggers on push to `main` and version tags (`v*.*.*`). Runs on `windows-latest`. Steps:
1. Checkout
2. Install .NET 9 SDK
3. Install Godot 4.6 + export templates (cached)
4. `godot4 --headless --export-release "Windows Desktop" ...`
5. Inno Setup compile
6. Upload `CorditeWars_Setup.exe` as artifact

### `build-linux.yml`

Triggers on push to `main` and version tags. Runs on `ubuntu-22.04`. Steps:
1. Checkout
2. Install .NET 9 SDK
3. Install Godot 4.6 + export templates
4. Export Linux binary
5. `snapcraft --destructive-mode`
6. Upload `.snap` as artifact

### `build-android.yml`

Triggers on version tags only (APK uploads to Play Store are manual). Runs on `ubuntu-22.04`. Steps:
1. Checkout
2. Setup Java 17, Android SDK
3. Install Godot 4.6 + export templates + Android build template
4. Export and sign APK
5. Upload signed APK as artifact

---

## Troubleshooting

### "Missing export templates" on Godot 4.6

Download templates from `https://godotengine.org/download` and install via Editor → Manage Export Templates, or place the `.tpz` file in `$GODOT_DATA_DIR/export_templates/4.6.stable/`.

### Windows build: DLL not found at runtime

Ensure `--embed-pck` is enabled in export presets. The C# .NET runtime DLLs must be present alongside the executable. Use the Inno Setup `[Files]` section with `recursesubdirs` to pick up all `*.dll` files from the build directory.

### macOS: "App is damaged and can't be opened"

This means the app was not notarized or was quarantined by Gatekeeper. Users who download a notarized DMG should never see this. For development builds, the developer can bypass with:
```bash
xattr -cr /path/to/CorditeWars.app
```

### Android: Crash on launch (low-end device)

Check `adb logcat -s Godot` for the crash log. Most commonly caused by:
- Out of memory: reduce texture sizes in export presets
- OpenGL ES 3.0 not available on extremely old devices (pre-2016): these are below minimum spec

### Linux: Snap fails with "confinement not available"

Run `snap install --dangerous cordite-wars_*.snap` for development. Production builds use `grade: stable` and are submitted to the Snap Store for review.

### Android: Black screen after splash

Usually caused by a missing `.NET 9` runtime (the Android export template must have Mono/C# support compiled in). Ensure you are using Godot 4.6 with the Mono build, and that the Android build template was installed correctly:

```bash
godot4 --install-android-build-template
```

---

*This document is maintained alongside the project source. Update it whenever export settings, platform targets, or build steps change.*
