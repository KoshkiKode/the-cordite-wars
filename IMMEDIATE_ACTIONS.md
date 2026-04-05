# 🚨 Immediate Action Items

## 🔴 CRITICAL: Fix Export Failures

**Status**: Run #13 failed — all 4 export jobs failed with "Android build template not installed"

**Root Cause**: The Android build template extraction in the workflow is placing files locally (`android/build/`), but Godot looks for templates in its user data directory (`~/.local/share/godot/export_templates/`). The firebelley/godot-export action bypasses our local files and downloads templates again, but the extracted files don't persist.

**Quick Fix Options**:

### Option A: Remove Manual Extraction (Simplest)
The manual extraction was meant to solve a problem that firebelley already handles. Just **delete the "Install Android Build Template" steps** from the workflow—let firebelley download & install templates normally.

**Action**: Remove these sections from `.github/workflows/export.yml`:
- Line 23–57 (export-windows job)
- Line 85–119 (export-linux job)
- Line 258–291 (export-android job)
- Similar in export-ios job

**Timeline**: 5 minutes, test immediately

### Option B: Fix Template Installation Path
Keep the manual extraction but install to the correct Godot path (`~/.local/share/godot/export_templates/VERSION/`).

**Action**: Update Python script to extract directly to Godot's cache.

**Timeline**: 10 minutes, test

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

## 📋 Checklist: Export Workflow Recovery

- [ ] **Identify exact fix**: Option A (remove) vs Option B (fix path)
- [ ] **Edit workflow**: Remove or update template installation steps
- [ ] **Commit & push**: Trigger new run (e.g., run #14)
- [ ] **Monitor run**: Check GitHub Actions logs
- [ ] **Verify artifacts**: At least one successful export per platform
- [ ] **Test one export**: Download Windows EXE, launch locally, verify gameplay

---

## 🎯 Do You Want Me To:

1. **Fix the export workflow now** (Option A or B)?
2. **Add smoke tests** to validate each export?
3. **Create a test release** (tag v0.0.1) to validate the full workflow?
4. **Something else**?

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
