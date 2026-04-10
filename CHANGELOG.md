# Changelog

All notable changes to Cordite Wars: Six Fronts are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Stealth mechanics: units with `IsStealthed` are invisible to enemies unless a detector unit is within sight range
- Detector units per faction: Arcloft Vigilant jets, Bastion Patrol Rovers and Watcher towers, Ironmarch Signal Trucks, Valkyr Overwatch Drones and Kestrel jets; Bastion Corvettes and Depth Ward submarines detect naval stealth
- Stealth units per faction: Kragmore Mole Rat sappers, Stormrend Flicker Mines, Valkyr Windrunners, and Arcloft Patch Drone (infiltration support); naval stealth submarines for Arcloft (Phantom Sub), Bastion (Depth Ward), Kragmore (Deep Bore), and Stormrend (Riptide Sub)
- Per-category faction visual differentiation: unit meshes blend faction colour with category base colour via `CohesiveMaterial` (55% faction, 45% category)
- Stealth-reveal countdown: stealthed units that fire are revealed for 15 ticks (StealthRevealDuration) before re-entering stealth
- Campaign missions updated for all six factions to integrate stealth mechanics — new briefings, faction-specific objectives, and tactical twists referencing detector/stealth units
- New Bastion campaign mission "Ghost in the Network" (mission 7, Hard): destroy Arcloft's underwater surveillance network using Corvettes and Depth Wards
- Multi-platform export infrastructure (Windows, Linux, macOS, Android, iOS)
- Semantic versioning automation across all platform exports
- SHA256 checksum generation and verification tools
- Automated GitHub Releases workflow
- Comprehensive deployment and store submission guides
- Platform-specific testing and QA documentation

### Changed
- Updated CI/CD pipeline for multi-platform exports

### Fixed
- Android build template extraction for non-Android targets
- `bump-version.py` now correctly reads and writes `config/version` from `project.godot` (was using the wrong key `application/version`)
- `bump-version.py` now updates `versions/shared/version.json`, `versions/android/build.gradle`, `versions/ios/ios-info.plist`, and `config/version_major/minor/patch` fields in `project.godot` so all platform versions stay in sync

### Security
- Added optional Android APK signing via GitHub Actions secrets
- Added optional macOS code signing and notarization

## [0.1.0] - 2026-04-05

### Added
- Initial multi-platform game export pipeline
- Windows export (.exe, .msi)
- Linux export (binary, .snap)
- macOS export (.app, .dmg)
- Android export (.apk)
- iOS/iPadOS handoff export (Xcode project)
- Build guide documentation
- Export presets for all platforms
- C# assembly building integration
- Export artifact generation and upload

### Documentation
- docs/build-guide.md - Building for all platforms
- docs/deployment-guide.md - Distribution instructions per platform
- docs/store-submission-reference.md - Store submission quick reference
- docs/platform-testing-qa.md - Testing checklists and performance targets
- RELEASE_INFRASTRUCTURE.md - Release process and tooling documentation

---

## Version Format

Releases follow semantic versioning:
- **MAJOR**: Breaking changes or major feature releases
- **MINOR**: New features, backwards compatible
- **PATCH**: Bug fixes, no new features

Example: `0.1.0` → `0.1.1` (patch), `0.2.0` (minor), `1.0.0` (major)

---

## How to Document Changes

When submitting a PR or preparing a release:

1. **Determine version bump**: major / minor / patch
2. **Update CHANGELOG.md**:
   ```markdown
   ## [X.Y.Z] - YYYY-MM-DD

   ### Added
   - New feature description

   ### Changed
   - Modified behavior description

   ### Fixed
   - Bug fix description

   ### Security
   - Security improvement description
   ```

3. **Run version bump script**:
   ```bash
   python3 bump-version.py [major|minor|patch]
   ```

4. **Commit and tag**:
   ```bash
   git add CHANGELOG.md
   git commit -m "docs: Update CHANGELOG for v0.1.1"
   git tag v0.1.1
   git push origin main --tags
   ```

---

## Release Process

1. **Prepare changes**: Merge features into `main`
2. **Update CHANGELOG**: Document changes under `[Unreleased]` → new version
3. **Bump version**: Run `bump-version.py` script
4. **Create tag**: `git tag v0.1.0`
5. **Push**: `git push origin main --tags` (triggers auto-release)
6. **Verify**: Check GitHub Releases for all artifacts

---

## Notes

- **Pre-release**: Tag as `v0.1.0-rc.1`, `v0.1.0-beta.1`, etc.
- **Hotfix**: For critical fixes to production, tag as `v0.1.1` immediately
- **Long-term support**: Mark release channel in GitHub Releases as "Latest" or "Pre-release"

---

## Unreleased Changes Tracking

Current work in progress:
- [ ] Playability smoke tests for each platform export
- [ ] Performance metrics collection
- [ ] Analytics/crash reporting integration (optional)
- [ ] Store update automation (optional)
