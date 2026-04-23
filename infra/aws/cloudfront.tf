###############################################################################
# CloudFront distribution — public CDN in front of the private S3 bucket.
#
# Two origins:
#   * S3 (origin path /public)         — serves the static downloads page,
#                                        the `releases/latest.json` manifest,
#                                        and any other free assets.
#   * Lambda Function URL (/api/*)     — handles Stripe Checkout creation,
#                                        webhook ingestion, and presigned-URL
#                                        issuance for paid artifacts.
###############################################################################

resource "aws_cloudfront_origin_access_control" "releases" {
  name                              = "${var.bucket_name}-oac"
  description                       = "OAC for Cordite Wars release artifacts"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

locals {
  has_custom_domain = var.domain_name != ""
  cdn_aliases       = local.has_custom_domain ? [var.domain_name] : []
  # Lambda Function URLs are of the form
  # https://<id>.lambda-url.<region>.on.aws/ — strip the scheme + trailing
  # slash to get a hostname suitable for a CloudFront origin.
  lambda_origin_host = replace(replace(aws_lambda_function_url.paywall.function_url, "https://", ""), "/", "")
}

resource "aws_cloudfront_distribution" "releases" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "Cordite Wars release artifacts"
  price_class         = var.price_class
  http_version        = "http2and3"
  default_root_object = "index.html"

  aliases = local.cdn_aliases

  # --- Static origin (S3, scoped to /public) ------------------------------
  origin {
    domain_name              = aws_s3_bucket.releases.bucket_regional_domain_name
    origin_id                = "s3-releases-public"
    origin_access_control_id = aws_cloudfront_origin_access_control.releases.id
    origin_path              = "/public"
  }

  # --- Dynamic origin (Lambda Function URL) -------------------------------
  origin {
    domain_name = local.lambda_origin_host
    origin_id   = "lambda-paywall"

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  # --- Default behavior: serve static assets from S3 ----------------------
  default_cache_behavior {
    target_origin_id       = "s3-releases-public"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true

    # AWS-managed "CachingOptimized" policy — long TTLs, no query strings/cookies forwarded.
    # The static page + manifest are deliberately re-uploaded on each release
    # (and invalidated by the deploy workflow), so long TTLs are safe.
    cache_policy_id = "658327ea-f89d-4fab-a63d-7e88639e58f6"
  }

  # --- /api/* behavior: forward to Lambda, never cache --------------------
  ordered_cache_behavior {
    path_pattern           = "/api/*"
    target_origin_id       = "lambda-paywall"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true

    # Managed "CachingDisabled" policy.
    cache_policy_id = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad"
    # Managed "AllViewerExceptHostHeader" origin request policy — forwards
    # query strings, cookies, and most headers (Stripe needs Stripe-Signature)
    # but rewrites Host so the Lambda Function URL accepts the request.
    origin_request_policy_id = "b689b0a8-53d0-40ab-baf2-68738e2966ac"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = !local.has_custom_domain
    acm_certificate_arn            = local.has_custom_domain ? aws_acm_certificate_validation.releases[0].certificate_arn : null
    ssl_support_method             = local.has_custom_domain ? "sni-only" : null
    minimum_protocol_version       = local.has_custom_domain ? "TLSv1.2_2021" : "TLSv1"
  }
}
