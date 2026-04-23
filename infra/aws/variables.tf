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
