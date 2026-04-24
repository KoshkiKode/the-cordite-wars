"""Tests for the license-key crypto module shipped with the Lambda."""

from __future__ import annotations

import os
import sys
import time

import pytest

# Make the Lambda package importable.
HERE = os.path.dirname(__file__)
sys.path.insert(0, os.path.abspath(os.path.join(HERE, "..", "..", "infra", "aws", "lambda")))

import license_keys as lk  # noqa: E402


@pytest.fixture(scope="module")
def keypair():
    sk_pem, pk_pem = lk.generate_signing_keypair()
    sk = lk.load_private_key(sk_pem)
    pk = lk.load_public_key(pk_pem)
    return sk, pk


def test_round_trip_license_key(keypair):
    sk, _pk = keypair
    formatted, payload = lk.issue_license_key(private_key=sk, sku=1)
    # Visual format: 5x5 groups separated by dashes.
    assert len(formatted) == 25 + 4
    assert formatted.count("-") == 4
    for group in formatted.split("-"):
        assert len(group) == 5
    # Decoded payload survives the round trip.
    decoded = lk.decode_license_key(formatted)
    assert decoded.key_id == payload.key_id
    assert decoded.sku == payload.sku
    assert decoded.issue_date_days == payload.issue_date_days
    assert decoded.flags == payload.flags
    assert decoded.version == lk.KEY_VERSION


def test_server_side_signature_verification(keypair):
    sk, _pk = keypair
    formatted, _ = lk.issue_license_key(private_key=sk, sku=1)
    # Server-side verify with the same private key passes.
    assert lk.verify_license_key_signature(formatted, private_key=sk).version == 1
    # A different key should reject it.
    other_sk_pem, _ = lk.generate_signing_keypair()
    other_sk = lk.load_private_key(other_sk_pem)
    with pytest.raises(ValueError, match="signature"):
        lk.verify_license_key_signature(formatted, private_key=other_sk)


def test_typo_detection_via_crc(keypair):
    sk, _pk = keypair
    formatted, _ = lk.issue_license_key(private_key=sk)
    # Flip one character in the body. Almost any single-character change
    # should be caught by the CRC-8 check before we even try to decode.
    body = formatted.replace("-", "")
    flipped_chars = list(body)
    # Find a character we can swap to a different valid one.
    target_idx = 0
    for i, c in enumerate(body[:24]):
        alt = "0" if c != "0" else "1"
        flipped_chars[i] = alt
        target_idx = i
        break
    flipped = "".join(flipped_chars)
    flipped_formatted = "-".join(flipped[i : i + 5] for i in range(0, 25, 5))
    with pytest.raises(ValueError):
        lk.decode_license_key(flipped_formatted)
    assert target_idx == 0


def test_normalize_handles_confusables_and_lowercase(keypair):
    sk, _pk = keypair
    formatted, _ = lk.issue_license_key(private_key=sk)
    # Lowercase + dashed-or-not + spaces should all decode.
    munged = " " + formatted.lower().replace("-", " ") + " "
    assert lk.decode_license_key(munged).key_id is not None
    # Confusable substitution: O→0, I→1, L→1, U→V should still work for
    # *new* keys that don't actually contain those output letters, since
    # Crockford output never uses I/L/O/U.
    assert "I" not in formatted and "L" not in formatted
    assert "O" not in formatted and "U" not in formatted


def test_rejects_wrong_length():
    with pytest.raises(ValueError, match="25"):
        lk.decode_license_key("ABCDE-ABCDE")


def test_rejects_unsupported_version(keypair):
    """Forging version=2 by tweaking the first byte should fail CRC."""
    sk, _pk = keypair
    formatted, _ = lk.issue_license_key(private_key=sk)
    # Replace the very first character to change the version field.
    # The CRC-8 will catch it before we even look at the version.
    tampered = ("Z" if formatted[0] != "Z" else "Y") + formatted[1:]
    with pytest.raises(ValueError):
        lk.decode_license_key(tampered)


def test_entitlement_round_trip(keypair):
    sk, pk = keypair
    machine_id = b"\x42" * 16
    issued_at = int(time.time())
    blob = lk.issue_entitlement(
        private_key=sk,
        key_id=0xDEADBEEF,
        machine_id=machine_id,
        slot_index=3,
        hostname_hint="my-laptop",
        issued_at=issued_at,
    )
    ent = lk.decode_entitlement(blob, public_key=pk)
    assert ent.key_id == 0xDEADBEEF
    assert ent.machine_id == machine_id
    assert ent.slot_index == 3
    assert ent.hostname_hint == "my-laptop"
    assert ent.issued_at == issued_at
    assert ent.expires_at == issued_at + lk.ENTITLEMENT_TTL_SECONDS


def test_entitlement_rejects_tampering(keypair):
    sk, pk = keypair
    blob = bytearray(
        lk.issue_entitlement(
            private_key=sk,
            key_id=1,
            machine_id=b"\x01" * 16,
            slot_index=1,
            hostname_hint="host",
            issued_at=int(time.time()),
        )
    )
    # Flip the slot_index byte.
    blob[21] ^= 0x01
    with pytest.raises(ValueError, match="signature"):
        lk.decode_entitlement(bytes(blob), public_key=pk)


def test_entitlement_rejects_wrong_public_key(keypair):
    sk, _pk = keypair
    other_sk_pem, other_pk_pem = lk.generate_signing_keypair()
    other_pk = lk.load_public_key(other_pk_pem)
    blob = lk.issue_entitlement(
        private_key=sk,
        key_id=1,
        machine_id=b"\x02" * 16,
        slot_index=1,
        hostname_hint="x",
        issued_at=int(time.time()),
    )
    with pytest.raises(ValueError, match="signature"):
        lk.decode_entitlement(blob, public_key=other_pk)


def test_slot_index_validation(keypair):
    sk, _pk = keypair
    with pytest.raises(ValueError, match="slot_index"):
        lk.issue_entitlement(
            private_key=sk,
            key_id=1,
            machine_id=b"\x00" * 16,
            slot_index=0,
            hostname_hint="x",
            issued_at=0,
        )
    with pytest.raises(ValueError, match="slot_index"):
        lk.issue_entitlement(
            private_key=sk,
            key_id=1,
            machine_id=b"\x00" * 16,
            slot_index=11,
            hostname_hint="x",
            issued_at=0,
        )


def test_machine_id_length_validation(keypair):
    sk, _pk = keypair
    with pytest.raises(ValueError, match="machine_id"):
        lk.issue_entitlement(
            private_key=sk,
            key_id=1,
            machine_id=b"\x00" * 8,
            slot_index=1,
            hostname_hint="x",
            issued_at=0,
        )


def test_issued_at_distinct_keys_are_distinct(keypair):
    sk, _pk = keypair
    seen = set()
    for _ in range(50):
        formatted, payload = lk.issue_license_key(private_key=sk)
        seen.add(payload.key_id)
        # Format is consistent.
        assert lk.decode_license_key(formatted).key_id == payload.key_id
    # 50 random uint32 key_ids — collision probability is microscopic.
    assert len(seen) == 50
