#!/usr/bin/env python3
"""
Generate SHA256 checksums for all exported artifacts.
Creates checksums.txt and checksums.json for distribution verification.
"""

import sys
import json
import hashlib
import argparse
from pathlib import Path
from datetime import datetime

def compute_sha256(file_path: Path) -> str:
    """Compute SHA256 checksum of a file."""
    sha256_hash = hashlib.sha256()
    with open(file_path, "rb") as f:
        for byte_block in iter(lambda: f.read(4096), b""):
            sha256_hash.update(byte_block)
    return sha256_hash.hexdigest()

def generate_checksums(build_dir: Path, output_format: str = "both") -> dict:
    """
    Generate checksums for all artifacts in build directory.
    
    Args:
        build_dir: Directory containing exports (e.g., build/)
        output_format: 'txt' (legacy), 'json', or 'both'
    
    Returns:
        Dict of {filename: checksum}
    """
    checksums = {}
    artifacts = []
    
    # Find all exported artifacts
    for pattern in [
        "windows/*.exe",
        "windows/*.msi",
        "linux/CorditeWars",
        "linux/*.snap",
        "android/*.apk",
        "macos/*.zip",
        "macos/*.dmg",
        "ios/*.zip",  # iOS handoff
    ]:
        for file_path in build_dir.glob(pattern):
            if file_path.is_file():
                checksum = compute_sha256(file_path)
                rel_path = file_path.relative_to(build_dir)
                checksums[str(rel_path)] = checksum
                artifacts.append({
                    "file": str(rel_path),
                    "size": file_path.stat().st_size,
                    "checksum": checksum,
                    "algorithm": "sha256"
                })
                print(f"✓ {rel_path}: {checksum[:16]}...")
    
    if not checksums:
        print(f"⚠ No artifacts found in {build_dir}")
        return checksums
    
    # Write checksums.txt (for verification with `sha256sum -c`)
    if output_format in ("txt", "both"):
        txt_file = build_dir / "checksums.txt"
        with open(txt_file, "w") as f:
            for rel_path, checksum in sorted(checksums.items()):
                f.write(f"{checksum}  {rel_path}\n")
        print(f"\n✓ Generated {txt_file}")
    
    # Write checksums.json (structured format)
    if output_format in ("json", "both"):
        json_file = build_dir / "checksums.json"
        json_data = {
            "generated": datetime.utcnow().isoformat() + "Z",
            "algorithm": "sha256",
            "artifacts": sorted(artifacts, key=lambda x: x["file"])
        }
        with open(json_file, "w") as f:
            json.dump(json_data, f, indent=2)
        print(f"✓ Generated {json_file}")
    
    return checksums

def verify_checksums(checksums_file: Path, build_dir: Path = None) -> bool:
    """
    Verify checksums from checksums.txt or checksums.json.
    
    Args:
        checksums_file: Path to checksums.txt or checksums.json
        build_dir: Base directory for artifact paths (default: parent of checksums file)
    
    Returns:
        True if all checksums match, False otherwise
    """
    if build_dir is None:
        build_dir = checksums_file.parent
    
    mismatches = []
    
    if checksums_file.suffix == ".json":
        with open(checksums_file) as f:
            data = json.load(f)
        
        for artifact in data["artifacts"]:
            file_path = build_dir / artifact["file"]
            if not file_path.exists():
                print(f"✗ Missing: {artifact['file']}")
                mismatches.append(artifact["file"])
                continue
            
            actual_checksum = compute_sha256(file_path)
            if actual_checksum != artifact["checksum"]:
                print(f"✗ Mismatch: {artifact['file']}")
                print(f"  Expected: {artifact['checksum']}")
                print(f"  Got:      {actual_checksum}")
                mismatches.append(artifact["file"])
            else:
                print(f"✓ {artifact['file']}")
    
    else:  # txt format
        with open(checksums_file) as f:
            for line in f:
                if not line.strip():
                    continue
                parts = line.split(None, 1)
                if len(parts) != 2:
                    continue
                
                checksum, rel_path = parts
                checksum = checksum.strip()
                rel_path = rel_path.strip()
                if not rel_path:
                    continue
                file_path = build_dir / rel_path
                
                if not file_path.exists():
                    print(f"✗ Missing: {rel_path}")
                    mismatches.append(rel_path)
                    continue
                
                actual_checksum = compute_sha256(file_path)
                if actual_checksum != checksum:
                    print(f"✗ Mismatch: {rel_path}")
                    print(f"  Expected: {checksum}")
                    print(f"  Got:      {actual_checksum}")
                    mismatches.append(rel_path)
                else:
                    print(f"✓ {rel_path}")
    
    if mismatches:
        print(f"\n✗ {len(mismatches)} checksum(s) failed")
        return False
    
    print(f"\n✓ All checksums verified")
    return True

def main():
    parser = argparse.ArgumentParser(
        description="Generate and verify SHA256 checksums for game exports"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    
    # Generate command
    gen_parser = subparsers.add_parser("generate", help="Generate checksums for artifacts")
    gen_parser.add_argument(
        "build_dir",
        type=Path,
        help="Build directory containing exports (e.g., build/)"
    )
    gen_parser.add_argument(
        "-f", "--format",
        choices=["txt", "json", "both"],
        default="both",
        help="Output format (default: both)"
    )
    
    # Verify command
    ver_parser = subparsers.add_parser("verify", help="Verify checksums")
    ver_parser.add_argument(
        "checksums_file",
        type=Path,
        help="checksums.txt or checksums.json to verify against"
    )
    ver_parser.add_argument(
        "-d", "--dir",
        type=Path,
        help="Build directory (default: parent of checksums file)"
    )
    
    args = parser.parse_args()
    
    if args.command == "generate":
        generate_checksums(args.build_dir.resolve(), args.format)
    
    elif args.command == "verify":
        checksums_file = args.checksums_file.resolve()
        if not checksums_file.exists():
            print(f"✗ {checksums_file} not found")
            sys.exit(1)
        
        success = verify_checksums(checksums_file, args.dir)
        sys.exit(0 if success else 1)

if __name__ == "__main__":
    main()
