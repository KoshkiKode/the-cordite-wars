output "s3_bucket_name" {
  description = "Name of the S3 bucket holding release artifacts. Set this as the `AWS_RELEASES_BUCKET` GitHub Actions variable."
  value       = aws_s3_bucket.releases.bucket
}

output "s3_bucket_arn" {
  description = "ARN of the releases S3 bucket."
  value       = aws_s3_bucket.releases.arn
}

output "cloudfront_distribution_id" {
  description = "CloudFront distribution ID. Set this as the `AWS_CLOUDFRONT_DISTRIBUTION_ID` GitHub Actions variable so the deploy workflow can issue invalidations."
  value       = aws_cloudfront_distribution.releases.id
}

output "cloudfront_domain_name" {
  description = "Default *.cloudfront.net hostname for the distribution. Use this if you didn't set a custom domain."
  value       = aws_cloudfront_distribution.releases.domain_name
}

output "public_download_base_url" {
  description = "Base URL to embed on koshkikode.com. Append `releases/<version>/<filename>`."
  value       = local.has_custom_domain ? "https://${var.domain_name}" : "https://${aws_cloudfront_distribution.releases.domain_name}"
}

output "github_actions_role_arn" {
  description = "IAM role ARN that the GitHub Actions deploy workflow assumes via OIDC. Set this as the `AWS_RELEASES_ROLE_ARN` GitHub Actions variable."
  value       = aws_iam_role.github_actions_releases.arn
}

output "aws_region" {
  description = "AWS region of the S3 bucket. Set this as the `AWS_REGION` GitHub Actions variable."
  value       = var.region
}

###############################################################################
# Paywall outputs
###############################################################################

output "orders_table_name" {
  description = "DynamoDB table holding paid orders."
  value       = aws_dynamodb_table.orders.name
}

output "lambda_function_name" {
  description = "Name of the paywall Lambda."
  value       = aws_lambda_function.paywall.function_name
}

output "lambda_function_url" {
  description = "Direct invocation URL for the Lambda (mainly useful for local debugging — production traffic goes via CloudFront /api/*)."
  value       = aws_lambda_function_url.paywall.function_url
}

output "stripe_secret_arn" {
  description = "ARN of the Secrets Manager secret holding the Stripe API key + webhook signing secret. Update its value in the AWS console after `terraform apply`."
  value       = aws_secretsmanager_secret.stripe.arn
}

output "stripe_webhook_url" {
  description = "URL to register as a Stripe webhook endpoint (events: checkout.session.completed). Listens for paid orders."
  value       = local.has_custom_domain ? "https://${var.domain_name}/api/webhook" : "https://${aws_cloudfront_distribution.releases.domain_name}/api/webhook"
}

###############################################################################
# Licensing outputs
###############################################################################

output "licenses_table_name" {
  description = "DynamoDB table holding issued license keys (hashed)."
  value       = aws_dynamodb_table.licenses.name
}

output "machine_slots_table_name" {
  description = "DynamoDB table holding per-license machine activations (10-machine cap)."
  value       = aws_dynamodb_table.machine_slots.name
}

output "license_signing_secret_arn" {
  description = "ARN of the Secrets Manager secret holding the Ed25519 license signing keypair. Generate with tools/license_keygen.py and paste in via the AWS console."
  value       = aws_secretsmanager_secret.license_signing.arn
}

output "ses_dkim_records" {
  description = "DKIM CNAME records that must be added to your DNS provider for the From-domain when SES is enabled. Empty when ses_from_address is unset."
  value = local.ses_enabled ? [
    for token in aws_sesv2_email_identity.from[0].dkim_signing_attributes[0].tokens : {
      name  = "${token}._domainkey.${local.ses_from_domain}"
      type  = "CNAME"
      value = "${token}.dkim.amazonses.com"
    }
  ] : []
}

output "ses_feedback_topic_arn" {
  description = "SNS topic that receives SES bounce/complaint events. Subscribe to it for monitoring."
  value       = local.ses_enabled ? aws_sns_topic.ses_feedback[0].arn : ""
}
