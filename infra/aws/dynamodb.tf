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

###############################################################################
# DynamoDB — licenses table.
#
# One row per issued license key. Created by the Stripe webhook handler when
# a checkout completes; read on every activation request to confirm the key
# was actually issued by us and isn't revoked.
#
# We never store the raw key — only `key_hash` (salted SHA-256). The
# `key_id` (the random uint32 embedded in the key itself) is the primary
# index and is what activations look up by.
###############################################################################

resource "aws_dynamodb_table" "licenses" {
  name         = "${var.bucket_name}-licenses"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "key_id"

  attribute {
    name = "key_id"
    type = "S"
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}

###############################################################################
# DynamoDB — machine_slots table.
#
# Composite key: (key_id, machine_id). Up to MAX_SLOTS_PER_LICENSE (10) rows
# per key_id at any one time, enforced by the activation Lambda via
# conditional puts. `released_at` is set when a slot is freed (manually or by
# the inactivity sweep), and `ttl_at` then garbage-collects the row 30 days
# later so the user sees a clean slate when re-installing.
###############################################################################

resource "aws_dynamodb_table" "machine_slots" {
  name         = "${var.bucket_name}-machine-slots"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "key_id"
  range_key    = "machine_id"

  attribute {
    name = "key_id"
    type = "S"
  }
  attribute {
    name = "machine_id"
    type = "S"
  }

  ttl {
    attribute_name = "ttl_at"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}
