#!/usr/bin/env python3
"""
license_keygen.py — local CLI for generating Cordite Wars signing keys
and (optionally) one-off license keys for press, support replacements,
and dev testing.

The web/checkout flow issues keys *automatically* via the Stripe webhook;
this tool exists for the bootstrap step (generating the production
signing key) and for occasional manual issuance.

Usage
-----

  # 1. Bootstrap: produce the Ed25519 keypair to paste into Secrets Manager
  #    AND the public key (raw 32-byte hex) to embed in the game binary:
  python3 tools/license_keygen.py generate-signing-key

  # 2. Issue a one-off key (dev/test/comp). Reads the private key PEM from
  #    stdin so it never appears in shell history:
  cat private_key.pem | python3 tools/license_keygen.py mint --sku 1

  # 3. Decode + inspect a key (no signing key required):
  python3 tools/license_keygen.py inspect XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

# Allow importing the shared crypto module without installing the Lambda package.
_LAMBDA_DIR = Path(__file__).resolve().parents[1] / "infra" / "aws" / "lambda"
sys.path.insert(0, str(_LAMBDA_DIR))

import license_keys as lk  # noqa: E402


def cmd_generate_signing_key(args: argparse.Namespace) -> int:
    sk_pem, pk_pem = lk.generate_signing_keypair()
    pk = lk.load_public_key(pk_pem)
    pk_raw = lk.public_key_raw_bytes(pk)
    pk_hex = pk_raw.hex()

    if args.json:
        print(json.dumps({
            "private_key_pem": sk_pem,
            "public_key_pem":  pk_pem,
            "public_key_raw_hex": pk_hex,
        }, indent=2))
    else:
        print("=== PRIVATE KEY (paste into Secrets Manager: <bucket>/license-signing) ===")
        print(sk_pem)
        print("=== PUBLIC KEY (PEM) ===")
        print(pk_pem)
        print("=== PUBLIC KEY (raw 32-byte hex, embed in game binary) ===")
        print(pk_hex)
        print()
        print("DO NOT regenerate this key after launch — every issued license")
        print("and every customer's stored entitlement becomes invalid.")
    return 0


def _read_private_key_input(args: argparse.Namespace):
    if args.key_file:
        text = Path(args.key_file).read_text()
    else:
        if sys.stdin.isatty():
            sys.stderr.write(
                "ERROR: pipe the private key PEM into stdin or pass --key-file PATH.\n"
            )
            return None
        text = sys.stdin.read()
    return lk.load_private_key(text)


def cmd_mint(args: argparse.Namespace) -> int:
    sk = _read_private_key_input(args)
    if sk is None:
        return 2
    formatted, payload = lk.issue_license_key(
        private_key=sk,
        sku=args.sku,
        flags=args.flags,
    )
    if args.json:
        print(json.dumps({
            "key": formatted,
            "key_id": payload.key_id,
            "sku": payload.sku,
            "issue_date_days": payload.issue_date_days,
            "flags": payload.flags,
        }, indent=2))
    else:
        print(formatted)
    return 0


def cmd_inspect(args: argparse.Namespace) -> int:
    try:
        payload = lk.decode_license_key(args.key)
    except ValueError as e:
        sys.stderr.write(f"Invalid key: {e}\n")
        return 1
    issue_date = payload.issue_date_days + lk.ISSUE_DATE_EPOCH_DAYS
    import datetime as _dt
    issue_iso = (_dt.date(1970, 1, 1) + _dt.timedelta(days=issue_date)).isoformat()
    print(json.dumps({
        "version":    payload.version,
        "key_id":     payload.key_id,
        "sku":        payload.sku,
        "issue_date": issue_iso,
        "flags":      payload.flags,
    }, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_gen = sub.add_parser("generate-signing-key", help="Generate a new Ed25519 signing keypair.")
    p_gen.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    p_gen.set_defaults(func=cmd_generate_signing_key)

    p_mint = sub.add_parser("mint", help="Issue a one-off license key (dev/test/comp).")
    p_mint.add_argument("--sku", type=int, default=1)
    p_mint.add_argument("--flags", type=int, default=0)
    p_mint.add_argument("--key-file", type=str, default=None,
                        help="Path to the signing private key PEM. Default: read from stdin.")
    p_mint.add_argument("--json", action="store_true")
    p_mint.set_defaults(func=cmd_mint)

    p_inspect = sub.add_parser("inspect", help="Decode + display a license key.")
    p_inspect.add_argument("key")
    p_inspect.set_defaults(func=cmd_inspect)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
