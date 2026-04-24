###############################################################################
# Lambda paywall function.
#
# Receives requests from CloudFront's `/api/*` behavior and exposes three
# routes (see `lambda/handler.py`):
#
#   POST /api/checkout    → create Stripe Checkout Session, return {url}
#   POST /api/webhook     → verify Stripe signature, mark order paid
#   GET  /api/download    → 302 to short-TTL S3 presigned URL
#
# Zero non-stdlib runtime deps: `boto3` is bundled in the Python Lambda
# runtime, and Stripe is called via `urllib` + `hmac`.
###############################################################################

# Pulls the bucket name into the function's env so the same code can be
# reused if the bucket is renamed.
locals {
  lambda_env = {
    ORDERS_TABLE                    = aws_dynamodb_table.orders.name
    LICENSES_TABLE                  = aws_dynamodb_table.licenses.name
    MACHINE_SLOTS_TABLE             = aws_dynamodb_table.machine_slots.name
    BUCKET_NAME                     = aws_s3_bucket.releases.bucket
    STRIPE_SECRET_ARN               = aws_secretsmanager_secret.stripe.arn
    STRIPE_PRICE_ID                 = var.stripe_price_id
    STRIPE_PRODUCT_NAME             = var.stripe_product_name
    DOWNLOAD_URL_TTL_SECONDS        = tostring(var.download_url_ttl_seconds)
    DOWNLOAD_REDEMPTION_LIMIT       = tostring(var.download_redemption_limit)
    ORDER_RETENTION_DAYS            = tostring(var.order_retention_days)
    PUBLIC_BASE_PATH                = local.has_custom_domain ? "https://${var.domain_name}" : ""
    LATEST_MANIFEST_KEY             = "public/releases/latest.json"
    LICENSE_SIGNING_SECRET_ARN      = aws_secretsmanager_secret.license_signing.arn
    SES_FROM_ADDRESS                = var.ses_from_address
    SES_CONFIGURATION_SET           = var.ses_from_address != "" ? aws_sesv2_configuration_set.licenses[0].configuration_set_name : ""
    SLOT_INACTIVITY_RECLAIM_SECONDS = tostring(var.slot_inactivity_reclaim_days * 86400)
  }
}

# Vendor `cryptography` (and its native dependencies) into the Lambda zip.
# We pull the manylinux wheel that matches the Lambda arm64 runtime so the
# native bits load without compilation. `null_resource` runs pip when the
# requirements file or the source changes.
resource "null_resource" "lambda_deps" {
  triggers = {
    requirements_hash = filesha256("${path.module}/lambda/requirements.txt")
    handler_hash      = filesha256("${path.module}/lambda/handler.py")
    licensing_hash    = filesha256("${path.module}/lambda/licensing.py")
    keys_hash         = filesha256("${path.module}/lambda/license_keys.py")
  }

  provisioner "local-exec" {
    command = <<-EOT
      set -e
      rm -rf "${path.module}/.build/pkg"
      mkdir -p "${path.module}/.build/pkg"
      cp "${path.module}/lambda/handler.py" \
         "${path.module}/lambda/licensing.py" \
         "${path.module}/lambda/license_keys.py" \
         "${path.module}/.build/pkg/"
      python3 -m pip install \
        --target "${path.module}/.build/pkg" \
        --platform manylinux2014_aarch64 \
        --implementation cp \
        --python-version 3.12 \
        --only-binary=:all: \
        --no-compile \
        -r "${path.module}/lambda/requirements.txt"
    EOT
  }
}

# Bundle the handler + vendored dependencies. Depends on the build step so
# the zip is regenerated whenever the source or requirements change.
data "archive_file" "paywall" {
  type        = "zip"
  source_dir  = "${path.module}/.build/pkg"
  output_path = "${path.module}/.build/paywall.zip"
  depends_on  = [null_resource.lambda_deps]
}

resource "aws_iam_role" "paywall" {
  name        = "${var.bucket_name}-paywall"
  description = "Execution role for the Cordite Wars paywall Lambda."

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "paywall_basic_logs" {
  role       = aws_iam_role.paywall.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

data "aws_iam_policy_document" "paywall" {
  # Read the Stripe API key + webhook secret.
  statement {
    sid       = "StripeSecretRead"
    effect    = "Allow"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.stripe.arn]
  }

  # Read the Ed25519 license signing key (used to sign keys + entitlements).
  statement {
    sid       = "LicenseSigningSecretRead"
    effect    = "Allow"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.license_signing.arn]
  }

  # Track + look up orders.
  statement {
    sid    = "OrdersTable"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
    ]
    resources = [aws_dynamodb_table.orders.arn]
  }

  # License records: issuance writes, activation reads.
  statement {
    sid    = "LicensesTable"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
    ]
    resources = [aws_dynamodb_table.licenses.arn]
  }

  # Per-machine activation slots (10-machine cap enforced via conditional puts).
  statement {
    sid    = "MachineSlotsTable"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
      "dynamodb:Query",
      "dynamodb:Scan",
    ]
    resources = [aws_dynamodb_table.machine_slots.arn]
  }

  # Generate presigned URLs for paid artifacts only. Generating a presigned
  # URL is purely a client-side signing operation, but the principal still
  # needs the underlying GetObject permission for the URL to be valid.
  statement {
    sid       = "PaidArtifactRead"
    effect    = "Allow"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.releases.arn}/paid/*"]
  }

  # Fetch the public release manifest so the Lambda can validate that the
  # filename being requested actually exists in the current release.
  statement {
    sid       = "ManifestRead"
    effect    = "Allow"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.releases.arn}/public/releases/*"]
  }

  # Send the license-key delivery email via SES (only when SES is configured).
  dynamic "statement" {
    for_each = var.ses_from_address != "" ? [1] : []
    content {
      sid    = "SesSendLicenseEmail"
      effect = "Allow"
      actions = [
        "ses:SendEmail",
      ]
      # Restrict to the configured From identity + configuration set.
      resources = [
        "arn:aws:ses:${var.region}:${data.aws_caller_identity.current.account_id}:identity/${var.ses_from_address}",
        "arn:aws:ses:${var.region}:${data.aws_caller_identity.current.account_id}:identity/${local.ses_from_domain}",
        aws_sesv2_configuration_set.licenses[0].arn,
      ]
    }
  }
}

data "aws_caller_identity" "current" {}

resource "aws_iam_role_policy" "paywall" {
  name   = "paywall-permissions"
  role   = aws_iam_role.paywall.id
  policy = data.aws_iam_policy_document.paywall.json
}

resource "aws_cloudwatch_log_group" "paywall" {
  name              = "/aws/lambda/${var.bucket_name}-paywall"
  retention_in_days = var.lambda_log_retention_days
}

resource "aws_lambda_function" "paywall" {
  function_name    = "${var.bucket_name}-paywall"
  description      = "Stripe Checkout + license issuance + activation gateway for Cordite Wars."
  role             = aws_iam_role.paywall.arn
  runtime          = "python3.12"
  handler          = "handler.handle"
  filename         = data.archive_file.paywall.output_path
  source_code_hash = data.archive_file.paywall.output_base64sha256
  timeout          = 30
  memory_size      = 512
  architectures    = ["arm64"]

  environment {
    variables = local.lambda_env
  }

  depends_on = [
    aws_iam_role_policy.paywall,
    aws_iam_role_policy_attachment.paywall_basic_logs,
    aws_cloudwatch_log_group.paywall,
  ]
}

# Function URL is the inbound endpoint that CloudFront's `/api/*` behavior
# proxies to. We allow unauthenticated invocations because access is gated
# by Stripe (for /checkout and /webhook) and by Stripe-session lookup (for
# /download); CloudFront is the public entrypoint.
resource "aws_lambda_function_url" "paywall" {
  function_name      = aws_lambda_function.paywall.function_name
  authorization_type = "NONE"
}

# Nightly slot-sweep — releases machine slots that have been inactive
# longer than `slot_inactivity_reclaim_days`. Re-uses the paywall Lambda.
resource "aws_cloudwatch_event_rule" "slot_sweep" {
  name                = "${var.bucket_name}-slot-sweep"
  description         = "Nightly: release inactive license-machine slots."
  schedule_expression = "cron(15 4 * * ? *)" # 04:15 UTC daily
}

resource "aws_cloudwatch_event_target" "slot_sweep" {
  rule      = aws_cloudwatch_event_rule.slot_sweep.name
  target_id = "paywall-lambda"
  arn       = aws_lambda_function.paywall.arn
}

resource "aws_lambda_permission" "slot_sweep_invoke" {
  statement_id  = "AllowEventBridgeSlotSweep"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.paywall.function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.slot_sweep.arn
}
