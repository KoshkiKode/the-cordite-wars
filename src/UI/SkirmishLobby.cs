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

    private const int MaxSlots = 6;

    private OptionButton _mapSelector = null!;
    private Label _mapInfoLabel = null!;
    private VBoxContainer _playerSlotsBox = null!;
    private OptionButton _startingCordite = null!;
    private OptionButton _gameSpeed = null!;
    private CheckBox _fogOfWar = null!;
    private Button _startBtn = null!;
    private Button _addAiBtn = null!;

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
        // Try to load from MapLoader
        try
        {
            var loader = new MapLoader();
            loader.LoadAllMaps("res://data/maps");
            var ids = loader.GetMapIds();
            _mapIds = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                _mapIds[i] = ids[i];

            for (int i = 0; i < _mapIds.Length; i++)
            {
                if (loader.HasMap(_mapIds[i]))
                {
                    var map = loader.GetMap(_mapIds[i]);
                    _mapSelector.AddItem(map.DisplayName, i);
                }
                else
                {
                    _mapSelector.AddItem(_mapIds[i], i);
                }
            }
        }
        catch
        {
            GD.PushWarning("[SkirmishLobby] Could not load maps.");
        }

        // Add fallback if no maps found
        if (_mapSelector.ItemCount == 0)
        {
            _mapSelector.AddItem("Crossroads", 0);
            _mapSelector.AddItem("Six Fronts", 1);
            _mapSelector.AddItem("Coral Atoll", 2);
            _mapIds = new[] { "crossroads", "six_fronts", "coral_atoll" };
        }

        _mapSelector.Selected = 0;
        UpdateMapInfo();
    }

    private void OnMapSelected(long index)
    {
        UpdateMapInfo();
        RefreshSlotVisibility();
    }

    private void UpdateMapInfo()
    {
        // Try to get map details
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
        GD.Print($"  Map: {_mapSelector.GetItemText(_mapSelector.Selected)}");
        GD.Print($"  Players: {_playerCount}");
        for (int i = 0; i < _playerCount; i++)
        {
            string faction = _slotFaction[i].GetItemText(_slotFaction[i].Selected);
            string diff = i == 0 ? "Human" : DifficultyNames[_slotDifficulty[i].Selected];
            GD.Print($"  Slot {i + 1}: {faction} ({diff})");
        }
        // TODO: Transition to game scene
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
