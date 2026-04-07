# Deployment Guide: Cordite Wars: Six Fronts

This guide covers deploying your game to all major platforms and distribution channels.

## Table of Contents

- [Windows](#windows)
- [Linux](#linux)
- [macOS](#macos)
- [Android](#android)
- [iOS](#ios)

---

## Windows

### Direct Distribution

1. **Download from GitHub Releases**
   - `.exe` (portable executable)
   - `.msi` (Windows installer)

2. **Install via MSI**
   ```powershell
   # Admin required
   msiexec /i CorditeWars-Windows.msi
   ```

3. **Launch**
   - Via Start Menu (after MSI install)
   - Or run `CorditeWars.exe` directly

### Steam Distribution

#### Prerequisites

1. **Steamworks account** — create at https://partner.steamgames.com/ ($100 one-time fee)
2. **App ID** — assigned by Valve after app creation; replace `YOUR_STEAM_APP_ID` in
   `steam/app-build.vdf` and the `steam_appid.txt` file in the repo root.
3. **Depot IDs** — Valve assigns one depot per platform; update
   `steam/windows-depot.vdf`, `steam/linux-depot.vdf`, `steam/macos-depot.vdf`.

#### Automated CI/CD Upload

The `.github/workflows/steam-deploy.yml` workflow automatically uploads to Steam
whenever a versioned tag is pushed (`v*`).

Required GitHub Secrets:

| Secret | Description |
|--------|-------------|
| `STEAM_APP_ID` | Numeric Steam App ID (e.g. `2345678`) |
| `STEAM_WINDOWS_DEPOT_ID` | Windows depot ID |
| `STEAM_LINUX_DEPOT_ID` | Linux depot ID |
| `STEAM_MACOS_DEPOT_ID` | macOS depot ID |
| `STEAM_USERNAME` | Steamworks builder account username |
| `STEAM_PASSWORD` | Steamworks builder account password |
| `STEAM_TOTP_SECRET` | Steam Guard shared secret (base32) — optional but recommended |

To trigger manually: **Actions → Deploy to Steam → Run workflow**.

#### Manual Upload via SteamCMD

```bash
# Install SteamCMD (Linux/macOS)
mkdir ~/steamcmd && cd ~/steamcmd
curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz

# Patch the VDF files with your real IDs first, then:
~/steamcmd/steamcmd.sh \
  +login YOUR_USERNAME YOUR_PASSWORD \
  +run_app_build "$(pwd)/steam/app-build.vdf" \
  +quit
```

#### Promoting a Build to Default Branch

After upload, set the build live in the Steamworks dashboard:
**App Admin → SteamPipe → Builds → Set build live on branch: default**

Or use SteamCMD directly:
```bash
~/steamcmd/steamcmd.sh \
  +login YOUR_USERNAME YOUR_PASSWORD \
  +app_set_build_live YOUR_APP_ID BUILD_ID default \
  +quit
```

#### Steamworks Integration in Code

`SteamManager` (`src/Systems/Platform/SteamManager.cs`) wraps the Steamworks API:

- **Achievements** — defined in `data/achievements.json`; call
  `SteamManager.Instance?.UnlockAchievement("ACHIEVEMENT_ID")`.
- **Rich Presence** — set automatically on match start/end via `OnMatchStarted()` /
  `OnMatchWon()` / `OnMatchLost()`.
- **Cloud Saves** — call `SteamManager.Instance?.NotifySaveChanged()` after every save.
- **Overlay** — enabled automatically by `SteamAPI.Init()` in `TryInitSteam()`.

To activate real Steamworks calls, add [Steamworks.NET](https://steamworks.github.io/)
via NuGet and replace the stub bodies in the `// Native shim layer` section of
`SteamManager.cs`.

### Microsoft Store

1. **Register app** in Partner Center
   - Requires developer account ($19)
   - Configure pricing, metadata

2. **Prepare package**
   - Use `.msix` format (upgrade from `.msi`)
   - Sign certificate (included with Partner Center or self-signed for testing)

3. **Upload**
   - Via Partner Center dashboard
   - Certification takes 24–48 hours

### Epic Games Store

1. **Register** as publisher (revenue share: 88/12)
2. **Complete publishing agreement**
3. **Submit via Publishing Tools dashboard**
   - Binaries (.exe), metadata, screenshots
   - Review typically 1–2 weeks

---

## Linux

### Direct Distribution

1. **Portable Binary**
   - Download `CorditeWars` from GitHub Releases
   - Make executable:
     ```bash
     chmod +x CorditeWars
     ./CorditeWars
     ```

2. **Snap Store**
   - Install: `snap install corditewars` (once approved)
   - Launch: `corditewars`

### Publishing to Snap Store

1. **Create snapcraft account** at https://snapcraft.io

2. **Upload snap**
   ```bash
   snapcraft upload --release=stable CorditeWars_*.snap
   ```

3. **Review process** (~1 day for stable channel)

4. **Configure channels**
   - `stable`: Production release
   - `candidate`: Pre-release testing
   - `beta`: Early access
   - `edge`: Nightly builds

### Steam Distribution

Same as Windows (see [Steam Distribution](#steam-distribution) above).

### Flathub (Optional)

1. **Submit PR** to https://github.com/flathub/flathub with `.flatpak-info` and manifest
2. **Review & merge** (1–2 weeks)
3. **Install**: `flatpak install flathub com.koshkikode.corditewars`

---

## macOS

### Direct Distribution

1. **Download DMG**
   - GitHub Releases provides `.dmg`

2. **Install**
   ```bash
   # Mount and install
   hdiutil attach CorditeWars-macOS.dmg
   cp -r /Volumes/CorditeWars/CorditeWars.app /Applications/
   hdiutil detach /Volumes/CorditeWars
   ```

3. **First Launch**
   - Right-click → Open (bypass Gatekeeper for unsigned apps)
   - Allow in Security & Privacy settings

### Code Signing & Notarization

To distribute outside App Store with Gatekeeper acceptance:

1. **Obtain Developer Certificate**
   - Apple Developer account ($99/year)
   - Request in Xcode → Preferences → Accounts

2. **Sign locally** (CI does this)
   ```bash
   codesign -s "Developer ID Application" \
     --options runtime \
     --entitlements entitlements.plist \
     /path/to/CorditeWars.app
   ```

3. **Notarize**
   ```bash
   xcrun notarytool submit CorditeWars-macOS.dmg \
     --apple-id <apple-id> \
     --password <app-password> \
     --team-id <team-id>
   ```

4. **Wait for approval** (~5–10 minutes)

5. **Staple ticket**
   ```bash
   xcrun stapler staple /Applications/CorditeWars.app
   ```

### Mac App Store

1. **Set up in App Store Connect**
   - Configure app metadata, pricing, screenshots

2. **Update build with App Store settings**
   - Set Bundle ID to `com.koshkikode.corditewars`
   - Ensure minimum macOS version (currently 10.14)

3. **Create app submission**
   - Via Xcode Cloud or manual upload
   - Review typically 24–48 hours

---

## Android

### Direct Distribution

1. **Download APK** from GitHub Releases

2. **Install**
   - Enable "Unknown Sources" in Settings
   - Open file → Install

### Google Play Store

1. **Create Google Play Developer Account**
   - One-time fee: $25
   - Payment method required

2. **Set up app in Play Console**
   - Configure store listing (title, description, screenshots, icon)
   - Set content rating (questionnaire)
   - Configure pricing & distribution

3. **Upload APK**
   - Use signed release APK (CI handles this)
   - Upload to internal testing track first

4. **Review process**
   - Automated review: ~30 minutes
   - Manual review: 24–48 hours

5. **Roll out**
   - Start with 5% of users
   - Monitor crash rates
   - Gradually increase to 100%

### Samsung Galaxy Store

1. **Register** for seller account (free)

2. **Submit app**
   - Via Seller Portal
   - APK upload, metadata, binary analysis

3. **Approval** typically 24 hours

### Amazon Appstore

1. **Register** Amazon Developer Account

2. **Submit APK** via Developer Console

3. **Approval** typically 24–48 hours

### APK Signing (CI Automatic)

The GitHub Actions workflow automatically signs APK if `ANDROID_RELEASE_KEYSTORE_B64` secret is provided:

```bash
# To generate keystore locally (one-time):
keytool -genkey -v -keystore release-key.keystore \
  -keyalg RSA -keysize 2048 -validity 10000 \
  -alias cordite-release

# Encode for GitHub Actions secret:
base64 release-key.keystore
```

---

## iOS

### Overview

iOS exports are **Xcode project handoffs**—not compiled binaries. You must:

1. Download from GitHub Releases
2. Open in Xcode on macOS
3. Configure signing & provisioning
4. Build & submit yourself

### Preparation

1. **Apple Developer Account** ($99/year)

2. **Create Certificates**
   - In Xcode: Preferences → Accounts → Manage Certificates
   - Request "iOS Development" and "iOS Distribution" certificates

3. **Create App ID**
   - App Store Connect → Identifiers
   - Set Bundle ID to `com.koshkikode.corditewars`

4. **Create Provisioning Profiles**
   - Development profile (for testing on devices)
   - Distribution profile (for App Store)

### Building for App Store

1. **Open Xcode Project**
   - Extract GitHub Releases `.zip`
   - Open `.xcodeproj`

2. **Configure Signing**
   - Select Project → Targets
   - General → Signing & Capabilities
   - Team: Your Apple Developer team
   - Provisioning Profile: Auto-selected

3. **Build**
   ```bash
   xcodebuild -scheme CorditeWars -configuration Release \
     -archivePath build/CorditeWars.xcarchive archive
   ```

4. **Export for App Store**
   - Xcode → Window → Organizer
   - Select archive → Distribute App
   - Select "App Store Connect" → next → next...
   - Choose "Automatically manage signing"

5. **Upload to App Store Connect**
   - Or use Transporter app to upload `.ipa`

6. **Fill App Store metadata**
   - Screenshots, description, release notes
   - Content rating, pricing, availability

7. **Submit for Review**
   - Review typically 24–48 hours

### TestFlight (Beta Testing)

Before production release:

1. **Upload to TestFlight**
   - After uploading to App Store Connect
   - Goes to internal testers automatically

2. **Invite External Testers**
   - App Store Connect → TestFlight → External Testers
   - Send invite link
   - Beta review typically 24 hours

3. **Testers Install via TestFlight App**

---

## Version Synchronization

All platforms automatically sync versions via the CI pipeline:

```bash
# Bump version across all platforms
python3 bump-version.py patch

# Commit and tag
git add .
git commit -m "v0.1.1 release"
git tag v0.1.1
git push origin main --tags
```

This triggers:
- Version update in `project.godot`
- Version update in all `export_presets.cfg` files
- Version update in `snapcraft.yaml`
- Version update in macOS `Info.plist`
- Automatic export on all platforms
- GitHub Release creation with artifacts

---

## Verification

Always verify downloads before installation:

```bash
# Check checksums
sha256sum -c checksums.txt

# Verify file sizes are reasonable
ls -lh CorditeWars*
```

---

## Support

For build issues:
- Check [Build Guide](build-guide.md)
- Review GitHub Actions logs: https://github.com/KoshkiKode/the-cordite-wars/actions

For store submission questions:
- Google Play: https://support.google.com/googleplay/android-developer
- Apple App Store: https://developer.apple.com/support/
- Steam: https://partner.steamgames.com/documentation/
- Microsoft: https://partner.microsoft.com/en-us/dashboard/home
- Epic: https://publishing.unrealengine.com/
- Samsung: https://developer.samsung.com/galaxy-store/
- Amazon: https://developer.amazon.com/apps-and-games/amazon-appstore

---

## Troubleshooting

### "File not found" during install
- Re-download from GitHub Releases
- Verify checksum matches `checksums.txt`

### App crashes on startup
- Check minimum OS requirements (see Platform Requirements below)
- Enable detailed logs if available

### Store rejection
- Common reasons: metadata issues, content rating, privacy policy
- Check store-specific guidelines linked above

---

## Platform Requirements

| Platform | Min OS | Arch | Storage | RAM |
|----------|--------|------|---------|-----|
| Windows | 7 SP1 | x86-64 | 500 MB | 4 GB |
| Linux | Ubuntu 20.04 | x86-64 | 500 MB | 4 GB |
| macOS | 10.14 | x86-64 + ARM64 | 500 MB | 4 GB |
| Android | 5.1 (API 22) | ARM64 | 300 MB | 2 GB |
| iOS | 13.0 | ARM64 | 300 MB | 2 GB |
