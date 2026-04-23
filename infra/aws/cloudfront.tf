###############################################################################
# CloudFront distribution — public CDN in front of the private S3 bucket.
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
}

resource "aws_cloudfront_distribution" "releases" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "Cordite Wars release artifacts"
  price_class         = var.price_class
  http_version        = "http2and3"
  default_root_object = ""

  aliases = local.cdn_aliases

  origin {
    domain_name              = aws_s3_bucket.releases.bucket_regional_domain_name
    origin_id                = "s3-releases"
    origin_access_control_id = aws_cloudfront_origin_access_control.releases.id
  }

  default_cache_behavior {
    target_origin_id       = "s3-releases"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true

    # AWS-managed "CachingOptimized" policy — long TTLs, no query strings/cookies forwarded.
    # Release artifacts are immutable per version, so this is ideal.
    cache_policy_id = "658327ea-f89d-4fab-a63d-7e88639e58f6"
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
