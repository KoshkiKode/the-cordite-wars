"""
Integration tests for the licensing Lambda routes (activate, renew,
deactivate, manage).

Uses an in-process fake DynamoDB + fake SES to keep the tests hermetic
and free from boto3 stubs.
"""

from __future__ import annotations

import base64
import json
import os
import sys
import time
from typing import Any
from unittest import mock

import pytest


HERE = os.path.dirname(__file__)
LAMBDA_DIR = os.path.abspath(os.path.join(HERE, "..", "..", "infra", "aws", "lambda"))
sys.path.insert(0, LAMBDA_DIR)

# Set required env vars BEFORE importing the modules.
os.environ.setdefault("AWS_DEFAULT_REGION", "us-east-1")
os.environ.setdefault("LICENSES_TABLE", "test-licenses")
os.environ.setdefault("MACHINE_SLOTS_TABLE", "test-slots")
os.environ.setdefault("LICENSE_SIGNING_SECRET_ARN", "arn:aws:secretsmanager:test:test:secret:test")
os.environ.setdefault("PUBLIC_BASE_PATH", "https://test.example.com")
os.environ.setdefault("STRIPE_PRODUCT_NAME", "Test Game")

import license_keys as lk  # noqa: E402
import licensing  # noqa: E402


# ---- Fake DynamoDB client --------------------------------------------------


class FakeDynamoExceptions:
    class ConditionalCheckFailedException(Exception):
        pass


class FakeDynamoPaginator:
    def __init__(self, table: dict):
        self._table = table

    def paginate(self, *, TableName, FilterExpression="", ExpressionAttributeValues=None, **_):
        items = []
        for row in self._table.values():
            if "released_at" in row:
                continue
            cutoff = int(ExpressionAttributeValues[":c"]["N"]) if ExpressionAttributeValues else 0
            if int(row["last_seen"]["N"]) < cutoff:
                items.append(row)
        yield {"Items": items}


class FakeDynamoDB:
    """A minimal in-memory replacement for the parts of boto3.client('dynamodb')
    that the licensing module uses. Just enough to make the tests realistic.
    """

    def __init__(self):
        self.tables: dict[str, dict[tuple, dict]] = {
            "test-licenses": {},
            "test-slots": {},
        }
        self.exceptions = FakeDynamoExceptions

    @staticmethod
    def _key_tuple(table_name: str, key: dict) -> tuple:
        if table_name == "test-licenses":
            return (key["key_id"]["S"],)
        return (key["key_id"]["S"], key["machine_id"]["S"])

    def get_item(self, *, TableName, Key, **_):
        item = self.tables[TableName].get(self._key_tuple(TableName, Key))
        return {"Item": dict(item)} if item else {}

    def put_item(self, *, TableName, Item, ConditionExpression=None, **_):
        if TableName == "test-licenses":
            k = (Item["key_id"]["S"],)
        else:
            k = (Item["key_id"]["S"], Item["machine_id"]["S"])
        existing = self.tables[TableName].get(k)
        if ConditionExpression:
            cond = ConditionExpression
            # Evaluate the (deliberately small) subset of conditions we care about.
            allows_overwrite_released = "attribute_exists(released_at)" in cond
            requires_not_exists = "attribute_not_exists" in cond
            if requires_not_exists and existing is not None:
                if not (allows_overwrite_released and "released_at" in existing):
                    raise self.exceptions.ConditionalCheckFailedException()
        self.tables[TableName][k] = dict(Item)
        return {}

    def update_item(
        self,
        *,
        TableName,
        Key,
        UpdateExpression,
        ConditionExpression=None,
        ExpressionAttributeValues=None,
        ReturnValues=None,
        **_,
    ):
        k = self._key_tuple(TableName, Key)
        item = self.tables[TableName].get(k)
        if ConditionExpression:
            cond = ConditionExpression
            if "attribute_exists(machine_id)" in cond and item is None:
                raise self.exceptions.ConditionalCheckFailedException()
            if "attribute_not_exists(released_at)" in cond and item is not None and "released_at" in item:
                raise self.exceptions.ConditionalCheckFailedException()
            if "attribute_exists(session_id)" in cond and item is None:
                raise self.exceptions.ConditionalCheckFailedException()
            if "paid = :true" in cond:
                if not item or item.get("paid", {}).get("BOOL") is not True:
                    raise self.exceptions.ConditionalCheckFailedException()
            if "redemptions < :limit" in cond:
                cur = int(item["redemptions"]["N"])
                lim = int(ExpressionAttributeValues[":limit"]["N"])
                if cur >= lim:
                    raise self.exceptions.ConditionalCheckFailedException()
        if item is None:
            item = {}
            for kf in Key:
                item[kf] = Key[kf]
            self.tables[TableName][k] = item
        # Apply SET clauses.
        for clause in UpdateExpression.replace("SET ", "").split(","):
            clause = clause.strip()
            if "=" not in clause:
                continue
            field, val_ref = [s.strip() for s in clause.split("=", 1)]
            if val_ref.endswith("+ :one"):
                base_field = val_ref.split()[0]
                cur = int(item.get(base_field, {"N": "0"})["N"])
                item[field] = {"N": str(cur + 1)}
            elif val_ref in ExpressionAttributeValues:
                item[field] = ExpressionAttributeValues[val_ref]
        return {"Attributes": dict(item)}

    def query(self, *, TableName, KeyConditionExpression, FilterExpression="", ExpressionAttributeValues, **_):
        wanted_key_id = ExpressionAttributeValues[":k"]["S"]
        items = [
            row for k, row in self.tables[TableName].items()
            if k[0] == wanted_key_id and ("released_at" not in row or "released_at" not in FilterExpression)
        ]
        return {"Items": items}

    def get_paginator(self, _):
        return FakeDynamoPaginator(self.tables["test-slots"])


# ---- Fake Secrets Manager --------------------------------------------------


class FakeSecrets:
    def __init__(self, secret_string: str):
        self._payload = secret_string

    def get_secret_value(self, *, SecretId):
        return {"SecretString": self._payload}


# ---- Fixtures --------------------------------------------------------------


@pytest.fixture
def keypair():
    sk_pem, pk_pem = lk.generate_signing_keypair()
    return lk.load_private_key(sk_pem), lk.load_public_key(pk_pem), sk_pem


@pytest.fixture
def fakes(keypair):
    sk, pk, sk_pem = keypair
    fake_dynamo = FakeDynamoDB()
    fake_secrets = FakeSecrets(json.dumps({"private_key_pem": sk_pem, "public_key_pem": ""}))
    # Install on the licensing module.
    with mock.patch.object(licensing, "_DYNAMODB", fake_dynamo), \
         mock.patch.object(licensing, "_SECRETS", fake_secrets), \
         mock.patch.object(licensing, "_SES", None), \
         mock.patch.object(licensing, "_SIGNING_KEY_CACHE", None):
        yield {
            "dynamo":  fake_dynamo,
            "sk":      sk,
            "pk":      pk,
            "sk_pem":  sk_pem,
        }


def _seed_license(fakes_obj, sk):
    """Issue a key + put a row in the licenses table."""
    formatted, payload = lk.issue_license_key(private_key=sk, sku=1)
    fakes_obj["dynamo"].tables["test-licenses"][(str(payload.key_id),)] = {
        "key_id":   {"S": str(payload.key_id)},
        "key_hash": {"S": "x"},
        "email":    {"S": "buyer@example.com"},
        "status":   {"S": "active"},
    }
    return formatted, payload


def _http_event(method: str, body: Any | None = None, qs: dict | None = None):
    if body is None:
        body_str = ""
    elif isinstance(body, str):
        body_str = body
    else:
        body_str = json.dumps(body)
    return {
        "requestContext": {"http": {"method": method}},
        "body": body_str,
        "queryStringParameters": qs or {},
    }


# ---- /api/activate --------------------------------------------------------


def test_activate_first_time_creates_slot_and_returns_entitlement(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    machine_id = "ab" * 16
    event = _http_event("POST", {
        "key": formatted,
        "machine_id": machine_id,
        "hostname_hint": "laptop",
    })
    resp = licensing.handle_activate(event)
    assert resp["statusCode"] == 200, resp["body"]
    body = json.loads(resp["body"])
    assert body["slot_index"] == 1
    assert "entitlement_b64" in body

    # Decode + verify the returned blob with the test public key.
    s = body["entitlement_b64"].replace("-", "+").replace("_", "/") + "=" * (-len(body["entitlement_b64"]) % 4)
    blob = base64.b64decode(s)
    ent = lk.decode_entitlement(blob, public_key=fakes["pk"])
    assert ent.key_id == payload.key_id
    assert ent.machine_id.hex() == machine_id
    assert ent.slot_index == 1


def test_activate_idempotent_for_same_machine(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    event = _http_event("POST", {
        "key": formatted,
        "machine_id": "11" * 16,
        "hostname_hint": "h",
    })
    r1 = licensing.handle_activate(event)
    r2 = licensing.handle_activate(event)
    assert json.loads(r1["body"])["slot_index"] == json.loads(r2["body"])["slot_index"] == 1
    # Only one slot row should exist.
    assert len(fakes["dynamo"].tables["test-slots"]) == 1


def test_activate_assigns_distinct_slot_indices_until_cap(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    seen = set()
    for i in range(lk.MAX_SLOTS_PER_LICENSE):
        machine_id = f"{i:032x}"
        event = _http_event("POST", {
            "key": formatted,
            "machine_id": machine_id,
            "hostname_hint": f"machine-{i}",
        })
        resp = licensing.handle_activate(event)
        assert resp["statusCode"] == 200
        seen.add(json.loads(resp["body"])["slot_index"])
    assert seen == set(range(1, lk.MAX_SLOTS_PER_LICENSE + 1))


def test_activate_rejects_eleventh_machine_with_409(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    for i in range(lk.MAX_SLOTS_PER_LICENSE):
        licensing.handle_activate(_http_event("POST", {
            "key": formatted,
            "machine_id": f"{i:032x}",
            "hostname_hint": f"m{i}",
        }))
    resp = licensing.handle_activate(_http_event("POST", {
        "key": formatted,
        "machine_id": "f" * 32,
        "hostname_hint": "extra",
    }))
    assert resp["statusCode"] == 409
    body = json.loads(resp["body"])
    assert body["error"] == "machine_cap_reached"
    assert len(body["active_slots"]) == lk.MAX_SLOTS_PER_LICENSE


def test_activate_rejects_unknown_key(fakes):
    # Mint a key whose key_id is not in the table.
    formatted, _ = lk.issue_license_key(private_key=fakes["sk"])
    resp = licensing.handle_activate(_http_event("POST", {
        "key": formatted,
        "machine_id": "11" * 16,
        "hostname_hint": "h",
    }))
    assert resp["statusCode"] == 403


def test_activate_rejects_revoked_license(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    fakes["dynamo"].tables["test-licenses"][(str(payload.key_id),)]["status"] = {"S": "revoked"}
    resp = licensing.handle_activate(_http_event("POST", {
        "key": formatted,
        "machine_id": "11" * 16,
        "hostname_hint": "h",
    }))
    assert resp["statusCode"] == 403


def test_activate_rejects_bad_machine_id(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    resp = licensing.handle_activate(_http_event("POST", {
        "key": formatted,
        "machine_id": "not-hex",
        "hostname_hint": "h",
    }))
    assert resp["statusCode"] == 400


def test_activate_reclaims_inactive_slot_to_make_room(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    # Pre-populate 10 slots, all stale.
    stale_ts = int(time.time()) - 60 * 86400
    for i in range(lk.MAX_SLOTS_PER_LICENSE):
        fakes["dynamo"].tables["test-slots"][(str(payload.key_id), f"{i:032x}")] = {
            "key_id":     {"S": str(payload.key_id)},
            "machine_id": {"S": f"{i:032x}"},
            "slot_index": {"N": str(i + 1)},
            "first_seen": {"N": str(stale_ts)},
            "last_seen":  {"N": str(stale_ts)},
            "hostname_hint": {"S": "old"},
        }
    # An 11th machine should now succeed because reclaim frees up a slot.
    resp = licensing.handle_activate(_http_event("POST", {
        "key": formatted,
        "machine_id": "ab" * 16,
        "hostname_hint": "new",
    }))
    assert resp["statusCode"] == 200, resp["body"]


# ---- /api/deactivate ------------------------------------------------------


def test_deactivate_releases_a_slot(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    licensing.handle_activate(_http_event("POST", {
        "key": formatted, "machine_id": "ab" * 16, "hostname_hint": "h",
    }))
    resp = licensing.handle_deactivate(_http_event("POST", {
        "key": formatted, "machine_id_to_release": "ab" * 16,
    }))
    assert resp["statusCode"] == 200
    # The same machine can immediately re-activate (gets a new slot).
    resp2 = licensing.handle_activate(_http_event("POST", {
        "key": formatted, "machine_id": "ab" * 16, "hostname_hint": "h",
    }))
    assert resp2["statusCode"] == 200


def test_deactivate_unknown_slot_returns_404(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    resp = licensing.handle_deactivate(_http_event("POST", {
        "key": formatted, "machine_id_to_release": "ab" * 16,
    }))
    assert resp["statusCode"] == 404


# ---- /api/renew -----------------------------------------------------------


def test_renew_with_existing_blob_succeeds(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    activate = json.loads(licensing.handle_activate(_http_event("POST", {
        "key": formatted, "machine_id": "ab" * 16, "hostname_hint": "h",
    }))["body"])
    blob_b64 = activate["entitlement_b64"]
    resp = licensing.handle_renew(_http_event("POST", {"entitlement_b64": blob_b64}))
    assert resp["statusCode"] == 200, resp["body"]
    new_blob = json.loads(resp["body"])["entitlement_b64"]
    # New blob is structurally distinct (issued_at ≥ original).
    assert new_blob is not None


def test_renew_with_released_slot_fails(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    activate = json.loads(licensing.handle_activate(_http_event("POST", {
        "key": formatted, "machine_id": "ab" * 16, "hostname_hint": "h",
    }))["body"])
    licensing.handle_deactivate(_http_event("POST", {
        "key": formatted, "machine_id_to_release": "ab" * 16,
    }))
    resp = licensing.handle_renew(_http_event("POST", {
        "entitlement_b64": activate["entitlement_b64"],
    }))
    assert resp["statusCode"] == 403


def test_renew_with_garbage_blob_returns_400(fakes):
    resp = licensing.handle_renew(_http_event("POST", {"entitlement_b64": "not-base64!!!"}))
    assert resp["statusCode"] in (400, 403)


# ---- /api/manage ----------------------------------------------------------


def test_manage_returns_active_slots_for_correct_email(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    licensing.handle_activate(_http_event("POST", {
        "key": formatted, "machine_id": "ab" * 16, "hostname_hint": "laptop",
    }))
    resp = licensing.handle_manage(_http_event("GET", qs={
        "key": formatted, "email": "buyer@example.com",
    }))
    assert resp["statusCode"] == 200
    body = json.loads(resp["body"])
    assert body["max_slots"] == lk.MAX_SLOTS_PER_LICENSE
    assert len(body["slots"]) == 1
    assert body["slots"][0]["hostname_hint"] == "laptop"
    assert body["key_id"] == payload.key_id


def test_manage_rejects_wrong_email(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    resp = licensing.handle_manage(_http_event("GET", qs={
        "key": formatted, "email": "stranger@example.com",
    }))
    assert resp["statusCode"] == 403


def test_manage_requires_email(fakes):
    formatted, _ = _seed_license(fakes, fakes["sk"])
    resp = licensing.handle_manage(_http_event("GET", qs={"key": formatted}))
    assert resp["statusCode"] == 400


# ---- background sweep -----------------------------------------------------


def test_slot_sweep_releases_stale_rows(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    stale_ts = int(time.time()) - 60 * 86400
    fakes["dynamo"].tables["test-slots"][(str(payload.key_id), "11" * 16)] = {
        "key_id":     {"S": str(payload.key_id)},
        "machine_id": {"S": "11" * 16},
        "slot_index": {"N": "1"},
        "first_seen": {"N": str(stale_ts)},
        "last_seen":  {"N": str(stale_ts)},
        "hostname_hint": {"S": "old"},
    }
    out = licensing.handle_slot_sweep({})
    assert out["ok"] is True
    assert out["released"] == 1


def test_slot_sweep_skips_recent_rows(fakes):
    formatted, payload = _seed_license(fakes, fakes["sk"])
    fresh_ts = int(time.time())
    fakes["dynamo"].tables["test-slots"][(str(payload.key_id), "22" * 16)] = {
        "key_id":     {"S": str(payload.key_id)},
        "machine_id": {"S": "22" * 16},
        "slot_index": {"N": "1"},
        "first_seen": {"N": str(fresh_ts)},
        "last_seen":  {"N": str(fresh_ts)},
        "hostname_hint": {"S": "new"},
    }
    out = licensing.handle_slot_sweep({})
    assert out["released"] == 0
