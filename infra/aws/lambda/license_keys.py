"""
License key + entitlement crypto for Cordite Wars.

Key format (25 chars, Crockford Base32, grouped 5-5-5-5-5):

    XXXXX-XXXXX-XXXXX-XXXXX-XXXXX

Encoded payload (15 bytes raw → 24 base32 chars + 1 check char = 25 chars):

    offset  size  field
    ------  ----  ------------------------------------------------
    0       1     version       (currently 1)
    1       4     key_id        (random uint32, identifies the row in DynamoDB)
    5       1     sku           (product SKU code; 1 = standard)
    6       2     issue_date    (days since 2025-01-01, uint16)
    8       1     flags         (reserved, currently 0)
    9       6     signature     (first 6 bytes of an Ed25519 signature over bytes 0..8)

The 6-byte signature gives ~48 bits of forgery resistance. That's not
cryptographically tight, but it's only a *first-line* filter — every key
is also looked up in DynamoDB on activation, so a forged key that happens
to verify still won't match any row. The check is here to (a) reject
typos quickly without a network call and (b) make it expensive to spam
the activation endpoint with random strings.

The 25th character (`check`) is a Crockford Base32 encoding of a CRC-8 of
the previous 24 chars, used for early typo detection in the UI before we
even attempt signature verification.

Entitlement blob format (binary, base64-url encoded for storage):

    offset  size  field
    ------  ----  ------------------------------------------------
    0       1     version       (currently 1)
    1       4     key_id
    5       16    machine_id    (SHA-256 truncated to 16 bytes)
    21      1     slot_index    (1..10)
    22      4     issued_at     (unix seconds, uint32)
    26      4     expires_at    (unix seconds, uint32)
    30      1     hostname_len
    31      N     hostname_hint (UTF-8, max 64 bytes)
    ...     64    signature     (full Ed25519 signature over preceding bytes)

Both keys and entitlements are signed with the same Ed25519 private key
held in Secrets Manager; the public key is embedded in the game binary
so verification works fully offline.
"""

from __future__ import annotations

import secrets
import struct
from dataclasses import dataclass
from typing import Final

from cryptography.hazmat.primitives.asymmetric.ed25519 import (
    Ed25519PrivateKey,
    Ed25519PublicKey,
)
from cryptography.hazmat.primitives import serialization
from cryptography.exceptions import InvalidSignature

# --- Constants ---------------------------------------------------------------

KEY_VERSION: Final[int] = 1
ENTITLEMENT_VERSION: Final[int] = 1

# Crockford Base32 alphabet — excludes I, L, O, U to avoid confusion with
# 1/0 and to keep keys readable when written down.
_CROCKFORD: Final[str] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
_CROCKFORD_DECODE: Final[dict[str, int]] = {c: i for i, c in enumerate(_CROCKFORD)}
# Common typo-substitutions accepted on input.
for _src, _dst in (("I", "1"), ("L", "1"), ("O", "0"), ("U", "V")):
    _CROCKFORD_DECODE[_src] = _CROCKFORD_DECODE[_dst]

# Days-since epoch for `issue_date`.
ISSUE_DATE_EPOCH_DAYS: Final[int] = 20089  # 2025-01-01 in unix days

KEY_PAYLOAD_LEN: Final[int] = 15  # bytes
KEY_CHARS_NO_CHECK: Final[int] = 24  # 15 bytes * 8 / 5 = 24
KEY_CHARS_TOTAL: Final[int] = 25  # + 1 CRC-8 check char

ENTITLEMENT_FIXED_LEN: Final[int] = 31  # bytes before hostname_hint
ENTITLEMENT_SIG_LEN: Final[int] = 64
ENTITLEMENT_MAX_LEN: Final[int] = ENTITLEMENT_FIXED_LEN + 64 + ENTITLEMENT_SIG_LEN
ENTITLEMENT_TTL_SECONDS: Final[int] = 400 * 86400  # ~13 months

MACHINE_ID_LEN: Final[int] = 16
MAX_SLOTS_PER_LICENSE: Final[int] = 10


# --- Crockford Base32 (5 bits/char) -----------------------------------------


def _b32_encode(data: bytes) -> str:
    """Encode bytes as Crockford Base32. Output length = ceil(len(data)*8/5)."""
    bits = 0
    value = 0
    out = []
    for byte in data:
        value = (value << 8) | byte
        bits += 8
        while bits >= 5:
            bits -= 5
            out.append(_CROCKFORD[(value >> bits) & 0x1F])
    if bits:
        out.append(_CROCKFORD[(value << (5 - bits)) & 0x1F])
    return "".join(out)


def _b32_decode(text: str, expected_bytes: int) -> bytes:
    bits = 0
    value = 0
    out = bytearray()
    for ch in text:
        try:
            v = _CROCKFORD_DECODE[ch]
        except KeyError:
            raise ValueError(f"Invalid Crockford Base32 character: {ch!r}") from None
        value = (value << 5) | v
        bits += 5
        if bits >= 8:
            bits -= 8
            out.append((value >> bits) & 0xFF)
    if len(out) < expected_bytes:
        raise ValueError("Encoded payload truncated")
    return bytes(out[:expected_bytes])


# --- CRC-8 (poly 0x07, init 0x00) — small, lookup-free, plenty for typos ----


def _crc8(data: bytes) -> int:
    crc = 0
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = ((crc << 1) ^ 0x07) & 0xFF if crc & 0x80 else (crc << 1) & 0xFF
    return crc


# --- Public dataclasses ------------------------------------------------------


@dataclass(frozen=True)
class LicenseKeyPayload:
    version: int
    key_id: int
    sku: int
    issue_date_days: int
    flags: int

    def to_bytes_unsigned(self) -> bytes:
        return struct.pack(
            ">BIBHB",
            self.version,
            self.key_id,
            self.sku,
            self.issue_date_days,
            self.flags,
        )


@dataclass(frozen=True)
class Entitlement:
    version: int
    key_id: int
    machine_id: bytes  # 16 bytes
    slot_index: int
    issued_at: int
    expires_at: int
    hostname_hint: str

    def to_bytes_unsigned(self) -> bytes:
        host = self.hostname_hint.encode("utf-8")[:64]
        return (
            struct.pack(
                ">BI16sBIIB",
                self.version,
                self.key_id,
                self.machine_id,
                self.slot_index,
                self.issued_at,
                self.expires_at,
                len(host),
            )
            + host
        )


# --- Signing keys ------------------------------------------------------------


def load_private_key(pem_or_b64: str) -> Ed25519PrivateKey:
    """Load an Ed25519 private key from either PEM or base64-encoded raw bytes."""
    s = pem_or_b64.strip()
    if s.startswith("-----BEGIN"):
        return serialization.load_pem_private_key(s.encode(), password=None)  # type: ignore[return-value]
    import base64

    raw = base64.b64decode(s)
    return Ed25519PrivateKey.from_private_bytes(raw)


def load_public_key(pem_or_b64: str) -> Ed25519PublicKey:
    s = pem_or_b64.strip()
    if s.startswith("-----BEGIN"):
        return serialization.load_pem_public_key(s.encode())  # type: ignore[return-value]
    import base64

    raw = base64.b64decode(s)
    return Ed25519PublicKey.from_public_bytes(raw)


# --- License key encode / decode --------------------------------------------


def issue_license_key(
    *,
    private_key: Ed25519PrivateKey,
    sku: int = 1,
    issue_date_days: int | None = None,
    flags: int = 0,
    key_id: int | None = None,
) -> tuple[str, LicenseKeyPayload]:
    """Generate a fresh license key. Returns (formatted_key, payload)."""
    if key_id is None:
        key_id = secrets.randbits(32)
    if issue_date_days is None:
        import time

        issue_date_days = int(time.time() // 86400) - ISSUE_DATE_EPOCH_DAYS
        if not 0 <= issue_date_days <= 0xFFFF:
            raise ValueError("issue_date_days out of range — re-anchor the epoch")

    payload = LicenseKeyPayload(
        version=KEY_VERSION,
        key_id=key_id,
        sku=sku,
        issue_date_days=issue_date_days,
        flags=flags,
    )
    unsigned = payload.to_bytes_unsigned()  # 9 bytes
    sig_full = private_key.sign(unsigned)
    payload_bytes = unsigned + sig_full[:6]  # 15 bytes total
    body = _b32_encode(payload_bytes)[:KEY_CHARS_NO_CHECK]
    check = _CROCKFORD[_crc8(body.encode("ascii")) & 0x1F]
    raw = body + check
    formatted = "-".join(raw[i : i + 5] for i in range(0, KEY_CHARS_TOTAL, 5))
    return formatted, payload


def normalize_key(text: str) -> str:
    """Strip whitespace + dashes, uppercase, and apply confusable folding."""
    s = "".join(text.split()).replace("-", "").upper()
    return "".join(_CROCKFORD[_CROCKFORD_DECODE[c]] if c in _CROCKFORD_DECODE else c for c in s)


def decode_license_key(text: str) -> LicenseKeyPayload:
    """Parse a key, validate CRC + format. Does NOT verify the signature.

    The truncated Ed25519 signature in a license key cannot be verified
    with only a public key (see `verify_license_key_signature` for the
    server-side check). Clients should treat a successfully decoded key
    as a *candidate* and rely on either (a) the activation server's
    DynamoDB lookup or (b) the resulting signed entitlement blob as the
    authoritative trust anchor.

    Raises ValueError on format errors, bad CRC, or unsupported version.
    """
    norm = normalize_key(text)
    if len(norm) != KEY_CHARS_TOTAL:
        raise ValueError(f"License key must be {KEY_CHARS_TOTAL} characters")

    body, check = norm[:KEY_CHARS_NO_CHECK], norm[KEY_CHARS_NO_CHECK:]
    expected_check = _CROCKFORD[_crc8(body.encode("ascii")) & 0x1F]
    if check != expected_check:
        raise ValueError("License key checksum mismatch (typo?)")

    try:
        payload_bytes = _b32_decode(body, KEY_PAYLOAD_LEN)
    except ValueError as e:
        raise ValueError(f"Malformed license key: {e}") from e

    unsigned = payload_bytes[:9]
    version, key_id, sku, issue_days, flags = struct.unpack(">BIBHB", unsigned)
    if version != KEY_VERSION:
        raise ValueError(f"Unsupported license key version: {version}")

    return LicenseKeyPayload(
        version=version,
        key_id=key_id,
        sku=sku,
        issue_date_days=issue_days,
        flags=flags,
    )


def verify_license_key_signature(
    text: str,
    *,
    private_key: Ed25519PrivateKey,
) -> LicenseKeyPayload:
    """Server-side strict verify of the truncated signature in a key.

    Truncated Ed25519 signatures cannot be verified with the public key
    alone — there's no math that lets you check 6 of the 64 signature
    bytes. So the server (which holds the private key) re-signs the
    9-byte payload with the same deterministic signer and compares the
    first 6 bytes. This catches keys that were *not* issued by us with
    ~2^-48 false-accept probability.

    The client never calls this. The client trusts the *entitlement blob*
    instead, which carries a full 64-byte signature and IS verifiable
    with only the public key.
    """
    norm = normalize_key(text)
    if len(norm) != KEY_CHARS_TOTAL:
        raise ValueError(f"License key must be {KEY_CHARS_TOTAL} characters")
    body, check = norm[:KEY_CHARS_NO_CHECK], norm[KEY_CHARS_NO_CHECK:]
    if check != _CROCKFORD[_crc8(body.encode("ascii")) & 0x1F]:
        raise ValueError("License key checksum mismatch (typo?)")
    payload_bytes = _b32_decode(body, KEY_PAYLOAD_LEN)
    unsigned, sig_truncated = payload_bytes[:9], payload_bytes[9:]
    expected_sig = private_key.sign(unsigned)[:6]
    if not _const_eq(sig_truncated, expected_sig):
        raise ValueError("License key signature invalid (forged or tampered)")
    version, key_id, sku, issue_days, flags = struct.unpack(">BIBHB", unsigned)
    if version != KEY_VERSION:
        raise ValueError(f"Unsupported license key version: {version}")
    return LicenseKeyPayload(
        version=version,
        key_id=key_id,
        sku=sku,
        issue_date_days=issue_days,
        flags=flags,
    )


def _const_eq(a: bytes, b: bytes) -> bool:
    if len(a) != len(b):
        return False
    diff = 0
    for x, y in zip(a, b):
        diff |= x ^ y
    return diff == 0


# --- Entitlement encode / decode --------------------------------------------


def issue_entitlement(
    *,
    private_key: Ed25519PrivateKey,
    key_id: int,
    machine_id: bytes,
    slot_index: int,
    hostname_hint: str,
    issued_at: int,
    ttl_seconds: int = ENTITLEMENT_TTL_SECONDS,
) -> bytes:
    if len(machine_id) != MACHINE_ID_LEN:
        raise ValueError(f"machine_id must be {MACHINE_ID_LEN} bytes")
    if not 1 <= slot_index <= MAX_SLOTS_PER_LICENSE:
        raise ValueError("slot_index out of range")

    ent = Entitlement(
        version=ENTITLEMENT_VERSION,
        key_id=key_id,
        machine_id=machine_id,
        slot_index=slot_index,
        issued_at=issued_at,
        expires_at=issued_at + ttl_seconds,
        hostname_hint=hostname_hint,
    )
    unsigned = ent.to_bytes_unsigned()
    sig = private_key.sign(unsigned)
    return unsigned + sig


def decode_entitlement(
    blob: bytes,
    *,
    public_key: Ed25519PublicKey,
) -> Entitlement:
    if len(blob) < ENTITLEMENT_FIXED_LEN + ENTITLEMENT_SIG_LEN:
        raise ValueError("Entitlement blob too short")

    fixed = blob[:ENTITLEMENT_FIXED_LEN]
    version, key_id, mid, slot, issued_at, expires_at, host_len = struct.unpack(
        ">BI16sBIIB", fixed
    )
    if version != ENTITLEMENT_VERSION:
        raise ValueError(f"Unsupported entitlement version: {version}")
    if host_len > 64:
        raise ValueError("hostname_hint length out of range")

    expected_total = ENTITLEMENT_FIXED_LEN + host_len + ENTITLEMENT_SIG_LEN
    if len(blob) != expected_total:
        raise ValueError("Entitlement blob length mismatch")

    host_bytes = blob[ENTITLEMENT_FIXED_LEN : ENTITLEMENT_FIXED_LEN + host_len]
    sig = blob[-ENTITLEMENT_SIG_LEN:]
    unsigned = blob[: ENTITLEMENT_FIXED_LEN + host_len]
    try:
        public_key.verify(sig, unsigned)
    except InvalidSignature as e:
        raise ValueError("Entitlement signature invalid") from e

    return Entitlement(
        version=version,
        key_id=key_id,
        machine_id=mid,
        slot_index=slot,
        issued_at=issued_at,
        expires_at=expires_at,
        hostname_hint=host_bytes.decode("utf-8", errors="replace"),
    )


# --- Convenience -------------------------------------------------------------


def generate_signing_keypair() -> tuple[str, str]:
    """Generate a new Ed25519 signing keypair. Returns (private_pem, public_pem)."""
    sk = Ed25519PrivateKey.generate()
    pk = sk.public_key()
    sk_pem = sk.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode()
    pk_pem = pk.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ).decode()
    return sk_pem, pk_pem


def public_key_raw_bytes(pk: Ed25519PublicKey) -> bytes:
    return pk.public_bytes(
        encoding=serialization.Encoding.Raw,
        format=serialization.PublicFormat.Raw,
    )


__all__ = [
    "ENTITLEMENT_TTL_SECONDS",
    "ENTITLEMENT_VERSION",
    "Entitlement",
    "ISSUE_DATE_EPOCH_DAYS",
    "KEY_CHARS_TOTAL",
    "KEY_VERSION",
    "LicenseKeyPayload",
    "MACHINE_ID_LEN",
    "MAX_SLOTS_PER_LICENSE",
    "decode_entitlement",
    "decode_license_key",
    "generate_signing_keypair",
    "issue_entitlement",
    "issue_license_key",
    "load_private_key",
    "load_public_key",
    "normalize_key",
    "public_key_raw_bytes",
    "verify_license_key_signature",
]


# Re-anchor sanity check: ISSUE_DATE_EPOCH_DAYS must equal
# (2025-01-01 - 1970-01-01).days. Compute at import to fail loud on regression.
def _verify_epoch() -> None:
    import datetime as _dt

    expected = (_dt.date(2025, 1, 1) - _dt.date(1970, 1, 1)).days
    if expected != ISSUE_DATE_EPOCH_DAYS:
        raise RuntimeError(
            f"ISSUE_DATE_EPOCH_DAYS is {ISSUE_DATE_EPOCH_DAYS}, should be {expected}"
        )


_verify_epoch()
