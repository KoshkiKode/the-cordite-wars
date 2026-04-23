# Cordite Wars — Downloads Web Page

A single static HTML page (`index.html`, no build step, no dependencies) that
fronts the AWS release-hosting bucket. It auto-discovers the latest release by
fetching `releases/latest.json` and lists the artifacts grouped by platform.

## Design

Modeled on the [Vetviona](https://github.com/KoshkiKode/vetviona/blob/main/website/index.html)
landing page (same dark-glass aesthetic, sticky topbar with backdrop blur,
gradient brand mark, badge + chips + manifesto block) but adapted to the
Cordite Wars palette:

| Token | Color |
|---|---|
| `--bg` / `--bg-soft` | `#0f1020` / `#1a1a2e` (matches `assets/icons/icon.svg`) |
| `--brand` | `#e94560` (matches the icon's accent red) |
| `--brand-2` | `#c9a86e` (brass accent borrowed from the KoshkiKode visual language) |
| `--steel` | `#6b87b3` (industrial blue) |

## How it gets served

This page is uploaded to the **root** of the S3 release bucket by
`.github/workflows/deploy-aws.yml` on every release. CloudFront's
`default_root_object = "index.html"` setting means a request to the CDN apex
(`https://downloads.koshkikode.com/`) returns this page directly.

```
s3://<bucket>/
├── index.html              ← this file
└── releases/
    ├── latest.json         ← updated on each non-prerelease publish
    └── 0.1.1/
        ├── CorditeWars-Setup.exe
        ├── CorditeWars.dmg
        └── …
```

The page calls `fetch("releases/latest.json")` to find the current version,
then either:

1. Lists artifacts via S3's REST API (`?list-type=2&prefix=releases/<v>/`),
   which CloudFront passes through to the bucket; **or**
2. Falls back to a `files` array embedded in `latest.json` if listing isn't
   permitted on the bucket.

No backend, no admin, no user picker — just the page, the manifest, and the
files.

## Updating the page

Edit `index.html` directly and push. The next release publish (or a manual
run of the **Deploy Release Artifacts to AWS** workflow) re-uploads it.

To preview locally:

```bash
cd web/downloads
python3 -m http.server 8000
# open http://localhost:8000
```

It will show the "no release published yet" empty state because there's no
`releases/latest.json` next to it.
