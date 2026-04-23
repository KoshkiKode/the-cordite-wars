# AWS Release Hosting — Cordite Wars

This directory contains the [Terraform](https://developer.hashicorp.com/terraform)
configuration for the AWS infrastructure that hosts Cordite Wars release
artifacts (`.exe`, `.msi`, `.dmg`, `.apk`, `.aab`, `.tar.gz`, …) on
KoshkiKode's own website instead of relying on GitHub Releases for downloads.

## What this provisions

| Resource | Purpose |
|---|---|
| **S3 bucket** (`aws_s3_bucket.releases`) | Private origin that stores the actual files under `releases/<version>/<filename>`. Versioning + lifecycle rules enabled. |
| **CloudFront distribution** (`aws_cloudfront_distribution.releases`) | Public, HTTPS-terminating CDN. The only thing allowed to read the S3 bucket. |
| **Origin Access Control** (`aws_cloudfront_origin_access_control.releases`) | Locks the S3 bucket so only this CloudFront distribution can fetch from it. |
| **GitHub OIDC provider + IAM role** (`aws_iam_openid_connect_provider.github`, `aws_iam_role.github_actions_releases`) | Lets the `deploy-aws` GitHub Actions workflow assume an AWS role via OIDC — **no long-lived AWS access keys** stored in GitHub. |
| **(optional) ACM certificate + Route 53 records** | If you set `domain_name`, a TLS cert is issued in `us-east-1` and CloudFront is bound to e.g. `downloads.koshkikode.com`. |

The full setup walkthrough — including DNS, the one-time bootstrap, and how to
wire it into GitHub Actions — lives in
[`docs/aws-hosting-setup.md`](../../docs/aws-hosting-setup.md).

## Quick start

```bash
cd infra/aws

# 1. Configure your AWS credentials locally (one of):
#    - aws configure
#    - export AWS_PROFILE=koshkikode
#    - aws sso login

# 2. Copy the example tfvars and fill in your values
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars

# 3. Initialise and apply
terraform init
terraform plan
terraform apply
```

After `apply`, Terraform prints the values you need to paste into GitHub
(repository **Variables** and **Secrets**):

- `cloudfront_domain_name` → public CDN hostname
- `s3_bucket_name`         → bucket the workflow uploads to
- `github_actions_role_arn`→ IAM role the workflow assumes via OIDC

## Cost (rough order of magnitude)

For a small game release with infrequent downloads:

- S3 storage: **\$0.023 / GB-month**
- CloudFront egress (first 1 TB/month, North America): **\$0.085 / GB**
- Requests: negligible at this scale

A handful of multi-GB releases downloaded a few hundred times per month
typically lands in single-digit dollars/month, well within the AWS founders
credit.

## Backend / state

For a single-maintainer project the default **local** state file is fine.
If you ever want shared/remote state, uncomment the `backend "s3"` block in
[`backend.tf`](./backend.tf) after creating a state bucket + DynamoDB lock
table manually (chicken-and-egg: that bucket can't be managed by this same
config).
