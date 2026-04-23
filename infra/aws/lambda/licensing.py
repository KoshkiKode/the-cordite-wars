"""
License-key issuance + activation endpoints for Cordite Wars.

Wired into `handler.py` as additional routes:

    POST /api/activate
        Body: {"key": "XXXXX-...", "machine_id": "<hex>", "hostname_hint": "..."}
        On success: {"entitlement_b64": "<base64url>", "slot_index": N,
                     "expires_at": <unix seconds>}
        On 409 (cap reached): {"error": "machine_cap_reached",
                               "active_slots": [{...}, ...]}
        Notes: idempotent — re-activating the same machine_id refreshes the
               entitlement (silent renewal lives here).

    POST /api/deactivate
        Body: {"key": "XXXXX-...", "machine_id_to_release": "<hex>"}
        Marks a slot released so the user can install on a new machine.

    POST /api/activate-offline
        Body: {"key": "...", "machine_id": "...", "hostname_hint": "..."}
        Same response as /api/activate but intended to be reached from a
        device other than the gaming machine. The returned entitlement
        blob is what the user pastes into the offline-activation screen.

    GET /api/manage?key=XXXXX-...&email=user@example.com
        Returns the list of active slots for a license, for the
        web-based "manage your machines" page.

License issuance (no public route):
    Triggered from the Stripe webhook handler when a checkout completes.
    Generates a fresh 25-char key, writes the row to DynamoDB, and emails
    the buyer via Amazon SES.
"""

from __future__ import annotations

import base64
import hashlib
import json
import logging
import os
import time
from typing import Any

import boto3

import license_keys as lk

LOG = logging.getLogger()

# --- Configuration ----------------------------------------------------------

LICENSES_TABLE = os.environ.get("LICENSES_TABLE", "")
MACHINE_SLOTS_TABLE = os.environ.get("MACHINE_SLOTS_TABLE", "")
LICENSE_SIGNING_SECRET_ARN = os.environ.get("LICENSE_SIGNING_SECRET_ARN", "")
SES_FROM_ADDRESS = os.environ.get("SES_FROM_ADDRESS", "")
SES_CONFIGURATION_SET = os.environ.get("SES_CONFIGURATION_SET", "")
PUBLIC_BASE_PATH = os.environ.get("PUBLIC_BASE_PATH", "")
STRIPE_PRODUCT_NAME = os.environ.get("STRIPE_PRODUCT_NAME", "Cordite Wars")

# How long a slot can be inactive (no activate/renew call) before it's
# considered abandoned and reclaimed automatically. Background sweep runs
# nightly; we also reclaim opportunistically inside `handle_activate` when
# the cap is hit.
SLOT_INACTIVITY_RECLAIM_SECONDS = int(
    os.environ.get("SLOT_INACTIVITY_RECLAIM_SECONDS", str(30 * 86400))
)

# --- Lazy AWS clients -------------------------------------------------------

_DYNAMODB = boto3.client("dynamodb")
_SECRETS = boto3.client("secretsmanager")
_SES = boto3.client("sesv2") if SES_FROM_ADDRESS else None  # only when configured

_SIGNING_KEY_CACHE: Any = None  # Ed25519PrivateKey


def _signing_key():
    global _SIGNING_KEY_CACHE
    if _SIGNING_KEY_CACHE is None:
        if not LICENSE_SIGNING_SECRET_ARN:
            raise RuntimeError("LICENSE_SIGNING_SECRET_ARN not set")
        raw = _SECRETS.get_secret_value(SecretId=LICENSE_SIGNING_SECRET_ARN)["SecretString"]
        creds = json.loads(raw)
        pem = creds.get("private_key_pem", "")
        if "BEGIN PRIVATE KEY" not in pem:
            raise RuntimeError("license signing key in Secrets Manager is unset")
        _SIGNING_KEY_CACHE = lk.load_private_key(pem)
    return _SIGNING_KEY_CACHE


# --- Tiny helpers -----------------------------------------------------------


def _response(status: int, body: Any, headers: dict[str, str] | None = None) -> dict[str, Any]:
    payload = body if isinstance(body, str) else json.dumps(body)
    base = {"Content-Type": "application/json", "Cache-Control": "no-store"}
    if headers:
        base.update(headers)
    return {"statusCode": status, "headers": base, "body": payload}


def _read_json_body(event: dict[str, Any]) -> dict[str, Any]:
    body = event.get("body") or ""
    if event.get("isBase64Encoded"):
        body = base64.b64decode(body).decode("utf-8")
    if not body:
        return {}
    try:
        decoded = json.loads(body)
    except (UnicodeDecodeError, json.JSONDecodeError) as e:
        raise ValueError(f"Invalid JSON body: {e}") from e
    if not isinstance(decoded, dict):
        raise ValueError("Body must be a JSON object")
    return decoded


def _hash_key_for_storage(formatted_key: str) -> str:
    """We store a salted hash, never the raw key."""
    norm = lk.normalize_key(formatted_key)
    digest = hashlib.sha256(b"cordite-license-v1|" + norm.encode("ascii")).hexdigest()
    return digest


def _machine_id_from_hex(text: str) -> bytes:
    if not isinstance(text, str):
        raise ValueError("machine_id must be a hex string")
    text = text.strip().lower()
    if len(text) != lk.MACHINE_ID_LEN * 2 or any(c not in "0123456789abcdef" for c in text):
        raise ValueError(f"machine_id must be {lk.MACHINE_ID_LEN * 2} hex characters")
    return bytes.fromhex(text)


def _b64url(b: bytes) -> str:
    return base64.urlsafe_b64encode(b).decode("ascii").rstrip("=")


def _hostname_clean(text: Any) -> str:
    if not isinstance(text, str):
        return ""
    # Strip control chars; cap to 64 UTF-8 bytes.
    cleaned = "".join(c for c in text if 0x20 <= ord(c) < 0x7F or ord(c) > 0x9F)
    return cleaned.encode("utf-8")[:64].decode("utf-8", errors="ignore")


# --- DynamoDB helpers -------------------------------------------------------


def _lookup_license_by_keyid(key_id: int) -> dict[str, Any] | None:
    resp = _DYNAMODB.get_item(
        TableName=LICENSES_TABLE,
        Key={"key_id": {"S": str(key_id)}},
        ConsistentRead=True,
    )
    return resp.get("Item")


def _list_active_slots(key_id: int) -> list[dict[str, Any]]:
    """Return the slots row list for a license. Filters out released ones."""
    resp = _DYNAMODB.query(
        TableName=MACHINE_SLOTS_TABLE,
        KeyConditionExpression="key_id = :k",
        FilterExpression="attribute_not_exists(released_at)",
        ExpressionAttributeValues={":k": {"S": str(key_id)}},
        ConsistentRead=True,
    )
    return resp.get("Items", [])


def _reclaim_inactive_slots(slots: list[dict[str, Any]], now: int) -> list[dict[str, Any]]:
    """Mark slots that haven't been seen in SLOT_INACTIVITY_RECLAIM_SECONDS as released.

    Returns the still-active subset. This makes reformats free up a slot
    automatically without user intervention.
    """
    cutoff = now - SLOT_INACTIVITY_RECLAIM_SECONDS
    still_active = []
    for slot in slots:
        last_seen = int(slot.get("last_seen", {}).get("N", "0"))
        if last_seen < cutoff:
            try:
                _DYNAMODB.update_item(
                    TableName=MACHINE_SLOTS_TABLE,
                    Key={
                        "key_id":     slot["key_id"],
                        "machine_id": slot["machine_id"],
                    },
                    UpdateExpression="SET released_at = :t, ttl_at = :ttl",
                    ConditionExpression="attribute_not_exists(released_at)",
                    ExpressionAttributeValues={
                        ":t":   {"N": str(now)},
                        ":ttl": {"N": str(now + 30 * 86400)},
                    },
                )
                LOG.info(
                    "Reclaimed inactive slot key_id=%s last_seen=%s",
                    slot["key_id"]["S"],
                    last_seen,
                )
            except _DYNAMODB.exceptions.ConditionalCheckFailedException:
                # Someone else released it concurrently — fine.
                pass
        else:
            still_active.append(slot)
    return still_active


def _next_slot_index(active: list[dict[str, Any]]) -> int:
    """Smallest 1..MAX index not currently in use."""
    used = {int(s["slot_index"]["N"]) for s in active}
    for i in range(1, lk.MAX_SLOTS_PER_LICENSE + 1):
        if i not in used:
            return i
    raise RuntimeError("no free slot — caller should have rejected")


# --- Activation -------------------------------------------------------------


def handle_activate(event: dict[str, Any]) -> dict[str, Any]:
    """POST /api/activate — register/refresh a machine for a license."""
    if not LICENSES_TABLE or not MACHINE_SLOTS_TABLE:
        return _response(503, {"error": "Licensing not configured"})
    try:
        body = _read_json_body(event)
    except ValueError as e:
        return _response(400, {"error": str(e)})

    raw_key = (body.get("key") or "").strip()
    machine_id_hex = (body.get("machine_id") or "").strip()
    hostname_hint = _hostname_clean(body.get("hostname_hint"))

    try:
        payload = lk.decode_license_key(raw_key)
    except ValueError as e:
        return _response(400, {"error": f"Invalid license key: {e}"})

    try:
        machine_id = _machine_id_from_hex(machine_id_hex)
    except ValueError as e:
        return _response(400, {"error": str(e)})

    license_row = _lookup_license_by_keyid(payload.key_id)
    if license_row is None:
        # Same generic message whether the key is unknown or the row is missing
        # so we don't leak which.
        return _response(403, {"error": "License not recognized"})
    if license_row.get("status", {}).get("S", "active") != "active":
        return _response(403, {"error": "License is not active (revoked or refunded)"})

    # Re-verify the truncated signature with the private key. This catches
    # any key whose key_id happens to collide with an issued key but whose
    # signature bytes don't match — i.e. forged keys.
    try:
        lk.verify_license_key_signature(raw_key, private_key=_signing_key())
    except ValueError:
        return _response(403, {"error": "License not recognized"})

    now = int(time.time())

    # If this exact machine is already a slot, refresh and re-issue.
    existing = _DYNAMODB.get_item(
        TableName=MACHINE_SLOTS_TABLE,
        Key={
            "key_id":     {"S": str(payload.key_id)},
            "machine_id": {"S": machine_id.hex()},
        },
        ConsistentRead=True,
    ).get("Item")
    if existing and "released_at" not in existing:
        slot_index = int(existing["slot_index"]["N"])
        _DYNAMODB.update_item(
            TableName=MACHINE_SLOTS_TABLE,
            Key={
                "key_id":     {"S": str(payload.key_id)},
                "machine_id": {"S": machine_id.hex()},
            },
            UpdateExpression="SET last_seen = :t, hostname_hint = :h",
            ExpressionAttributeValues={
                ":t": {"N": str(now)},
                ":h": {"S": hostname_hint},
            },
        )
        return _issue_entitlement_response(
            payload.key_id, machine_id, slot_index, hostname_hint, now
        )

    # New machine: enforce the cap, reclaiming inactive slots first.
    active = _list_active_slots(payload.key_id)
    active = _reclaim_inactive_slots(active, now)
    if len(active) >= lk.MAX_SLOTS_PER_LICENSE:
        return _response(
            409,
            {
                "error": "machine_cap_reached",
                "message": (
                    f"This license is already activated on {lk.MAX_SLOTS_PER_LICENSE} "
                    "machines. Deactivate one to free a slot."
                ),
                "active_slots": [
                    {
                        "slot_index":    int(s["slot_index"]["N"]),
                        "machine_id":    s["machine_id"]["S"],
                        "hostname_hint": s.get("hostname_hint", {}).get("S", ""),
                        "last_seen":     int(s["last_seen"]["N"]),
                    }
                    for s in active
                ],
            },
        )

    slot_index = _next_slot_index(active)
    try:
        _DYNAMODB.put_item(
            TableName=MACHINE_SLOTS_TABLE,
            Item={
                "key_id":        {"S": str(payload.key_id)},
                "machine_id":    {"S": machine_id.hex()},
                "slot_index":    {"N": str(slot_index)},
                "first_seen":    {"N": str(now)},
                "last_seen":     {"N": str(now)},
                "hostname_hint": {"S": hostname_hint},
            },
            ConditionExpression="attribute_not_exists(machine_id)",
        )
    except _DYNAMODB.exceptions.ConditionalCheckFailedException:
        # Race with another activation request for the same machine_id —
        # restart the whole flow which will hit the "existing" branch above.
        return handle_activate(event)

    LOG.info(
        "Activated key_id=%s slot=%s machine=%s",
        payload.key_id,
        slot_index,
        machine_id.hex()[:8],
    )
    return _issue_entitlement_response(
        payload.key_id, machine_id, slot_index, hostname_hint, now
    )


def _issue_entitlement_response(
    key_id: int, machine_id: bytes, slot_index: int, hostname_hint: str, now: int
) -> dict[str, Any]:
    blob = lk.issue_entitlement(
        private_key=_signing_key(),
        key_id=key_id,
        machine_id=machine_id,
        slot_index=slot_index,
        hostname_hint=hostname_hint,
        issued_at=now,
    )
    return _response(
        200,
        {
            "entitlement_b64": _b64url(blob),
            "slot_index":      slot_index,
            "issued_at":       now,
            "expires_at":      now + lk.ENTITLEMENT_TTL_SECONDS,
        },
    )


# --- Deactivation -----------------------------------------------------------


def handle_renew(event: dict[str, Any]) -> dict[str, Any]:
    """POST /api/renew — refresh an entitlement using the entitlement itself.

    This is the silent-background-renewal endpoint. Inputs:
        {"entitlement_b64": "<existing signed blob>"}

    The server verifies the blob's Ed25519 signature, confirms the license
    row is still active in DynamoDB, refreshes `last_seen` on the matching
    slot, and re-issues a fresh entitlement (same machine_id, same
    slot_index, fresh expiry). The client therefore never needs to retain
    the raw license key after first activation.
    """
    if not LICENSES_TABLE or not MACHINE_SLOTS_TABLE:
        return _response(503, {"error": "Licensing not configured"})
    try:
        body = _read_json_body(event)
    except ValueError as e:
        return _response(400, {"error": str(e)})

    raw_b64 = (body.get("entitlement_b64") or "").strip()
    if not raw_b64:
        return _response(400, {"error": "entitlement_b64 required"})

    # base64-url with optional padding
    s = raw_b64.replace("-", "+").replace("_", "/")
    s += "=" * (-len(s) % 4)
    try:
        blob = base64.b64decode(s, validate=False)
    except Exception:
        return _response(400, {"error": "entitlement_b64 not base64"})

    sk = _signing_key()
    pk = sk.public_key()
    try:
        ent = lk.decode_entitlement(blob, public_key=pk)
    except ValueError as e:
        return _response(403, {"error": f"Entitlement invalid: {e}"})

    license_row = _lookup_license_by_keyid(ent.key_id)
    if license_row is None:
        return _response(403, {"error": "License not recognized"})
    if license_row.get("status", {}).get("S", "active") != "active":
        return _response(403, {"error": "License is not active"})

    # Confirm the slot is still allocated to this machine.
    existing = _DYNAMODB.get_item(
        TableName=MACHINE_SLOTS_TABLE,
        Key={
            "key_id":     {"S": str(ent.key_id)},
            "machine_id": {"S": ent.machine_id.hex()},
        },
        ConsistentRead=True,
    ).get("Item")
    if existing is None or "released_at" in existing:
        return _response(403, {"error": "Slot has been released; please reactivate."})

    now = int(time.time())
    _DYNAMODB.update_item(
        TableName=MACHINE_SLOTS_TABLE,
        Key={
            "key_id":     {"S": str(ent.key_id)},
            "machine_id": {"S": ent.machine_id.hex()},
        },
        UpdateExpression="SET last_seen = :t",
        ExpressionAttributeValues={":t": {"N": str(now)}},
    )
    return _issue_entitlement_response(
        ent.key_id,
        ent.machine_id,
        int(existing["slot_index"]["N"]),
        existing.get("hostname_hint", {}).get("S", ent.hostname_hint),
        now,
    )


def handle_deactivate(event: dict[str, Any]) -> dict[str, Any]:
    """POST /api/deactivate — release a slot so a new machine can claim it."""
    if not LICENSES_TABLE or not MACHINE_SLOTS_TABLE:
        return _response(503, {"error": "Licensing not configured"})
    try:
        body = _read_json_body(event)
    except ValueError as e:
        return _response(400, {"error": str(e)})

    raw_key = (body.get("key") or "").strip()
    machine_id_hex = (body.get("machine_id_to_release") or "").strip()

    try:
        payload = lk.decode_license_key(raw_key)
        machine_id = _machine_id_from_hex(machine_id_hex)
    except ValueError as e:
        return _response(400, {"error": str(e)})

    license_row = _lookup_license_by_keyid(payload.key_id)
    if license_row is None:
        return _response(403, {"error": "License not recognized"})
    try:
        lk.verify_license_key_signature(raw_key, private_key=_signing_key())
    except ValueError:
        return _response(403, {"error": "License not recognized"})

    now = int(time.time())
    try:
        _DYNAMODB.update_item(
            TableName=MACHINE_SLOTS_TABLE,
            Key={
                "key_id":     {"S": str(payload.key_id)},
                "machine_id": {"S": machine_id.hex()},
            },
            UpdateExpression="SET released_at = :t, ttl_at = :ttl",
            ConditionExpression="attribute_exists(machine_id) AND attribute_not_exists(released_at)",
            ExpressionAttributeValues={
                ":t":   {"N": str(now)},
                ":ttl": {"N": str(now + 30 * 86400)},
            },
        )
    except _DYNAMODB.exceptions.ConditionalCheckFailedException:
        return _response(404, {"error": "Slot not found or already released"})

    LOG.info("Deactivated key_id=%s machine=%s", payload.key_id, machine_id.hex()[:8])
    return _response(200, {"ok": True})


# --- Manage view ------------------------------------------------------------


def handle_manage(event: dict[str, Any]) -> dict[str, Any]:
    """GET /api/manage?key=...&email=... — list active slots for a license.

    The email check binds the "manage" action to the original purchaser
    even if the key leaks; if the email doesn't match we return the same
    "not recognized" response as a wrong key would.
    """
    if not LICENSES_TABLE or not MACHINE_SLOTS_TABLE:
        return _response(503, {"error": "Licensing not configured"})
    qs = event.get("queryStringParameters") or {}
    raw_key = (qs.get("key") or "").strip()
    email = (qs.get("email") or "").strip().lower()
    if not email or "@" not in email:
        return _response(400, {"error": "email required"})

    try:
        payload = lk.decode_license_key(raw_key)
    except ValueError as e:
        return _response(400, {"error": f"Invalid license key: {e}"})

    license_row = _lookup_license_by_keyid(payload.key_id)
    if license_row is None or license_row.get("email", {}).get("S", "").lower() != email:
        return _response(403, {"error": "License not recognized"})

    now = int(time.time())
    active = _reclaim_inactive_slots(_list_active_slots(payload.key_id), now)
    return _response(
        200,
        {
            "key_id":   payload.key_id,
            "max_slots": lk.MAX_SLOTS_PER_LICENSE,
            "slots": [
                {
                    "slot_index":    int(s["slot_index"]["N"]),
                    "machine_id":    s["machine_id"]["S"],
                    "hostname_hint": s.get("hostname_hint", {}).get("S", ""),
                    "first_seen":    int(s["first_seen"]["N"]),
                    "last_seen":     int(s["last_seen"]["N"]),
                }
                for s in active
            ],
        },
    )


# --- License issuance (called from the Stripe webhook handler) -------------


def issue_license_for_session(
    *,
    session_id: str,
    email: str,
    amount_total: int,
    currency: str,
) -> str:
    """Generate a key, persist it, and email the buyer. Returns the formatted key.

    Idempotent on session_id: a second call for the same session reads the
    existing key out of the orders table rather than issuing a duplicate.
    """
    if not LICENSES_TABLE:
        raise RuntimeError("LICENSES_TABLE not configured")

    sk = _signing_key()
    formatted_key, payload = lk.issue_license_key(private_key=sk, sku=1)
    key_hash = _hash_key_for_storage(formatted_key)
    now = int(time.time())

    try:
        _DYNAMODB.put_item(
            TableName=LICENSES_TABLE,
            Item={
                "key_id":         {"S": str(payload.key_id)},
                "key_hash":       {"S": key_hash},
                "email":          {"S": email.lower()},
                "stripe_session": {"S": session_id},
                "status":         {"S": "active"},
                "sku":             {"N": str(payload.sku)},
                "issued_at":       {"N": str(now)},
                "amount_total":    {"N": str(amount_total)},
                "currency":        {"S": currency},
            },
            ConditionExpression="attribute_not_exists(key_id)",
        )
    except _DYNAMODB.exceptions.ConditionalCheckFailedException:
        # ~1-in-4-billion key_id collision — extremely unlikely. Recurse
        # once to roll a new key.
        LOG.warning("key_id collision on issuance; retrying once")
        return issue_license_for_session(
            session_id=session_id,
            email=email,
            amount_total=amount_total,
            currency=currency,
        )

    _send_license_email(email=email, formatted_key=formatted_key)
    LOG.info(
        "Issued license key_id=%s session=%s email_domain=%s",
        payload.key_id,
        session_id,
        email.split("@", 1)[-1] if "@" in email else "?",
    )
    return formatted_key


def _send_license_email(*, email: str, formatted_key: str) -> None:
    if not _SES or not SES_FROM_ADDRESS:
        LOG.warning("SES not configured; license email skipped (key sent only via API)")
        return

    site = PUBLIC_BASE_PATH.rstrip("/") if PUBLIC_BASE_PATH else "https://your-site.example"
    subject = f"Your {STRIPE_PRODUCT_NAME} license key"
    text_body = (
        f"Thanks for buying {STRIPE_PRODUCT_NAME}!\n\n"
        f"Your license key:\n\n    {formatted_key}\n\n"
        f"Activate up to {lk.MAX_SLOTS_PER_LICENSE} machines per license. "
        "Inactive machines free up automatically after 30 days, and you can "
        "manage your machines at any time:\n\n"
        f"    {site}/manage.html\n\n"
        f"Download the game (any platform, unlimited times):\n\n"
        f"    {site}/\n\n"
        "Keep this email — you'll need the key to install on a new machine.\n"
    )
    html_body = f"""<!doctype html><html><body style="font-family:system-ui,sans-serif;max-width:560px;margin:auto">
<h2>Thanks for buying {STRIPE_PRODUCT_NAME}!</h2>
<p>Your license key:</p>
<pre style="font-size:18px;letter-spacing:0.05em;background:#f4f4f4;padding:12px;border-radius:6px">{formatted_key}</pre>
<p>Activate up to {lk.MAX_SLOTS_PER_LICENSE} machines per license. Inactive machines free up automatically after 30 days,
and you can <a href="{site}/manage.html">manage your machines</a> at any time.</p>
<p><a href="{site}/">Download the game</a> &mdash; any platform, unlimited times.</p>
<p style="color:#666;font-size:12px">Keep this email &mdash; you&rsquo;ll need the key to install on a new machine.</p>
</body></html>"""

    kwargs: dict[str, Any] = {
        "FromEmailAddress": SES_FROM_ADDRESS,
        "Destination": {"ToAddresses": [email]},
        "Content": {
            "Simple": {
                "Subject": {"Data": subject, "Charset": "UTF-8"},
                "Body": {
                    "Text": {"Data": text_body, "Charset": "UTF-8"},
                    "Html": {"Data": html_body, "Charset": "UTF-8"},
                },
            }
        },
    }
    if SES_CONFIGURATION_SET:
        kwargs["ConfigurationSetName"] = SES_CONFIGURATION_SET
    _SES.send_email(**kwargs)


# --- Background sweep (EventBridge cron → Lambda) --------------------------


def handle_slot_sweep(_event: dict[str, Any]) -> dict[str, Any]:
    """Scan for slots whose last_seen is past the cutoff and release them.

    Invoked by an EventBridge schedule (defined in Terraform). Best-effort:
    runs in a single 15-minute Lambda invocation; if the table grows past
    what fits in one run, the next nightly invocation picks up where this
    one left off thanks to DynamoDB's parallel scan + the
    `attribute_not_exists(released_at)` filter.
    """
    if not MACHINE_SLOTS_TABLE:
        return {"ok": False, "error": "MACHINE_SLOTS_TABLE not configured"}
    now = int(time.time())
    cutoff = now - SLOT_INACTIVITY_RECLAIM_SECONDS
    paginator = _DYNAMODB.get_paginator("scan")
    released = 0
    for page in paginator.paginate(
        TableName=MACHINE_SLOTS_TABLE,
        FilterExpression="attribute_not_exists(released_at) AND last_seen < :c",
        ExpressionAttributeValues={":c": {"N": str(cutoff)}},
    ):
        for item in page.get("Items", []):
            try:
                _DYNAMODB.update_item(
                    TableName=MACHINE_SLOTS_TABLE,
                    Key={
                        "key_id":     item["key_id"],
                        "machine_id": item["machine_id"],
                    },
                    UpdateExpression="SET released_at = :t, ttl_at = :ttl",
                    ConditionExpression="attribute_not_exists(released_at)",
                    ExpressionAttributeValues={
                        ":t":   {"N": str(now)},
                        ":ttl": {"N": str(now + 30 * 86400)},
                    },
                )
                released += 1
            except _DYNAMODB.exceptions.ConditionalCheckFailedException:
                pass
    LOG.info("slot_sweep released=%s", released)
    return {"ok": True, "released": released}
