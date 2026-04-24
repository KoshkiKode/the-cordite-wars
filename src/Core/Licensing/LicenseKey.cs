using System;
using System.Buffers.Binary;
using System.Text;

namespace CorditeWars.Core.Licensing;

/// <summary>
/// Parses + format-validates a 25-character Cordite Wars license key.
///
/// Format (mirror of <c>infra/aws/lambda/license_keys.py</c>):
///
///   <c>XXXXX-XXXXX-XXXXX-XXXXX-XXXXX</c>  (Crockford Base32, dashes optional)
///
/// Payload bytes (15 raw bytes → 24 base32 chars + 1 CRC-8 check char):
///
///     0       1     version       (currently 1)
///     1       4     key_id        (uint32, big-endian)
///     5       1     sku           (1 = standard)
///     6       2     issue_date    (days since 2025-01-01, uint16)
///     8       1     flags
///     9       6     truncated Ed25519 signature
///
/// The truncated signature can <em>only</em> be verified server-side (the
/// server has the matching private key and re-signs to compare). Client-side
/// verification of the truncated portion is mathematically impossible, so
/// the client trusts the entitlement blob returned by activation as the
/// authoritative offline trust anchor.
///
/// Client-side this class only: (a) normalises whitespace + dashes +
/// confusables, (b) verifies the CRC-8 to catch typos before any network
/// call, and (c) decodes the metadata fields for display.
/// </summary>
public sealed class LicenseKey
{
    /// <summary>Crockford Base32 alphabet (no I, L, O, U).</summary>
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly int[] CrockfordDecode = BuildDecodeTable();

    public const int TotalChars = 25;
    public const int BodyChars = 24;
    public const int PayloadBytes = 15;
    public const int CurrentVersion = 1;

    public byte Version { get; }
    public uint KeyId { get; }
    public byte Sku { get; }
    public ushort IssueDateDays { get; }
    public byte Flags { get; }

    /// <summary>The canonical formatted key (uppercase, dashed in 5-char groups).</summary>
    public string Formatted { get; }

    private LicenseKey(string formatted, byte version, uint keyId, byte sku, ushort issueDateDays, byte flags)
    {
        Formatted = formatted;
        Version = version;
        KeyId = keyId;
        Sku = sku;
        IssueDateDays = issueDateDays;
        Flags = flags;
    }

    /// <summary>Parses a key from arbitrary user input. Returns false with
    /// a human-readable error if the input is not a syntactically valid
    /// Cordite Wars license key.</summary>
    public static bool TryParse(string? input, out LicenseKey? key, out string? error)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "License key is empty.";
            return false;
        }

        string norm = Normalize(input);
        if (norm.Length != TotalChars)
        {
            error = $"License key must be {TotalChars} characters (got {norm.Length}).";
            return false;
        }

        string body = norm.Substring(0, BodyChars);
        char check = norm[BodyChars];
        char expectedCheck = CrockfordAlphabet[Crc8(Encoding.ASCII.GetBytes(body)) & 0x1F];
        if (check != expectedCheck)
        {
            error = "License key checksum mismatch — looks like a typo. Double-check what you typed.";
            return false;
        }

        byte[] payload;
        try
        {
            payload = Base32Decode(body, PayloadBytes);
        }
        catch (FormatException ex)
        {
            error = $"Malformed license key: {ex.Message}";
            return false;
        }

        byte version = payload[0];
        if (version != CurrentVersion)
        {
            error = $"Unsupported license key version: {version}.";
            return false;
        }

        uint keyId = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(1, 4));
        byte sku = payload[5];
        ushort issueDateDays = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(6, 2));
        byte flags = payload[8];

        // Re-emit canonical formatting so the UI displays exactly what we'll send to the server.
        string formatted = string.Join(
            "-",
            norm.Substring(0, 5),
            norm.Substring(5, 5),
            norm.Substring(10, 5),
            norm.Substring(15, 5),
            norm.Substring(20, 5));

        key = new LicenseKey(formatted, version, keyId, sku, issueDateDays, flags);
        error = null;
        return true;
    }

    /// <summary>
    /// Normalize: uppercase, strip whitespace + dashes, fold I/L/O/U
    /// confusables to 1/1/0/V. Returns the body with no separators.
    /// </summary>
    public static string Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char raw in input)
        {
            if (raw is ' ' or '-' or '\t' or '\r' or '\n') continue;
            char c = char.ToUpperInvariant(raw);
            sb.Append(c switch
            {
                'I' => '1',
                'L' => '1',
                'O' => '0',
                'U' => 'V',
                _   => c,
            });
        }
        return sb.ToString();
    }

    // --- Crockford Base32 decode --------------------------------------------

    private static int[] BuildDecodeTable()
    {
        var table = new int[128];
        for (int i = 0; i < table.Length; i++) table[i] = -1;
        for (int i = 0; i < CrockfordAlphabet.Length; i++)
        {
            table[CrockfordAlphabet[i]] = i;
        }
        return table;
    }

    private static byte[] Base32Decode(string text, int expectedBytes)
    {
        int bits = 0;
        int value = 0;
        var output = new byte[expectedBytes];
        int outIdx = 0;
        foreach (char c in text)
        {
            if (c >= 128 || CrockfordDecode[c] < 0)
            {
                throw new FormatException($"Invalid character '{c}'.");
            }
            value = (value << 5) | CrockfordDecode[c];
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                if (outIdx >= expectedBytes)
                {
                    // We have enough bytes; the rest is padding bits.
                    return output;
                }
                output[outIdx++] = (byte)((value >> bits) & 0xFF);
            }
        }
        if (outIdx < expectedBytes)
        {
            throw new FormatException("Encoded payload truncated.");
        }
        return output;
    }

    // --- CRC-8 (poly 0x07, init 0x00) ---------------------------------------

    private static int Crc8(ReadOnlySpan<byte> data)
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

    /// <summary>
    /// Returns the issue date as a UTC DateTime by adding <see cref="IssueDateDays"/>
    /// to the epoch anchor (2025-01-01).
    /// </summary>
    public DateTime IssueDateUtc() => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(IssueDateDays);
}
