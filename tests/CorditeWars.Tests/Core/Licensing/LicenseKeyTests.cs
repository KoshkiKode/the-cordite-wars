using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CorditeWars.Core.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace CorditeWars.Tests.Core.Licensing;

/// <summary>
/// Unit tests for the offline pieces of the licensing system. The HTTP
/// client and the silent-renewal background task are exercised separately
/// where possible — these tests stick to deterministic in-memory work so
/// they're stable in CI.
/// </summary>
public class LicenseKeyTests
{
    // --- LicenseKey.TryParse / Normalize ------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDE-ABCDE")]
    public void TryParse_RejectsObviouslyWrongInput(string input)
    {
        Assert.False(LicenseKey.TryParse(input, out var key, out var err));
        Assert.Null(key);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Normalize_StripsWhitespaceDashesAndFoldsConfusables()
    {
        // I→1, L→1, O→0, U→V, plus dashes + spaces removed and uppercased.
        Assert.Equal("110V", LicenseKey.Normalize(" i-l-o-u "));
        Assert.Equal("ABCDE", LicenseKey.Normalize("a-b\nc\td-e"));
    }

    [Fact]
    public void TryParse_RejectsBadChecksum()
    {
        // Use a known-good key from the round-trip test then corrupt the
        // last (check) character.
        var (validFormatted, _) = TestKeyFactory.IssueKey();
        // Replace the last char with something else.
        char last = validFormatted[^1];
        char alt = last == '0' ? 'Z' : '0';
        string broken = validFormatted[..^1] + alt;
        Assert.False(LicenseKey.TryParse(broken, out _, out var err));
        Assert.Contains("checksum", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_RoundTrip_PreservesFields()
    {
        var (formatted, expected) = TestKeyFactory.IssueKey(sku: 1, issueDateDays: 100, flags: 0);
        Assert.True(LicenseKey.TryParse(formatted, out var key, out var err));
        Assert.Null(err);
        Assert.NotNull(key);
        Assert.Equal(LicenseKey.CurrentVersion, key!.Version);
        Assert.Equal(expected.KeyId, key.KeyId);
        Assert.Equal(expected.Sku, key.Sku);
        Assert.Equal(expected.IssueDateDays, key.IssueDateDays);
        Assert.Equal(expected.Flags, key.Flags);
        // Canonical output: 5×5 with dashes.
        Assert.Equal(29, key.Formatted.Length);
        Assert.Equal(4, key.Formatted.Count(c => c == '-'));
    }

    [Fact]
    public void TryParse_AcceptsLowercaseAndExtraSpaces()
    {
        var (formatted, _) = TestKeyFactory.IssueKey();
        string munged = " " + formatted.ToLowerInvariant().Replace("-", "  ") + "\n";
        Assert.True(LicenseKey.TryParse(munged, out var key, out _));
        Assert.Equal(formatted, key!.Formatted);
    }

    [Fact]
    public void IssueDateUtc_ResolvesAgainstEpochAnchor()
    {
        var (formatted, _) = TestKeyFactory.IssueKey(issueDateDays: 0);
        Assert.True(LicenseKey.TryParse(formatted, out var key, out _));
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), key!.IssueDateUtc());
    }

    // --- Entitlement decode + signature verification ------------------------

    [Fact]
    public void Entitlement_RoundTrip_VerifiesSignature()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = Enumerable.Repeat((byte)0xAB, 16).ToArray();
        long issuedAt = 1_700_000_000;
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, keyId: 0xDEADBEEF, machineId: machineId,
            slotIndex: 4, hostname: "test-host", issuedAt: (uint)issuedAt);

        var ent = Entitlement.DecodeAndVerify(blob, pk);
        Assert.Equal(0xDEADBEEFu, ent.KeyId);
        Assert.Equal(machineId, ent.MachineId);
        Assert.Equal(4, ent.SlotIndex);
        Assert.Equal("test-host", ent.HostnameHint);
        Assert.Equal((uint)issuedAt, ent.IssuedAt);
        Assert.True(ent.ExpiresAt > ent.IssuedAt);
    }

    [Fact]
    public void Entitlement_RejectsTamperedSlotIndex()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        // Flip slot_index byte at offset 21.
        blob[21] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => Entitlement.DecodeAndVerify(blob, pk));
    }

    [Fact]
    public void Entitlement_RejectsWithWrongPublicKey()
    {
        var (sk, _) = TestKeyFactory.GenerateKeypair();
        var (_, otherPk) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        Assert.Throws<InvalidDataException>(() => Entitlement.DecodeAndVerify(blob, otherPk));
    }

    [Fact]
    public void Entitlement_ShouldRenew_TrueAfterHalfwayPoint()
    {
        // Construct an entitlement whose lifetime is well in the past:
        // issuedAt now − 10 days, expiresAt now + 5 days. We're past the
        // halfway point so renewal should be requested.
        var ent = new Entitlement
        {
            Version = Entitlement.CurrentVersion,
            KeyId = 1,
            MachineId = new byte[16],
            SlotIndex = 1,
            IssuedAt = (uint)DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds(),
            ExpiresAt = (uint)DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds(),
            HostnameHint = "x",
        };
        Assert.True(ent.ShouldRenew(DateTime.UtcNow));
    }

    [Fact]
    public void Entitlement_ShouldRenew_FalseEarlyInLifetime()
    {
        var ent = new Entitlement
        {
            Version = Entitlement.CurrentVersion,
            KeyId = 1,
            MachineId = new byte[16],
            SlotIndex = 1,
            IssuedAt = (uint)DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds(),
            ExpiresAt = (uint)DateTimeOffset.UtcNow.AddDays(400).ToUnixTimeSeconds(),
            HostnameHint = "x",
        };
        Assert.False(ent.ShouldRenew(DateTime.UtcNow));
    }

    [Fact]
    public void Entitlement_IsExpired_WhenPastExpiry()
    {
        var ent = new Entitlement
        {
            Version = Entitlement.CurrentVersion,
            KeyId = 1,
            MachineId = new byte[16],
            SlotIndex = 1,
            IssuedAt = (uint)DateTimeOffset.UtcNow.AddDays(-400).ToUnixTimeSeconds(),
            ExpiresAt = (uint)DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds(),
            HostnameHint = "x",
        };
        Assert.True(ent.IsExpired(DateTime.UtcNow));
    }

    // --- EntitlementStore round trip + machine-id mismatch -----------------

    [Fact]
    public void EntitlementStore_SaveThenLoad_RoundTrips()
    {
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, 42, machineId, 1, "h",
            (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob);
        Assert.True(store.TryLoad(Convert.ToHexString(machineId).ToLowerInvariant(), out var ent, out _));
        Assert.NotNull(ent);
        Assert.Equal(42u, ent!.KeyId);
    }

    [Fact]
    public void EntitlementStore_RejectsMismatchedMachine()
    {
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = Enumerable.Repeat((byte)0xAA, 16).ToArray();
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, 1, machineId, 1, "h",
            (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob);
        Assert.False(store.TryLoad(Convert.ToHexString(new byte[16]).ToLowerInvariant(),
            out _, out var reason));
        Assert.Contains("machine", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntitlementStore_TryLoad_FalseWhenFileMissing()
    {
        using var tempDir = new TempDir();
        var (_, pk) = TestKeyFactory.GenerateKeypair();
        var store = new EntitlementStore(tempDir.Path, pk);
        Assert.False(store.TryLoad(null, out _, out var reason));
        Assert.Contains("no entitlement", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntitlementStore_RejectsExpiredEntitlement()
    {
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = new byte[16];
        // Issued 800 days ago with default 400-day TTL → expired.
        long issued = DateTimeOffset.UtcNow.AddDays(-800).ToUnixTimeSeconds();
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, 1, machineId, 1, "h", (uint)issued,
            ttlSeconds: 400 * 86400);
        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob);
        Assert.False(store.TryLoad(null, out _, out var reason));
        Assert.Contains("expired", reason, StringComparison.OrdinalIgnoreCase);
    }

    // --- StorefrontDetector -------------------------------------------------

    [Fact]
    public void StorefrontDetector_ReturnsStandalone_WhenNoMarkers()
    {
        using var tempDir = new TempDir();
        Assert.Equal(StorefrontDetector.Storefront.Standalone,
            StorefrontDetector.Detect(tempDir.Path));
    }

    [Fact]
    public void StorefrontDetector_DetectsSteam()
    {
        using var tempDir = new TempDir();
        File.WriteAllText(Path.Combine(tempDir.Path, "steam_appid.txt"), "12345");
        Assert.Equal(StorefrontDetector.Storefront.Steam,
            StorefrontDetector.Detect(tempDir.Path));
    }

    [Fact]
    public void StorefrontDetector_DetectsGog()
    {
        using var tempDir = new TempDir();
        File.WriteAllText(Path.Combine(tempDir.Path, "goggame-1234567890.info"), "{}");
        Assert.Equal(StorefrontDetector.Storefront.Gog,
            StorefrontDetector.Detect(tempDir.Path));
    }

    // --- MachineFingerprint -------------------------------------------------

    [Fact]
    public void MachineFingerprint_IsStableAcrossCalls()
    {
        string a = MachineFingerprint.Compute();
        string b = MachineFingerprint.Compute();
        Assert.Equal(a, b);
        // 16 bytes hex = 32 chars.
        Assert.Equal(32, a.Length);
        Assert.True(a.All(c => "0123456789abcdef".Contains(c)));
    }

    // --- Entitlement.DecodeAndVerify — additional error paths ---------------

    [Fact]
    public void Entitlement_DecodeAndVerify_NullPublicKey_Throws()
    {
        var (sk, _) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        Assert.Throws<ArgumentException>(
            () => Entitlement.DecodeAndVerify(blob, null!));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_WrongPublicKeyLength_Throws()
    {
        var (sk, _) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        Assert.Throws<ArgumentException>(
            () => Entitlement.DecodeAndVerify(blob, new byte[31])); // must be 32
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_TooShortBlob_Throws()
    {
        var (_, pk) = TestKeyFactory.GenerateKeypair();
        byte[] tooShort = new byte[Entitlement.FixedHeaderBytes + Entitlement.SignatureBytes - 1];
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(tooShort, pk));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_UnsupportedVersion_Throws()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        // Corrupt version byte to 99
        blob[0] = 99;
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(blob, pk));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_HostnameTooLong_Throws()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        // Issue a valid blob, then patch hostnameLen byte to 65 (> MaxHostnameBytes=64)
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        // hostnameLen is at offset 30.
        blob[30] = 65;
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(blob, pk));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_BlobLengthMismatch_Throws()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        // Issue a valid blob, then append an extra byte to cause length mismatch.
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, new byte[16], 1, "h", 1);
        byte[] padded = new byte[blob.Length + 1];
        Buffer.BlockCopy(blob, 0, padded, 0, blob.Length);
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(padded, pk));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_SlotIndexZero_Throws()
    {
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        // Issue a validly-signed blob with slotIndex=0 (< 1, out of range).
        // Signature is valid so the check proceeds to the slot validation.
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, keyId: 1, machineId: new byte[16],
            slotIndex: 0,   // out of valid range [1..10]
            hostname: "h", issuedAt: 1);
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(blob, pk));
    }

    [Fact]
    public void Entitlement_DecodeAndVerify_ExpiresAtNotAfterIssuedAt_Throws()
    {
        // Build a blob where expiresAt <= issuedAt by using ttlSeconds=0.
        // IssueEntitlement uses issuedAt + ttlSeconds for expires, so ttlSeconds=0
        // makes expiresAt == issuedAt.
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] blob = TestKeyFactory.IssueEntitlement(
            sk, 1, new byte[16], 1, "h",
            issuedAt: 1_000_000,
            ttlSeconds: 0);
        Assert.Throws<InvalidDataException>(
            () => Entitlement.DecodeAndVerify(blob, pk));
    }

    [Fact]
    public void Entitlement_IssuedAtUtc_MatchesExpected()
    {
        uint ts = 1_700_000_000;
        var ent = new Entitlement
        {
            Version = Entitlement.CurrentVersion,
            KeyId = 1,
            MachineId = new byte[16],
            SlotIndex = 1,
            IssuedAt = ts,
            ExpiresAt = ts + 86400
        };
        DateTime expected = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
        Assert.Equal(expected, ent.IssuedAtUtc);
    }

    // --- EntitlementStore — additional paths --------------------------------

    [Fact]
    public void EntitlementStore_Constructor_BadPublicKey_Throws()
    {
        using var tempDir = new TempDir();
        Assert.Throws<ArgumentException>(
            () => new EntitlementStore(tempDir.Path, new byte[31])); // must be 32
    }

    [Fact]
    public void EntitlementStore_FilePath_ReturnsExpectedPath()
    {
        using var tempDir = new TempDir();
        var (_, pk) = TestKeyFactory.GenerateKeypair();
        var store = new EntitlementStore(tempDir.Path, pk);

        string expectedPath = System.IO.Path.Combine(
            tempDir.Path,
            Path.GetFileName(EntitlementStore.LicenseSubdir),
            Path.GetFileName(EntitlementStore.FileName));
        Assert.Equal(expectedPath, store.FilePath);
    }

    [Fact]
    public void EntitlementStore_Save_ThenOverwrite_UsesReplace()
    {
        // Saves twice so the second save triggers File.Replace (file already exists).
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = new byte[16];
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] blob1 = TestKeyFactory.IssueEntitlement(sk, 1, machineId, 1, "h", now);
        byte[] blob2 = TestKeyFactory.IssueEntitlement(sk, 2, machineId, 1, "h", now);

        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob1);              // first save — File.Move path
        store.Save(blob2);              // second save — File.Replace path

        Assert.True(store.TryLoad(null, out var ent, out _));
        Assert.Equal(2u, ent!.KeyId);  // second save should win
    }

    [Fact]
    public void EntitlementStore_TryLoad_InvalidBlobOnDisk_ReturnsFalse()
    {
        // Write garbage bytes directly to the file — simulates a corrupt save.
        using var tempDir = new TempDir();
        var (_, pk) = TestKeyFactory.GenerateKeypair();
        var store = new EntitlementStore(tempDir.Path, pk);

        // Write garbage to the store's path.
        string licensePath = System.IO.Path.Combine(
            tempDir.Path, EntitlementStore.LicenseSubdir, EntitlementStore.FileName);
        System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(tempDir.Path, EntitlementStore.LicenseSubdir));
        File.WriteAllBytes(licensePath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        bool ok = store.TryLoad(null, out _, out string? reason);
        Assert.False(ok);
        Assert.Contains("invalid", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntitlementStore_Clear_DeletesFile()
    {
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = new byte[16];
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, machineId, 1, "h", now);

        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob);
        Assert.True(File.Exists(store.FilePath));

        store.Clear();
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void EntitlementStore_Clear_WhenNoFile_DoesNotThrow()
    {
        using var tempDir = new TempDir();
        var (_, pk) = TestKeyFactory.GenerateKeypair();
        var store = new EntitlementStore(tempDir.Path, pk);

        // No file has been saved — Clear should be a no-op.
        var ex = Record.Exception(() => store.Clear());
        Assert.Null(ex);
    }

    [Fact]
    public void EntitlementStore_TryLoad_UnreadableFile_ReturnsFalseWithReason()
    {
        using var tempDir = new TempDir();
        var (sk, pk) = TestKeyFactory.GenerateKeypair();
        byte[] machineId = new byte[16];
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] blob = TestKeyFactory.IssueEntitlement(sk, 1, machineId, 1, "h", now);

        var store = new EntitlementStore(tempDir.Path, pk);
        store.Save(blob);

        // Remove read permission so File.ReadAllBytes throws.
        System.IO.File.SetAttributes(store.FilePath, System.IO.FileAttributes.ReadOnly);
        try
        {
            // Make file completely inaccessible by changing permissions via chmod.
            var startProc = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("chmod", $"0 \"{store.FilePath}\"")
                { UseShellExecute = false });
            startProc!.WaitForExit();

            bool ok = store.TryLoad(null, out _, out string? reason);
            Assert.False(ok);
            Assert.Contains("failed to read", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Restore so TempDir.Dispose can delete the file.
            var restoreProc = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("chmod", $"644 \"{store.FilePath}\"")
                { UseShellExecute = false });
            restoreProc!.WaitForExit();
        }
    }

    // --- helpers ------------------------------------------------------------

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cordite-licensing-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}

/// <summary>Helpers that mint test license keys + entitlements without
/// going through the network. Exists in test code only.</summary>
internal static class TestKeyFactory
{
    public static (byte[] privateKeyRaw32, byte[] publicKeyRaw32) GenerateKeypair()
    {
        var rng = new SecureRandom();
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(rng));
        var pair = gen.GenerateKeyPair();
        var sk = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        var pk = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        return (sk, pk);
    }

    /// <summary>Mint a license key by reproducing the Python encoding logic.</summary>
    public static (string formatted, KeyFields fields) IssueKey(
        byte sku = 1,
        ushort issueDateDays = 0,
        byte flags = 0)
    {
        var (sk, _) = GenerateKeypair();
        uint keyId = (uint)Random.Shared.Next();
        var unsigned = new byte[9];
        unsigned[0] = LicenseKey.CurrentVersion;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsigned.AsSpan(1, 4), keyId);
        unsigned[5] = sku;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(unsigned.AsSpan(6, 2), issueDateDays);
        unsigned[8] = flags;

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(sk, 0));
        signer.BlockUpdate(unsigned, 0, unsigned.Length);
        byte[] sig = signer.GenerateSignature();

        var payload = new byte[15];
        Buffer.BlockCopy(unsigned, 0, payload, 0, 9);
        Buffer.BlockCopy(sig, 0, payload, 9, 6);

        string body = CrockfordEncode(payload).Substring(0, 24);
        char check = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[Crc8(System.Text.Encoding.ASCII.GetBytes(body)) & 0x1F];
        string raw = body + check;
        string formatted = string.Join("-",
            raw.Substring(0, 5), raw.Substring(5, 5), raw.Substring(10, 5),
            raw.Substring(15, 5), raw.Substring(20, 5));
        return (formatted, new KeyFields(keyId, sku, issueDateDays, flags));
    }

    public static byte[] IssueEntitlement(
        byte[] privateKeyRaw32,
        uint keyId,
        byte[] machineId,
        byte slotIndex,
        string hostname,
        uint issuedAt,
        uint ttlSeconds = 400 * 86400)
    {
        var hostBytes = System.Text.Encoding.UTF8.GetBytes(hostname);
        if (hostBytes.Length > 64)
        {
            Array.Resize(ref hostBytes, 64);
        }
        var unsigned = new byte[31 + hostBytes.Length];
        unsigned[0] = Entitlement.CurrentVersion;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsigned.AsSpan(1, 4), keyId);
        Buffer.BlockCopy(machineId, 0, unsigned, 5, 16);
        unsigned[21] = slotIndex;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsigned.AsSpan(22, 4), issuedAt);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsigned.AsSpan(26, 4), issuedAt + ttlSeconds);
        unsigned[30] = (byte)hostBytes.Length;
        Buffer.BlockCopy(hostBytes, 0, unsigned, 31, hostBytes.Length);

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKeyRaw32, 0));
        signer.BlockUpdate(unsigned, 0, unsigned.Length);
        byte[] sig = signer.GenerateSignature();

        var blob = new byte[unsigned.Length + sig.Length];
        Buffer.BlockCopy(unsigned, 0, blob, 0, unsigned.Length);
        Buffer.BlockCopy(sig, 0, blob, unsigned.Length, sig.Length);
        return blob;
    }

    public readonly record struct KeyFields(uint KeyId, byte Sku, ushort IssueDateDays, byte Flags);

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static string CrockfordEncode(byte[] data)
    {
        int bits = 0;
        int value = 0;
        var sb = new System.Text.StringBuilder();
        foreach (byte b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(value >> bits) & 0x1F]);
            }
        }
        if (bits > 0)
        {
            sb.Append(Alphabet[(value << (5 - bits)) & 0x1F]);
        }
        return sb.ToString();
    }

    private static int Crc8(byte[] data)
    {
        int crc = 0;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
        }
        return crc;
    }
}
