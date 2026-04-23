###############################################################################
# Stripe credentials — stored in Secrets Manager as a single JSON blob.
#
# Terraform creates the secret with placeholder values. After `terraform apply`
# update the secret value in the AWS console (Secrets Manager → this secret →
# "Retrieve secret value" → "Edit") with the real keys from your Stripe
# dashboard:
#
#   {
#     "api_key":         "sk_live_...",
#     "webhook_secret":  "whsec_..."
#   }
#
# The Lambda reads this on cold start and caches it for the duration of the
# execution environment.
###############################################################################

resource "aws_secretsmanager_secret" "stripe" {
  name                    = "${var.bucket_name}/stripe"
  description             = "Stripe API key + webhook signing secret used by the Cordite Wars paywall Lambda."
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "stripe_placeholder" {
  secret_id = aws_secretsmanager_secret.stripe.id
  secret_string = jsonencode({
    api_key        = "REPLACE_WITH_sk_live_OR_sk_test_KEY"
    webhook_secret = "REPLACE_WITH_whsec_KEY"
  })

  # Don't overwrite the real secret value after the operator fills it in.
  lifecycle {
    ignore_changes = [secret_string]
  }
}
