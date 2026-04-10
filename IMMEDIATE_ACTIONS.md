# 🚨 Immediate Action Items

## ✅ FIXED: Export Pipeline (was failing on all 5 jobs)

**Root Cause** (identified & fixed):
1. `firebelley/godot-export@v7.0.0` does NOT have an `export_name` input — the correct parameter is `presets_to_export`. Using `export_name` was silently ignored, causing the action to export ALL presets on every job.
2. The Android preset failed on every runner because release keystore env vars were partially set (`GODOT_ANDROID_KEYSTORE_RELEASE_PATH` was always set to `./release.keystore` even when the file didn't exist, while `_USER` and `_PASSWORD` were empty).
3. Missing `workflow_call` trigger in `export.yml` prevented `release.yml` from invoking it.

**Fix Applied**:
- Replaced `export_name` with `presets_to_export` on all 5 export jobs
- Added `use_preset_export_path: true` so exports go to paths defined in `export_presets.cfg`
- Split Android export into conditional debug/release paths — release keystore env vars only set when secrets are configured
- Added `workflow_call` trigger to `export.yml` so `release.yml` can chain to it

---

## ✅ FIXED: Android .NET Export

**Root Cause**: The APK preset (`preset.2`) had `gradle_build/use_gradle_build=false`. For a C#/.NET game, Godot requires a Gradle build to bundle the .NET runtime. Without it, the APK would start and immediately crash because the .NET assembly cannot be loaded.

**Fix Applied**:
- Set `gradle_build/use_gradle_build=true` on `preset.2` (APK)
- Also aligned `preset.2` to arm64-v8a only (matching the AAB/Google Play preset) — keeps the APK lean and matches modern Android requirements
- Both the APK (sideload) and AAB (Google Play) presets now use Gradle builds

> **Note on "experimental" label**: Godot 4.6 still tags C#/.NET Android export as experimental in the editor UI, meaning it receives less test coverage than the GDScript path and edge cases may surface. With `use_gradle_build=true` correctly set, the build pipeline itself is sound. Treat Android as supported-with-caveats for alpha: test on real hardware before each release and file Godot upstream issues for any platform-specific crashes.

---

## ✅ ADDED: Headless Smoke Test

A `smoke-test-linux` job is now part of `export.yml`. It:
- Runs after `export-linux` completes
- Downloads the Linux export artifact
- Runs the game with `xvfb-run` + `--quit-after 60` (60 physics frames = 2 s at 30 Hz)
- Asserts exit code 0 — any crash, missing autoload, or broken boot scene fails the build

This prevents broken exports from being uploaded as release artifacts.

---

## ⚠️ Still Pending

### 1. Test the Release Workflow End-to-End
- Push a test tag `v0.0.1-test`
- Verify exports run on all platforms
- Verify GitHub Release created with all artifacts
- Verify checksums generated

**Timeline**: 30 minutes

### 2. Manual Platform Testing
- Download each artifact from GitHub Release
- Test on actual hardware (or emulators)
- Verify game is playable on each platform

**Timeline**: 2–4 hours

---

## 📋 Checklist: Export Workflow Recovery

- [x] **Identify exact fix**: Wrong parameter name (`export_name` → `presets_to_export`)
- [x] **Edit workflow**: Fixed all 5 export jobs + Android keystore handling
- [x] **Added `workflow_call`**: release.yml can now trigger export.yml
- [x] **Committed & pushed**: Export fix is on main
- [x] **Added smoke test**: `smoke-test-linux` job catches startup crashes
- [x] **Fixed Android Gradle**: `gradle_build/use_gradle_build=true` on APK preset
- [ ] **Monitor run**: Check GitHub Actions logs after next push to main
- [ ] **Verify artifacts**: At least one successful export per platform
- [ ] **Test one export**: Download Windows EXE, launch locally, verify gameplay
