# AWS Release Hosting — Setup Guide

This walkthrough takes a brand-new AWS account from zero to *"Cordite Wars
release artifacts are downloadable from `https://downloads.koshkikode.com/`"*.

It is the human-facing companion to the Terraform config in
[`infra/aws/`](../infra/aws/) and the GitHub Actions workflow in
[`.github/workflows/deploy-aws.yml`](../.github/workflows/deploy-aws.yml).

---

## Architecture

```
                   ┌──────────────────────┐
   git push v0.1.1 │  release.yml         │
   ───────────────►│  → builds artifacts  │
                   │  → publishes GitHub  │
                   │     Release          │
                   └──────────┬───────────┘
                              │  release.published event
                              ▼
                   ┌──────────────────────┐
                   │ deploy-aws.yml       │
                   │ (assumes IAM role    │
                   │  via OIDC — no keys) │
                   └──────────┬───────────┘
                              │  aws s3 sync
                              ▼
                   ┌──────────────────────┐        ┌──────────────────────┐
                   │ S3 bucket (private)  │◄───────│ CloudFront (HTTPS)   │
                   │ releases/<ver>/...   │  OAC   │ downloads.koshki…    │
                   └──────────────────────┘        └──────────┬───────────┘
                                                              │
                                                  player ◄────┘
```

- **No long-lived AWS keys** end up in GitHub. The workflow assumes an IAM
  role via [GitHub's OIDC provider](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect).
- The S3 bucket is **private**. Only CloudFront can read it (via Origin
  Access Control + a bucket policy).
- Artifacts live at `releases/<version>/<filename>` and are served with a
  long, immutable cache. A small `releases/latest.json` manifest is updated
  on each non-prerelease publish so a website can discover the current
  version without listing the bucket.

---

## One-time AWS bootstrap

You only do these steps once per AWS account.

### 1. Create / sign in to your AWS account

Use the AWS founders credit account. Create an **IAM user with `AdministratorAccess`**
just for the Terraform bootstrap (you can disable / delete it once the infra
is up).

### 2. Install tools locally

| Tool | Min version | Install |
|---|---|---|
| Terraform | 1.6 | https://developer.hashicorp.com/terraform/install |
| AWS CLI v2 | latest | https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html |

```bash
aws configure   # paste the bootstrap user's access key + secret + region
aws sts get-caller-identity   # sanity check
```

### 3. Provision the infrastructure

```bash
cd infra/aws
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars        # set bucket_name (must be globally unique)

terraform init
terraform plan
terraform apply
```

`terraform apply` prints something like:

```
Outputs:

aws_region                  = "us-east-1"
cloudfront_distribution_id  = "E1A2B3C4D5E6F7"
cloudfront_domain_name      = "d1234abcd.cloudfront.net"
github_actions_role_arn     = "arn:aws:iam::123456789012:role/cordite-github-actions-releases"
public_download_base_url    = "https://d1234abcd.cloudfront.net"
s3_bucket_arn               = "arn:aws:s3:::cordite-releases-koshkikode"
s3_bucket_name              = "cordite-releases-koshkikode"
```

Keep this output around — you'll paste a few values into GitHub next.

### 4. Wire up GitHub

In **Settings → Secrets and variables → Actions → Variables**, add the
following **repository variables** (not secrets — they're not sensitive,
and the workflow needs to log them for debugging):

| Variable | Value |
|---|---|
| `AWS_RELEASES_BUCKET` | `s3_bucket_name` from Terraform output |
| `AWS_RELEASES_ROLE_ARN` | `github_actions_role_arn` from Terraform output |
| `AWS_REGION` | `aws_region` from Terraform output (default `us-east-1`) |
| `AWS_CLOUDFRONT_DISTRIBUTION_ID` | `cloudfront_distribution_id` from Terraform output (optional — enables cache invalidation) |

Then create an **environment** named `release`:

1. **Settings → Environments → New environment → `release`**
2. (Recommended) **Required reviewers** → add yourself, so AWS pushes need a manual click.
3. (Recommended) **Deployment branches and tags** → "Selected branches and tags" → allow `v*.*.*` tag patterns and the `main` branch only.

The Terraform `assume_role` policy already restricts the OIDC trust to
`environment:release`, so even if a workflow tries to assume the role from
another environment AWS will reject it.

---

## (Optional) Custom domain — `downloads.koshkikode.com`

You have two paths depending on where DNS for `koshkikode.com` lives.

### Path A — Route 53 manages the zone

1. In `terraform.tfvars`, set:
   ```hcl
   domain_name     = "downloads.koshkikode.com"
   route53_zone_id = "Z0123456789ABCDEFGHIJ"   # from Route 53 → Hosted zones
   ```
2. `terraform apply`. ACM cert validation, the ALIAS A/AAAA records, and the
   CloudFront alias are all created automatically. Allow ~5–15 minutes for
   the cert to validate and CloudFront to redeploy.

### Path B — DNS is at another registrar (Cloudflare, Namecheap, etc.)

1. In `terraform.tfvars`, set `domain_name` but leave `route53_zone_id = ""`.
2. `terraform apply` once. It will fail at `aws_acm_certificate_validation`,
   but `terraform plan` / the AWS console will show the DNS validation
   `CNAME` record(s) it needs.
3. Add those `CNAME` records at your DNS provider.
4. Re-run `terraform apply`. Validation succeeds, distribution updates.
5. At your DNS provider, add a `CNAME`:
   ```
   downloads.koshkikode.com  CNAME  d1234abcd.cloudfront.net
   ```
   (Some providers — Cloudflare, Route 53 itself — also support apex/ALIAS records.)

### Verify

```bash
curl -I https://downloads.koshkikode.com/releases/latest.json
# HTTP/2 200
# content-type: application/json
# x-cache: Hit from cloudfront
```

---

## Releasing

Once everything is wired up, the day-to-day flow is unchanged:

```bash
python3 bump-version.py patch
git commit -am "Release v0.1.1"
git tag v0.1.1
git push origin main --tags
```

Then:

1. `release.yml` runs (existing workflow) — builds, publishes the GitHub Release.
2. The **`release.published`** event triggers `deploy-aws.yml`.
3. (If you enabled "Required reviewers" on the `release` environment) GitHub
   waits for your approval.
4. `deploy-aws.yml` downloads the release assets, syncs them to
   `s3://<bucket>/releases/0.1.1/`, refreshes `releases/latest.json`, and
   invalidates the CloudFront cache.

To re-publish an older release manually:

> **Actions → Deploy Release Artifacts to AWS → Run workflow → tag = v0.1.0**

---

## Embedding download links on your website

After publish, files live at:

```
https://downloads.koshkikode.com/releases/0.1.1/CorditeWars-Setup.exe
https://downloads.koshkikode.com/releases/0.1.1/CorditeWars_0.1.1_linux_x86_64.tar.gz
https://downloads.koshkikode.com/releases/0.1.1/CorditeWars.dmg
…
```

The `releases/latest.json` manifest lets a static site auto-detect the current
version:

```js
const r = await fetch("https://downloads.koshkikode.com/releases/latest.json");
const { version, base_url } = await r.json();
const winUrl = `https://downloads.koshkikode.com/${base_url}CorditeWars-Setup.exe`;
```

---

## Cost & quotas

Rough back-of-envelope for ~10 GB of artifacts and a few hundred downloads/month:

| Item | Estimate |
|---|---|
| S3 storage (10 GB) | ~$0.23 / month |
| CloudFront egress (~50 GB) | ~$4.25 / month |
| Requests | < $0.10 / month |
| **Total** | **~$5 / month** |

Easily within the AWS founders credit. Watch the AWS billing dashboard for
the first few releases to confirm.

---

## Tearing it down

```bash
cd infra/aws
terraform destroy
```

You will need to **empty the S3 bucket first** (versioned buckets are not
auto-deleted by Terraform). Fastest way:

```bash
aws s3 rm "s3://<bucket-name>" --recursive
aws s3api delete-objects --bucket <bucket-name> \
  --delete "$(aws s3api list-object-versions --bucket <bucket-name> \
    --query '{Objects: Versions[].{Key:Key, VersionId:VersionId}}')"
```

---

## Troubleshooting

**`AccessDenied` when the workflow assumes the role**
: Check that you created the `release` environment in GitHub and that the
  workflow runs from a `v*` tag or the `main` branch. The Terraform default
  `github_oidc_subjects` only trusts those.

**`AccessDenied` on `s3:PutObject`**
: The IAM role only has write access to keys under `releases/`. Make sure
  the workflow is uploading to that prefix (it is, by default).

**CloudFront still serves the old `latest.json`**
: Either set `AWS_CLOUDFRONT_DISTRIBUTION_ID` so the workflow invalidates,
  or wait out the default TTL (60 s for `latest.json` per the workflow's
  `Cache-Control` header).

**ACM cert stuck in `PENDING_VALIDATION`**
: DNS validation `CNAME`s are missing or wrong. Run `terraform plan` and
  copy the exact records from `aws_acm_certificate.releases[0].domain_validation_options`.
