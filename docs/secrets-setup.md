# GitHub Secrets Setup — Cordite Wars: Six Fronts

This document lists every repository secret used by the CI/CD pipelines, where
to find each value, and how to add it to GitHub.

---

## How to add a secret

1. Go to **Settings → Secrets and variables → Actions** in your GitHub repository,
   or visit `https://github.com/KoshkiKode/cordite/settings/secrets/actions`
2. Click **New repository secret**
3. Paste the name exactly as shown in the tables below
4. Paste the value and click **Add secret**

> Secrets are write-only after creation — you can update or delete them but not read them back.

---

## Steam

Used by `.github/workflows/steam-deploy.yml`.  
Sign in at [partner.steamgames.com](https://partner.steamgames.com/) to find these values.

| Secret | Required | Description | Where to find it |
|--------|----------|-------------|-----------------|
| `STEAM_APP_ID` | ✅ | Numeric App ID for Cordite Wars (e.g. `2345678`). **Must not be `480`** — that is Valve's public test app. | Steamworks dashboard → your app → **App Admin** |
| `STEAM_WINDOWS_DEPOT_ID` | ✅ | Depot ID for the Windows build | Steamworks → App Admin → **SteamPipe → Depots** |
| `STEAM_LINUX_DEPOT_ID` | ✅ | Depot ID for the Linux build | Steamworks → App Admin → **SteamPipe → Depots** |
| `STEAM_MACOS_DEPOT_ID` | ✅ | Depot ID for the macOS build | Steamworks → App Admin → **SteamPipe → Depots** |
| `STEAM_USERNAME` | ✅ | Steamworks **builder account** username (a dedicated CI account, not your personal login) | Create at [store.steampowered.com](https://store.steampowered.com), then grant it permission in Steamworks → **Users & Permissions** |
| `STEAM_PASSWORD` | ✅ | Password for the builder account above | Set when you create the account |
| `STEAM_TOTP_SECRET` | ⚠️ recommended | Base32 Steam Guard shared secret for the builder account. Lets CI generate its own one-time code instead of waiting for an email/app confirmation. | After enabling Steam Guard on the builder account, the shared secret is shown in the Steam Desktop Authenticator or the Steam mobile app's **Manage Steam Guard** page |

### Steam Guard TOTP

Without `STEAM_TOTP_SECRET`, SteamCMD will attempt to log in without a 2FA
code.  If Steam Guard is enabled on the builder account the login will fail.
Options:

- **Recommended**: Use the [Steam Desktop Authenticator](https://github.com/Jessecar96/SteamDesktopAuthenticator)
  to extract the base32 `shared_secret` from the builder account's maFile, then
  store it as `STEAM_TOTP_SECRET`.
- **Alternative**: Disable Steam Guard on the builder account (not recommended
  for a shared/CI account).

### First-time Steamworks setup

1. Create a Steamworks account at <https://partner.steamgames.com/> ($100 one-time fee).
2. Create a new app; Valve will assign an App ID and three Depot IDs (Windows, Linux, macOS).
3. Update `steam_appid.txt` in the repo root with the real App ID.
4. Update `steam/app-build.vdf`, `steam/windows-depot.vdf`, `steam/linux-depot.vdf`,
   and `steam/macos-depot.vdf` — replace every `YOUR_STEAM_APP_ID`, `YOUR_WINDOWS_DEPOT_ID`,
   etc. placeholder with the real values.
5. Add all six secrets above to GitHub.
6. Push a `v*` tag or trigger **Actions → Deploy to Steam → Run workflow** manually.

---

## GOG

Used by `.github/workflows/deploy-gog.yml`.  
Sign in at [devportal.gog.com](https://devportal.gog.com/) to find these values.

| Secret | Required | Description | Where to find it |
|--------|----------|-------------|-----------------|
| `GOG_PRODUCT_ID` | ✅ | Numeric GOG Product ID for Cordite Wars | GOG Developer Portal → your game → **Overview** |
| `GOG_CLIENT_ID` | ✅ | OAuth2 client ID for Pipeline Builder API access | GOG Developer Portal → **API Keys / Integrations** |
| `GOG_CLIENT_SECRET` | ✅ | OAuth2 client secret paired with `GOG_CLIENT_ID` | Same page as client ID — copy immediately, it won't be shown again |
| `GOG_USERNAME` | ✅ | GOG developer account username | Your GOG developer account login |
| `GOG_PASSWORD` | ✅ | GOG developer account password | Your GOG developer account login |

### First-time GOG setup

1. Apply for a GOG developer account at <https://devportal.gog.com/>.
2. Once approved, create a new game; GOG will assign a Product ID.
3. Generate API credentials (client ID + secret) under **API Keys / Integrations**.
4. Update `gog/build-windows.json`, `gog/build-linux.json`, and `gog/build-macos.json`
   — replace `YOUR_GOG_PRODUCT_ID`, `YOUR_GOG_CLIENT_ID`, and `YOUR_GOG_CLIENT_SECRET`
   placeholders with the real values **or** leave placeholders in place and rely
   entirely on the CI `sed` patch step (both approaches work).
5. Add all five secrets above to GitHub.
6. Push a `v*` tag or trigger **Actions → Deploy to GOG → Run workflow** manually.

> **GOG DRM policy**: GOG requires all games to be DRM-free on the GOG platform.
> Do not include any online-validation or launcher-lock mechanism in the GOG builds.
> The Steam version may use Steam DRM wrapping, but that must not be present in the
> GOG depots.

---

## Android

Used by `.github/workflows/export.yml` and `.github/workflows/deploy-google-play.yml`.

| Secret | Required | Description | Where to find it |
|--------|----------|-------------|-----------------|
| `ANDROID_RELEASE_KEYSTORE_B64` | ✅ for signed builds | Base64-encoded release keystore file | `base64 -w 0 release.keystore` |
| `ANDROID_RELEASE_KEYSTORE_ALIAS` | ✅ for signed builds | Alias used when the keystore was created | Set during `keytool -genkey` |
| `ANDROID_RELEASE_KEYSTORE_PASSWORD` | ✅ for signed builds | Password for the keystore (store password) | Set during `keytool -genkey` |
| `ANDROID_RELEASE_KEY_PASSWORD` | Optional | Password for the release key entry (falls back to `ANDROID_RELEASE_KEYSTORE_PASSWORD` when unset) | Set during `keytool -genkey` if different from store password |
| `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | ✅ for Play Store | Full JSON of the Google Play service account | Google Play Console → **Setup → API access → Service accounts** |

---

## Apple (macOS / iOS / iPadOS)

Used by `.github/workflows/export.yml` and `.github/workflows/deploy-app-store.yml`.

| Secret | Required | Description | Where to find it |
|--------|----------|-------------|-----------------|
| `APPLE_DEV_ID` | ✅ for signing | Apple Developer ID (e.g. `Developer ID Application: Your Name (TEAMID)`) | Keychain Access or [developer.apple.com](https://developer.apple.com) → Certificates |
| `APPLE_TEAM_ID` | ✅ for signing | 10-character Apple Team ID | [developer.apple.com](https://developer.apple.com) → Account → Membership |
| `APPLE_ID` | ✅ for notarization | Apple ID email used for App Store Connect | Your Apple Developer account email |
| `AC_PASSWORD` | ✅ for notarization | App-specific password for the Apple ID above | [appleid.apple.com](https://appleid.apple.com) → Sign-In and Security → App-Specific Passwords |
| `IOS_PROVISIONING_PROFILE` | ✅ for iOS | Base64-encoded `.mobileprovision` file | [developer.apple.com](https://developer.apple.com) → Certificates, Identifiers & Profiles |
| `IOS_P12_CERTIFICATE` | ✅ for iOS | Base64-encoded `.p12` distribution certificate | Keychain Access → export → `base64 -w 0 cert.p12` |
| `IOS_P12_PASSWORD` | ✅ for iOS | Password set when exporting the `.p12` | Set when you export from Keychain Access |

---

## Quick-reference checklist

Copy this into a private note or 1Password/Bitwarden item as you fill each one in.

### Steam
- [ ] `STEAM_APP_ID`
- [ ] `STEAM_WINDOWS_DEPOT_ID`
- [ ] `STEAM_LINUX_DEPOT_ID`
- [ ] `STEAM_MACOS_DEPOT_ID`
- [ ] `STEAM_USERNAME`
- [ ] `STEAM_PASSWORD`
- [ ] `STEAM_TOTP_SECRET` *(optional but recommended)*
- [ ] `steam_appid.txt` updated in repo
- [ ] `steam/*.vdf` placeholders replaced in repo

### GOG
- [ ] `GOG_PRODUCT_ID`
- [ ] `GOG_CLIENT_ID`
- [ ] `GOG_CLIENT_SECRET`
- [ ] `GOG_USERNAME`
- [ ] `GOG_PASSWORD`
- [ ] `gog/build-*.json` placeholders replaced in repo *(or left for CI sed patch)*

### Android
- [ ] `ANDROID_RELEASE_KEYSTORE_B64`
- [ ] `ANDROID_RELEASE_KEYSTORE_ALIAS`
- [ ] `ANDROID_RELEASE_KEYSTORE_PASSWORD`
- [ ] `ANDROID_RELEASE_KEY_PASSWORD` *(only if different from keystore password)*
- [ ] `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`

### Apple
- [ ] `APPLE_DEV_ID`
- [ ] `APPLE_TEAM_ID`
- [ ] `APPLE_ID`
- [ ] `AC_PASSWORD`
- [ ] `IOS_PROVISIONING_PROFILE`
- [ ] `IOS_P12_CERTIFICATE`
- [ ] `IOS_P12_PASSWORD`

---

## AWS release hosting (S3 + CloudFront)

Used by `.github/workflows/deploy-aws.yml` to publish release artifacts to
KoshkiKode's own S3 bucket / CloudFront distribution. See
[`docs/aws-hosting-setup.md`](./aws-hosting-setup.md) for the full bootstrap.

These are **repository variables**, not secrets — they are not sensitive
(the role can only be assumed via OIDC from this repo, not with the ARN
alone). Add them under **Settings → Secrets and variables → Actions →
Variables**.

| Variable | Required | Description | Where to find it |
|--------|----------|-------------|-----------------|
| `AWS_RELEASES_BUCKET` | ✅ | Name of the S3 bucket that stores release artifacts | `terraform output s3_bucket_name` (in `infra/aws/`) |
| `AWS_RELEASES_ROLE_ARN` | ✅ | IAM role the workflow assumes via OIDC | `terraform output github_actions_role_arn` |
| `AWS_REGION` | ✅ | Region of the S3 bucket (default `us-east-1`) | `terraform output aws_region` |
| `AWS_CLOUDFRONT_DISTRIBUTION_ID` | Optional | If set, the workflow invalidates `latest.json` and the new version's prefix after upload | `terraform output cloudfront_distribution_id` |

> **No AWS access keys are stored in GitHub.** Authentication uses the
> built-in GitHub OIDC provider; the trust policy on the IAM role restricts
> federation to this repository, the `release` environment, and `v*` tags
> by default.

### AWS checklist
- [ ] `AWS_RELEASES_BUCKET` (variable)
- [ ] `AWS_RELEASES_ROLE_ARN` (variable)
- [ ] `AWS_REGION` (variable)
- [ ] `AWS_CLOUDFRONT_DISTRIBUTION_ID` (variable, optional)
- [ ] `release` environment created (Settings → Environments)
