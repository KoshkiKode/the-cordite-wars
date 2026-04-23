###############################################################################
# Optional custom domain: ACM certificate (us-east-1) + Route 53 records.
# Only created when var.domain_name is non-empty.
###############################################################################

resource "aws_acm_certificate" "releases" {
  count = local.has_custom_domain ? 1 : 0

  provider = aws.us_east_1

  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

# DNS validation records — only created automatically when a Route 53 zone
# is provided. If you manage DNS elsewhere, copy the `domain_validation_options`
# from `terraform plan` and add them at your DNS provider, then re-run apply.
resource "aws_route53_record" "cert_validation" {
  for_each = local.has_custom_domain && var.route53_zone_id != "" ? {
    for dvo in aws_acm_certificate.releases[0].domain_validation_options :
    dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  } : {}

  zone_id         = var.route53_zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 60
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "releases" {
  count = local.has_custom_domain ? 1 : 0

  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.releases[0].arn
  validation_record_fqdns = var.route53_zone_id != "" ? [for r in aws_route53_record.cert_validation : r.fqdn] : null
}

# ALIAS record pointing the custom domain at CloudFront.
resource "aws_route53_record" "releases_alias" {
  count = local.has_custom_domain && var.route53_zone_id != "" ? 1 : 0

  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.releases.domain_name
    zone_id                = aws_cloudfront_distribution.releases.hosted_zone_id
    evaluate_target_health = false
  }
}

resource "aws_route53_record" "releases_alias_aaaa" {
  count = local.has_custom_domain && var.route53_zone_id != "" ? 1 : 0

  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = "AAAA"

  alias {
    name                   = aws_cloudfront_distribution.releases.domain_name
    zone_id                = aws_cloudfront_distribution.releases.hosted_zone_id
    evaluate_target_health = false
  }
}
