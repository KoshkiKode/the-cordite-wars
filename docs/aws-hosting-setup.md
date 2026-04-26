# AWS Release Hosting — Setup Guide

This walkthrough takes a brand-new AWS account from zero to *"buyers can pay
on `https://downloads.koshkikode.com/` and instantly download Cordite Wars."*

It is the human-facing companion to the Terraform config in
[`infra/aws/`](../infra/aws/) and the GitHub Actions workflow in
[`.github/workflows/deploy-aws.yml`](../.github/workflows/deploy-aws.yml).

---

## Architecture

```
   git push v0.1.1 ──► release.yml ──► GitHub Release
                                              │  release.published
                                              ▼
                                       deploy-aws.yml
                                       (OIDC role — no keys)
                                              │
                                              ▼
                       ┌──────────────────────────────────────┐
                       │  S3 bucket (private, versioned)      │
                       │   public/index.html                  │
                       │   public/releases/latest.json        │
                       │   paid/<version>/<artifact>          │
                       └──────────────┬───────────────────────┘
                                      │ OAC, /public/* only
                                      ▼
   buyer ──► CloudFront (HTTPS, /api/* + /*) ──► Lambda paywall
                                                        │
                                                        ├─ Stripe Checkout
                                                        ├─ DynamoDB orders
                                                        └─ S3 presigned URLs
                                                            (paid/* only)
```

- **No long-lived AWS keys** in GitHub. The deploy workflow assumes an IAM
  role via [GitHub's OIDC provider](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect).
- The S3 bucket is **private**. CloudFront can read **only the `public/` prefix**
  (downloads page + manifest) — the `paid/` prefix is unreachable from the CDN
  and is only fetched via Lambda-issued presigned URLs that expire in minutes.
- A **Stripe Checkout Session** is the only credential a buyer needs. The
  session ID is persisted in the buyer's `localStorage`; the Lambda enforces
  payment status, redemption budget, and TTL via DynamoDB.

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

### 3. Create the Stripe product

In the [Stripe dashboard](https://dashboard.stripe.com/):

1. **Products → Add product** → name it (e.g. *Cordite Wars: Six Fronts*),
   set a **one-time** price (e.g. `$14.99 USD`).
2. Click into the new price. Copy the **price ID** — it looks like
   `price_1PxyzABC123`.
3. **Developers → API keys** → copy the **Secret key** (`sk_test_…` for
   testing, `sk_live_…` for production).
4. Webhook signing secret comes later — see step 6.

### 4. Provision the infrastructure

```bash
cd infra/aws
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars        # set bucket_name + stripe_price_id

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
github_actions_role_arn     = "arn:aws:iam::123…/role/cordite-…-releases"
lambda_function_name        = "cordite-releases-koshkikode-paywall"
lambda_function_url         = "https://abc123.lambda-url.us-east-1.on.aws/"
orders_table_name           = "cordite-releases-koshkikode-orders"
public_download_base_url    = "https://d1234abcd.cloudfront.net"
s3_bucket_name              = "cordite-releases-koshkikode"
stripe_secret_arn           = "arn:aws:secretsmanager:…:secret:cordite-…/stripe-XYZ"
stripe_webhook_url          = "https://d1234abcd.cloudfront.net/api/webhook"
```

Keep this output handy.

### 5. Paste the Stripe API key into Secrets Manager

The Terraform creates the secret with placeholders so the Lambda fails fast
if you forget. Replace them:

```bash
aws secretsmanager put-secret-value \
  --secret-id "$(terraform output -raw stripe_secret_arn)" \
  --secret-string '{"api_key":"sk_test_…","webhook_secret":"REPLACE_AFTER_STEP_6"}'
```

Or via the console: **Secrets Manager → cordite-…/stripe → Retrieve secret value
→ Edit**.

### 6. Register the Stripe webhook

1. **Stripe dashboard → Developers → Webhooks → Add endpoint**.
2. Endpoint URL: the value of `stripe_webhook_url` from `terraform output`
   (e.g. `https://downloads.koshkikode.com/api/webhook` once you've set up the
   custom domain — see below).
3. Events to send: `checkout.session.completed`.
4. After saving, Stripe shows a **Signing secret** that begins with `whsec_…`.
   Paste it back into Secrets Manager:

   ```bash
   aws secretsmanager put-secret-value \
     --secret-id "$(terraform output -raw stripe_secret_arn)" \
     --secret-string '{"api_key":"sk_test_…","webhook_secret":"whsec_…"}'
   ```

The Lambda caches the secret per cold-start, so updates take effect within a
minute or so.

### 7. Wire up GitHub

In **Settings → Secrets and variables → Actions → Variables**, add the
following **repository variables** (not secrets — they're not sensitive):

| Variable | Value |
|---|---|
| `AWS_RELEASES_BUCKET` | `s3_bucket_name` |
| `AWS_RELEASES_ROLE_ARN` | `github_actions_role_arn` |
| `AWS_REGION` | `aws_region` |
| `AWS_CLOUDFRONT_DISTRIBUTION_ID` | `cloudfront_distribution_id` (optional — enables cache invalidation) |

Then create an **environment** named `release`:

1. **Settings → Environments → New environment → `release`**
2. (Recommended) **Required reviewers** → add yourself.
3. (Recommended) **Deployment branches and tags** → "Selected branches and tags"
   → allow `v*.*.*` tag patterns and the `main` branch only.

The Terraform `assume_role` policy already restricts the OIDC trust to
`environment:release`.

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
   CloudFront alias are all created automatically. Allow ~5–15 minutes.

### Path B — DNS is at another registrar (Cloudflare, Namecheap, etc.)

1. In `terraform.tfvars`, set `domain_name` but leave `route53_zone_id = ""`.
2. `terraform apply` once. It will fail at `aws_acm_certificate_validation`,
   but `terraform plan` / the AWS console will show the DNS validation
   `CNAME` record(s) it needs.
3. Add those `CNAME` records at your DNS provider.
4. Re-run `terraform apply`.
5. At your DNS provider, add a `CNAME`:
   ```
   downloads.koshkikode.com  CNAME  d1234abcd.cloudfront.net
   ```

### Verify

```bash
curl -I https://downloads.koshkikode.com/
# HTTP/2 200
# content-type: text/html; charset=utf-8

curl -I https://downloads.koshkikode.com/releases/latest.json
# HTTP/2 200
# content-type: application/json

curl -fsS https://downloads.koshkikode.com/api/health
# {"ok": true}
```

After updating the domain, **edit the Stripe webhook endpoint** (step 6) so
its URL points at the custom domain, and update the signing secret in Secrets
Manager if Stripe rotated it.

---

## Releasing

The day-to-day flow is unchanged from non-paywalled releases:

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
4. `deploy-aws.yml`:
   - downloads release assets,
   - syncs them to `s3://<bucket>/paid/0.1.1/` (private — never CDN-served),
   - re-uploads `web/downloads/index.html` to `s3://<bucket>/public/index.html`,
   - writes a manifest with file sizes + sha256 to `s3://<bucket>/public/releases/latest.json`,
   - invalidates `/`, `/index.html`, `/releases/latest.json` on CloudFront.

To re-publish an older release manually:

> **Actions → Deploy Release Artifacts to AWS → Run workflow → tag = v0.1.0**

---

## How the paywall actually works

| Step | Where | Notes |
|---|---|---|
| 1. Visitor lands on `/` | CloudFront → S3 `public/index.html` | Anonymous; sees a "Buy & download" CTA. |
| 2. Click "Buy & download" | Page POSTs `/api/checkout` | Lambda creates a Stripe Checkout Session, returns its URL. |
| 3. Browser redirected to Stripe | Stripe-hosted page | We never see card data. |
| 4. Payment completes | Stripe sends `checkout.session.completed` to `/api/webhook` | Lambda verifies the `Stripe-Signature` header against the signing secret and writes a row to DynamoDB (`paid=true, redemptions=0`). |
| 5. Stripe redirects buyer back to `/?session_id=cs_…` | Page persists `session_id` to `localStorage` | No accounts. |
| 6. Click any platform's download | Page calls `/api/download?session_id=…&filename=…` | Lambda atomically increments `redemptions` (subject to `< limit`), validates filename against the current manifest, and 302s to a short-TTL S3 presigned URL. |
| 7. File downloads from S3 directly | — | Bandwidth bypasses Lambda. |

Tunables (in `terraform.tfvars`):

- `download_url_ttl_seconds` (default `900`) — how long a presigned URL works.
- `download_redemption_limit` (default `10`) — total downloads per paid order
  (covers reinstalls + multi-platform).
- `order_retention_days` (default `90`) — DynamoDB TTL on orders.

---

## (Optional) Hosting the marketing site on AWS Amplify

The repo contains two separate web assets:

| Path | What it is | Hosted on |
|---|---|---|
| `web/downloads/` | Paywalled downloads page + licence manager | **S3 → CloudFront** (deployed by `deploy-aws.yml`) |
| `cordite-site-temp/` | Static marketing site (hero, videos, screenshots + paywall CTA) | **AWS Amplify** (separate) |

The marketing site calls the same Lambda endpoints (`/api/checkout`,
`/api/download`) and the same `releases/latest.json` manifest, so it
must be able to reach the CloudFront distribution.

### One-time Amplify setup

1. **Create an Amplify app** in the [AWS Amplify console](https://console.aws.amazon.com/amplify):
   - **New app → Host web app → GitHub** → connect `KoshkiKode/cordite`
   - **Branch**: `main` (or whichever branch you want to publish)
   - **App root directory**: `cordite-site-temp`

2. **Build settings** — Amplify auto-detects `cordite-site-temp/amplify.yml`.
   No extra build commands are needed; the site is pure static HTML/JS.

3. **Configure API rewrites** (required when Amplify is on a different domain
   from CloudFront):

   In the Amplify console → **App → Hosting → Rewrites and redirects**,
   add two rules:

   | Source address | Target address | Type |
   |---|---|---|
   | `/api/<*>` | `https://<CLOUDFRONT_DOMAIN>/api/<*>` | `200` (Rewrite) |
   | `/releases/<*>` | `https://<CLOUDFRONT_DOMAIN>/releases/<*>` | `200` (Rewrite) |

   Replace `<CLOUDFRONT_DOMAIN>` with the `cloudfront_domain_name` (or your
   custom domain) from `terraform output` in `infra/aws/`.

   **Alternative — set `API_BASE` in `app.js` instead of rewrites:**
   Open `cordite-site-temp/app.js` and set the `API_BASE` constant at the top
   to the full CloudFront URL:
   ```js
   const API_BASE = "https://d1234abcd.cloudfront.net";
   ```
   Rewrites keep `app.js` origin-agnostic; the `API_BASE` approach is simpler
   for a single environment.

4. **Custom domain (optional)** — in Amplify console → **Domain management**,
   add e.g. `koshkikode.com`. Amplify provisions the ACM certificate and creates
   the CloudFront distribution automatically.

5. **Deploy** — push to the configured branch and Amplify builds + publishes
   automatically. You can also trigger a manual deploy from the console.

### AWS services used

| Service | Purpose |
|---|---|
| **S3** | Private origin bucket for release artifacts and static downloads page |
| **CloudFront** | Public CDN; serves static page + routes `/api/*` to Lambda |
| **Lambda** | Paywall logic: Stripe Checkout, webhook, presigned-URL issuance, licence keys |
| **DynamoDB** | Order tracking and licence records |
| **Secrets Manager** | Stripe API key + webhook signing secret; Ed25519 licence signing keypair |
| **SES** (optional) | Licence-key delivery email |
| **ACM** (optional) | TLS certificate for a custom domain on CloudFront |
| **Route 53** (optional) | DNS automation for the custom domain |
| **AWS Amplify** (optional) | Hosts the `cordite-site-temp` marketing site |
| **IAM + OIDC** | Keyless GitHub Actions deploy via federated identity |

---

## Embedding download links elsewhere

Because downloads are paywalled, the only public URL you should embed is the
landing page itself:

```
https://downloads.koshkikode.com/
```

The `releases/latest.json` manifest is still publicly readable so you can
display the current version on koshkikode.com:

```js
const r = await fetch("https://downloads.koshkikode.com/releases/latest.json");
const { version, files } = await r.json();
console.log(`Latest: v${version} (${files.length} files)`);
```

---

## Cost & quotas

Rough back-of-envelope for a small launch (~10 GB of artifacts, 1k buyers,
average ~3 downloads each):

| Item | Estimate |
|---|---|
| S3 storage (10 GB) | ~$0.23 / month |
| S3 GET (presigned, ~3k) | < $0.01 |
| CloudFront egress (~150 GB) | ~$13 |
| Lambda invocations (~10k) | < $0.10 |
| DynamoDB on-demand (~10k writes/reads) | < $0.05 |
| Secrets Manager (1 secret) | $0.40 |
| **Total** | **~$14 / month** at this volume |

Stripe takes its standard processing fee on top.

---

## Tearing it down

```bash
cd infra/aws
terraform destroy
```

You will need to **empty the S3 bucket first** (versioned buckets are not
auto-deleted by Terraform). Fastest way:

```bash
BUCKET=cordite-releases-koshkikode
aws s3 rm "s3://${BUCKET}" --recursive
aws s3api delete-objects --bucket "$BUCKET" \
  --delete "$(aws s3api list-object-versions --bucket "$BUCKET" \
    --query '{Objects: Versions[].{Key:Key, VersionId:VersionId}}')"
```

Secrets Manager has a 7-day recovery window — `terraform destroy` schedules
deletion; the secret is gone for good a week later.

---

## Troubleshooting

**`AccessDenied` when the workflow assumes the role**
: Check that you created the `release` environment in GitHub and that the
  workflow runs from a `v*` tag or the `main` branch. The Terraform default
  `github_oidc_subjects` only trusts those.

**`AccessDenied` on `s3:PutObject`**
: The IAM role only has write access to keys under `paid/` and `public/`.
  Make sure the workflow is uploading to those prefixes (it is, by default).

**CloudFront still serves the old `latest.json` or page**
: Either set `AWS_CLOUDFRONT_DISTRIBUTION_ID` so the workflow invalidates,
  or wait out the default TTL (60 s for `latest.json` per the workflow's
  `Cache-Control` header; ≤5 min for the page).

**ACM cert stuck in `PENDING_VALIDATION`**
: DNS validation `CNAME`s are missing or wrong. Run `terraform plan` and
  copy the exact records from `aws_acm_certificate.releases[0].domain_validation_options`.

**Stripe webhook arriving but order isn't recorded**
: Tail the Lambda's CloudWatch log group (`/aws/lambda/<bucket>-paywall`).
  Most common causes: webhook signing secret in Secrets Manager doesn't match
  the one Stripe shows, or the endpoint URL in Stripe still points at an old
  CloudFront hostname.

**Buyer paid, sees "Order not found, not paid, or download limit reached"**
: That's the Lambda's deliberately-vague 403. Check the order in DynamoDB
  (`orders_table_name`); if it isn't there, the webhook didn't arrive (see
  above). If `redemptions >= limit`, raise `download_redemption_limit` and
  re-`apply`.

**Testing locally without paying**
: Use Stripe **test mode** keys (`sk_test_…` + a test webhook signing secret).
  The Lambda accepts both `sk_test_…` and `sk_live_…`. Use Stripe's CLI to
  forward webhooks to the live endpoint while testing card numbers like
  `4242 4242 4242 4242`.
