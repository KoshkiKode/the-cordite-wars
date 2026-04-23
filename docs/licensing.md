# Licensing System

Cordite Wars uses a **25-character license key** + **10-machine activation cap**
+ **silent background renewal** model. Players who buy the game on
[koshkikode.com](https://downloads.koshkikode.com) receive a license key by
email; the game uses it once to activate the device, then verifies offline
on every subsequent launch. Buyers can install on up to **10 machines per
license**, with **unlimited downloads** of the installer itself.

This document is the canonical reference. The matching code lives in:

- `infra/aws/lambda/license_keys.py` — wire format, Ed25519 signing/verification.
- `infra/aws/lambda/licensing.py`    — activation / renewal / deactivation routes.
- `infra/aws/lambda/handler.py`      — Stripe webhook → license issuance.
- `src/Core/Licensing/`              — game-side parser, store, gate, HTTP client.
- `tools/license_keygen.py`          — CLI for bootstrap + manual issuance.

## At a glance

| Aspect                | Choice                                                                         |
| --------------------- | ------------------------------------------------------------------------------ |
| Email delivery        | Amazon SES (verified domain, DKIM, bounce/complaint SNS)                       |
| Key length            | 25 characters, Crockford Base32, grouped 5-5-5-5-5                             |
| Server-side trust     | Ed25519 truncated signature, re-signed and compared against the private key    |
| Client-side trust     | CRC-8 typo guard + signed entitlement blob (full Ed25519 sig, embedded pubkey) |
| Activation cap        | 10 machines per license, conditional DynamoDB write                            |
| Offline grace period  | ~400 days per entitlement, refreshed silently on every online launch           |
| Inactive slot release | 30 days no-launch → automatic, plus manual "Release" button on the web UI      |
| Storefront bypass     | Steam (steam_appid.txt) and GOG (goggame-*.info) skip the gate entirely        |
| Downloads             | Unlimited, separate from activation                                            |

## Why this shape?

- **25 chars** — fits in payment-receipt emails, two-line dialogs, and on a
  Post-it. Long enough for ~125 bits of payload (10-machine cap, key id,
  issue date, sku, flags) plus a truncated signature and a typo-detection
  CRC.
- **10 machines** — covers households (gaming PC + laptop + Steam Deck) and
  modders/streamers who run multiple test installs, without giving away
  unlimited shareability.
- **Crockford Base32** — no `I`, `L`, `O`, `U`, so no confusable characters
  on hand-typed keys; client also folds typed `I→1`, `L→1`, `O→0`, `U→V`.
- **Silent background renewal** — players never see a "renew your license"
  prompt. The game refreshes its 13-month entitlement on any launch with
  internet, transparently. Players only notice the system at all if they
  go a full year offline.
- **GOG bypass** — GOG's contractual policy is DRM-free distribution, so the
  GOG build of Cordite Wars detects the storefront marker and skips the
  gate entirely. Same for Steam (which has its own ownership check via
  Steamworks).

## Wire format — license key

A key is 15 bytes, base32-encoded into 24 chars, plus a 1-char CRC-8 check
character. Total: 25 chars, displayed as 5 groups of 5.

```
offset  size  field
------  ----  ------------------------------------------------
0       1     version       (currently 1)
1       4     key_id        (random uint32, identifies the row in DynamoDB)
5       1     sku           (1 = standard)
6       2     issue_date    (days since 2025-01-01, uint16)
8       1     flags         (reserved, currently 0)
9       6     signature     (first 6 bytes of an Ed25519 signature over bytes 0..8)
```

The truncated 6-byte signature gives ~48 bits of forgery resistance against
random guessing. Crucially, the truncated signature **cannot be verified
client-side** with only the public key — there's no math that lets you
verify 6 of 64 Ed25519 signature bytes. The client therefore treats the
key as a *candidate* and trusts (a) the activation server's DynamoDB lookup
or (b) the resulting signed entitlement blob. The 6 bytes exist primarily
so the *server* can reject keys that weren't issued by us before doing any
DB work.

## Wire format — entitlement blob

After activation the server returns a binary blob (base64-url encoded for
transport) that the game persists at `user://license/entitlement.dat`:

```
offset  size  field
------  ----  ------------------------------------------------
0       1     version       (currently 1)
1       4     key_id
5       16    machine_id    (SHA-256 truncated to 16 bytes)
21      1     slot_index    (1..10)
22      4     issued_at     (unix seconds, uint32)
26      4     expires_at    (unix seconds, uint32)
30      1     hostname_len
31      N     hostname_hint (UTF-8, max 64 bytes)
...     64    signature     (full Ed25519 over preceding bytes)
```

This blob is the **client-side trust anchor**. It's signed with a full
Ed25519 signature, so the game verifies it offline against the embedded
public key. A blob is good for 400 days from issue.

## Lifecycle

```
┌───────────────────────────────────────────────────────────────────────┐
│ 1. Stripe Checkout completes                                          │
│    → Stripe webhook hits /api/webhook                                 │
│    → handler issues a fresh 25-char key                               │
│    → row written to DynamoDB `licenses`                               │
│    → SES email sent to buyer                                          │
└───────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────────────┐
│ 2. Buyer installs game, enters key                                    │
│    → Game POSTs /api/activate with (key, machine_id, hostname_hint)   │
│    → Server: re-sign-verify, count slots, conditional write to        │
│      `machine_slots`, return signed entitlement blob                  │
│    → Game saves blob to user://license/entitlement.dat                │
└───────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────────────┐
│ 3. Subsequent launches                                                │
│    → Game verifies blob signature offline → boot                      │
│    → If past halfway point AND online: silently POST /api/renew       │
│      with the existing blob → store fresh blob → no UI shown          │
└───────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────────────┐
│ 4. New machine                                                        │
│    → Activation succeeds while slots < 10                             │
│    → If slots == 10: server first reclaims any slot inactive >30 days │
│    → If still 10: server returns 409 with the slot list; UI prompts   │
│      the user to release one via /api/deactivate                      │
└───────────────────────────────────────────────────────────────────────┘
```

## Offline activation

For users without internet on the gaming machine:

1. They visit `https://downloads.example.com/manage.html` from any device
   with internet.
2. They enter their email + license key + the machine fingerprint shown by
   the game's "Offline activation" tab.
3. The site calls `/api/activate-offline` and returns the base64
   entitlement blob.
4. The user copies the blob, pastes it into the game, and the game stores
   it as if it had received it from a normal activation.

(The web-side flow for step 2/3 ships in a follow-up — for the initial
release the manage page handles the "free a slot" path, and offline
entitlement issuance is handled by support on demand. The endpoint and
in-game UI are already in place.)

## Storefront bypass

`StorefrontDetector.Detect(installDir)` looks for two markers:

- `steam_appid.txt`     — present in every Steam installation
- `goggame-*.info`      — present in every GOG installation

When either is found the game logs the fact and skips the licensing gate
entirely. We deliberately do not call any storefront SDK from this code so
that non-storefront builds don't need to ship the SDKs.

## Key rotation

The Ed25519 signing key in Secrets Manager is the **single root of trust**
for everything: keys, entitlements, renewals. Rotating it requires:

1. Re-signing every existing license.
2. Re-issuing every customer's entitlement.
3. Shipping a new game build with the new public key embedded.

In other words: **don't rotate after launch unless the private key is
believed compromised**. The Secrets Manager `recovery_window_in_days = 30`
gives a 30-day undo window if the key is accidentally deleted.

If a rotation is unavoidable, support both old and new public keys in the
game binary for the duration of one major release cycle, run a one-shot
backfill that re-signs every active license under the new key, and only
then drop the old public key.

## Operator runbook

### Bootstrap (one time)

```bash
# 1. Generate the production signing key.
python3 tools/license_keygen.py generate-signing-key

# 2. Paste the PEM into AWS Secrets Manager:
#      <bucket>/license-signing
#    as JSON:
#      { "private_key_pem": "...", "public_key_pem": "..." }

# 3. Copy the `public_key_raw_hex` value into:
#      src/Core/Licensing/LicenseConfig.cs
#    in the `LicenseSigningPublicKey` array.

# 4. Set ses_from_address in terraform.tfvars; apply.

# 5. Add the DKIM CNAMEs (output `ses_dkim_records`) to your DNS provider.

# 6. Subscribe to the SNS topic (output `ses_feedback_topic_arn`) so you
#    get bounce/complaint notifications.
```

### Manual key issuance

```bash
# For press, support replacements, dev test rigs:
cat /path/to/private_key.pem | python3 tools/license_keygen.py mint --json
```

### Looking up a customer

The DynamoDB Console:

- `<bucket>-licenses`    — find the row by `email` (scan by attribute).
- `<bucket>-machine-slots` — see which machines are claimed for a `key_id`.

### Refunds

To revoke a license after a refund:

1. In `<bucket>-licenses`, set `status` to `"revoked"` for the matching row.
2. Subsequent `/api/activate` and `/api/renew` calls will return 403.
3. Existing entitlements remain valid until they expire (~400 days max).
   To kill them sooner, also set `released_at` on every row in
   `<bucket>-machine-slots` for that `key_id`.
