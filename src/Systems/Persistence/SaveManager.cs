using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using CorditeWars.Core;

namespace CorditeWars.Systems.Persistence;

/// <summary>
/// Custom JSON converter that serializes FixedPoint as its raw int value (lossless).
/// The standard FixedPointJsonConverter in FactionRegistry writes floats for human-readable
/// data files; saves need exact round-trip fidelity, so we write the raw integer instead.
/// </summary>
public sealed class FixedPointSaveJsonConverter : JsonConverter<FixedPoint>
{
    public override FixedPoint Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return FixedPoint.FromRaw((int)reader.GetInt64());
        }
        throw new JsonException($"Expected a number for FixedPoint but got {reader.TokenType}.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        FixedPoint value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Raw);
    }
}

/// <summary>
/// Manages saving and loading complete game state to/from disk.
/// Uses Godot FileAccess/DirAccess for cross-platform compatibility
/// (user:// maps to app-specific storage on Android/iOS).
/// </summary>
public sealed partial class SaveManager : Node
{
    private const string SaveDirectory = "user://saves";
    private const string SaveFileExtension = ".cwsave";
    private const string LegacySaveFileExtension = ".json";
    private const int MaxAutoSaves = 3;

    private static readonly JsonSerializerOptions SaveJsonOptions = CreateSaveJsonOptions();

    private static JsonSerializerOptions CreateSaveJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    /// <summary>
    /// Ensures the save directory exists. Must be called before any save/load operations.
    /// </summary>
    private static void EnsureSaveDirectory()
    {
        if (!DirAccess.DirExistsAbsolute(SaveDirectory))
        {
            var err = DirAccess.MakeDirAbsolute(SaveDirectory);
            if (err != Error.Ok)
            {
                GD.PushError($"[SaveManager] Failed to create save directory: {err}");
            }
        }
    }

    /// <summary>
    /// Serializes the given save data and writes it to user://saves/{slotName}.cwsave.
    /// Returns true on success.
    /// </summary>
    public bool SaveGame(string slotName, SaveGameData data)
    {
        if (!IsValidSlotName(slotName))
        {
            GD.PushError($"[SaveManager] Invalid save slot name: '{slotName}'.");
            return false;
        }

        EnsureSaveDirectory();

        string filePath = GetProprietarySavePath(slotName);
        string legacyPath = GetLegacySavePath(slotName);

        try
        {
            byte[] payload = SaveFileCodec.Encode(data, SaveJsonOptions);

            using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                GD.PushError($"[SaveManager] Could not open '{filePath}' for writing (error: {FileAccess.GetOpenError()}).");
                return false;
            }

            file.StoreBuffer(payload);
            file.Flush();

            // Keep a single canonical file per slot.
            if (FileAccess.FileExists(legacyPath))
            {
                DirAccess.RemoveAbsolute(legacyPath);
            }

            EventBus.Instance?.EmitGameSaved(slotName);
            GD.Print($"[SaveManager] Game saved to slot '{slotName}'.");
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"[SaveManager] Save failed for slot '{slotName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a save file from disk and deserializes it.
    /// Returns null if the file is missing or corrupt.
    /// </summary>
    public SaveGameData? LoadGame(string slotName)
    {
        if (!IsValidSlotName(slotName))
        {
            GD.PushError($"[SaveManager] Invalid save slot name: '{slotName}'.");
            return null;
        }

        string filePath = ResolveLoadPath(slotName);

        if (!FileAccess.FileExists(filePath))
        {
            GD.PushWarning($"[SaveManager] Save file not found: '{filePath}'.");
            return null;
        }

        try
        {
            using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
            if (file is null)
            {
                GD.PushError($"[SaveManager] Could not open '{filePath}' for reading (error: {FileAccess.GetOpenError()}).");
                return null;
            }

            byte[] payload = file.GetBuffer((long)file.GetLength());
            SaveGameData? data = SaveFileCodec.Decode(payload, SaveJsonOptions);

            if (data != null)
            {
                EventBus.Instance?.EmitGameLoaded(slotName);
                GD.Print($"[SaveManager] Game loaded from slot '{slotName}'.");
            }

            return data;
        }
        catch (Exception ex)
        {
            GD.PushError($"[SaveManager] Load failed for slot '{slotName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Deletes a save file from disk. Returns true if the file was removed.
    /// </summary>
    public bool DeleteSave(string slotName)
    {
        if (!IsValidSlotName(slotName))
        {
            GD.PushError($"[SaveManager] Invalid save slot name: '{slotName}'.");
            return false;
        }

        string primaryPath = GetProprietarySavePath(slotName);
        string legacyPath = GetLegacySavePath(slotName);

        bool removedAny = false;

        if (FileAccess.FileExists(primaryPath))
        {
            var err = DirAccess.RemoveAbsolute(primaryPath);
            if (err != Error.Ok)
            {
                GD.PushError($"[SaveManager] Failed to delete '{primaryPath}': {err}");
                return false;
            }
            removedAny = true;
        }

        if (FileAccess.FileExists(legacyPath))
        {
            var err = DirAccess.RemoveAbsolute(legacyPath);
            if (err != Error.Ok)
            {
                GD.PushError($"[SaveManager] Failed to delete '{legacyPath}': {err}");
                return false;
            }
            removedAny = true;
        }

        if (!removedAny)
        {
            GD.PushWarning($"[SaveManager] Cannot delete — file not found for slot '{slotName}'.");
            return false;
        }

        GD.Print($"[SaveManager] Deleted save slot '{slotName}'.");
        return true;
    }

    /// <summary>
    /// Lists all save slots with metadata, sorted by slot name.
    /// Reads just enough of each file to extract the header fields.
    /// </summary>
    public SortedList<string, SaveSlotInfo> GetSaveSlots()
    {
        var slots = new SortedList<string, SaveSlotInfo>();

        EnsureSaveDirectory();

        using var dir = DirAccess.Open(SaveDirectory);
        if (dir is null)
        {
            GD.PushWarning($"[SaveManager] Could not open save directory (error: {DirAccess.GetOpenError()}).");
            return slots;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir()
                && (fileName.EndsWith(SaveFileExtension, StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(LegacySaveFileExtension, StringComparison.OrdinalIgnoreCase)))
            {
                bool isLegacy = fileName.EndsWith(LegacySaveFileExtension, StringComparison.OrdinalIgnoreCase);
                int extensionLength = isLegacy ? LegacySaveFileExtension.Length : SaveFileExtension.Length;
                string slotName = fileName.Substring(0, fileName.Length - extensionLength);
                string filePath = $"{SaveDirectory}/{fileName}";

                try
                {
                    using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
                    if (file is not null)
                    {
                        byte[] payload = file.GetBuffer((long)file.GetLength());
                        SaveGameData? data = SaveFileCodec.Decode(payload, SaveJsonOptions);

                        if (data != null)
                        {
                            if (slots.ContainsKey(slotName))
                            {
                                // Prefer proprietary saves if both legacy and proprietary files are present.
                                if (isLegacy)
                                {
                                    fileName = dir.GetNext();
                                    continue;
                                }

                                slots.Remove(slotName);
                            }

                            var info = new SaveSlotInfo
                            {
                                SlotName = slotName,
                                MapId = data.MapId,
                                MapDisplayName = data.MapId,
                                CurrentTick = data.CurrentTick,
                                SaveTimestamp = data.SaveTimestamp,
                                PlayerCount = data.Players.Length,
                                Version = data.Version
                            };
                            slots.Add(slotName, info);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[SaveManager] Could not read save metadata from '{filePath}': {ex.Message}");
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();
        return slots;
    }

    /// <summary>
    /// Saves to an auto-save slot and rotates old auto-saves (keeps last 3).
    /// Slot names: autosave_0, autosave_1, autosave_2 (0 = newest).
    /// </summary>
    public void AutoSave(SaveGameData data)
    {
        EnsureSaveDirectory();

        // Rotate: delete oldest, shift others
        string oldestPath = $"{SaveDirectory}/autosave_{MaxAutoSaves - 1}{SaveFileExtension}";
        if (FileAccess.FileExists(oldestPath))
        {
            DirAccess.RemoveAbsolute(oldestPath);
        }

        for (int i = MaxAutoSaves - 2; i >= 0; i--)
        {
            string fromPath = $"{SaveDirectory}/autosave_{i}{SaveFileExtension}";
            string toPath = $"{SaveDirectory}/autosave_{i + 1}{SaveFileExtension}";
            string fromFileName = $"autosave_{i}{SaveFileExtension}";
            string toFileName = $"autosave_{i + 1}{SaveFileExtension}";

            if (FileAccess.FileExists(fromPath))
            {
                if (FileAccess.FileExists(toPath))
                {
                    DirAccess.RemoveAbsolute(toPath);
                }

                using var dir = DirAccess.Open(SaveDirectory);
                if (dir is not null)
                {
                    dir.Rename(fromFileName, toFileName);
                }
            }
        }

        SaveGame("autosave_0", data);
        GD.Print("[SaveManager] Auto-save completed.");
    }

    /// <summary>
    /// Restores full game state from save data. This is the entry point
    /// for rebuilding all managers and systems from a loaded save file.
    /// The actual restoration is delegated to GameSession.LoadFromSave,
    /// which has references to all the required systems.
    /// This method validates the save data before returning it.
    /// </summary>
    public SaveGameData? RestoreGame(string slotName)
    {
        SaveGameData? data = LoadGame(slotName);
        if (data is null)
        {
            GD.PushError($"[SaveManager] Cannot restore — load failed for slot '{slotName}'.");
            return null;
        }

        if (data.ProtocolVersion != 1)
        {
            GD.PushError($"[SaveManager] Unsupported protocol version {data.ProtocolVersion} in slot '{slotName}'.");
            return null;
        }

        return data;
    }

    private static string GetProprietarySavePath(string slotName) => $"{SaveDirectory}/{slotName}{SaveFileExtension}";

    private static string GetLegacySavePath(string slotName) => $"{SaveDirectory}/{slotName}{LegacySaveFileExtension}";

    private static string ResolveLoadPath(string slotName)
    {
        string proprietaryPath = GetProprietarySavePath(slotName);
        if (FileAccess.FileExists(proprietaryPath))
        {
            return proprietaryPath;
        }

        return GetLegacySavePath(slotName);
    }

    private static bool IsValidSlotName(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
        {
            return false;
        }

        for (int i = 0; i < slotName.Length; i++)
        {
            char c = slotName[i];
            bool ok = char.IsLetterOrDigit(c) || c is '_' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
