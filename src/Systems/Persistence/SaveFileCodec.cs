using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Encodes and decodes save files using the Cordite proprietary binary container.
/// </summary>
public static class SaveFileCodec
{
    private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("CORDSAVE");
    private const byte FormatVersion = 1;

    public static byte[] Encode(SaveGameData data, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        string json = JsonSerializer.Serialize(data, options);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        output.Write(MagicHeader, 0, MagicHeader.Length);
        output.WriteByte(FormatVersion);

        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return output.ToArray();
    }

    public static SaveGameData? Decode(byte[] payload, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);

        if (payload.Length == 0)
        {
            return null;
        }

        // Legacy compatibility: raw JSON saves.
        if (payload[0] == (byte)'{')
        {
            string legacyJson = Encoding.UTF8.GetString(payload);
            return JsonSerializer.Deserialize<SaveGameData>(legacyJson, options);
        }

        if (!IsProprietaryPayload(payload))
        {
            throw new InvalidDataException("Unrecognized save payload format.");
        }

        using var input = new MemoryStream(payload, MagicHeader.Length + 1, payload.Length - (MagicHeader.Length + 1));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<SaveGameData>(json, options);
    }

    public static bool IsProprietaryPayload(byte[] payload)
    {
        if (payload.Length < MagicHeader.Length + 1)
        {
            return false;
        }

        for (int i = 0; i < MagicHeader.Length; i++)
        {
            if (payload[i] != MagicHeader[i])
            {
                return false;
            }
        }

        return payload[MagicHeader.Length] == FormatVersion;
    }
}
