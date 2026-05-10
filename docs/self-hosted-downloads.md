# Cordite Wars — Self-Hosted Downloads Server

> **Self-hosted deployment only.** This guide covers running the paywalled downloads
> page and file server on your own hardware using Docker, Caddy, and GoDaddy DNS.
> Stripe Payment Links handle checkout — no custom backend code required.

The architecture is intentionally minimal: Caddy serves static files from a bind-mounted
directory; Stripe Payment Links collect payment; a lightweight Node.js webhook handler
grants download access by issuing short-lived signed URLs. No cloud provider account is needed.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Prerequisites](#2-prerequisites)
3. [Directory Layout on the Server](#3-directory-layout-on-the-server)
4. [Environment Variables Reference](#4-environment-variables-reference)
5. [Dockerfile](#5-dockerfile)
6. [Run with Docker Compose](#6-run-with-docker-compose)
7. [Caddy Configuration](#7-caddy-configuration)
8. [DNS — GoDaddy](#8-dns--godaddy)
9. [Stripe Setup](#9-stripe-setup)
10. [Publishing a Release](#10-publishing-a-release)
11. [Backups](#11-backups)
12. [Updates](#12-updates)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Architecture Overview

```
Internet
    │
    ▼
GoDaddy DNS  ──▶  A record → your public IP  (cron updates every 5 min if dynamic)
    │
    ▼
Router  ──▶  Port 80/443 forwarded to server LAN IP
    │
    ▼
Caddy (port 80/443)  ──▶  auto-HTTPS via Let's Encrypt
    │
    ├──▶  downloads.koshkikode.com            →  static HTML downloads page
    └──▶  downloads.koshkikode.com/api/**     →  localhost:3400 (Node.js webhook + token server)

Node.js token server (port 3400, internal only)
    ├── POST /api/webhook          Stripe webhook — marks purchase as fulfilled
    ├── GET  /api/token?session=&file=   Issues a short-lived HMAC download token
    └── GET  /api/file?token=&file=      Validates token and streams the file from disk

Artifact storage
    /var/lib/cordite-downloads/
        v1.0.0/
            CorditeWars-v1.0.0-windows.zip
            CorditeWars-v1.0.0-linux.tar.gz
            CorditeWars-v1.0.0-macos.dmg
            CorditeWars-v1.0.0-android.apk
        latest.json
        checksums.txt
```

Paid artifacts live on disk and are **never** served directly by Caddy — they are only
reachable through short-lived HMAC tokens issued by the Node.js process after a
Stripe webhook confirms payment.

Free artifacts (demo builds, checksums, `latest.json`) can be served directly by Caddy
without any token.

---

## 2. Prerequisites

- Server running Debian 12 with Docker and Caddy installed
  → See [unshelvd/HOME_SERVER_SETUP.md](https://github.com/KoshkiKode/unshelvd/blob/main/HOME_SERVER_SETUP.md)
  for the full Debian + Docker + Caddy first-time setup (the steps are identical — just
  swap `unshelvd` for `cordite` where applicable)
- Ports 80 and 443 forwarded to the server's LAN IP
- A Stripe account with Payment Links enabled
- `downloads.koshkikode.com` A record in GoDaddy pointing at your public IP

---

## 3. Directory Layout on the Server

```bash
# Create storage directories
mkdir -p /var/lib/cordite-downloads
mkdir -p /opt/cordite-downloads      # Node.js app lives here
```

After uploading your first release (see [Section 10](#10-publishing-a-release)), the
storage directory will look like:

```
/var/lib/cordite-downloads/
├── latest.json                              # public — served directly by Caddy
├── checksums.txt                            # public — served directly by Caddy
└── v1.0.0/
    ├── CorditeWars-v1.0.0-windows.zip       # paid
    ├── CorditeWars-v1.0.0-linux.tar.gz      # paid
    ├── CorditeWars-v1.0.0-macos.dmg         # paid
    └── CorditeWars-v1.0.0-android.apk       # paid
```

---

## 4. Environment Variables Reference

Create `/opt/cordite-downloads/.env`:

```bash
# Server port (internal only — Caddy proxies inbound HTTPS)
PORT=3400

# REQUIRED: Stripe webhook signing secret
# Get from: Stripe Dashboard → Developers → Webhooks → Signing secret
STRIPE_WEBHOOK_SECRET=whsec_...

# REQUIRED: stable random secret for HMAC download token signing.
# Generate: openssl rand -hex 32
# Must not change after first run — changing it invalidates any outstanding tokens.
DOWNLOAD_TOKEN_SECRET=<generate-with-openssl-rand-hex-32>

# Token TTL in seconds (default: 300 = 5 minutes)
DOWNLOAD_TOKEN_TTL=300

# Path INSIDE the container where artifacts are stored (matches bind mount below)
ARTIFACT_DIR=/data

# Public base URL used in webhook fulfillment emails / redirect URLs
PUBLIC_URL=https://downloads.koshkikode.com

# REQUIRED: Stripe secret key (used to look up checkout session details in webhook)
# Get from: Stripe Dashboard → Developers → API Keys → Secret key
STRIPE_SECRET_KEY=sk_live_...
```

> **Never commit `.env` to git.**

---

## 5. Dockerfile

Create `/opt/cordite-downloads/Dockerfile`:

```dockerfile
FROM node:20-alpine
WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev
COPY server.js ./
EXPOSE 3400
CMD ["node", "server.js"]
```

Create `/opt/cordite-downloads/package.json`:

```json
{
  "name": "cordite-downloads",
  "version": "1.0.0",
  "private": true,
  "dependencies": {
    "stripe": "^14.0.0"
  }
}
```

Create `/opt/cordite-downloads/server.js`:

```javascript
const http = require('http');
const fs   = require('fs');
const path = require('path');
const crypto = require('crypto');
const { Stripe } = require('stripe');

const PORT          = parseInt(process.env.PORT || '3400');
const ARTIFACT_DIR  = process.env.ARTIFACT_DIR || '/data';
const TOKEN_SECRET  = process.env.DOWNLOAD_TOKEN_SECRET;
const TOKEN_TTL     = parseInt(process.env.DOWNLOAD_TOKEN_TTL || '300');
const WEBHOOK_SECRET = process.env.STRIPE_WEBHOOK_SECRET;
const stripe        = new Stripe(process.env.STRIPE_SECRET_KEY);

if (!TOKEN_SECRET || !WEBHOOK_SECRET) {
  console.error('DOWNLOAD_TOKEN_SECRET and STRIPE_WEBHOOK_SECRET must be set');
  process.exit(1);
}

// In-memory fulfilled sessions (replace with a JSON file or SQLite for persistence)
const fulfilledSessions = new Set();

function signToken(sessionId, filename, expiresAt) {
  const payload = `${sessionId}:${filename}:${expiresAt}`;
  const sig = crypto.createHmac('sha256', TOKEN_SECRET).update(payload).digest('hex');
  return Buffer.from(JSON.stringify({ sessionId, filename, expiresAt, sig })).toString('base64url');
}

function verifyToken(raw) {
  try {
    const { sessionId, filename, expiresAt, sig } = JSON.parse(Buffer.from(raw, 'base64url').toString());
    if (Date.now() > expiresAt) return null;
    const expected = crypto.createHmac('sha256', TOKEN_SECRET)
      .update(`${sessionId}:${filename}:${expiresAt}`).digest('hex');
    if (!crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(expected))) return null;
    return { sessionId, filename };
  } catch { return null; }
}

async function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
  });
}

function parseQuery(url) {
  return Object.fromEntries(new URL(url, 'http://x').searchParams);
}

const server = http.createServer(async (req, res) => {
  const url = req.url || '/';

  // POST /api/webhook — Stripe webhook
  if (req.method === 'POST' && url.startsWith('/api/webhook')) {
    const body = await readBody(req);
    let event;
    try {
      event = stripe.webhooks.constructEvent(body, req.headers['stripe-signature'], WEBHOOK_SECRET);
    } catch (e) {
      res.writeHead(400); res.end('Bad signature'); return;
    }
    if (event.type === 'checkout.session.completed') {
      fulfilledSessions.add(event.data.object.id);
      console.log(`Fulfilled: ${event.data.object.id}`);
    }
    res.writeHead(200); res.end('ok'); return;
  }

  // GET /api/token?session=<id>&file=<filename>
  if (req.method === 'GET' && url.startsWith('/api/token')) {
    const { session, file } = parseQuery(url);
    if (!session || !file) { res.writeHead(400); res.end('Missing params'); return; }
    if (!fulfilledSessions.has(session)) { res.writeHead(403); res.end('Not purchased'); return; }
    // Sanitize filename — no path traversal
    const safeFile = path.basename(file);
    const token = signToken(session, safeFile, Date.now() + TOKEN_TTL * 1000);
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ token })); return;
  }

  // GET /api/file?token=<token>&file=<filename>
  if (req.method === 'GET' && url.startsWith('/api/file')) {
    const { token, file } = parseQuery(url);
    const verified = token ? verifyToken(token) : null;
    if (!verified) { res.writeHead(401); res.end('Invalid or expired token'); return; }
    const safeFile = path.basename(file || verified.filename);
    if (safeFile !== verified.filename) { res.writeHead(403); res.end('Filename mismatch'); return; }
    // Find file in any version subdirectory
    let filePath = null;
    for (const entry of fs.readdirSync(ARTIFACT_DIR)) {
      const candidate = path.join(ARTIFACT_DIR, entry, safeFile);
      if (fs.existsSync(candidate)) { filePath = candidate; break; }
    }
    if (!filePath) { res.writeHead(404); res.end('File not found'); return; }
    const stat = fs.statSync(filePath);
    res.writeHead(200, {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${safeFile}"`,
      'Content-Length': stat.size,
    });
    fs.createReadStream(filePath).pipe(res); return;
  }

  res.writeHead(404); res.end('Not found');
});

server.listen(PORT, '127.0.0.1', () => console.log(`cordite-downloads listening on ${PORT}`));
```

---

## 6. Run with Docker Compose

Create `/opt/cordite-downloads/docker-compose.yml`:

```yaml
services:
  downloads:
    build: .
    container_name: cordite-downloads
    restart: unless-stopped
    env_file: .env
    ports:
      - "127.0.0.1:3400:3400"
    volumes:
      - /var/lib/cordite-downloads:/data:ro
```

Note the `:ro` — the Node.js process only needs read access to the artifacts.

```bash
cd /opt/cordite-downloads
docker compose up --build -d
docker compose logs -f
# Expected: "cordite-downloads listening on 3400"
```

Health check:
```bash
curl http://localhost:3400/api/health
# Expected: {"ok":true}  (add a /api/health route to server.js if you want one)
```

---

## 7. Caddy Configuration

Edit `/etc/caddy/Caddyfile` and add:

```
downloads.koshkikode.com {
    # Public files — served directly (no auth)
    handle /latest.json  { file_server { root /var/lib/cordite-downloads } }
    handle /checksums.txt { file_server { root /var/lib/cordite-downloads } }

    # API routes — proxied to Node.js token server
    handle /api/* {
        reverse_proxy localhost:3400
    }

    # Everything else — static downloads page
    handle {
        file_server { root /var/lib/cordite-downloads/web }
        try_files {path} /index.html
    }
}
```

Reload Caddy:

```bash
caddy fmt --overwrite /etc/caddy/Caddyfile
systemctl reload caddy
journalctl -u caddy -f
```

Caddy provisions the Let's Encrypt cert automatically on first request. Verify:

```bash
curl https://downloads.koshkikode.com/latest.json
```

---

## 8. DNS — GoDaddy

1. Log in to [GoDaddy DNS](https://dcc.godaddy.com/manage/dns) for `koshkikode.com`.
2. Add (or update) an **A record**:
   - **Type:** A
   - **Name:** `downloads`
   - **Value:** your server's public IP
   - **TTL:** 600
3. Propagation takes up to 10 minutes. Verify: `dig downloads.koshkikode.com +short`

### Dynamic IP (home server)

If your home IP changes, add a second entry to the GoDaddy DDNS cron job already set up
for `unshelvd` (see `unshelvd/HOME_SERVER_SETUP.md § Dynamic DNS`). Copy the cron script
and change `SUBDOMAIN="unshelvd"` to `SUBDOMAIN="downloads"`. Both can run from the same
cron job file if you loop over an array of subdomains.

---

## 9. Stripe Setup

Cordite uses **Stripe Payment Links** — no custom checkout code to maintain.

### Create a Payment Link for each platform

1. Go to [Stripe Dashboard → Payment Links](https://dashboard.stripe.com/payment-links)
2. Create a product called e.g. `"Cordite Wars — Windows"`
3. Set the price (one-time purchase)
4. Under **After payment** → set the redirect URL to:
   `https://downloads.koshkikode.com/?session_id={CHECKOUT_SESSION_ID}&platform=windows`
5. Copy the Payment Link URL — add it to your downloads page HTML as the "Buy" button href

Repeat for Linux, macOS, Android, iOS.

### Create the Stripe webhook

1. Stripe Dashboard → Developers → Webhooks → **Add endpoint**
2. URL: `https://downloads.koshkikode.com/api/webhook`
3. Events to send:
   - `checkout.session.completed`
4. Copy the **Signing secret** (`whsec_...`) → add to `.env` as `STRIPE_WEBHOOK_SECRET`
5. Restart the container: `docker compose restart downloads`

### Download flow (end-to-end)

```
User clicks "Buy" (Stripe Payment Link)
    → Stripe Checkout page (hosted by Stripe)
    → Payment succeeds
    → Stripe fires checkout.session.completed webhook → /api/webhook
    → Stripe redirects user to:
       https://downloads.koshkikode.com/?session_id=<id>&platform=windows

Downloads page JS:
    → GET /api/token?session=<id>&file=CorditeWars-v1.0.0-windows.zip
    → Receives { token: "..." }
    → Triggers: GET /api/file?token=<token>&file=CorditeWars-v1.0.0-windows.zip
    → Node.js validates HMAC token, streams file from /data/v1.0.0/
```

The token expires after `DOWNLOAD_TOKEN_TTL` seconds (default 5 minutes). If the user
needs to re-download later, they can revisit the redirect URL from Stripe's receipt email
— the `session_id` is stable, so the token server re-issues a fresh token.

> **Note on persistence:** The in-memory `fulfilledSessions` Set in `server.js` above is
> cleared on container restart. For production, replace it with a simple JSON file append
> or a SQLite database so purchases survive restarts.

---

## 10. Publishing a Release

After the GitHub Actions `release.yml` workflow completes and uploads artifacts to
GitHub Releases, run this on your server to mirror them locally:

```bash
#!/bin/bash
# Usage: ./publish-release.sh v1.0.0
VERSION=${1:?"Usage: $0 <version>"}
DEST="/var/lib/cordite-downloads/${VERSION}"
REPO="KoshkiKode/cordite"

mkdir -p "$DEST"

# Download all assets from the GitHub Release
gh release download "$VERSION" --repo "$REPO" --dir "$DEST"

# Update latest.json
cat > /var/lib/cordite-downloads/latest.json <<EOF
{
  "version": "${VERSION}",
  "published": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "files": {
    "windows": "${VERSION}/CorditeWars-${VERSION}-windows.zip",
    "linux":   "${VERSION}/CorditeWars-${VERSION}-linux.tar.gz",
    "macos":   "${VERSION}/CorditeWars-${VERSION}-macos.dmg",
    "android": "${VERSION}/CorditeWars-${VERSION}-android.apk"
  }
}
EOF

# Copy checksums
cp "$DEST/checksums.txt" /var/lib/cordite-downloads/checksums.txt

echo "Published ${VERSION} to ${DEST}"
```

Requires the [GitHub CLI (`gh`)](https://cli.github.com/) installed on the server
(`apt-get install -y gh`) and authenticated (`gh auth login`).

---

## 11. Backups

Artifacts on disk are the canonical source of truth. Back up the entire storage directory:

```bash
# Add to cron (weekly is fine — artifacts don't change often)
0 4 * * 0 root tar -czf /var/backups/cordite-downloads-$(date +\%Y-\%m-\%d).tar.gz \
  /var/lib/cordite-downloads/ && \
  find /var/backups -name 'cordite-downloads-*.tar.gz' -mtime +30 -delete
```

The `server.js` purchased-sessions state should also be persisted (see the note in
Section 9). If using a JSON file, back it up alongside artifacts.

---

## 12. Updates

```bash
cd /opt/cordite-downloads
# Edit server.js or package.json as needed
docker compose up --build -d
```

---

## 13. Troubleshooting

```bash
# Container logs
docker compose logs cordite-downloads -f

# Caddy logs (SSL, routing)
journalctl -u caddy -f

# Test token endpoint directly
curl "http://localhost:3400/api/token?session=cs_test_abc&file=CorditeWars-v1.0.0-windows.zip"
# Expected 403 if session not in fulfilledSessions (correct — no webhook fired yet)

# Test Stripe webhook with the Stripe CLI
stripe listen --forward-to https://downloads.koshkikode.com/api/webhook
stripe trigger checkout.session.completed

# Disk space
df -h /var/lib/cordite-downloads

# File permissions
ls -la /var/lib/cordite-downloads/
# Should be readable by Docker user (uid 1000 in node:alpine)
# Fix: chown -R 1000:1000 /var/lib/cordite-downloads
```

### Common errors

| Error | Fix |
|---|---|
| `403 Not purchased` on token request | Webhook hasn't fired yet — check Stripe webhook logs or wait a few seconds after checkout |
| `401 Invalid or expired token` | Token TTL elapsed — reload the redirect URL to get a fresh token |
| `404 File not found` | Filename in request doesn't match what's on disk — verify `latest.json` filenames |
| SSL cert not provisioning | Port 80 not forwarded, or DNS not pointing at you yet — `journalctl -u caddy -f` |
| Container won't start | Check `.env` for missing `DOWNLOAD_TOKEN_SECRET` or `STRIPE_WEBHOOK_SECRET` |

---

*KoshkiKode — Self-hosted on Debian + Docker + Caddy + GoDaddy DNS*
