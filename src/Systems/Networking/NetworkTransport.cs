using System;
using Godot;

namespace UnnamedRTS.Systems.Networking;

/// <summary>
/// Low-level UDP transport using Godot 4's ENetMultiplayerPeer.
/// Handles connection lifecycle, packet sending, and peer management.
/// Commands use reliable ordered channel 0; checksums use unreliable channel 1.
/// </summary>
public partial class NetworkTransport : Node
{
    /// <summary>Default game traffic port (ENet UDP).</summary>
    public const int DefaultPort = 7947;

    /// <summary>Maximum players supported.</summary>
    public const int MaxPlayers = 6;

    private ENetMultiplayerPeer? _peer;
    private bool _isHost;
    private int _localPeerId;

    /// <summary>Whether this transport is currently connected (host or client).</summary>
    public bool IsConnected => _peer != null && _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected;

    /// <summary>Whether this peer is the server/host.</summary>
    public bool IsHost => _isHost;

    /// <summary>The local Godot multiplayer peer ID (1 for host).</summary>
    public int LocalPeerId => _localPeerId;

    // ── Events raised to LockstepManager / LobbyManager ────────────

    /// <summary>Fired when a remote peer connects. Argument is the Godot peer ID.</summary>
    public event Action<long>? PeerConnected;

    /// <summary>Fired when a remote peer disconnects. Argument is the Godot peer ID.</summary>
    public event Action<long>? PeerDisconnected;

    /// <summary>Fired when we successfully connect to the host (client-side only).</summary>
    public event Action? ConnectedToHost;

    /// <summary>Fired when our connection attempt fails (client-side only).</summary>
    public event Action? ConnectionFailed;

    /// <summary>Fired when we receive a command packet. Args: senderPeerId, data.</summary>
    public event Action<int, byte[]>? CommandReceived;

    /// <summary>Fired when we receive a checksum packet. Args: senderPeerId, data.</summary>
    public event Action<int, byte[]>? ChecksumReceived;

    // ── Packet type prefixes ───────────────────────────────────────

    private const byte PacketTypeCommand = 0;
    private const byte PacketTypeChecksum = 1;

    public override void _Ready()
    {
        // Wire Godot multiplayer signals
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
    }

    public override void _ExitTree()
    {
        Disconnect();
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
    }

    /// <summary>
    /// Creates an ENet server and starts listening for incoming connections.
    /// </summary>
    public Error HostGame(int port = DefaultPort)
    {
        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateServer(port, MaxPlayers);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[NetworkTransport] Failed to create server on port {port}: {err}");
            _peer = null;
            return err;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isHost = true;
        _localPeerId = 1; // Host is always peer ID 1 in Godot
        GD.Print($"[NetworkTransport] Hosting on port {port}.");
        return Error.Ok;
    }

    /// <summary>
    /// Connects to a remote host as a client.
    /// </summary>
    public Error JoinGame(string address, int port = DefaultPort)
    {
        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[NetworkTransport] Failed to connect to {address}:{port}: {err}");
            _peer = null;
            return err;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isHost = false;
        GD.Print($"[NetworkTransport] Connecting to {address}:{port}...");
        return Error.Ok;
    }

    /// <summary>
    /// Cleanly disconnects and tears down the ENet peer.
    /// </summary>
    public void Disconnect()
    {
        if (_peer != null)
        {
            _peer.Close();
            Multiplayer.MultiplayerPeer = null;
            _peer = null;
            _isHost = false;
            _localPeerId = 0;
            GD.Print("[NetworkTransport] Disconnected.");
        }
    }

    /// <summary>
    /// Sends a command packet to a specific peer (reliable ordered).
    /// </summary>
    public void SendCommand(int targetPeerId, byte[] data)
    {
        byte[] packet = PrependType(PacketTypeCommand, data);
        RpcId(targetPeerId, MethodName.OnPacketReceived, packet);
    }

    /// <summary>
    /// Broadcasts a command packet to all peers (reliable ordered).
    /// </summary>
    public void BroadcastCommand(byte[] data)
    {
        byte[] packet = PrependType(PacketTypeCommand, data);
        Rpc(MethodName.OnPacketReceived, packet);
    }

    /// <summary>
    /// Sends a checksum packet to a specific peer.
    /// Uses the same reliable channel for simplicity (checksum packets are small and infrequent).
    /// </summary>
    public void SendChecksum(int targetPeerId, byte[] data)
    {
        byte[] packet = PrependType(PacketTypeChecksum, data);
        RpcId(targetPeerId, MethodName.OnPacketReceived, packet);
    }

    /// <summary>
    /// Broadcasts a checksum packet to all peers.
    /// </summary>
    public void BroadcastChecksum(byte[] data)
    {
        byte[] packet = PrependType(PacketTypeChecksum, data);
        Rpc(MethodName.OnPacketReceived, packet);
    }

    /// <summary>
    /// RPC entry point for all incoming packets. Dispatches by type prefix.
    /// MultiplayerApi.RpcMode.AnyPeer allows any connected peer to call this.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void OnPacketReceived(byte[] packet)
    {
        if (packet.Length < 2) return;

        int senderId = Multiplayer.GetRemoteSenderId();
        byte packetType = packet[0];

        // Strip the type prefix
        byte[] payload = new byte[packet.Length - 1];
        Array.Copy(packet, 1, payload, 0, payload.Length);

        switch (packetType)
        {
            case PacketTypeCommand:
                CommandReceived?.Invoke(senderId, payload);
                break;
            case PacketTypeChecksum:
                ChecksumReceived?.Invoke(senderId, payload);
                break;
            default:
                GD.PushWarning($"[NetworkTransport] Unknown packet type {packetType} from peer {senderId}.");
                break;
        }
    }

    // ── Multiplayer signal callbacks ────────────────────────────────

    private void OnPeerConnected(long peerId)
    {
        GD.Print($"[NetworkTransport] Peer connected: {peerId}");
        PeerConnected?.Invoke(peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        GD.Print($"[NetworkTransport] Peer disconnected: {peerId}");
        PeerDisconnected?.Invoke(peerId);
    }

    private void OnConnectedToServer()
    {
        _localPeerId = Multiplayer.GetUniqueId();
        GD.Print($"[NetworkTransport] Connected to host. Local peer ID: {_localPeerId}");
        ConnectedToHost?.Invoke();
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr("[NetworkTransport] Connection to host failed.");
        _peer = null;
        ConnectionFailed?.Invoke();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static byte[] PrependType(byte type, byte[] data)
    {
        byte[] result = new byte[data.Length + 1];
        result[0] = type;
        Array.Copy(data, 0, result, 1, data.Length);
        return result;
    }
}
