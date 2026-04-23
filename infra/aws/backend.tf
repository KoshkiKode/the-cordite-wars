# Default to local state — works fine for a solo maintainer.
#
# To switch to remote state, manually create an S3 bucket + DynamoDB lock table
# (they cannot be managed by this same config — chicken and egg) and uncomment
# the block below.
#
# terraform {
#   backend "s3" {
#     bucket         = "cordite-tfstate"
#     key            = "release-hosting/terraform.tfstate"
#     region         = "us-east-1"
#     dynamodb_table = "cordite-tfstate-lock"
#     encrypt        = true
#   }
# }
