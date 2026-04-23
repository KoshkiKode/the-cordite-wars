# 🎮 Multi-Platform Export Infrastructure

This directory contains scripts and workflows for automated multi-platform game export, versioning, and distribution.

## Quick Start

### Bump Version & Export

```bash
# Bump patch version across all platforms
python3 bump-version.py patch

# Commit and tag to trigger export workflow
git add .
git commit -m "v0.1.1 release"
git tag v0.1.1
git push origin main --tags
```

This automatically:
- ✓ Exports on all platforms (Windows, Linux, macOS, Android, iOS)
- ✓ Generates checksums
- ✓ Creates GitHub Release with all artifacts
- ✓ Updates version in project.godot, export_presets.cfg, snapcraft.yaml, Info.plist

### Verify Artifacts

```bash
# Generate checksums (if not auto-generated)
python3 generate-checksums.py generate build/

# Verify all artifacts
python3 generate-checksums.py verify build/checksums.txt
```

---

## 📋 Files & Scripts

### Scripts

| Script | Purpose |
|--------|---------|
| `bump-version.py` | Bump semantic version across all platforms (major/minor/patch) |
| `generate-checksums.py` | Generate/verify SHA256 checksums for artifacts |

### Workflows

| Workflow | Trigger | Output |
|----------|---------|--------|
| `.github/workflows/export.yml` | Manual run / reusable call | All platform exports (WIP artifacts) |
| `.github/workflows/release.yml` | Push tag `v*` | GitHub Release with all artifacts + checksums |

### Documentation

| Document | Purpose |
|----------|---------|
| `docs/deployment-guide.md` | Platform-specific deployment & store submission instructions |
| `docs/store-submission-reference.md` | Quick reference for each app store (costs, requirements, timelines) |
| `docs/platform-testing-qa.md` | Testing checklist & performance targets per platform |

---

## 📦 Versioning

### Format

Use semantic versioning: `MAJOR.MINOR.PATCH` (e.g., `0.1.0`, `1.2.3`)

### Bumping Version

```bash
# Patch bump: 0.1.0 → 0.1.1
python3 bump-version.py patch

# Minor bump: 0.1.0 → 0.2.0
python3 bump-version.py minor

# Major bump: 0.1.0 → 1.0.0
python3 bump-version.py major

# Set exact version
python3 bump-version.py set 1.0.0
```

This updates:
- ✓ `project.godot` → `application/version`
- ✓ `versions/shared/export_presets.cfg` → all presets (Windows, Linux, macOS, Android, iOS)
- ✓ `versions/linux/snapcraft.yaml` → `version`
- ✓ `versions/macos/Info.plist` → `CFBundleShortVersionString` + `CFBundleVersion`

---

## 🚀 Release Process

### 1. Prepare Release

```bash
# Make code changes, test locally

# Bump version
python3 bump-version.py patch

# Commit
git add -A
git commit -m "Release v0.1.1: Bug fixes and performance improvements"
```

### 2. Tag & Push

```bash
# Create tag (must match v*.*.* pattern to trigger release workflow)
git tag v0.1.1

# Push to origin (triggers release creation; release calls export internally)
git push origin main --tags
```

### 3. GitHub Actions Workflow

The `release.yml` workflow automatically:
1. Runs export workflow for all platforms
2. Downloads all artifacts from export jobs
3. Generates SHA256 checksums (txt + json)
4. Creates release notes
5. Publishes GitHub Release with artifacts

### 4. Verify Release

- Visit: https://github.com/KoshkiKode/cordite/releases
- Download artifacts
- Verify checksums:
  ```bash
  sha256sum -c checksums.txt
  ```

---

## 📊 Checksums

### Generate

```bash
# Generate checksums for all artifacts in build/
python3 generate-checksums.py generate build/

# Outputs:
# - checksums.txt (legacy format, compatible with sha256sum -c)
# - checksums.json (structured format with metadata)
```

### Verify

```bash
# Verify using checksums.txt
python3 generate-checksums.py verify checksums.txt

# Or manually with sha256sum
sha256sum -c checksums.txt

# Verify using checksums.json
python3 generate-checksums.py verify checksums.json
```

### Formats

**checksums.txt** (compatible with `sha256sum -c`)
```
abc123... windows/CorditeWars.exe
def456... linux/CorditeWars
ghi789... android/CorditeWars.apk
```

**checksums.json** (structured)
```json
{
  "generated": "2026-04-05T21:14:00Z",
  "algorithm": "sha256",
  "artifacts": [
    {
      "file": "windows/CorditeWars.exe",
      "size": 52428800,
      "checksum": "abc123..."
    }
  ]
}
```

---

## 🌍 Platform Coverage

| Platform | Export | Package | Status |
|----------|--------|---------|--------|
| **Windows** | ✓ .exe | ✓ .msi | Production |
| **Linux** | ✓ binary | ✓ .snap | Production |
| **macOS** | ✓ .app | ✓ .dmg | Production |
| **Android** | ✓ .apk | - | Production |
| **iOS** | ✓ handoff | - | Xcode handoff |

---

## 🏪 Distribution Targets

All documented in `docs/store-submission-reference.md`:

### Desktop
- Steam
- Epic Games Store
- GOG
- Microsoft Store (Windows)
- App Store (macOS)

### Mobile
- Google Play Store (Android)
- Apple App Store (iOS/iPadOS)
- Samsung Galaxy Store (Android)
- Amazon Appstore (Android)

### Linux
- Snap Store
- Flathub

---

## 📝 Deployment Instructions

Full platform-specific deployment guides in `docs/deployment-guide.md`:

- **Windows**: MSI installer, Steam, Microsoft Store, Epic
- **Linux**: Binary, Snap Store, Steam
- **macOS**: DMG, code-signing, notarization, App Store
- **Android**: APK install, Google Play Store, Samsung Galaxy, Amazon
- **iOS**: Xcode handoff, App Store, TestFlight

---

## 🧪 Testing & QA

Comprehensive testing checklist in `docs/platform-testing-qa.md`:

- Minimum requirements per platform
- Test checklist for each device
- Performance targets (FPS, memory, battery)
- Screen resolution testing
- Known issues & workarounds
- Optimization tips

---

## 🔗 GitHub Actions Secrets

For signing & distribution (optional, adds automation):

| Secret | Used For | Example |
|--------|----------|---------|
| `ANDROID_RELEASE_KEYSTORE_B64` | Android APK signing | Base64-encoded `.keystore` |
| `APPLE_DEV_ID` | macOS code-signing | "Developer ID Application: ..." |
| `APPLE_ID` | Notarization | Apple email |
| `APPLE_TEAM_ID` | Notarization | 10-character team ID |
| `AC_PASSWORD` | Notarization app-specific password | Generated in Apple ID settings |

**Note**: These are optional. Exports work without them (unsigned, debug keystore, etc).

For full secrets/variables documentation see [`docs/secrets-setup.md`](docs/secrets-setup.md).

---

## ☁️ AWS Hosting (self-hosted downloads)

Releases can also be mirrored from GitHub to KoshkiKode's own AWS account so
download links can live on `koshkikode.com` (or e.g. `downloads.koshkikode.com`)
instead of `github.com/.../releases/...`.

| Component | Where |
|---|---|
| Terraform (S3 bucket, CloudFront, OAC, GitHub OIDC IAM role, optional ACM + Route 53) | [`infra/aws/`](infra/aws/) |
| Workflow (runs on `release.published`) | [`.github/workflows/deploy-aws.yml`](.github/workflows/deploy-aws.yml) |
| Setup walkthrough | [`docs/aws-hosting-setup.md`](docs/aws-hosting-setup.md) |

After publish, artifacts are available at:

```
https://<your-cdn-domain>/releases/<version>/<filename>
https://<your-cdn-domain>/releases/latest.json    # version manifest
```

---

## 📚 Quick Links

- **Build Guide**: [docs/build-guide.md](docs/build-guide.md)
- **Deployment Guide**: [docs/deployment-guide.md](docs/deployment-guide.md)
- **Store Reference**: [docs/store-submission-reference.md](docs/store-submission-reference.md)
- **Testing & QA**: [docs/platform-testing-qa.md](docs/platform-testing-qa.md)
- **GitHub Actions**: [.github/workflows/](../.github/workflows/)
- **Godot Export Presets**: [versions/shared/export_presets.cfg](versions/shared/export_presets.cfg)

---

## 🐛 Troubleshooting

### Version bump didn't update all files
- Check file exists: `versions/linux/snapcraft.yaml`, `versions/macos/Info.plist`
- Verify paths in `bump-version.py`

### Export workflow failed
- Check GitHub Actions logs: https://github.com/KoshkiKode/cordite/actions
- Review [Build Guide](docs/build-guide.md)

### Checksums don't match
- Ensure you're comparing checksums from same build
- Check file wasn't modified after export
- Re-download from GitHub Releases

### Can't push tag
- Ensure tag matches `v*.*.*` pattern for release workflow
- Example: `v0.1.0` ✓, `version-0.1.0` ✗

---

## 📞 Support

- **Issues**: https://koshkikode.com
- **Discussions**: https://koshkikode.com
