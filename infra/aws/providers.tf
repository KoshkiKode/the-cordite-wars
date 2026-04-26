# Default provider — used for S3, IAM, etc. Region is configurable.
provider "aws" {
  region             = var.region
  allowed_account_ids = ["261142221895"]

  default_tags {
    tags = {
      Project    = "cordite-wars"
      Component  = "release-hosting"
      ManagedBy  = "terraform"
      Repository = "KoshkiKode/cordite"
    }
  }
}

# Aliased provider pinned to us-east-1.
# CloudFront requires its ACM certificates to live in us-east-1, regardless
# of where the rest of the infra is deployed.
provider "aws" {
  alias               = "us_east_1"
  region              = "us-east-1"
  allowed_account_ids = ["261142221895"]

  default_tags {
    tags = {
      Project    = "cordite-wars"
      Component  = "release-hosting"
      ManagedBy  = "terraform"
      Repository = "KoshkiKode/cordite"
    }
  }
}
