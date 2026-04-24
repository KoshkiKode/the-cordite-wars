using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace CorditeWars.Core.Licensing;

/// <summary>
/// Decoded entitlement blob — the signed proof that "this license is
/// activated on this machine, valid until <c>ExpiresAt</c>".
///
/// Mirror of the Python <c>Entitlement</c> dataclass in
/// <c>infra/aws/lambda/license_keys.py</c>. Format (binary):
///
///   offset  size  field
///   ------  ----  ------------------------------------------------
///   0       1     version       (currently 1)
///   1       4     key_id        (uint32, big-endian)
///   5       16    machine_id    (SHA-256 truncated to 16 bytes)
///   21      1     slot_index    (1..10)
///   22      4     issued_at     (unix seconds, uint32)
///   26      4     expires_at    (unix seconds, uint32)
///   30      1     hostname_len
///   31      N     hostname_hint (UTF-8, max 64 bytes)
///   ...     64    signature     (Ed25519 over preceding bytes)
/// </summary>
public sealed class Entitlement
{
    public const int CurrentVersion = 1;
    public const int FixedHeaderBytes = 31;
    public const int SignatureBytes = 64;
    public const int MachineIdBytes = 16;
    public const int MaxHostnameBytes = 64;

    public byte Version { get; init; }
    public uint KeyId { get; init; }
    public byte[] MachineId { get; init; } = Array.Empty<byte>();
    public byte SlotIndex { get; init; }
    public uint IssuedAt { get; init; }
    public uint ExpiresAt { get; init; }
    public string HostnameHint { get; init; } = "";

    public DateTime IssuedAtUtc => DateTimeOffset.FromUnixTimeSeconds(IssuedAt).UtcDateTime;
    public DateTime ExpiresAtUtc => DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).UtcDateTime;

    public string MachineIdHex() => Convert.ToHexString(MachineId).ToLowerInvariant();

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>True if the entitlement is past its halfway point — used
    /// to decide whether to attempt silent background renewal.</summary>
    public bool ShouldRenew(DateTime nowUtc)
    {
        long lifeSeconds = (long)ExpiresAt - IssuedAt;
        if (lifeSeconds <= 0) return true;
        long halfway = IssuedAt + (lifeSeconds / 2);
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= halfway;
    }

    /// <summary>
    /// Verify + decode a signed entitlement blob using the embedded public key.
    /// </summary>
    public static Entitlement DecodeAndVerify(byte[] blob, byte[] publicKeyRaw32)
    {
        if (blob is null) throw new ArgumentNullException(nameof(blob));
        if (publicKeyRaw32 is null || publicKeyRaw32.Length != 32)
        {
            throw new ArgumentException("Public key must be 32 raw bytes.", nameof(publicKeyRaw32));
        }
        if (blob.Length < FixedHeaderBytes + SignatureBytes)
        {
            throw new InvalidDataException("Entitlement blob too short.");
        }

        byte version = blob[0];
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported entitlement version {version}.");
        }

        uint keyId = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(1, 4));
        byte[] machineId = blob[5..(5 + MachineIdBytes)];
        byte slotIndex = blob[21];
        uint issuedAt = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(22, 4));
        uint expiresAt = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(26, 4));
        byte hostnameLen = blob[30];
        if (hostnameLen > MaxHostnameBytes)
        {
            throw new InvalidDataException("hostname_hint too long.");
        }

        int totalLen = FixedHeaderBytes + hostnameLen + SignatureBytes;
        if (blob.Length != totalLen)
        {
            throw new InvalidDataException("Entitlement blob length mismatch.");
        }

        string hostname = Encoding.UTF8.GetString(blob, FixedHeaderBytes, hostnameLen);
        byte[] unsigned = blob[..(FixedHeaderBytes + hostnameLen)];
        byte[] signature = blob[(blob.Length - SignatureBytes)..];

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKeyRaw32, 0));
        verifier.BlockUpdate(unsigned, 0, unsigned.Length);
        if (!verifier.VerifySignature(signature))
        {
            throw new InvalidDataException("Entitlement signature is invalid.");
        }

        if (slotIndex < 1 || slotIndex > 10)
        {
            throw new InvalidDataException("slot_index out of range.");
        }
        if (expiresAt <= issuedAt)
        {
            throw new InvalidDataException("expires_at <= issued_at.");
        }

        return new Entitlement
        {
            Version = version,
            KeyId = keyId,
            MachineId = machineId,
            SlotIndex = slotIndex,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            HostnameHint = hostname,
        };
    }
}

/// <summary>
/// Reads + writes the signed entitlement blob to the user-data directory.
/// Treats a missing file, an invalid signature, an expired entitlement, or
/// a fingerprint mismatch all the same: as "not activated".
///
/// File location: <c>user://license/entitlement.dat</c>
/// (resolved with <see cref="Godot.OS.GetUserDataDir"/> — but to keep the
/// logic Godot-independent for unit testing, the constructor accepts a
/// directory path directly).
/// </summary>
public sealed class EntitlementStore
{
    public const string FileName = "entitlement.dat";
    public const string LicenseSubdir = "license";

    private readonly string _path;
    private readonly byte[] _publicKey;

    public EntitlementStore(string userDataDir, byte[] publicKey32)
    {
        if (publicKey32 is null || publicKey32.Length != 32)
        {
            throw new ArgumentException("Public key must be 32 raw bytes.", nameof(publicKey32));
        }
        Directory.CreateDirectory(System.IO.Path.Combine(userDataDir, LicenseSubdir));
        _path = System.IO.Path.Combine(userDataDir, LicenseSubdir, FileName);
        _publicKey = publicKey32;
    }

    /// <summary>The path the store reads/writes. Useful for diagnostics.</summary>
    public string FilePath => _path;

    /// <summary>Persist a freshly-issued entitlement blob to disk.</summary>
    public void Save(byte[] blob)
    {
        // Validate before persisting so we never write garbage to disk.
        Entitlement.DecodeAndVerify(blob, _publicKey);
        // Atomic-ish replace via tmp + move so a crash mid-write doesn't
        // corrupt the existing entitlement.
        string tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, blob);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, null);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }

    /// <summary>Try to load + verify the stored entitlement, optionally
    /// confirming it matches the current machine fingerprint.</summary>
    public bool TryLoad(string? expectedMachineIdHex, out Entitlement? entitlement, out string? reason)
    {
        entitlement = null;
        if (!File.Exists(_path))
        {
            reason = "no entitlement on disk";
            return false;
        }

        byte[] blob;
        try
        {
            blob = File.ReadAllBytes(_path);
        }
        catch (Exception ex)
        {
            reason = $"failed to read entitlement: {ex.Message}";
            return false;
        }

        Entitlement ent;
        try
        {
            ent = Entitlement.DecodeAndVerify(blob, _publicKey);
        }
        catch (Exception ex)
        {
            reason = $"entitlement invalid: {ex.Message}";
            return false;
        }

        if (ent.IsExpired(DateTime.UtcNow))
        {
            reason = "entitlement expired";
            return false;
        }

        if (expectedMachineIdHex != null
            && !string.Equals(ent.MachineIdHex(), expectedMachineIdHex, StringComparison.OrdinalIgnoreCase))
        {
            reason = "entitlement was issued for a different machine fingerprint";
            return false;
        }

        entitlement = ent;
        reason = null;
        return true;
    }

    /// <summary>Delete the stored entitlement (used on explicit deactivation).</summary>
    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
