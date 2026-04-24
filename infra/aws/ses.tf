###############################################################################
# Amazon SES — license-key delivery email.
#
# The Lambda sends one transactional email per purchase containing the
# 25-character license key. We use SESv2 with a configuration set so we
# can route bounces/complaints to SNS for monitoring.
#
# Domain identity setup is the operator's responsibility:
#
#   1. Set `ses_from_address` in terraform.tfvars (e.g. "keys@example.com").
#   2. After `terraform apply`, take the DKIM tokens from
#      `aws_sesv2_email_identity.from.dkim_signing_attributes` and add the
#      three CNAMEs to your DNS provider (Route53 not assumed here so we
#      surface them as outputs).
#   3. Add an SPF TXT record + a DMARC policy to the same domain.
#   4. Request production access in the SES console (sandbox = 200 emails/day,
#      verified-recipient-only).
#
# When `ses_from_address` is empty, none of these resources are created and
# the Lambda gracefully skips email sending (the key is still issued and
# returned by the API).
###############################################################################

locals {
  ses_enabled     = var.ses_from_address != ""
  ses_from_domain = local.ses_enabled ? element(split("@", var.ses_from_address), 1) : ""
}

# Verify the *domain* (not just the address) so DKIM works and so we don't
# need a per-address verification step for every From we might use.
resource "aws_sesv2_email_identity" "from" {
  count          = local.ses_enabled ? 1 : 0
  email_identity = local.ses_from_domain

  dkim_signing_attributes {
    next_signing_key_length = "RSA_2048_BIT"
  }
}

resource "aws_sesv2_configuration_set" "licenses" {
  count                  = local.ses_enabled ? 1 : 0
  configuration_set_name = "${var.bucket_name}-licenses"

  delivery_options {
    tls_policy = "REQUIRE"
  }

  reputation_options {
    reputation_metrics_enabled = true
  }

  sending_options {
    sending_enabled = true
  }
}

# Bounce + complaint feedback → SNS topic. An operator can subscribe an
# email address (or PagerDuty / Slack via Lambda) to this topic to get
# alerted when a license email bounces.
resource "aws_sns_topic" "ses_feedback" {
  count = local.ses_enabled ? 1 : 0
  name  = "${var.bucket_name}-ses-feedback"
}

resource "aws_sesv2_configuration_set_event_destination" "feedback" {
  count                  = local.ses_enabled ? 1 : 0
  configuration_set_name = aws_sesv2_configuration_set.licenses[0].configuration_set_name
  event_destination_name = "feedback-to-sns"

  event_destination {
    enabled = true
    matching_event_types = [
      "BOUNCE",
      "COMPLAINT",
      "DELIVERY_DELAY",
      "REJECT",
    ]
    sns_destination {
      topic_arn = aws_sns_topic.ses_feedback[0].arn
    }
  }
}
