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

    // ── Combat Events ───────────────────────────────────────────────

    /// <summary>Fired when a weapon discharges. weaponType is the WeaponType enum cast to int.</summary>
    [Signal] public delegate void AttackFiredEventHandler(int attackerId, int weaponType, Vector3 position);

    /// <summary>Fired when a shot impacts (hit or miss). isHit indicates accuracy result.</summary>
    [Signal] public delegate void AttackImpactEventHandler(int targetId, bool isHit, bool hasAoe, Vector3 position);

    /// <summary>Fired when a unit is destroyed in combat.</summary>
    [Signal] public delegate void UnitDeathEventHandler(int unitId, int unitCategory, Vector3 position);

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

    // ── Economy Events ─────────────────────────────────────────────────

    [Signal] public delegate void HarvesterDeliveredEventHandler(int playerId, int corditeAmount);
    [Signal] public delegate void EconomyBuildingCompletedEventHandler(int playerId, string buildingId);
    [Signal] public delegate void InsufficientFundsEventHandler(int playerId);
    [Signal] public delegate void NodeDepletedEventHandler(int nodeId);

    // ── Tech Tree Events ─────────────────────────────────────────────

    [Signal] public delegate void UpgradeStartedEventHandler(int playerId, string upgradeId);
    [Signal] public delegate void UpgradeCompletedEventHandler(int playerId, string upgradeId);
    [Signal] public delegate void TechRequirementNotMetEventHandler(int playerId, string reason);

    // ── Map Events ─────────────────────────────────────────────────

    [Signal] public delegate void MapLoadedEventHandler(string mapId);

    // ── Persistence Events ──────────────────────────────────────────

    [Signal] public delegate void GameSavedEventHandler(string slotName);
    [Signal] public delegate void GameLoadedEventHandler(string slotName);

    // ── Selection & Command Events ──────────────────────────────────

    [Signal] public delegate void SelectionChangedEventHandler(int[] unitIds);
    [Signal] public delegate void UnitOrderedEventHandler(int[] unitIds, string orderType);

    // ── Production Events ────────────────────────────────────────────

    [Signal] public delegate void ProductionStartedEventHandler(Node building, string unitTypeId);
    [Signal] public delegate void ProductionCompletedEventHandler(Node building, string unitTypeId);

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

    public void EmitMoveCommandIssued(Vector3 target) =>
        EmitSignal(SignalName.MoveCommandIssued, target);
    public void EmitAttackCommandIssued(Node target) =>
        EmitSignal(SignalName.AttackCommandIssued, target);

    // ── Combat Emit Helpers ─────────────────────────────────────────

    public void EmitAttackFired(int attackerId, int weaponType, Vector3 position) =>
        EmitSignal(SignalName.AttackFired, attackerId, weaponType, position);
    public void EmitAttackImpact(int targetId, bool isHit, bool hasAoe, Vector3 position) =>
        EmitSignal(SignalName.AttackImpact, targetId, isHit, hasAoe, position);
    public void EmitUnitDeath(int unitId, int unitCategory, Vector3 position) =>
        EmitSignal(SignalName.UnitDeath, unitId, unitCategory, position);
    public void EmitBuildCommandIssued(string buildingId, Vector3 position) =>
        EmitSignal(SignalName.BuildCommandIssued, buildingId, position);

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

    // ── Economy Emit Helpers ───────────────────────────────────────────

    public void EmitHarvesterDelivered(int playerId, int corditeAmount) =>
        EmitSignal(SignalName.HarvesterDelivered, playerId, corditeAmount);
    public void EmitEconomyBuildingCompleted(int playerId, string buildingId) =>
        EmitSignal(SignalName.EconomyBuildingCompleted, playerId, buildingId);
    public void EmitInsufficientFunds(int playerId) =>
        EmitSignal(SignalName.InsufficientFunds, playerId);
    public void EmitNodeDepleted(int nodeId) =>
        EmitSignal(SignalName.NodeDepleted, nodeId);

    // ── Tech Tree Emit Helpers ────────────────────────────────────────

    public void EmitUpgradeStarted(int playerId, string upgradeId) =>
        EmitSignal(SignalName.UpgradeStarted, playerId, upgradeId);
    public void EmitUpgradeCompleted(int playerId, string upgradeId) =>
        EmitSignal(SignalName.UpgradeCompleted, playerId, upgradeId);
    public void EmitTechRequirementNotMet(int playerId, string reason) =>
        EmitSignal(SignalName.TechRequirementNotMet, playerId, reason);

    // ── Map Emit Helpers ───────────────────────────────────────────

    public void EmitMapLoaded(string mapId) => EmitSignal(SignalName.MapLoaded, mapId);

    // ── Selection & Command Emit Helpers ────────────────────────────

    public void EmitSelectionChanged(int[] unitIds) =>
        EmitSignal(SignalName.SelectionChanged, unitIds);
    public void EmitUnitOrdered(int[] unitIds, string orderType) =>
        EmitSignal(SignalName.UnitOrdered, unitIds, orderType);

    // ── Production Emit Helpers ──────────────────────────────────────

    public void EmitProductionStarted(Node building, string unitTypeId) =>
        EmitSignal(SignalName.ProductionStarted, building, unitTypeId);
    public void EmitProductionCompleted(Node building, string unitTypeId) =>
        EmitSignal(SignalName.ProductionCompleted, building, unitTypeId);

    // ── Persistence Emit Helpers ────────────────────────────────────

    public void EmitGameSaved(string slotName) => EmitSignal(SignalName.GameSaved, slotName);
    public void EmitGameLoaded(string slotName) => EmitSignal(SignalName.GameLoaded, slotName);

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
