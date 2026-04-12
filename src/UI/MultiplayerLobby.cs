using Godot;
using CorditeWars.Core;
using CorditeWars.Game;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Networking;

namespace CorditeWars.UI;

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
    private HBoxContainer _addAiRow = null!;
    private OptionButton _mapSelector = null!;
    private Button _readyBtn = null!;
    private Button _startBtn = null!;
    private Button _leaveLobbyBtn = null!;
    private Label _lobbyStatusLabel = null!;

    private static readonly string[] MapIds = ["crossroads", "six_fronts", "coral_atoll", "archipelago", "dust_bowl", "iron_ridge"];
    private static readonly string[] MapDisplayNames = ["Crossroads", "Six Fronts", "Coral Atoll", "Archipelago", "Dust Bowl", "Iron Ridge"];

    private bool _isHost;
    private bool _isReady;
    private bool _isSearching;

    private AudioManager? _audioManager;

    public override void _Ready()
    {
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
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

        // Add AI buttons row (host only — visibility toggled in _Process)
        var addAiRow = new HBoxContainer();
        addAiRow.AddThemeConstantOverride("separation", 8);
        addAiRow.Name = "AddAiRow";
        _lobbyPanel.AddChild(addAiRow);

        var addAiEasyBtn = new Button();
        addAiEasyBtn.Text = "+ AI Easy";
        UITheme.StyleButton(addAiEasyBtn);
        addAiEasyBtn.Pressed += () => OnAddAiPressed(0);
        addAiRow.AddChild(addAiEasyBtn);

        var addAiMedBtn = new Button();
        addAiMedBtn.Text = "+ AI Medium";
        UITheme.StyleButton(addAiMedBtn);
        addAiMedBtn.Pressed += () => OnAddAiPressed(1);
        addAiRow.AddChild(addAiMedBtn);

        var addAiHardBtn = new Button();
        addAiHardBtn.Text = "+ AI Hard";
        UITheme.StyleButton(addAiHardBtn);
        addAiHardBtn.Pressed += () => OnAddAiPressed(2);
        addAiRow.AddChild(addAiHardBtn);

        _addAiRow = addAiRow;

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
        for (int i = 0; i < MapDisplayNames.Length; i++)
            _mapSelector.AddItem(MapDisplayNames[i], i);
        _mapSelector.Selected = 0;
        _mapSelector.ItemSelected += OnMapSelected;
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

        if (EventBus.Instance != null)
        {
            EventBus.Instance.Connect(EventBus.SignalName.MatchCountdown, Callable.From<int>(OnMatchCountdown));
            EventBus.Instance.Connect(EventBus.SignalName.JoinRejected, Callable.From<string>(OnJoinRejected));
        }
    }

    public override void _Process(double delta)
    {
        if (_state == LobbyState.InLobby && _lobbyManager != null)
        {
            RefreshLobbySlots();
            _startBtn.Visible = _isHost;
            _startBtn.Disabled = !(_lobbyManager.AllPlayersReady());
            _mapSelector.Disabled = !_isHost;
            // AI slot management is host-only and only when the lobby isn't full
            _addAiRow.Visible = _isHost && _lobbyManager.GetPlayerSlots().Count < LobbyManager.MaxSlots;
        }
    }

    // ── Browser Actions ───────────────────────────────────────────────

    private void OnHostPressed()
    {
        _audioManager?.PlayUiSoundById("ui_confirm");
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
        _audioManager?.PlayUiSoundById("ui_click");
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
        row.Name = $"Game_{AddressToNodeKey(hostAddress)}";

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
        joinBtn.Pressed += () =>
        {
            _audioManager?.PlayUiSoundById("ui_confirm");
            OnJoinGame(addr, port);
        };
        innerRow.AddChild(joinBtn);

        row.AddChild(panel);
        _gameListBox.AddChild(row);
    }

    private void OnGameLost(string hostAddress)
    {
        var node = _gameListBox.GetNodeOrNull($"Game_{AddressToNodeKey(hostAddress)}");
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

            // Label differs for human vs AI slots
            string slotLabel = slot.IsAI
                ? $"AI ({DifficultyLabel(slot.AIDifficulty)}): {slot.PlayerName}"
                : $"Player {slotKey + 1}: {slot.PlayerName}";
            var nameLabel = new Label();
            nameLabel.Text = slotLabel;
            nameLabel.CustomMinimumSize = new Vector2(220, 0);
            var nameColor = !string.IsNullOrEmpty(slot.FactionId) ? UITheme.GetFactionColorById(slot.FactionId) : UITheme.TextPrimary;
            UITheme.StyleLabel(nameLabel, UITheme.FontSizeNormal, nameColor);
            row.AddChild(nameLabel);

            // Faction display
            var factionLabel = new Label();
            factionLabel.Text = string.IsNullOrEmpty(slot.FactionId) ? "[No Faction]" : $"[{slot.FactionId}]";
            UITheme.StyleLabel(factionLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
            row.AddChild(factionLabel);

            // Ready status (AI is always ready)
            var readyLabel = new Label();
            readyLabel.Text = slot.IsReady ? "READY \u2713" : "NOT READY \u2717";
            UITheme.StyleLabel(readyLabel, UITheme.FontSizeNormal,
                slot.IsReady ? UITheme.SuccessColor : UITheme.TextMuted);
            row.AddChild(readyLabel);

            // Host can remove AI slots
            if (_isHost && slot.IsAI)
            {
                var removeBtn = new Button();
                removeBtn.Text = "\u2715";
                UITheme.StyleButton(removeBtn);
                int capturedId = slot.PlayerId;
                removeBtn.Pressed += () =>
                {
                    _audioManager?.PlayUiSoundById("ui_cancel");
                    _lobbyManager?.RemoveAiSlot(capturedId);
                };
                row.AddChild(removeBtn);
            }

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

    private static string DifficultyLabel(int d) => d switch
    {
        0 => "Easy",
        1 => "Medium",
        _ => "Hard"
    };

    private void OnAddAiPressed(int difficulty)
    {
        if (_lobbyManager == null || !_isHost) return;
        _audioManager?.PlayUiSoundById("ui_click");
        // Pick a faction not already taken
        string faction = PickAvailableFaction();
        _lobbyManager.AddAiSlot(difficulty, faction);
    }

    private string PickAvailableFaction()
    {
        if (_lobbyManager == null) return "valkyr";
        var taken = new System.Collections.Generic.HashSet<string>();
        var slots = _lobbyManager.GetPlayerSlots();
        for (int i = 0; i < slots.Count; i++)
        {
            string f = slots.Values[i].FactionId;
            if (!string.IsNullOrEmpty(f)) taken.Add(f);
        }
        for (int i = 0; i < UITheme.FactionIds.Length; i++)
        {
            if (!taken.Contains(UITheme.FactionIds[i]))
                return UITheme.FactionIds[i];
        }
        return UITheme.FactionIds[0]; // fallback — all taken
    }

    private void OnReadyToggle()
    {
        _audioManager?.PlayUiSoundById("ui_click");
        _isReady = !_isReady;
        _readyBtn.Text = _isReady ? "UNREADY" : "READY";
        _lobbyManager?.RequestSetReady(_isReady);
    }

    private void OnStartPressed()
    {
        if (!_isHost) return;
        if (_lobbyManager == null) return;
        if (!_lobbyManager.AllPlayersReady()) return;

        // Sync map selection to lobby before starting
        int mapIdx = _mapSelector.Selected;
        if (mapIdx >= 0 && mapIdx < MapIds.Length)
            _lobbyManager.SetMap(MapIds[mapIdx]);

        _lobbyManager.StartMatch();
        GD.Print("[MultiplayerLobby] Starting match...");
        // Scene transition happens via OnMatchCountdown triggered by LobbyManager.StartMatch()
    }

    private void OnLeaveLobby()
    {
        _audioManager?.PlayUiSoundById("ui_cancel");
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

        if (EventBus.Instance != null)
        {
            EventBus.Instance.Disconnect(EventBus.SignalName.MatchCountdown, Callable.From<int>(OnMatchCountdown));
            EventBus.Instance.Disconnect(EventBus.SignalName.JoinRejected, Callable.From<string>(OnJoinRejected));
        }

        SceneTransition.TransitionTo(GetTree(), "res://scenes/UI/MainMenu.tscn");
    }

    private void OnJoinRejected(string reason)
    {
        // Drop back to the browser and show why the join was refused
        _lobbyManager = null;
        Multiplayer.MultiplayerPeer = null;

        _state = LobbyState.Browser;
        _browserPanel.Visible = true;
        _lobbyPanel.Visible = false;

        _noGamesLabel.Text = $"Could not join: {reason}";
        _noGamesLabel.Visible = true;
        UITheme.StyleLabel(_noGamesLabel, UITheme.FontSizeNormal, new Color(1f, 0.35f, 0.35f));

        GD.PushWarning($"[MultiplayerLobby] Join rejected — {reason}");
    }

    private void OnMatchCountdown(int ticks)
    {
        if (_lobbyManager == null || ticks != 3) return; // Only transition on initial countdown

        // Generate MatchConfig from lobby slots
        var slots = _lobbyManager.GetPlayerSlots();
        var players = new System.Collections.Generic.List<PlayerConfig>();

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots.Values[i];
            players.Add(new PlayerConfig
            {
                PlayerId = slot.PlayerId,
                FactionId = string.IsNullOrEmpty(slot.FactionId) ? "valkyr" : slot.FactionId,
                IsAI = slot.IsAI,
                AIDifficulty = slot.AIDifficulty,
                PlayerName = slot.PlayerName
            });
        }

        var config = new MatchConfig
        {
            MapId = string.IsNullOrEmpty(_lobbyManager.SelectedMap) ? "crossroads" : _lobbyManager.SelectedMap,
            MatchSeed = _lobbyManager.MatchSeed,
            GameSpeed = 1,
            FogOfWar = true,
            StartingCordite = 5000,
            PlayerConfigs = players.ToArray(),
            // Each machine sets its own local player so GameSession controls the right faction.
            LocalPlayerId = _lobbyManager.LocalPlayerId
        };

        CorditeWars.Game.Main.PendingConfig = config;

        // Reparent NetworkTransport to scene root so it survives the scene change.
        // GameSession.SetupMultiplayer() looks for it at /root/NetworkTransport.
        ReparentNetworkTransport();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.Disconnect(EventBus.SignalName.MatchCountdown, Callable.From<int>(OnMatchCountdown));
        }

        SceneTransition.TransitionTo(GetTree(), "res://scenes/Game/Main.tscn");
    }

    /// <summary>
    /// Moves the NetworkTransport node to /root/ so it persists across the scene
    /// change. GameSession.SetupMultiplayer() looks for it at /root/NetworkTransport.
    /// </summary>
    private void ReparentNetworkTransport()
    {
        // Find the transport among our children
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            if (GetChild(i) is NetworkTransport transport)
            {
                RemoveChild(transport);
                transport.Name = "NetworkTransport";
                GetTree().Root.AddChild(transport);
                GD.Print("[MultiplayerLobby] Reparented NetworkTransport to /root/ for scene persistence.");
                return;
            }
        }
    }

    private void OnMapSelected(long index)
    {
        if (!_isHost || _lobbyManager == null) return;
        int idx = (int)index;
        if (idx >= 0 && idx < MapIds.Length)
        {
            _lobbyManager.SetMap(MapIds[idx]);
        }
    }

    /// <summary>Matches any character that is not a safe Godot node-name character.</summary>
    private static readonly System.Text.RegularExpressions.Regex UnsafeNodeNameChars =
        new(@"[^a-zA-Z0-9_]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Converts a host address (which may contain colons in IPv6 notation) into a
    /// safe node-name key. Colons and other characters that are illegal in Godot
    /// NodePaths are replaced with underscores.
    /// </summary>
    private static string AddressToNodeKey(string address)
        => UnsafeNodeNameChars.Replace(address, "_");

    public override void _ExitTree()
    {
        _discovery?.StopBroadcasting();
        _discovery?.StopListening();

        if (EventBus.Instance != null)
        {
            if (EventBus.Instance.IsConnected(EventBus.SignalName.MatchCountdown, Callable.From<int>(OnMatchCountdown)))
                EventBus.Instance.Disconnect(EventBus.SignalName.MatchCountdown, Callable.From<int>(OnMatchCountdown));

            if (EventBus.Instance.IsConnected(EventBus.SignalName.JoinRejected, Callable.From<string>(OnJoinRejected)))
                EventBus.Instance.Disconnect(EventBus.SignalName.JoinRejected, Callable.From<string>(OnJoinRejected));
        }
    }
}
