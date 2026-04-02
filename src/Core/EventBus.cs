using Godot;

namespace UnnamedRTS.Core;

/// <summary>
/// Global event bus autoload. Provides decoupled communication between
/// game systems via Godot signals. Any system can emit or subscribe
/// without direct references to other systems.
/// </summary>
public partial class EventBus : Node
{
    /// <summary>
    /// Singleton accessor. Set automatically by Godot autoload.
    /// </summary>
    public static EventBus? Instance { get; private set; }

    // ── Match Lifecycle ──────────────────────────────────────────────

    [Signal] public delegate void MatchStartedEventHandler(ulong seed);
    [Signal] public delegate void MatchPausedEventHandler();
    [Signal] public delegate void MatchResumedEventHandler();
    [Signal] public delegate void MatchEndedEventHandler();

    // ── Unit Events ──────────────────────────────────────────────────

    [Signal] public delegate void UnitSpawnedEventHandler(Node unit);
    [Signal] public delegate void UnitDestroyedEventHandler(Node unit);
    [Signal] public delegate void UnitSelectedEventHandler(Node unit);
    [Signal] public delegate void UnitDeselectedEventHandler(Node unit);
    [Signal] public delegate void SelectionClearedEventHandler();

    // ── Command Events ───────────────────────────────────────────────

    [Signal] public delegate void MoveCommandIssuedEventHandler(Vector3 target);
    [Signal] public delegate void AttackCommandIssuedEventHandler(Node target);
    [Signal] public delegate void BuildCommandIssuedEventHandler(string buildingId, Vector3 position);

    // ── Resource Events ──────────────────────────────────────────────

    [Signal] public delegate void ResourcesChangedEventHandler(int playerId, string resourceType, int newAmount);
    [Signal] public delegate void ResourceDepletedEventHandler(Node resourceNode);

    // ── Building Events ──────────────────────────────────────────────

    [Signal] public delegate void BuildingPlacedEventHandler(Node building);
    [Signal] public delegate void BuildingCompletedEventHandler(Node building);
    [Signal] public delegate void BuildingDestroyedEventHandler(Node building);

    // ── Fog of War Events ────────────────────────────────────────────

    [Signal] public delegate void FogUpdatedEventHandler(int playerId);
    [Signal] public delegate void AreaExploredEventHandler(int playerId, int cellX, int cellY);
    [Signal] public delegate void EntityRevealedEventHandler(int playerId, int entityId);
    [Signal] public delegate void EntityHiddenEventHandler(int playerId, int entityId);

    // ── Minimap Events ───────────────────────────────────────────────

    [Signal] public delegate void MinimapPingEventHandler(int playerIndex, int gridX, int gridY, int pingType);
    [Signal] public delegate void MinimapClickEventHandler(Vector3 worldPosition);
    [Signal] public delegate void BaseUnderAttackEventHandler(int playerId, Vector3 position);

    // ── Networking Events ────────────────────────────────────────────

    [Signal] public delegate void DesyncDetectedEventHandler(ulong tick);
    [Signal] public delegate void PlayerConnectedEventHandler(int playerId, string playerName);
    [Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
    [Signal] public delegate void LobbyUpdatedEventHandler();
    [Signal] public delegate void MatchCountdownEventHandler(int secondsRemaining);

    // ── UI Events ────────────────────────────────────────────────────

    [Signal] public delegate void TooltipRequestedEventHandler(string text, Vector2 position);
    [Signal] public delegate void TooltipDismissedEventHandler();

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[EventBus] Initialized.");
    }

    // ── Emit Helpers ─────────────────────────────────────────────────
    // These wrappers provide a clean API and centralize null checks.

    public void EmitMatchStarted(ulong seed) => EmitSignal(SignalName.MatchStarted, seed);
    public void EmitMatchPaused() => EmitSignal(SignalName.MatchPaused);
    public void EmitMatchResumed() => EmitSignal(SignalName.MatchResumed);
    public void EmitMatchEnded() => EmitSignal(SignalName.MatchEnded);

    public void EmitUnitSpawned(Node unit) => EmitSignal(SignalName.UnitSpawned, unit);
    public void EmitUnitDestroyed(Node unit) => EmitSignal(SignalName.UnitDestroyed, unit);
    public void EmitUnitSelected(Node unit) => EmitSignal(SignalName.UnitSelected, unit);
    public void EmitUnitDeselected(Node unit) => EmitSignal(SignalName.UnitDeselected, unit);
    public void EmitSelectionCleared() => EmitSignal(SignalName.SelectionCleared);

    public void EmitResourcesChanged(int playerId, string type, int amount) =>
        EmitSignal(SignalName.ResourcesChanged, playerId, type, amount);

    public void EmitBuildingPlaced(Node building) => EmitSignal(SignalName.BuildingPlaced, building);
    public void EmitBuildingCompleted(Node building) => EmitSignal(SignalName.BuildingCompleted, building);
    public void EmitBuildingDestroyed(Node building) => EmitSignal(SignalName.BuildingDestroyed, building);

    public void EmitFogUpdated(int playerId) => EmitSignal(SignalName.FogUpdated, playerId);
    public void EmitAreaExplored(int playerId, int cellX, int cellY) =>
        EmitSignal(SignalName.AreaExplored, playerId, cellX, cellY);
    public void EmitEntityRevealed(int playerId, int entityId) =>
        EmitSignal(SignalName.EntityRevealed, playerId, entityId);
    public void EmitEntityHidden(int playerId, int entityId) =>
        EmitSignal(SignalName.EntityHidden, playerId, entityId);

    public void EmitMinimapPing(int playerIndex, int gridX, int gridY, int pingType) =>
        EmitSignal(SignalName.MinimapPing, playerIndex, gridX, gridY, pingType);
    public void EmitMinimapClick(Vector3 worldPosition) =>
        EmitSignal(SignalName.MinimapClick, worldPosition);
    public void EmitBaseUnderAttack(int playerId, Vector3 position) =>
        EmitSignal(SignalName.BaseUnderAttack, playerId, position);

    // ── Networking Emit Helpers ──────────────────────────────────────

    public void EmitDesyncDetected(ulong tick) => EmitSignal(SignalName.DesyncDetected, tick);
    public void EmitPlayerConnected(int playerId, string playerName) =>
        EmitSignal(SignalName.PlayerConnected, playerId, playerName);
    public void EmitPlayerDisconnected(int playerId) =>
        EmitSignal(SignalName.PlayerDisconnected, playerId);
    public void EmitLobbyUpdated() => EmitSignal(SignalName.LobbyUpdated);
    public void EmitMatchCountdown(int secondsRemaining) =>
        EmitSignal(SignalName.MatchCountdown, secondsRemaining);
}
