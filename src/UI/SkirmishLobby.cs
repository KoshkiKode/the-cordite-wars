using Godot;
using UnnamedRTS.Game.World;

namespace UnnamedRTS.UI;

/// <summary>
/// Skirmish setup (vs AI). Map selector from MapLoader, player slots
/// with faction/difficulty dropdowns, game settings, Start button.
/// </summary>
public partial class SkirmishLobby : Control
{
    private static readonly string[] DifficultyNames = { "Easy", "Medium", "Hard" };
    private static readonly string[] StartingCorditeOptions = { "3000", "5000", "10000" };
    private static readonly string[] GameSpeedNames = { "Slow", "Normal", "Fast" };
    private static readonly string[] BiomeNames =
    {
        "temperate", "desert", "rocky", "coastal", "archipelago", "volcanic"
    };

    private const int MaxSlots = 6;
    private const string RandomMapId = "__random__";

    private OptionButton _mapSelector = null!;
    private Label _mapInfoLabel = null!;
    private VBoxContainer _playerSlotsBox = null!;
    private OptionButton _startingCordite = null!;
    private OptionButton _gameSpeed = null!;
    private CheckBox _fogOfWar = null!;
    private Button _startBtn = null!;
    private Button _addAiBtn = null!;
    private OptionButton _biomeSelector = null!;
    private HBoxContainer _biomeRow = null!;

    // Player slot controls
    private OptionButton[] _slotFaction = new OptionButton[MaxSlots];
    private OptionButton[] _slotDifficulty = new OptionButton[MaxSlots];
    private Button[] _slotRemoveBtn = new Button[MaxSlots];
    private HBoxContainer[] _slotRows = new HBoxContainer[MaxSlots];
    private int _playerCount = 2; // Minimum 2 players (1 human + 1 AI)

    // Map data
    private string[] _mapIds = System.Array.Empty<string>();
    private int _maxPlayersForMap = 6;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Outer margin
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 80);
        margin.AddThemeConstantOverride("margin_right", 80);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(outerVBox);

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        outerVBox.AddChild(header);

        var backBtn = new Button();
        backBtn.Text = "\u25C4 BACK";
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
        header.AddChild(backBtn);

        var headerSpacer = new Control();
        headerSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(headerSpacer);

        var title = new Label();
        title.Text = "SKIRMISH";
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // Map selector row
        var mapRow = new HBoxContainer();
        mapRow.AddThemeConstantOverride("separation", 16);
        outerVBox.AddChild(mapRow);

        var mapLabel = new Label();
        mapLabel.Text = "MAP:";
        UITheme.StyleLabel(mapLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
        mapRow.AddChild(mapLabel);

        _mapSelector = new OptionButton();
        _mapSelector.CustomMinimumSize = new Vector2(240, 0);
        UITheme.StyleOptionButton(_mapSelector);
        _mapSelector.ItemSelected += OnMapSelected;
        mapRow.AddChild(_mapSelector);

        var mapSpacer = new Control();
        mapSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mapRow.AddChild(mapSpacer);

        _mapInfoLabel = new Label();
        UITheme.StyleLabel(_mapInfoLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        mapRow.AddChild(_mapInfoLabel);

        // Biome selector row (visible only when "Random Map" is selected)
        _biomeRow = new HBoxContainer();
        _biomeRow.AddThemeConstantOverride("separation", 16);
        _biomeRow.Visible = false;
        outerVBox.AddChild(_biomeRow);

        var biomeLabel = new Label();
        biomeLabel.Text = "BIOME:";
        UITheme.StyleLabel(biomeLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
        _biomeRow.AddChild(biomeLabel);

        _biomeSelector = new OptionButton();
        _biomeSelector.CustomMinimumSize = new Vector2(180, 0);
        for (int b = 0; b < BiomeNames.Length; b++)
            _biomeSelector.AddItem(BiomeNames[b].ToUpperInvariant(), b);
        _biomeSelector.Selected = 0;
        UITheme.StyleOptionButton(_biomeSelector);
        _biomeRow.AddChild(_biomeSelector);

        // Separator
        var sep1 = new HSeparator();
        sep1.AddThemeColorOverride("separator", UITheme.Border);
        outerVBox.AddChild(sep1);

        // Player slots
        var playersLabel = new Label();
        playersLabel.Text = "PLAYERS";
        UITheme.StyleLabel(playersLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        outerVBox.AddChild(playersLabel);

        _playerSlotsBox = new VBoxContainer();
        _playerSlotsBox.AddThemeConstantOverride("separation", 8);
        outerVBox.AddChild(_playerSlotsBox);

        // Build all slots (some hidden initially)
        for (int i = 0; i < MaxSlots; i++)
            BuildPlayerSlot(i);

        // Add AI button
        _addAiBtn = new Button();
        _addAiBtn.Text = "+ Add AI Player";
        UITheme.StyleButton(_addAiBtn);
        _addAiBtn.Pressed += OnAddAiPressed;
        outerVBox.AddChild(_addAiBtn);

        // Separator
        var sep2 = new HSeparator();
        sep2.AddThemeColorOverride("separator", UITheme.Border);
        outerVBox.AddChild(sep2);

        // Game settings
        var settingsLabel = new Label();
        settingsLabel.Text = "GAME SETTINGS";
        UITheme.StyleLabel(settingsLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        outerVBox.AddChild(settingsLabel);

        var settingsGrid = new VBoxContainer();
        settingsGrid.AddThemeConstantOverride("separation", 8);
        outerVBox.AddChild(settingsGrid);

        // Starting Cordite
        _startingCordite = new OptionButton();
        for (int i = 0; i < StartingCorditeOptions.Length; i++)
            _startingCordite.AddItem(StartingCorditeOptions[i], i);
        _startingCordite.Selected = 1; // 5000
        UITheme.StyleOptionButton(_startingCordite);
        settingsGrid.AddChild(MakeSettingRow("Starting Cordite:", _startingCordite));

        // Game Speed
        _gameSpeed = new OptionButton();
        for (int i = 0; i < GameSpeedNames.Length; i++)
            _gameSpeed.AddItem(GameSpeedNames[i], i);
        _gameSpeed.Selected = 1; // Normal
        UITheme.StyleOptionButton(_gameSpeed);
        settingsGrid.AddChild(MakeSettingRow("Game Speed:", _gameSpeed));

        // Fog of War
        _fogOfWar = new CheckBox();
        _fogOfWar.Text = "Enabled";
        _fogOfWar.ButtonPressed = true;
        UITheme.StyleCheckBox(_fogOfWar);
        settingsGrid.AddChild(MakeSettingRow("Fog of War:", _fogOfWar));

        // Start button
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        outerVBox.AddChild(btnRow);

        _startBtn = new Button();
        _startBtn.Text = "\u25B6 START GAME";
        _startBtn.CustomMinimumSize = new Vector2(240, 0);
        UITheme.StyleAccentButton(_startBtn);
        _startBtn.Pressed += OnStartPressed;
        btnRow.AddChild(_startBtn);

        // Load maps and refresh UI
        LoadMaps();
        RefreshSlotVisibility();
    }

    private void BuildPlayerSlot(int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var label = new Label();
        label.Text = index == 0 ? $"Player {index + 1} (You):" : $"Player {index + 1} (AI):";
        label.CustomMinimumSize = new Vector2(160, 0);
        UITheme.StyleLabel(label, UITheme.FontSizeNormal, UITheme.TextPrimary);
        row.AddChild(label);

        // Faction dropdown
        var factionOpt = new OptionButton();
        factionOpt.AddItem("Random", 0);
        for (int f = 0; f < UITheme.FactionNames.Length; f++)
            factionOpt.AddItem(UITheme.FactionNames[f], f + 1);
        factionOpt.Selected = 0;
        factionOpt.CustomMinimumSize = new Vector2(140, 0);
        UITheme.StyleOptionButton(factionOpt);
        row.AddChild(factionOpt);
        _slotFaction[index] = factionOpt;

        // Difficulty dropdown (not for human player)
        var diffOpt = new OptionButton();
        for (int d = 0; d < DifficultyNames.Length; d++)
            diffOpt.AddItem(DifficultyNames[d], d);
        diffOpt.Selected = 1; // Medium
        diffOpt.CustomMinimumSize = new Vector2(120, 0);
        UITheme.StyleOptionButton(diffOpt);
        diffOpt.Visible = index != 0;
        row.AddChild(diffOpt);
        _slotDifficulty[index] = diffOpt;

        // Remove button (not for first two slots)
        var removeBtn = new Button();
        removeBtn.Text = "\u2715";
        UITheme.StyleButton(removeBtn);
        removeBtn.Visible = index >= 2;
        int idx = index;
        removeBtn.Pressed += () => OnRemoveSlot(idx);
        row.AddChild(removeBtn);
        _slotRemoveBtn[index] = removeBtn;

        _slotRows[index] = row;
        _playerSlotsBox.AddChild(row);
    }

    private void LoadMaps()
    {
        // "Random Map" is always the first entry
        _mapSelector.AddItem("\u2728 Random Map", 0);

        var idList = new System.Collections.Generic.List<string>();
        idList.Add(RandomMapId);

        // Try to load hand-crafted maps from MapLoader
        try
        {
            var loader = new MapLoader();
            loader.LoadAllMaps("res://data/maps");
            var ids = loader.GetMapIds();

            for (int i = 0; i < ids.Count; i++)
            {
                idList.Add(ids[i]);
                if (loader.HasMap(ids[i]))
                {
                    var map = loader.GetMap(ids[i]);
                    _mapSelector.AddItem(map.DisplayName, idList.Count - 1);
                }
                else
                {
                    _mapSelector.AddItem(ids[i], idList.Count - 1);
                }
            }
        }
        catch
        {
            GD.PushWarning("[SkirmishLobby] Could not load maps.");
        }

        _mapIds = idList.ToArray();

        // Add fallback if only random (no hand-crafted maps found)
        if (_mapSelector.ItemCount <= 1)
        {
            _mapSelector.AddItem("Crossroads", 1);
            _mapSelector.AddItem("Six Fronts", 2);
            _mapSelector.AddItem("Coral Atoll", 3);
            _mapIds = new[] { RandomMapId, "crossroads", "six_fronts", "coral_atoll" };
        }

        _mapSelector.Selected = 0;
        UpdateMapInfo();
    }

    private void OnMapSelected(long index)
    {
        UpdateMapInfo();
        RefreshSlotVisibility();
    }

    private bool IsRandomMapSelected()
    {
        int idx = _mapSelector.Selected;
        return idx >= 0 && idx < _mapIds.Length && _mapIds[idx] == RandomMapId;
    }

    private void UpdateMapInfo()
    {
        bool isRandom = IsRandomMapSelected();
        _biomeRow.Visible = isRandom;

        if (isRandom)
        {
            _maxPlayersForMap = 6;
            string biome = BiomeNames[_biomeSelector.Selected];
            _mapInfoLabel.Text = $"200x200 | Max 6 players | {biome} (generated)";
            return;
        }

        // Try to get map details for hand-crafted maps
        try
        {
            var loader = new MapLoader();
            loader.LoadAllMaps("res://data/maps");
            int idx = _mapSelector.Selected;
            if (idx >= 0 && idx < _mapIds.Length && loader.HasMap(_mapIds[idx]))
            {
                var map = loader.GetMap(_mapIds[idx]);
                _maxPlayersForMap = map.MaxPlayers;
                _mapInfoLabel.Text = $"{map.Width}x{map.Height} | Max {map.MaxPlayers} players | {map.Biome}";
                return;
            }
        }
        catch { /* Fallback below */ }

        _maxPlayersForMap = 6;
        _mapInfoLabel.Text = "Max 6 players";
    }

    private void RefreshSlotVisibility()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            _slotRows[i].Visible = i < _playerCount;
            _slotRemoveBtn[i].Visible = i >= 2 && i < _playerCount;
        }
        _addAiBtn.Visible = _playerCount < _maxPlayersForMap && _playerCount < MaxSlots;
    }

    private void OnAddAiPressed()
    {
        if (_playerCount < _maxPlayersForMap && _playerCount < MaxSlots)
        {
            _playerCount++;
            RefreshSlotVisibility();
        }
    }

    private void OnRemoveSlot(int index)
    {
        if (_playerCount <= 2) return;

        // Shift down slot settings
        for (int i = index; i < _playerCount - 1; i++)
        {
            _slotFaction[i].Selected = _slotFaction[i + 1].Selected;
            _slotDifficulty[i].Selected = _slotDifficulty[i + 1].Selected;
        }
        _playerCount--;
        RefreshSlotVisibility();
    }

    private void OnStartPressed()
    {
        GD.Print("[SkirmishLobby] Starting skirmish game...");
        
        var playerConfigs = new System.Collections.Generic.List<UnnamedRTS.Game.PlayerConfig>();
        for (int i = 0; i < _playerCount; i++)
        {
            string faction = _slotFaction[i].GetItemText(_slotFaction[i].Selected);
            if (faction == "Random") 
            {
                // Simple random: pick a random faction (1 through length-1)
                faction = _slotFaction[i].GetItemText(GD.RandRange(1, UITheme.FactionNames.Length));
            }
            // Normalize generic name into internal ID (lowercase, no spaces)
            faction = faction.ToLower().Replace(" ", "_");

            bool isAI = i > 0;
            int diff = isAI ? _slotDifficulty[i].Selected : 0;
            string name = isAI ? $"AI {faction}" : "Player 1";

            playerConfigs.Add(new UnnamedRTS.Game.PlayerConfig
            {
                PlayerId = i + 1,
                FactionId = faction,
                IsAI = isAI,
                AIDifficulty = diff,
                PlayerName = name
            });
            GD.Print($"  Slot {i + 1}: {faction} (AI: {isAI}, Diff: {diff})");
        }

        int corditeOpt = _startingCordite.Selected;
        int startingCordite = corditeOpt switch {
            0 => 3000,
            1 => 5000,
            2 => 10000,
            _ => 5000
        };

        var matchSeed = (ulong)System.DateTime.Now.Ticks;

        UnnamedRTS.Game.World.MapGenConfig? mapGen = null;
        string mapId;

        if (IsRandomMapSelected())
        {
            string biome = BiomeNames[_biomeSelector.Selected];
            mapGen = new UnnamedRTS.Game.World.MapGenConfig
            {
                Width = 200,
                Height = 200,
                PlayerCount = _playerCount,
                Biome = biome,
                Seed = matchSeed,
            };
            mapId = $"generated_{matchSeed}";
        }
        else
        {
            mapId = _mapIds[_mapSelector.Selected];
        }

        var config = new UnnamedRTS.Game.MatchConfig
        {
            MapId = mapId,
            MatchSeed = matchSeed,
            GameSpeed = 1,
            FogOfWar = _fogOfWar.ButtonPressed,
            StartingCordite = startingCordite,
            PlayerConfigs = playerConfigs.ToArray(),
            MapGeneration = mapGen,
        };

        UnnamedRTS.Game.Main.PendingConfig = config;
        SceneTransition.TransitionTo(GetTree(), "res://scenes/Game/Main.tscn");
    }

    private static HBoxContainer MakeSettingRow(string labelText, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        var label = new Label();
        label.Text = labelText;
        label.CustomMinimumSize = new Vector2(180, 0);
        UITheme.StyleLabel(label, UITheme.FontSizeNormal, UITheme.TextPrimary);
        row.AddChild(label);

        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);

        return row;
    }
}
