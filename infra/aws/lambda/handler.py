"""
Cordite Wars paywall — AWS Lambda handler.

Routes (all reachable through CloudFront's /api/* behavior):

  POST /api/checkout
      Body: {"return_url": "https://..."}        (optional)
      Creates a Stripe Checkout Session for the configured price and returns
      {"url": "https://checkout.stripe.com/..."} for the browser to follow.

  POST /api/webhook
      Stripe webhook target. Verifies the Stripe-Signature header against the
      shared signing secret and, on `checkout.session.completed`, marks the
      order as paid in DynamoDB.

  GET  /api/download?session_id=cs_test_...&filename=CorditeWars.exe
      Verifies the order is paid + still has redemption budget, then returns
      a 302 to a short-TTL S3 presigned URL pointing at
      s3://<bucket>/paid/<version>/<filename>.

Design constraints:
  * No external Python dependencies — boto3 ships with the runtime, and
    Stripe is hit via urllib + hmac for signature verification.
  * No user accounts / no admin: the Stripe Checkout Session ID returned to
    the browser is the only credential needed to download.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import logging
import os
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

import boto3
from botocore.config import Config as BotoConfig

import licensing  # noqa: E402 — sibling module, license-key endpoints

LOG = logging.getLogger()
LOG.setLevel(logging.INFO)

# --- Configuration (env vars set by Terraform) -------------------------------

ORDERS_TABLE = os.environ["ORDERS_TABLE"]
BUCKET_NAME = os.environ["BUCKET_NAME"]
STRIPE_SECRET_ARN = os.environ["STRIPE_SECRET_ARN"]
STRIPE_PRICE_ID = os.environ["STRIPE_PRICE_ID"]
STRIPE_PRODUCT_NAME = os.environ.get("STRIPE_PRODUCT_NAME", "Cordite Wars")
DOWNLOAD_URL_TTL = int(os.environ.get("DOWNLOAD_URL_TTL_SECONDS", "900"))
DOWNLOAD_REDEMPTION_LIMIT = int(os.environ.get("DOWNLOAD_REDEMPTION_LIMIT", "10"))
ORDER_RETENTION_DAYS = int(os.environ.get("ORDER_RETENTION_DAYS", "90"))
LATEST_MANIFEST_KEY = os.environ.get("LATEST_MANIFEST_KEY", "public/releases/latest.json")
PUBLIC_BASE_PATH = os.environ.get("PUBLIC_BASE_PATH", "")  # e.g. https://downloads.example.com

STRIPE_API_BASE = "https://api.stripe.com/v1"
STRIPE_TIMESTAMP_TOLERANCE = 5 * 60  # 5 minutes — Stripe's recommended default.

# --- AWS clients (cold-start scoped) -----------------------------------------

# SigV4 with addressing_style=virtual avoids legacy path-style URLs that
# break for buckets with dots in the name.
_S3 = boto3.client(
    "s3",
    config=BotoConfig(signature_version="s3v4", s3={"addressing_style": "virtual"}),
)
_DYNAMODB = boto3.client("dynamodb")
_SECRETS = boto3.client("secretsmanager")

_STRIPE_CACHE: dict[str, str] | None = None
_MANIFEST_CACHE: dict[str, Any] | None = None
_MANIFEST_CACHE_AT: float = 0.0
_MANIFEST_TTL = 30  # seconds


# --- Tiny helpers ------------------------------------------------------------


def _stripe_creds() -> dict[str, str]:
    global _STRIPE_CACHE
    if _STRIPE_CACHE is None:
        raw = _SECRETS.get_secret_value(SecretId=STRIPE_SECRET_ARN)["SecretString"]
        creds = json.loads(raw)
        if not creds.get("api_key", "").startswith(("sk_live_", "sk_test_")):
            raise RuntimeError(
                "Stripe API key in Secrets Manager looks unset (placeholder still in place)."
            )
        if not creds.get("webhook_secret", "").startswith("whsec_"):
            raise RuntimeError("Stripe webhook secret in Secrets Manager looks unset.")
        _STRIPE_CACHE = creds
    return _STRIPE_CACHE


def _response(status: int, body: Any, headers: dict[str, str] | None = None) -> dict[str, Any]:
    payload = body if isinstance(body, str) else json.dumps(body)
    base = {"Content-Type": "application/json", "Cache-Control": "no-store"}
    if headers:
        base.update(headers)
    return {"statusCode": status, "headers": base, "body": payload}


def _redirect(url: str) -> dict[str, Any]:
    return {
        "statusCode": 302,
        "headers": {"Location": url, "Cache-Control": "no-store"},
        "body": "",
    }


def _origin_from_event(event: dict[str, Any]) -> str:
    """Best-effort origin URL inference from the inbound request headers."""
    if PUBLIC_BASE_PATH:
        return PUBLIC_BASE_PATH.rstrip("/")
    headers = {k.lower(): v for k, v in (event.get("headers") or {}).items()}
    host = headers.get("x-forwarded-host") or headers.get("host")
    proto = headers.get("x-forwarded-proto", "https")
    if host:
        return f"{proto}://{host}"
    return ""  # caller is responsible for handling


def _stripe_post(path: str, form: dict[str, str], api_key: str) -> dict[str, Any]:
    """POST application/x-www-form-urlencoded to the Stripe REST API."""
    body = urllib.parse.urlencode(form).encode("utf-8")
    req = urllib.request.Request(
        f"{STRIPE_API_BASE}{path}",
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/x-www-form-urlencoded",
            "Stripe-Version": "2024-06-20",
        },
    )
    with urllib.request.urlopen(req, timeout=10) as resp:  # noqa: S310 — fixed https URL
        return json.loads(resp.read().decode("utf-8"))


def _read_body(event: dict[str, Any]) -> bytes:
    body = event.get("body") or ""
    if event.get("isBase64Encoded"):
        return base64.b64decode(body)
    return body.encode("utf-8") if isinstance(body, str) else bytes(body)


def _verify_stripe_signature(payload: bytes, signature_header: str, secret: str) -> bool:
    """Constant-time verify a Stripe webhook signature header (`Stripe-Signature`)."""
    if not signature_header:
        return False
    timestamp: str | None = None
    sigs: list[str] = []
    for part in signature_header.split(","):
        if "=" not in part:
            continue
        k, v = part.split("=", 1)
        if k == "t":
            timestamp = v
        elif k == "v1":
            sigs.append(v)
    if not timestamp or not sigs:
        return False
    try:
        ts = int(timestamp)
    except ValueError:
        return False
    if abs(time.time() - ts) > STRIPE_TIMESTAMP_TOLERANCE:
        LOG.warning("Stripe webhook timestamp outside tolerance.")
        return False
    signed = f"{timestamp}.".encode() + payload
    expected = hmac.new(secret.encode(), signed, hashlib.sha256).hexdigest()
    return any(hmac.compare_digest(expected, s) for s in sigs)


def _load_manifest() -> dict[str, Any]:
    """Fetch + cache the public release manifest."""
    global _MANIFEST_CACHE, _MANIFEST_CACHE_AT
    now = time.time()
    if _MANIFEST_CACHE is not None and (now - _MANIFEST_CACHE_AT) < _MANIFEST_TTL:
        return _MANIFEST_CACHE
    try:
        obj = _S3.get_object(Bucket=BUCKET_NAME, Key=LATEST_MANIFEST_KEY)
        manifest = json.loads(obj["Body"].read().decode("utf-8"))
    except _S3.exceptions.NoSuchKey:
        manifest = {"version": None, "files": []}
    _MANIFEST_CACHE = manifest
    _MANIFEST_CACHE_AT = now
    return manifest


def _safe_filename(name: str) -> bool:
    """Allow only flat filenames — no path traversal, no slashes."""
    if not name or len(name) > 256:
        return False
    if "/" in name or "\\" in name or ".." in name:
        return False
    # Printable ASCII only (release artifact names are ASCII by convention).
    return all(0x20 < ord(c) < 0x7F for c in name)


# --- Route handlers ----------------------------------------------------------


def handle_checkout(event: dict[str, Any]) -> dict[str, Any]:
    creds = _stripe_creds()
    origin = _origin_from_event(event)
    if not origin:
        return _response(400, {"error": "Cannot determine origin URL"})

    body_raw = _read_body(event)
    parsed: dict[str, Any] = {}
    if body_raw:
        try:
            decoded = json.loads(body_raw.decode("utf-8"))
            if isinstance(decoded, dict):
                parsed = decoded
        except (UnicodeDecodeError, json.JSONDecodeError):
            return _response(400, {"error": "Invalid JSON body"})

    return_url = parsed.get("return_url")
    base = (return_url or origin).rstrip("/")
    success_url = f"{base}/?session_id={{CHECKOUT_SESSION_ID}}"
    cancel_url = f"{base}/?canceled=1"

    form = {
        "mode": "payment",
        "line_items[0][price]": STRIPE_PRICE_ID,
        "line_items[0][quantity]": "1",
        "success_url": success_url,
        "cancel_url": cancel_url,
        "automatic_tax[enabled]": "false",
        "allow_promotion_codes": "true",
        "metadata[product]": STRIPE_PRODUCT_NAME,
    }
    try:
        session = _stripe_post("/checkout/sessions", form, creds["api_key"])
    except urllib.error.HTTPError as err:
        detail = err.read().decode("utf-8", errors="replace")
        LOG.error("Stripe /checkout/sessions failed: %s %s", err.code, detail)
        return _response(502, {"error": "Stripe error", "status": err.code})

    return _response(200, {"url": session["url"], "session_id": session["id"]})


def handle_webhook(event: dict[str, Any]) -> dict[str, Any]:
    creds = _stripe_creds()
    payload = _read_body(event)
    headers = {k.lower(): v for k, v in (event.get("headers") or {}).items()}
    signature = headers.get("stripe-signature", "")
    if not _verify_stripe_signature(payload, signature, creds["webhook_secret"]):
        LOG.warning("Webhook signature verification failed.")
        return _response(400, {"error": "Invalid signature"})

    try:
        evt = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return _response(400, {"error": "Invalid event body"})

    if evt.get("type") != "checkout.session.completed":
        return _response(200, {"ignored": evt.get("type")})

    session = evt.get("data", {}).get("object", {}) or {}
    if session.get("payment_status") != "paid":
        return _response(200, {"ignored": "not paid"})
    session_id = session.get("id")
    if not session_id:
        return _response(400, {"error": "Missing session id"})

    expires_at = int(time.time()) + ORDER_RETENTION_DAYS * 86400
    customer_email = (
        (session.get("customer_details") or {}).get("email")
        or session.get("customer_email")
        or ""
    ).strip().lower()

    # Idempotent license issuance: only fire on the first webhook for this
    # session. We store the issued key_id in the orders row so re-deliveries
    # don't issue duplicate keys + emails.
    license_key: str | None = None
    if customer_email:
        try:
            license_key = licensing.issue_license_for_session(
                session_id=session_id,
                email=customer_email,
                amount_total=int(session.get("amount_total") or 0),
                currency=session.get("currency") or "usd",
            )
        except Exception:  # pragma: no cover — surfaced via logs, won't 500 the webhook
            LOG.exception("License issuance failed for session=%s", session_id)
    else:
        LOG.warning("Stripe session %s had no customer email; license not issued", session_id)

    item: dict[str, Any] = {
        "session_id":   {"S": session_id},
        "paid":         {"BOOL": True},
        "redemptions":  {"N": "0"},
        "amount_total": {"N": str(session.get("amount_total") or 0)},
        "currency":     {"S": session.get("currency") or "usd"},
        "created_at":   {"N": str(int(time.time()))},
        "expires_at":   {"N": str(expires_at)},
    }
    if customer_email:
        item["email"] = {"S": customer_email}
    if license_key:
        # Store the *hashed* key for cross-reference; never the raw key.
        item["license_key_hash"] = {"S": licensing._hash_key_for_storage(license_key)}

    _DYNAMODB.put_item(TableName=ORDERS_TABLE, Item=item)
    LOG.info("Order recorded for session %s", session_id)
    return _response(200, {"ok": True})


def handle_download(event: dict[str, Any]) -> dict[str, Any]:
    qs = event.get("queryStringParameters") or {}
    session_id = (qs.get("session_id") or "").strip()
    filename = (qs.get("filename") or "").strip()

    if not session_id or not session_id.startswith("cs_"):
        return _response(400, {"error": "Missing or invalid session_id"})
    if not _safe_filename(filename):
        return _response(400, {"error": "Missing or invalid filename"})

    # Conditional update: only succeed if the order is paid AND we haven't
    # blown through the redemption budget. Atomic increment avoids races.
    try:
        result = _DYNAMODB.update_item(
            TableName=ORDERS_TABLE,
            Key={"session_id": {"S": session_id}},
            UpdateExpression="SET redemptions = redemptions + :one",
            ConditionExpression=(
                "attribute_exists(session_id) AND paid = :true AND redemptions < :limit"
            ),
            ExpressionAttributeValues={
                ":one":   {"N": "1"},
                ":true":  {"BOOL": True},
                ":limit": {"N": str(DOWNLOAD_REDEMPTION_LIMIT)},
            },
            ReturnValues="UPDATED_NEW",
        )
    except _DYNAMODB.exceptions.ConditionalCheckFailedException:
        # Either the order doesn't exist, isn't paid yet (webhook race), or
        # it's exhausted. Return a generic 403 so we don't leak which.
        return _response(
            403, {"error": "Order not found, not paid, or download limit reached"}
        )

    # Validate the requested filename actually exists in the current release.
    try:
        manifest = _load_manifest()
    except Exception:  # pragma: no cover — surfaced via 503
        LOG.exception("Failed to load manifest")
        return _response(503, {"error": "Release manifest unavailable"})

    version = manifest.get("version")
    files = {f.get("name") for f in (manifest.get("files") or []) if isinstance(f, dict)}
    if not version or filename not in files:
        return _response(404, {"error": "Unknown filename for current release"})

    key = f"paid/{version}/{filename}"
    try:
        url = _S3.generate_presigned_url(
            "get_object",
            Params={
                "Bucket": BUCKET_NAME,
                "Key": key,
                "ResponseContentDisposition": f'attachment; filename="{filename}"',
            },
            ExpiresIn=DOWNLOAD_URL_TTL,
        )
    except Exception:
        LOG.exception("Failed to generate presigned URL for %s", key)
        return _response(500, {"error": "Failed to sign download URL"})

    LOG.info(
        "Granted download for session=%s file=%s redemptions=%s",
        session_id,
        filename,
        result.get("Attributes", {}).get("redemptions", {}).get("N"),
    )
    return _redirect(url)


# --- Entry point -------------------------------------------------------------


def _route(event: dict[str, Any]) -> dict[str, Any]:
    # Lambda Function URL events come in API Gateway v2 ("HTTP API") shape.
    rc = event.get("requestContext") or {}
    method = (rc.get("http") or {}).get("method") or event.get("httpMethod") or "GET"
    raw_path = event.get("rawPath") or event.get("path") or "/"
    path = raw_path.rstrip("/") or "/"

    if method == "OPTIONS":
        return _response(204, "", {"Allow": "GET, POST, OPTIONS"})

    if path.endswith("/api/checkout") and method == "POST":
        return handle_checkout(event)
    if path.endswith("/api/webhook") and method == "POST":
        return handle_webhook(event)
    if path.endswith("/api/download") and method == "GET":
        return handle_download(event)
    if path.endswith("/api/activate") and method == "POST":
        return licensing.handle_activate(event)
    if path.endswith("/api/activate-offline") and method == "POST":
        # Same logic, different endpoint name so the website can present a
        # distinct UX for users activating from a different device.
        return licensing.handle_activate(event)
    if path.endswith("/api/renew") and method == "POST":
        return licensing.handle_renew(event)
    if path.endswith("/api/deactivate") and method == "POST":
        return licensing.handle_deactivate(event)
    if path.endswith("/api/manage") and method == "GET":
        return licensing.handle_manage(event)
    if path.endswith("/api/health") and method == "GET":
        return _response(200, {"ok": True})

    return _response(404, {"error": "Not found", "path": path, "method": method})


def handle(event: dict[str, Any], _context: Any) -> dict[str, Any]:
    # EventBridge cron events have no `requestContext`; route them to the
    # background slot-sweep handler.
    if event.get("source") == "aws.events" or event.get("detail-type") == "Scheduled Event":
        return licensing.handle_slot_sweep(event)
    try:
        return _route(event)
    except Exception as exc:  # pragma: no cover — last-resort guard
        LOG.exception("Unhandled error: %s", exc)
        return _response(500, {"error": "Internal error"})
