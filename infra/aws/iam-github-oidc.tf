###############################################################################
# GitHub OIDC — lets the deploy-aws workflow assume an IAM role without
# any long-lived access keys stored in repository secrets.
###############################################################################

# A single OIDC provider per AWS account is enough; if you've already created
# one for another project, set `create_oidc_provider = false` and import or
# reference it instead. Keeping it inline here for a one-shot bootstrap.
resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]

  # GitHub publishes the up-to-date list of thumbprints here:
  # https://github.blog/changelog/2023-06-27-github-actions-update-on-oidc-integration-with-aws/
  # Modern IAM evaluation actually validates the JWKS, but the field is required.
  thumbprint_list = [
    "6938fd4d98bab03faadb97b34396831e3780aea1",
    "1c58a3a8518e8759bf075b76b750d4f2df264fcd",
  ]
}

data "aws_iam_policy_document" "github_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values = [
        for s in var.github_oidc_subjects :
        "repo:${var.github_owner}/${var.github_repo}:${s}"
      ]
    }
  }
}

resource "aws_iam_role" "github_actions_releases" {
  name                 = "cordite-github-actions-releases"
  description          = "Assumed by KoshkiKode/cordite GitHub Actions to publish release artifacts to S3."
  assume_role_policy   = data.aws_iam_policy_document.github_assume_role.json
  max_session_duration = 3600
}

# Least-privilege policy: write to the releases bucket (paid + public prefixes)
# and invalidate CloudFront.
data "aws_iam_policy_document" "github_releases_permissions" {
  statement {
    sid    = "S3WriteReleases"
    effect = "Allow"
    actions = [
      "s3:PutObject",
      "s3:PutObjectAcl",
      "s3:GetObject",
      "s3:DeleteObject",
      "s3:AbortMultipartUpload",
      "s3:ListMultipartUploadParts",
    ]
    resources = [
      "${aws_s3_bucket.releases.arn}/paid/*",
      "${aws_s3_bucket.releases.arn}/public/*",
    ]
  }

  statement {
    sid       = "S3ListBucket"
    effect    = "Allow"
    actions   = ["s3:ListBucket", "s3:GetBucketLocation"]
    resources = [aws_s3_bucket.releases.arn]

    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["paid/*", "paid", "public/*", "public"]
    }
  }

  statement {
    sid    = "CloudFrontInvalidate"
    effect = "Allow"
    actions = [
      "cloudfront:CreateInvalidation",
      "cloudfront:GetInvalidation",
      "cloudfront:ListInvalidations",
    ]
    resources = [aws_cloudfront_distribution.releases.arn]
  }
}

resource "aws_iam_role_policy" "github_actions_releases" {
  name   = "publish-release-artifacts"
  role   = aws_iam_role.github_actions_releases.id
  policy = data.aws_iam_policy_document.github_releases_permissions.json
}
