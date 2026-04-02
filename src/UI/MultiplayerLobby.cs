using Godot;
using UnnamedRTS.Systems.Networking;

namespace UnnamedRTS.UI;

/// <summary>
/// LAN multiplayer lobby. Host/Find buttons, LAN game list from LanDiscovery,
/// lobby with player slots, faction select, ready toggle.
/// </summary>
public partial class MultiplayerLobby : Control
{
    private enum LobbyState
    {
        Browser,
        InLobby
    }

    private LobbyState _state = LobbyState.Browser;
    private LanDiscovery? _discovery;
    private LobbyManager? _lobbyManager;

    // Browser UI
    private VBoxContainer _browserPanel = null!;
    private VBoxContainer _gameListBox = null!;
    private Label _noGamesLabel = null!;
    private Button _hostBtn = null!;
    private Button _findBtn = null!;

    // Lobby UI
    private VBoxContainer _lobbyPanel = null!;
    private VBoxContainer _playerSlotsBox = null!;
    private OptionButton _mapSelector = null!;
    private Button _readyBtn = null!;
    private Button _startBtn = null!;
    private Button _leaveLobbyBtn = null!;
    private Label _lobbyStatusLabel = null!;

    private bool _isHost;
    private bool _isReady;
    private bool _isSearching;

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
        backBtn.Pressed += OnBackPressed;
        header.AddChild(backBtn);

        var headerSpacer = new Control();
        headerSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(headerSpacer);

        var title = new Label();
        title.Text = "MULTIPLAYER";
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // ── Browser Panel ─────────────────────────────────────────────
        _browserPanel = new VBoxContainer();
        _browserPanel.AddThemeConstantOverride("separation", 16);
        outerVBox.AddChild(_browserPanel);

        // Host / Find buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 16);
        _browserPanel.AddChild(btnRow);

        _hostBtn = new Button();
        _hostBtn.Text = "HOST GAME";
        _hostBtn.CustomMinimumSize = new Vector2(180, 0);
        UITheme.StyleAccentButton(_hostBtn);
        _hostBtn.Pressed += OnHostPressed;
        btnRow.AddChild(_hostBtn);

        _findBtn = new Button();
        _findBtn.Text = "FIND GAMES";
        _findBtn.CustomMinimumSize = new Vector2(180, 0);
        UITheme.StyleButton(_findBtn);
        _findBtn.Pressed += OnFindPressed;
        btnRow.AddChild(_findBtn);

        // LAN Games section
        var gamesHeader = new Label();
        gamesHeader.Text = "\u2500\u2500 LAN Games Found \u2500\u2500";
        UITheme.StyleLabel(gamesHeader, UITheme.FontSizeNormal, UITheme.TextSecondary);
        _browserPanel.AddChild(gamesHeader);

        var gameListScroll = new ScrollContainer();
        gameListScroll.CustomMinimumSize = new Vector2(0, 200);
        _browserPanel.AddChild(gameListScroll);

        _gameListBox = new VBoxContainer();
        _gameListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _gameListBox.AddThemeConstantOverride("separation", 4);
        gameListScroll.AddChild(_gameListBox);

        _noGamesLabel = new Label();
        _noGamesLabel.Text = "No games found. Click FIND GAMES to search.";
        UITheme.StyleLabel(_noGamesLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
        _gameListBox.AddChild(_noGamesLabel);

        // ── Lobby Panel ───────────────────────────────────────────────
        _lobbyPanel = new VBoxContainer();
        _lobbyPanel.AddThemeConstantOverride("separation", 12);
        _lobbyPanel.Visible = false;
        outerVBox.AddChild(_lobbyPanel);

        var lobbyHeader = new Label();
        lobbyHeader.Text = "\u2500\u2500 LOBBY \u2500\u2500";
        UITheme.StyleLabel(lobbyHeader, UITheme.FontSizeNormal, UITheme.TextSecondary);
        _lobbyPanel.AddChild(lobbyHeader);

        _lobbyStatusLabel = new Label();
        _lobbyStatusLabel.Text = "Waiting for players...";
        UITheme.StyleLabel(_lobbyStatusLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        _lobbyPanel.AddChild(_lobbyStatusLabel);

        // Player slots
        _playerSlotsBox = new VBoxContainer();
        _playerSlotsBox.AddThemeConstantOverride("separation", 8);
        _lobbyPanel.AddChild(_playerSlotsBox);

        // Map selector (host only)
        var mapRow = new HBoxContainer();
        mapRow.AddThemeConstantOverride("separation", 12);
        _lobbyPanel.AddChild(mapRow);

        var mapLabel = new Label();
        mapLabel.Text = "Map:";
        UITheme.StyleLabel(mapLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
        mapRow.AddChild(mapLabel);

        _mapSelector = new OptionButton();
        _mapSelector.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleOptionButton(_mapSelector);
        _mapSelector.AddItem("Crossroads", 0);
        _mapSelector.AddItem("Six Fronts", 1);
        _mapSelector.AddItem("Coral Atoll", 2);
        _mapSelector.Selected = 0;
        mapRow.AddChild(_mapSelector);

        // Lobby buttons
        var lobbyBtnRow = new HBoxContainer();
        lobbyBtnRow.AddThemeConstantOverride("separation", 16);
        _lobbyPanel.AddChild(lobbyBtnRow);

        _readyBtn = new Button();
        _readyBtn.Text = "READY";
        _readyBtn.CustomMinimumSize = new Vector2(120, 0);
        UITheme.StyleButton(_readyBtn);
        _readyBtn.Pressed += OnReadyToggle;
        lobbyBtnRow.AddChild(_readyBtn);

        _startBtn = new Button();
        _startBtn.Text = "\u25B6 START";
        _startBtn.CustomMinimumSize = new Vector2(120, 0);
        UITheme.StyleAccentButton(_startBtn);
        _startBtn.Pressed += OnStartPressed;
        _startBtn.Visible = false;
        lobbyBtnRow.AddChild(_startBtn);

        _leaveLobbyBtn = new Button();
        _leaveLobbyBtn.Text = "LEAVE";
        _leaveLobbyBtn.CustomMinimumSize = new Vector2(120, 0);
        UITheme.StyleButton(_leaveLobbyBtn);
        _leaveLobbyBtn.Pressed += OnLeaveLobby;
        lobbyBtnRow.AddChild(_leaveLobbyBtn);

        // Try to get LanDiscovery
        _discovery = new LanDiscovery();
        AddChild(_discovery);
        _discovery.GameFound += OnGameFound;
        _discovery.GameLost += OnGameLost;
    }

    public override void _Process(double delta)
    {
        if (_state == LobbyState.InLobby && _lobbyManager != null)
        {
            RefreshLobbySlots();
            _startBtn.Visible = _isHost;
            _startBtn.Disabled = !(_lobbyManager.AllPlayersReady());
            _mapSelector.Disabled = !_isHost;
        }
    }

    // ── Browser Actions ───────────────────────────────────────────────

    private void OnHostPressed()
    {
        _isHost = true;
        _lobbyManager = new LobbyManager();
        AddChild(_lobbyManager);

        var transport = new NetworkTransport();
        AddChild(transport);
        transport.HostGame();

        _lobbyManager.InitializeAsHost(transport, "Host");

        if (_discovery != null)
            _discovery.StartBroadcasting("My Game", 6, "Crossroads", NetworkTransport.DefaultPort);

        ShowLobby();
    }

    private void OnFindPressed()
    {
        if (_isSearching)
        {
            _discovery?.StopListening();
            _findBtn.Text = "FIND GAMES";
            _isSearching = false;
        }
        else
        {
            _discovery?.StartListening();
            _findBtn.Text = "STOP SEARCHING";
            _isSearching = true;
            _noGamesLabel.Text = "Searching for LAN games...";
        }
    }

    private void OnGameFound(string hostAddress, string hostName, string gameName, int currentPlayers, int maxPlayers, string mapName, int gamePort)
    {
        // Remove "no games" label
        _noGamesLabel.Visible = false;

        // Build game row
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.Name = $"Game_{hostAddress}";

        var panel = new Panel();
        panel.AddThemeStyleboxOverride("panel", UITheme.MakePanel());
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var innerRow = new HBoxContainer();
        innerRow.AddThemeConstantOverride("separation", 16);
        innerRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddChild(innerRow);

        var nameLabel = new Label();
        nameLabel.Text = $"\"{gameName}\"";
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        UITheme.StyleLabel(nameLabel, UITheme.FontSizeNormal, UITheme.TextPrimary);
        innerRow.AddChild(nameLabel);

        var mapLabel = new Label();
        mapLabel.Text = mapName;
        UITheme.StyleLabel(mapLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        innerRow.AddChild(mapLabel);

        var playersLabel = new Label();
        playersLabel.Text = $"{currentPlayers}/{maxPlayers}";
        UITheme.StyleLabel(playersLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        innerRow.AddChild(playersLabel);

        var joinBtn = new Button();
        joinBtn.Text = "JOIN";
        UITheme.StyleAccentButton(joinBtn);
        string addr = hostAddress;
        int port = gamePort;
        joinBtn.Pressed += () => OnJoinGame(addr, port);
        innerRow.AddChild(joinBtn);

        row.AddChild(panel);
        _gameListBox.AddChild(row);
    }

    private void OnGameLost(string hostAddress)
    {
        var node = _gameListBox.GetNodeOrNull($"Game_{hostAddress}");
        node?.QueueFree();

        // Show "no games" if empty
        bool hasGames = false;
        for (int i = 0; i < _gameListBox.GetChildCount(); i++)
        {
            var child = _gameListBox.GetChild(i);
            if (child != _noGamesLabel && child.IsInsideTree())
            {
                hasGames = true;
                break;
            }
        }
        _noGamesLabel.Visible = !hasGames;
    }

    private void OnJoinGame(string host, int port)
    {
        _isHost = false;
        _lobbyManager = new LobbyManager();
        AddChild(_lobbyManager);

        var transport = new NetworkTransport();
        AddChild(transport);
        transport.JoinGame(host, port);

        _lobbyManager.InitializeAsClient(transport, "Player");

        _discovery?.StopListening();
        ShowLobby();
    }

    // ── Lobby Actions ─────────────────────────────────────────────────

    private void ShowLobby()
    {
        _state = LobbyState.InLobby;
        _browserPanel.Visible = false;
        _lobbyPanel.Visible = true;
        _lobbyStatusLabel.Text = _isHost ? "Hosting — waiting for players..." : "Connected — waiting for host...";
        RefreshLobbySlots();
    }

    private void RefreshLobbySlots()
    {
        // Clear existing slots
        for (int i = _playerSlotsBox.GetChildCount() - 1; i >= 0; i--)
            _playerSlotsBox.GetChild(i).QueueFree();

        if (_lobbyManager == null) return;

        var slots = _lobbyManager.GetPlayerSlots();
        for (int i = 0; i < slots.Count; i++)
        {
            var slotKey = slots.Keys[i];
            var slot = slots[slotKey];

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            var nameLabel = new Label();
            nameLabel.Text = $"Player {slotKey + 1}: {slot.PlayerName}";
            nameLabel.CustomMinimumSize = new Vector2(200, 0);
            var nameColor = !string.IsNullOrEmpty(slot.FactionId) ? UITheme.GetFactionColorById(slot.FactionId) : UITheme.TextPrimary;
            UITheme.StyleLabel(nameLabel, UITheme.FontSizeNormal, nameColor);
            row.AddChild(nameLabel);

            // Faction display
            var factionLabel = new Label();
            factionLabel.Text = string.IsNullOrEmpty(slot.FactionId) ? "[No Faction]" : $"[{slot.FactionId}]";
            UITheme.StyleLabel(factionLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
            row.AddChild(factionLabel);

            // Ready status
            var readyLabel = new Label();
            readyLabel.Text = slot.IsReady ? "READY \u2713" : "NOT READY \u2717";
            UITheme.StyleLabel(readyLabel, UITheme.FontSizeNormal,
                slot.IsReady ? UITheme.SuccessColor : UITheme.TextMuted);
            row.AddChild(readyLabel);

            _playerSlotsBox.AddChild(row);
        }

        // Show open slots
        int filledSlots = slots.Count;
        for (int i = filledSlots; i < 6; i++)
        {
            var row = new HBoxContainer();
            var openLabel = new Label();
            openLabel.Text = $"Player {i + 1}: (open)";
            UITheme.StyleLabel(openLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
            row.AddChild(openLabel);
            _playerSlotsBox.AddChild(row);
        }
    }

    private void OnReadyToggle()
    {
        _isReady = !_isReady;
        _readyBtn.Text = _isReady ? "UNREADY" : "READY";
        _lobbyManager?.RequestSetReady(_isReady);
    }

    private void OnStartPressed()
    {
        if (!_isHost) return;
        if (_lobbyManager == null) return;
        if (!_lobbyManager.AllPlayersReady()) return;

        _lobbyManager.StartMatch();
        GD.Print("[MultiplayerLobby] Starting match...");
        // TODO: Transition to game scene
    }

    private void OnLeaveLobby()
    {
        _discovery?.StopBroadcasting();
        _discovery?.StopListening();
        Multiplayer.MultiplayerPeer = null;
        _lobbyManager = null;
        _isHost = false;
        _isReady = false;

        _state = LobbyState.Browser;
        _browserPanel.Visible = true;
        _lobbyPanel.Visible = false;
    }

    private void OnBackPressed()
    {
        if (_state == LobbyState.InLobby)
        {
            OnLeaveLobby();
            return;
        }

        _discovery?.StopListening();
        GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
    }

    public override void _ExitTree()
    {
        _discovery?.StopBroadcasting();
        _discovery?.StopListening();
    }
}
