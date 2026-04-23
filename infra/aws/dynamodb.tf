###############################################################################
# DynamoDB — orders table.
#
# One item per Stripe Checkout Session. Inserted by the Lambda when a
# `checkout.session.completed` webhook arrives, then read on every download
# request to verify the order is paid and within its redemption budget.
###############################################################################

resource "aws_dynamodb_table" "orders" {
  name         = "${var.bucket_name}-orders"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "session_id"

  attribute {
    name = "session_id"
    type = "S"
  }

  ttl {
    attribute_name = "expires_at"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}
