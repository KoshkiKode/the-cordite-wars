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

###############################################################################
# License signing key — Ed25519 private key used to sign issued license keys
# and entitlement blobs. Generate one with:
#
#     python3 tools/license_keygen.py generate-signing-key
#
# Then paste the PEM into the secret value:
#
#   {
#     "private_key_pem": "-----BEGIN PRIVATE KEY-----\n...",
#     "public_key_pem":  "-----BEGIN PUBLIC KEY-----\n..."
#   }
#
# The matching *public* key (raw 32-byte SubjectPublicKeyInfo) gets embedded
# into the C# game binary at build time — see `src/Core/Licensing/`.
#
# DO NOT regenerate this key after launch — every issued key/entitlement
# becomes invalid, including offline-stored entitlements on customer machines.
###############################################################################

resource "aws_secretsmanager_secret" "license_signing" {
  name                    = "${var.bucket_name}/license-signing"
  description             = "Ed25519 keypair used to sign Cordite Wars license keys + entitlements."
  recovery_window_in_days = 30
}

resource "aws_secretsmanager_secret_version" "license_signing_placeholder" {
  secret_id = aws_secretsmanager_secret.license_signing.id
  secret_string = jsonencode({
    private_key_pem = "REPLACE_WITH_PEM_FROM_tools/license_keygen.py"
    public_key_pem  = "REPLACE_WITH_MATCHING_PUBLIC_KEY_PEM"
  })

  lifecycle {
    ignore_changes = [secret_string]
  }
}
