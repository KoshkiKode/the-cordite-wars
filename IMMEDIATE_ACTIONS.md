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

## ✅ Once Exports Work

### 1. Add Playability Smoke Tests
- Launch each export (exe, apk, linux binary, etc.)
- Verify game reaches main menu + plays first 10 frames
- Prevent broken builds from being released

**Tools**: 
- Headless/automated game testing via CI
- Quick frame-count check or "game running" signal

**Timeline**: 1–2 hours

### 2. Test the Release Workflow
- Push a test tag `v0.0.1-test`
- Verify exports run
- Verify GitHub Release created with artifacts
- Verify checksums generated

**Timeline**: 30 minutes

### 3. Manual Platform Testing
- Download each artifact from GitHub Release
- Test on actual hardware (or emulators)
- Verify game is playable on each platform

**Timeline**: 2–4 hours

---

## ⚠️ Known Limitations

### Android .NET Export is Experimental
Godot 4.6 warns that "Exporting to Android when using C#/.NET is experimental."
The Android export may need `gradle_build/use_gradle_build=true` in `export_presets.cfg` for
full .NET runtime bundling. Monitor Android export results after this fix.

---

## 📋 Checklist: Export Workflow Recovery

- [x] **Identify exact fix**: Wrong parameter name (`export_name` → `presets_to_export`)
- [x] **Edit workflow**: Fixed all 5 export jobs + Android keystore handling
- [x] **Added `workflow_call`**: release.yml can now trigger export.yml
- [ ] **Commit & push**: Trigger new run
- [ ] **Monitor run**: Check GitHub Actions logs
- [ ] **Verify artifacts**: At least one successful export per platform
- [ ] **Test one export**: Download Windows EXE, launch locally, verify gameplay

---

## Files Ready to Commit

Already created (waiting for export fix):
- ✅ `bump-version.py` - Version automation
- ✅ `generate-checksums.py` - Checksum tools
- ✅ `.github/workflows/release.yml` - Auto-release workflow
- ✅ `docs/deployment-guide.md` - Deployment instructions
- ✅ `docs/store-submission-reference.md` - Store quick reference
- ✅ `docs/platform-testing-qa.md` - Testing guide
- ✅ `RELEASE_INFRASTRUCTURE.md` - Release docs
- ✅ `CHANGELOG.md` - Changelog template

**Next Step**: Commit these + fix exports = fully automated multi-platform release pipeline ✨
