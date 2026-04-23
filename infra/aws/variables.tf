variable "region" {
  description = "AWS region for the S3 bucket and IAM resources. CloudFront and its ACM cert always live in us-east-1."
  type        = string
  default     = "us-east-1"
}

variable "bucket_name" {
  description = "Globally unique S3 bucket name that will hold release artifacts. Example: 'cordite-releases-koshkikode'."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", var.bucket_name))
    error_message = "bucket_name must be a valid S3 bucket name (lowercase, 3-63 chars, no underscores)."
  }
}

variable "github_owner" {
  description = "GitHub organization or user that owns the repository allowed to assume the deploy role."
  type        = string
  default     = "KoshkiKode"
}

variable "github_repo" {
  description = "Repository name allowed to assume the deploy role via OIDC."
  type        = string
  default     = "cordite"
}

variable "github_oidc_subjects" {
  description = <<-EOT
    List of GitHub OIDC `sub` claim patterns allowed to assume the deploy role.
    Defaults restrict access to release-tag pushes and the deploy workflow on the default branch.
    See: https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect#example-subject-claims
  EOT
  type        = list(string)
  default = [
    "ref:refs/tags/v*",
    "ref:refs/heads/main",
    "environment:release",
  ]
}

variable "domain_name" {
  description = "Optional custom domain to bind to the CloudFront distribution (e.g. 'downloads.koshkikode.com'). Leave empty to use the default *.cloudfront.net hostname."
  type        = string
  default     = ""
}

variable "route53_zone_id" {
  description = "Optional Route 53 hosted zone ID for `domain_name`. If set, an ALIAS record is created automatically. Leave empty to manage DNS yourself (e.g. via your registrar)."
  type        = string
  default     = ""
}

variable "noncurrent_version_retention_days" {
  description = "How long to keep noncurrent (overwritten) object versions in S3 before permanent deletion."
  type        = number
  default     = 90
}

variable "price_class" {
  description = "CloudFront price class. PriceClass_100 = US/Canada/Europe only (cheapest)."
  type        = string
  default     = "PriceClass_100"

  validation {
    condition     = contains(["PriceClass_100", "PriceClass_200", "PriceClass_All"], var.price_class)
    error_message = "price_class must be one of PriceClass_100, PriceClass_200, or PriceClass_All."
  }
}

###############################################################################
# Paywall configuration
###############################################################################

variable "stripe_price_id" {
  description = <<-EOT
    Stripe Price ID (e.g. 'price_1Pxyz...') for a one-time purchase that grants
    download access to the current release on every supported platform. Create
    the Product + Price in the Stripe dashboard first, then paste the ID here.
  EOT
  type        = string
}

variable "stripe_currency" {
  description = "Currency code (lowercase ISO 4217) used by the Stripe Price. Informational only — used in Checkout Session metadata."
  type        = string
  default     = "usd"
}

variable "stripe_product_name" {
  description = "Human-readable product name shown in the success message and as Stripe Checkout fallback."
  type        = string
  default     = "Cordite Wars: Six Fronts"
}

variable "download_url_ttl_seconds" {
  description = "TTL (seconds) for the S3 presigned download URL handed out after a successful purchase. Keep short — the user will follow it immediately via a 302 redirect."
  type        = number
  default     = 900

  validation {
    condition     = var.download_url_ttl_seconds >= 60 && var.download_url_ttl_seconds <= 3600
    error_message = "download_url_ttl_seconds must be between 60 and 3600 (S3 presign max for SigV4 with role credentials is 1 hour)."
  }
}

variable "download_redemption_limit" {
  description = "Maximum total downloads (across all platforms) allowed per paid Checkout Session before the order is considered exhausted."
  type        = number
  default     = 10

  validation {
    condition     = var.download_redemption_limit >= 1 && var.download_redemption_limit <= 100
    error_message = "download_redemption_limit must be between 1 and 100."
  }
}

variable "order_retention_days" {
  description = "How long completed orders live in DynamoDB before TTL cleanup."
  type        = number
  default     = 90
}

variable "lambda_log_retention_days" {
  description = "Retention for the Lambda's CloudWatch log group."
  type        = number
  default     = 30
}

###############################################################################
# Licensing configuration
###############################################################################

variable "ses_from_address" {
  description = <<-EOT
    From-address used by SES when emailing license keys to buyers, e.g.
    "keys@yourdomain.com". Leave empty to disable email delivery (keys are
    still issued by the Lambda and returned via the API). When set, the
    domain is verified in SES; you must add the DKIM CNAMEs (output as
    `ses_dkim_records`) and a matching SPF/DMARC record to your DNS.
  EOT
  type        = string
  default     = ""

  validation {
    condition = var.ses_from_address == "" || can(regex(
      "^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$",
      var.ses_from_address,
    ))
    error_message = "ses_from_address must be a valid email address or empty."
  }
}

variable "slot_inactivity_reclaim_days" {
  description = <<-EOT
    A machine slot that has not had a successful activate/renew call within
    this many days is automatically released by the nightly sweep, freeing
    a slot on the user's license. This handles reformats and lost machines
    without burning a slot forever.
  EOT
  type        = number
  default     = 30

  validation {
    condition     = var.slot_inactivity_reclaim_days >= 7 && var.slot_inactivity_reclaim_days <= 365
    error_message = "slot_inactivity_reclaim_days must be between 7 and 365."
  }
}
