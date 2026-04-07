using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Godot;

namespace CorditeWars.Systems.Networking;

/// <summary>
/// LAN game discovery via UDP broadcast on port 7946.
/// Host broadcasts presence every 2 seconds; clients listen and emit GameFound/GameLost.
/// Uses raw System.Net.Sockets.UdpClient — NOT ENet — because ENet doesn't support broadcast.
/// </summary>
public partial class LanDiscovery : Node
{
    /// <summary>Default broadcast port for LAN discovery.</summary>
    public const int DefaultDiscoveryPort = 7946;

    /// <summary>Protocol magic string to filter stray UDP packets.</summary>
    private const string ProtocolMagic = "CORDITE";

    /// <summary>Protocol version for forward compatibility.</summary>
    private const string ProtocolVersion = "1.0";

    /// <summary>How often the host broadcasts, in seconds.</summary>
    private const int BroadcastIntervalMs = 2000;

    /// <summary>How long until a game is considered lost, in milliseconds.</summary>
    private const int TimeoutMs = 6000;

    // ── State ───────────────────────────────────────────────────────

    private UdpClient? _broadcastSender;
    private UdpClient? _listener;
    private bool _isBroadcasting;
    private bool _isListening;
    private string _broadcastPayload = "";
    private int _discoveryPort = DefaultDiscoveryPort;

    // Track elapsed time for broadcast interval
    private int _broadcastTimerMs;

    // Discovered games keyed by host address for deterministic ordering
    private readonly SortedList<string, LanGameInfo> _discoveredGames = new();

    // Timestamps for timeout detection (milliseconds since listening started)
    private int _elapsedMs;

    // ── Signals ─────────────────────────────────────────────────────

    [Signal] public delegate void GameFoundEventHandler(
        string hostAddress, string hostName, string gameName,
        int currentPlayers, int maxPlayers, string mapName, int gamePort);

    [Signal] public delegate void GameLostEventHandler(string hostAddress);

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Starts broadcasting this game's presence on the LAN.
    /// Called by the host after creating a game.
    /// </summary>
    public void StartBroadcasting(string gameName, int maxPlayers, string mapName, int gamePort, int discoveryPort = DefaultDiscoveryPort)
    {
        if (_isBroadcasting) return;

        _discoveryPort = discoveryPort;
        string hostName = OS.GetEnvironment("COMPUTERNAME");
        if (string.IsNullOrEmpty(hostName))
            hostName = OS.GetEnvironment("HOSTNAME");
        if (string.IsNullOrEmpty(hostName))
            hostName = "Host";

        // Broadcast packet format: "CORDITE|1.0|<hostName>|<gameName>|<current>/<max>|<mapName>|<gamePort>"
        // CurrentPlayers will be updated each broadcast cycle
        _broadcastPayload = $"{ProtocolMagic}|{ProtocolVersion}|{hostName}|{gameName}|1/{maxPlayers}|{mapName}|{gamePort}";

        try
        {
            _broadcastSender = new UdpClient();
            _broadcastSender.EnableBroadcast = true;
            _isBroadcasting = true;
            _broadcastTimerMs = BroadcastIntervalMs; // Send immediately on first tick
            GD.Print($"[LanDiscovery] Broadcasting on port {_discoveryPort}: {gameName}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LanDiscovery] Failed to start broadcasting: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the current player count in the broadcast payload.
    /// </summary>
    public void UpdateBroadcastPlayerCount(int currentPlayers, int maxPlayers, string gameName, string mapName, int gamePort)
    {
        if (!_isBroadcasting) return;

        string hostName = OS.GetEnvironment("COMPUTERNAME");
        if (string.IsNullOrEmpty(hostName))
            hostName = OS.GetEnvironment("HOSTNAME");
        if (string.IsNullOrEmpty(hostName))
            hostName = "Host";

        _broadcastPayload = $"{ProtocolMagic}|{ProtocolVersion}|{hostName}|{gameName}|{currentPlayers}/{maxPlayers}|{mapName}|{gamePort}";
    }

    /// <summary>
    /// Stops broadcasting.
    /// </summary>
    public void StopBroadcasting()
    {
        if (!_isBroadcasting) return;

        _isBroadcasting = false;
        try { _broadcastSender?.Close(); } catch { /* ignore */ }
        _broadcastSender = null;
        GD.Print("[LanDiscovery] Stopped broadcasting.");
    }

    /// <summary>
    /// Starts listening for LAN game broadcasts.
    /// Called by clients looking for games.
    /// </summary>
    public void StartListening(int discoveryPort = DefaultDiscoveryPort)
    {
        if (_isListening) return;

        _discoveryPort = discoveryPort;
        _discoveredGames.Clear();
        _elapsedMs = 0;

        try
        {
            _listener = new UdpClient(_discoveryPort);
            _listener.EnableBroadcast = true;
            _listener.Client.Blocking = false;
            _isListening = true;
            GD.Print($"[LanDiscovery] Listening on port {_discoveryPort}...");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LanDiscovery] Failed to start listening: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops listening for broadcasts.
    /// </summary>
    public void StopListening()
    {
        if (!_isListening) return;

        _isListening = false;
        try { _listener?.Close(); } catch { /* ignore */ }
        _listener = null;
        _discoveredGames.Clear();
        GD.Print("[LanDiscovery] Stopped listening.");
    }

    /// <summary>
    /// Returns all currently-known LAN games, keyed by host address.
    /// </summary>
    public SortedList<string, LanGameInfo> GetDiscoveredGames()
    {
        return _discoveredGames;
    }

    // ── Godot lifecycle ────────────────────────────────────────────

    public override void _Process(double delta)
    {
        int deltaMs = (int)(delta * 1000);

        if (_isBroadcasting)
        {
            _broadcastTimerMs += deltaMs;
            if (_broadcastTimerMs >= BroadcastIntervalMs)
            {
                _broadcastTimerMs = 0;
                SendBroadcast();
            }
        }

        if (_isListening)
        {
            _elapsedMs += deltaMs;
            ReceiveBroadcasts();
            CheckTimeouts();
        }
    }

    public override void _ExitTree()
    {
        StopBroadcasting();
        StopListening();
    }

    // ── Internal ───────────────────────────────────────────────────

    private void SendBroadcast()
    {
        if (_broadcastSender == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(_broadcastPayload);
            var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
            _broadcastSender.Send(data, data.Length, endpoint);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LanDiscovery] Broadcast send error: {ex.Message}");
        }
    }

    private void ReceiveBroadcasts()
    {
        if (_listener == null) return;

        // Non-blocking: read all available packets
        while (true)
        {
            try
            {
                if (_listener.Available <= 0) break;

                IPEndPoint? remoteEp = null;
                byte[] data = _listener.Receive(ref remoteEp);
                if (remoteEp == null) continue;

                string message = Encoding.UTF8.GetString(data);
                ParseBroadcast(remoteEp.Address.ToString(), message);
            }
            catch (SocketException)
            {
                break; // No more data / would block
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LanDiscovery] Receive error: {ex.Message}");
                break;
            }
        }
    }

    private void ParseBroadcast(string hostAddress, string message)
    {
        // Expected: "CORDITE|1.0|<hostName>|<gameName>|<current>/<max>|<mapName>|<gamePort>"
        // Split manually — no LINQ
        string[] parts = message.Split('|');
        if (parts.Length != 7) return;
        if (parts[0] != ProtocolMagic) return;
        if (parts[1] != ProtocolVersion) return;

        string hostName = parts[2];
        string gameName = parts[3];

        // Parse "current/max"
        string[] playerParts = parts[4].Split('/');
        if (playerParts.Length != 2) return;
        if (!int.TryParse(playerParts[0], out int currentPlayers)) return;
        if (!int.TryParse(playerParts[1], out int maxPlayers)) return;

        string mapName = parts[5];
        if (!int.TryParse(parts[6], out int gamePort)) return;

        var info = new LanGameInfo
        {
            HostAddress = hostAddress,
            HostName = hostName,
            GameName = gameName,
            CurrentPlayers = currentPlayers,
            MaxPlayers = maxPlayers,
            MapName = mapName,
            GamePort = gamePort,
            LastSeenMs = _elapsedMs
        };

        bool isNew = !_discoveredGames.ContainsKey(hostAddress);
        _discoveredGames[hostAddress] = info;

        if (isNew)
        {
            EmitSignal(SignalName.GameFound, hostAddress, hostName, gameName,
                currentPlayers, maxPlayers, mapName, gamePort);
        }
    }

    private void CheckTimeouts()
    {
        // Collect timed-out entries
        var toRemove = new List<string>();
        for (int i = 0; i < _discoveredGames.Count; i++)
        {
            var info = _discoveredGames.Values[i];
            if (_elapsedMs - info.LastSeenMs > TimeoutMs)
            {
                toRemove.Add(_discoveredGames.Keys[i]);
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            _discoveredGames.Remove(toRemove[i]);
            EmitSignal(SignalName.GameLost, toRemove[i]);
        }
    }
}

/// <summary>
/// Information about a discovered LAN game.
/// </summary>
public struct LanGameInfo
{
    public string HostAddress;
    public string HostName;
    public string GameName;
    public int CurrentPlayers;
    public int MaxPlayers;
    public string MapName;
    public int GamePort;
    /// <summary>Elapsed milliseconds when this game was last seen (for timeout).</summary>
    public int LastSeenMs;
}
