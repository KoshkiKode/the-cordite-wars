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
    ORDERS_TABLE              = aws_dynamodb_table.orders.name
    BUCKET_NAME               = aws_s3_bucket.releases.bucket
    STRIPE_SECRET_ARN         = aws_secretsmanager_secret.stripe.arn
    STRIPE_PRICE_ID           = var.stripe_price_id
    STRIPE_PRODUCT_NAME       = var.stripe_product_name
    DOWNLOAD_URL_TTL_SECONDS  = tostring(var.download_url_ttl_seconds)
    DOWNLOAD_REDEMPTION_LIMIT = tostring(var.download_redemption_limit)
    ORDER_RETENTION_DAYS      = tostring(var.order_retention_days)
    PUBLIC_BASE_PATH          = local.has_custom_domain ? "https://${var.domain_name}" : ""
    LATEST_MANIFEST_KEY       = "public/releases/latest.json"
  }
}

# Bundle the handler. No dependencies → just zip the source directory.
data "archive_file" "paywall" {
  type        = "zip"
  source_dir  = "${path.module}/lambda"
  output_path = "${path.module}/.build/paywall.zip"
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
}

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
  description      = "Stripe Checkout + presigned-URL gateway for Cordite Wars downloads."
  role             = aws_iam_role.paywall.arn
  runtime          = "python3.12"
  handler          = "handler.handle"
  filename         = data.archive_file.paywall.output_path
  source_code_hash = data.archive_file.paywall.output_base64sha256
  timeout          = 15
  memory_size      = 256
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
