using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CorditeWars.UI;

public partial class ReplayBrowserScreen : Control
{
    private VBoxContainer _listContainer = null!;
    private Label         _statusLabel   = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
        PopulateList();
    }

    private void BuildUI()
    {
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   60);
        margin.AddThemeConstantOverride("margin_right",  60);
        margin.AddThemeConstantOverride("margin_top",    40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(vbox);

        // Header row
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(header);

        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () => SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/MainMenu.tscn");
        header.AddChild(backBtn);

        var title = new Label();
        title.Text = Tr("MENU_REPLAYS");
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);

        vbox.AddChild(new HSeparator());

        // Scrollable replay list
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 8);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_listContainer);

        // Status label
        _statusLabel = new Label();
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_statusLabel);
    }

    private void PopulateList()
    {
        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();

        const string ReplayDir = "user://replays";
        if (!DirAccess.DirExistsAbsolute(ReplayDir))
        {
            _statusLabel.Text = "No replays found.";
            return;
        }

        using var dir = DirAccess.Open(ReplayDir);
        if (dir is null)
        {
            _statusLabel.Text = "Cannot open replay directory.";
            return;
        }

        var files = new List<string>();
        dir.ListDirBegin();
        string f = dir.GetNext();
        while (!string.IsNullOrEmpty(f))
        {
            if (!dir.CurrentIsDir() && f.StartsWith("replay_") && f.EndsWith(".json"))
                files.Add(f);
            f = dir.GetNext();
        }
        dir.ListDirEnd();

        files.Sort(StringComparer.OrdinalIgnoreCase);
        files.Reverse(); // newest first

        if (files.Count == 0)
        {
            _statusLabel.Text = "No replays found.";
            return;
        }

        _statusLabel.Text = string.Empty;

        foreach (string fileName in files)
        {
            string filePath = $"{ReplayDir}/{fileName}";
            string displayText = ParseDisplayText(fileName, filePath);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _listContainer.AddChild(row);

            var nameLabel = new Label();
            nameLabel.Text = displayText;
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            UITheme.StyleLabel(nameLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
            row.AddChild(nameLabel);

            var watchBtn = new Button();
            watchBtn.Text = "Watch";
            UITheme.StyleAccentButton(watchBtn);
            string capturedFile = fileName;
            watchBtn.Pressed += () => ShowWatchDialog(capturedFile);
            row.AddChild(watchBtn);

            var deleteBtn = new Button();
            deleteBtn.Text = "Delete";
            UITheme.StyleButton(deleteBtn);
            string capturedPath = filePath;
            deleteBtn.Pressed += () => DeleteReplay(capturedPath);
            row.AddChild(deleteBtn);

            _listContainer.AddChild(new HSeparator());
        }
    }

    private static string ParseDisplayText(string fileName, string filePath)
    {
        string mapId = "unknown";
        string timestamp = fileName;

        var parts = fileName.Replace(".json", "").Split('_');
        if (parts.Length >= 3)
        {
            mapId = string.Join("_", parts, 2, parts.Length - 2);
            string ts = parts[1];
            if (ts.Length >= 15)
            {
                string date = ts[..8];
                string time = ts.Length >= 15 ? ts[9..Math.Min(15, ts.Length)] : ts[9..];
                if (time.Length >= 6)
                    timestamp = $"{date[..4]}-{date[4..6]}-{date[6..8]}  {time[..2]}:{time[2..4]}:{time[4..6]}";
            }
        }

        string extra = string.Empty;
        try
        {
            using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
            if (file is not null)
            {
                string json = file.GetAsText();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("totalTicks", out var ticksEl))
                {
                    ulong ticks = ticksEl.GetUInt64();
                    int minutes = (int)(ticks / 30 / 60);
                    int seconds = (int)(ticks / 30 % 60);
                    extra = $"  [{minutes}:{seconds:00}]";
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[ReplayBrowserScreen] Could not read replay details from '{filePath}': {ex.Message}");
        }

        return $"{timestamp}  —  {mapId}{extra}";
    }

    private void ShowWatchDialog(string fileName)
    {
        string filePath = $"user://replays/{fileName}";
        string details = BuildReplayDetails(filePath, fileName);

        var dialog = new AcceptDialog();
        dialog.Title = "Replay Details";
        dialog.DialogText = details;
        dialog.OkButtonText = Tr("MENU_OK");
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += () => dialog.QueueFree();
        dialog.Canceled  += () => dialog.QueueFree();
    }

    private static string BuildReplayDetails(string filePath, string fileName)
    {
        try
        {
            using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
            if (file is null)
                return $"Could not open replay file.\n\nFile: {fileName}";

            using var doc = JsonDocument.Parse(file.GetAsText());
            var root = doc.RootElement;

            var sb = new StringBuilder();

            // Map
            if (root.TryGetProperty("map_id", out var mapEl))
                sb.AppendLine($"Map:      {mapEl.GetString()}");

            // Mission (campaign replays)
            if (root.TryGetProperty("mission_id", out var missionEl) &&
                missionEl.ValueKind == JsonValueKind.String)
                sb.AppendLine($"Mission:  {missionEl.GetString()}");

            // Duration
            if (root.TryGetProperty("total_ticks", out var ticksEl))
            {
                ulong ticks = ticksEl.GetUInt64();
                int minutes = (int)(ticks / 30 / 60);
                int seconds = (int)(ticks / 30 % 60);
                sb.AppendLine($"Duration: {minutes}:{seconds:00}");
            }

            // Players — parse into a name-lookup dictionary for the winner step below
            var playerNames = new System.Collections.Generic.Dictionary<int, string>();
            if (root.TryGetProperty("players", out var playersEl) &&
                playersEl.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine();
                sb.AppendLine("Players:");
                foreach (var player in playersEl.EnumerateArray())
                {
                    string name    = player.TryGetProperty("player_name", out var nEl) ? nEl.GetString() ?? "Unknown" : "Unknown";
                    string faction = player.TryGetProperty("faction_id",  out var fEl) ? fEl.GetString() ?? "?"       : "?";
                    bool   isAI    = player.TryGetProperty("is_ai",       out var aEl) && aEl.GetBoolean();
                    int    id      = player.TryGetProperty("player_id",   out var iEl) ? iEl.GetInt32() : -1;
                    sb.AppendLine($"  {name} ({faction}){(isAI ? " [AI]" : "")}");
                    if (id >= 0)
                        playerNames[id] = name;
                }
            }

            // Winner — resolve to player name when available
            if (root.TryGetProperty("winner_player_id", out var winnerEl))
            {
                int winnerId = winnerEl.GetInt32();
                sb.AppendLine();
                if (winnerId < 0)
                    sb.AppendLine("Result:   Draw");
                else if (playerNames.TryGetValue(winnerId, out string? winnerName))
                    sb.AppendLine($"Winner:   {winnerName}");
                else
                    sb.AppendLine($"Winner:   Player {winnerId}");
            }

            sb.AppendLine();
            sb.AppendLine("(Live playback is not yet available.)");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[ReplayBrowser] Failed to parse replay metadata for '{fileName}': {ex.Message}");
            return $"Could not read replay metadata.\n\nFile: {fileName}";
        }
    }

    private void DeleteReplay(string filePath)
    {
        var err = DirAccess.RemoveAbsolute(filePath);
        if (err == Error.Ok)
        {
            GD.Print($"[ReplayBrowser] Deleted: {filePath}");
            PopulateList();
        }
        else
        {
            _statusLabel.Text = $"Failed to delete replay (error {err}).";
        }
    }
}
