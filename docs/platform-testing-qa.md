# Platform Testing & QA Guide

This guide outlines testing procedures and known issues per platform for Cordite Wars: Six Fronts.

## Table of Contents

- [Windows](#windows)
- [Linux](#linux)
- [macOS](#macos)
- [Android](#android)
- [iOS](#ios)
- [Performance Targets](#performance-targets)
- [Known Issues](#known-issues)

---

## Windows

### Minimum Requirements

- **OS**: Windows 7 SP1 or later (64-bit)
- **Processor**: Intel i5 2nd Gen or equivalent
- **RAM**: 4 GB minimum, 8 GB recommended
- **Storage**: 500 MB free
- **Graphics**: GPU with OpenGL 4.1+ support (Intel HD Graphics 3000+, NVIDIA GTX 400+, AMD Radeon HD 5000+)
- **Audio**: DirectSound compatible

### Test Checklist

- [ ] Game launches from Start Menu after MSI install
- [ ] Game launches from portable `.exe`
- [ ] Display resolution detection works (runs windowed at 1920x1080)
- [ ] Full-screen mode toggles correctly
- [ ] Audio plays without distortion
- [ ] Controller input recognized (gamepad, keyboard)
- [ ] Saves persist in `%APPDATA%\Cordite Wars/`
- [ ] Uninstall via Programs & Features works cleanly
- [ ] No registry keys remain after uninstall

### Graphics Testing

- **Target FPS**: 60 FPS at 1920x1080 (low-end GPU), 144 FPS (mid-range+)
- Test on minimum spec hardware (Intel HD Graphics 3000 or equivalent)
- Verify no graphical glitches, texture corruption
- Check lighting & shadows render correctly

### Audio Testing

- [ ] Background music plays continuously
- [ ] SFX triggers on UI interactions
- [ ] Volume slider responds (0–100%)
- [ ] Mute toggle works
- [ ] No audio dropouts during gameplay

### Network Testing (if applicable)

- [ ] Connection timeout handled gracefully (shows error, doesn't crash)
- [ ] Reconnect works after network interruption
- [ ] No sensitive data logged to console

---

## Linux

### Minimum Requirements

- **OS**: Ubuntu 20.04 LTS or equivalent (CentOS 7+, Debian 10+)
- **Processor**: Same as Windows
- **RAM**: 4 GB minimum, 8 GB recommended
- **Storage**: 500 MB free
- **Graphics**: OpenGL 4.1+ (Mesa, Intel, NVIDIA drivers required)
- **Snap**: Required for snap distribution (can be installed via: `sudo apt install snapd`)

### Test Checklist

- [ ] Binary launches from terminal: `./CorditeWars`
- [ ] Snap installs: `snap install corditewars`
- [ ] Snap launches: `corditewars`
- [ ] Snap has proper file access (can read game data, write saves)
- [ ] Uninstall doesn't leave orphaned files: `snap remove corditewars`
- [ ] Portable binary works on fresh Ubuntu 20.04 install
- [ ] Window manager shortcuts work (Alt-Tab, Alt-F4, etc.)

### Distribution Testing

**Snap**
- [ ] App appears in Activities menu after install
- [ ] App icon displays correctly
- [ ] Desktop entry works (`/snap/corditewars/current/meta/gui/`)
- [ ] Plug permissions are correct (audio, graphics, network)

### Graphics Testing

- **Target FPS**: 60 FPS at 1920x1080
- Test on AMD Radeon (Mesa), NVIDIA (proprietary driver), Intel
- Verify Vulkan rendering works if available
- Check OpenGL fallback works

### Audio Testing

- [ ] PulseAudio integration works
- [ ] ALSA fallback works if PulseAudio unavailable
- [ ] Volume respects system audio levels
- [ ] No noise or clipping

---

## macOS

### Minimum Requirements

- **OS**: macOS 10.14 (Mojave) or later
- **Processor**: Intel Core 2 Duo or Apple Silicon (M1+)
- **RAM**: 4 GB minimum, 8 GB recommended
- **Storage**: 500 MB free
- **Graphics**: OpenGL 4.1+ (integrated GPU fine)

### Test Checklist

**Intel Mac**
- [ ] DMG mounts without errors
- [ ] App copies to Applications folder
- [ ] App launches (may need "Open" confirmation on first run)
- [ ] Gatekeeper doesn't block (unsigned or properly signed)
- [ ] App appears in Finder → Applications

**Apple Silicon Mac (M1+)**
- [ ] App runs under Rosetta 2 (if x86-64 binary)
- [ ] App runs natively if ARM64 binary available
- [ ] Performance acceptable (no excessive thermal throttling)

### Notarization Testing

- [ ] App passes Apple's malware check
- [ ] macOS doesn't show "unverified developer" warning
- [ ] App can be distributed via web directly (no App Store required)

### Graphics Testing

- **Target FPS**: 60 FPS at 2560x1440 (Retina display)
- Metal rendering preferred (Godot uses Metal on macOS)
- Check high-DPI rendering (text isn't blurry)

### Audio Testing

- [ ] CoreAudio integration works
- [ ] Volume slider affects system volume
- [ ] Headphone jack switching works (audio device switching)

---

## Android

### Minimum Requirements

- **OS**: Android 5.1 (API 22) or later
- **Processor**: ARM64 (armv8) minimum
- **RAM**: 2 GB minimum, 4 GB recommended
- **Storage**: 300 MB free
- **Screen**: 5.5"–6.5" (tested at 1920x1080, 1440x2560)
- **Permission**s: Graphics, Audio, Storage (for saves)

### Test Devices

Test on at least:
- [ ] Low-end (Redmi, Moto G series, ~2 GB RAM)
- [ ] Mid-range (Pixel 4a, Samsung Galaxy A series, ~4 GB RAM)
- [ ] High-end (Pixel 6, Galaxy S21, 8+ GB RAM)

### Test Checklist

- [ ] APK installs from "Unknown Sources"
- [ ] App appears in launcher
- [ ] App launches and shows splash screen
- [ ] Orientation works (portrait, landscape, auto-rotation)
- [ ] Touch input recognized (single-touch, multi-touch)
- [ ] Back button returns to previous menu or exits
- [ ] Home button backgrounds app correctly
- [ ] App resumes from background without crash
- [ ] Saves persist in `/sdcard/Android/data/com.corditewars.sixfronts/files/`
- [ ] Permissions (camera, storage) granted/denied handled
- [ ] No excessive battery drain (max 15% per hour gameplay)
- [ ] Overheating: Device stays below 45°C during gameplay

### Screen Resolution Testing

- [ ] Game renders at native resolution (1080x1920, 1440x2960, etc.)
- [ ] UI scales correctly on different DPI (320 dpi, 400 dpi, 440 dpi+)
- [ ] Text is readable (min 12sp font size)
- [ ] Touch targets are tappable (min 48dp)

### Performance Testing

- **Target FPS**: 60 FPS on mid-range, 30 FPS on low-end
- Monitor via Android Studio Profiler:
  ```bash
  adb shell dumpsys meminfo com.corditewars.sixfronts
  ```
- Check memory leak over 30 min gameplay
- Ensure no excessive GC pauses (frames > 17ms)

### Audio Testing

- [ ] Music plays without cutouts
- [ ] SFX volume independent from ringer
- [ ] Headphone jack switching works
- [ ] Bluetooth headphones connect
- [ ] System notifications don't interrupt audio

### Store-Specific Testing

**Google Play Store**
- [ ] APK meets 64-bit requirement (all Play Store apps must be 64-bit)
- [ ] Targetted API level is current (API 34+ as of 2024)
- [ ] No unsafe permissions (`android.permission.WRITE_EXTERNAL_STORAGE` deprecated for scoped storage)

---

## iOS

### Minimum Requirements

- **OS**: iOS 13.0 or later
- **Device**: iPhone 6s or later (A9 chip+)
- **RAM**: 2 GB minimum, 4 GB recommended
- **Storage**: 300 MB free
- **Screen**: 4.7"–6.7" (tested at 1125x2436, 1242x2688)

### Test Devices

Test on:
- [ ] Minimum spec (iPhone 6s, ~2 GB RAM, iOS 13)
- [ ] Mid-range (iPhone X, ~3 GB RAM, iOS 15+)
- [ ] Latest (iPhone 14+, 6 GB RAM, iOS 16+)

### Test Checklist

- [ ] App installs from Xcode (dev build)
- [ ] App installs from TestFlight
- [ ] App launches and shows splash
- [ ] Orientation works (portrait, landscape, auto-rotation)
- [ ] Touch input recognized (swipe, tap, long-press)
- [ ] Home button returns to home screen correctly
- [ ] App resumes from background
- [ ] App survives memory pressure (low memory warning handled)
- [ ] Saves persist in app sandbox (`~/Documents/` or `~/Library/`)
- [ ] Permissions (camera, microphone, location) handled
- [ ] No excessive battery drain
- [ ] Safe Area respected (notch, Dynamic Island, home indicator)

### Screen Resolution Testing

- **iPhone 12/13 mini** (5.4"): 1080x2340
- **iPhone 12/13/14** (6.1"): 1170x2532
- **iPhone 12/13 Pro Max** (6.7"): 1284x2778
- [ ] Game renders at native resolution
- [ ] Safe Area insets respected (don't hide UI behind notch/home indicator)
- [ ] UI scales correctly across device sizes

### Performance Testing

- **Target FPS**: 60 FPS on iPhone XS+, 30 FPS on iPhone 6s
- Use Xcode Instruments to profile:
  - Memory Leaks
  - Allocations
  - System Trace (frame rate)
- Check memory usage at start (< 200 MB), peak gameplay (< 500 MB)

### Audio Testing

- [ ] Music plays continuously
- [ ] Mute toggle respected (physical switch)
- [ ] Volume buttons adjust SFX (not music)
- [ ] AirPods connect/disconnect handled
- [ ] Bluetooth speaker switching works

---

## Performance Targets

| Platform | Resolution | Min FPS | Target FPS | Max Latency |
|----------|------------|---------|-----------|-----------|
| Windows (Low) | 1280x720 | 30 | 60 | 50ms |
| Windows (High) | 1920x1080 | 60 | 120+ | 16ms |
| Linux | 1920x1080 | 30 | 60 | 50ms |
| macOS (Intel) | 2560x1600 | 30 | 60 | 50ms |
| macOS (Apple Silicon) | 2560x1600 | 60 | 120 | 16ms |
| Android (Low) | 1080x1920 | 30 | 30 | 100ms |
| Android (High) | 1440x2960 | 30 | 60 | 50ms |
| iOS | 1170x2532 | 30 | 60 | 50ms |

---

## Known Issues

### Global

- [ ] **Loading stutter**: First launch takes 10–15 seconds (shader compilation)
  - **Workaround**: Pre-compile shaders on startup
  - **Fixed**: Godot 4.7+ with shader caching

### Windows

- [ ] **DirectX 12 crash on startup** (some older GPUs)
  - **Workaround**: Force OpenGL via `godot.exe --gl`
  - **Status**: Investigation ongoing

### Linux

- [ ] **Wayland window doesn't maximize** (some window managers)
  - **Workaround**: Use Xorg session or manually resize
  - **Fixed**: Godot 4.7+

### macOS

- [ ] **M1 Rosetta 2 performance reduced 20%** vs native build
  - **Workaround**: Build native ARM64 binary (Godot 4.6+ support)
  - **Status**: Native builds in progress

### Android

- [ ] **Memory leak on screen rotation** (fixed in Godot 4.6.1+)
  - **Workaround**: Disable auto-rotate or upgrade Godot
  - **Fixed**: 4.6.1+

- [ ] **Touch lag on Samsung OneUI 6+** (system issue)
  - **Workaround**: Disable system animations in Settings
  - **Status**: Samsung investigating

### iOS

- [ ] **Jailbroken device crashes on app open** (security check)
  - **Status**: Expected behavior (anti-piracy)

---

## Reporting Bugs

Submit issues to: https://github.com/KoshkiKode/cordite/issues

Include:
- **Platform & OS version** (Windows 11 22H2, Android 12, etc.)
- **Device specs** (CPU, RAM, GPU)
- **Steps to reproduce**
- **Logs**: Check these locations:
  - **Windows**: `%APPDATA%\Cordite Wars\logs\`
  - **Linux**: `~/.local/share/godot/logs/`
  - **macOS**: `~/Library/Application Support/Godot/logs/`
  - **Android**: Via Android Studio Logcat
  - **iOS**: Via Xcode Console

---

## Optimization Tips

For developers optimizing exports:

### All Platforms
- Use `--lto` linker optimization (reduces binary size 10–20%)
- Enable PCK encryption for assets (if DRM required)
- Strip symbols from release builds

### Windows
- Use `/O2` compiler optimization
- Link against static C++ runtime
- Consider UPX compression (reduces exe size 40%, tradeoff with load time)

### Linux
- Use `-O3` compiler optimization
- Strip all symbols: `strip --strip-all CorditeWars`
- Consider AppImage compression

### macOS
- Enable Bitcode (allows App Store optimization)
- Use `-O2` optimization, `-dead_strip` linker flag
- Code-sign with hardened runtime for better security

### Android
- Use minified ProGuard/R8 for Java code
- Enable `-ffunction-sections -fdata-sections` linker optimization
- Test on minimum API 22 device

### iOS
- Enable App Thinning (automatic in App Store Connect)
- Bitcode required for App Store
- Use `-Os` optimization for smaller binary
