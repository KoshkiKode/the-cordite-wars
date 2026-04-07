using System.Collections.Generic;
using Godot;
using CorditeWars.Core;

namespace CorditeWars.Game.Economy;

/// <summary>
/// States for the harvester AI loop.
/// </summary>
public enum HarvesterState
{
    Idle,
    MovingToNode,
    Gathering,
    MovingToRefinery,
    Delivering
}

/// <summary>
/// Runtime state for a single harvester unit.
/// </summary>
public sealed class HarvesterInstance
{
    public int UnitId { get; init; }
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;
    public HarvesterState State { get; set; }
    public FixedVector2 Position { get; set; }
    public int CorditeCarrying { get; set; }
    public int MaxCapacity { get; init; }
    public FixedPoint GatherRate { get; init; }
    public int AssignedNodeId { get; set; } = -1;
    public int AssignedRefineryId { get; set; } = -1;
    public FixedPoint GatherAccumulator { get; set; }
}

/// <summary>
/// A Cordite resource node on the map.
/// </summary>
public sealed class CorditeNode
{
    public int NodeId { get; init; }
    public FixedVector2 Position { get; init; }
    public int RemainingCordite { get; set; }
    public int MaxCordite { get; init; }
    public bool IsDepleted => RemainingCordite <= 0;
}

/// <summary>
/// Refinery location registered by the building system.
/// </summary>
public sealed class RefineryLocation
{
    public int RefineryId { get; init; }
    public int PlayerId { get; init; }
    public FixedVector2 Position { get; init; }
}

/// <summary>
/// Manages the harvester AI loop: idle -> move to node -> gather -> move to refinery -> deliver -> repeat.
/// All movement and gathering uses FixedPoint for determinism.
/// </summary>
public sealed partial class HarvesterSystem : Node
{
    private SortedList<int, HarvesterInstance> _harvesters = new();
    private SortedList<int, CorditeNode> _corditeNodes = new();
    private SortedList<int, RefineryLocation> _refineries = new();
    private SortedList<string, FactionEconomyConfig> _configs = new();
    private EconomyManager? _economyManager;

    /// <summary>
    /// Gather rate: 100 Cordite/sec for all factions.
    /// At 30 tps this is ~3.333 per tick (handled via accumulator).
    /// </summary>
    private static readonly FixedPoint GatherRatePerSecond = FixedPoint.FromInt(100);

    /// <summary>
    /// Distance threshold to consider "arrived" (1 cell, squared for comparison).
    /// 1.0 squared = 1.0 in FixedPoint.
    /// </summary>
    private static readonly FixedPoint ArrivalDistanceSq = FixedPoint.One;

    // ── Initialization ──────────────────────────────────────────────

    public void Initialize(
        SortedList<string, FactionEconomyConfig> configs,
        EconomyManager economyManager)
    {
        _configs = configs;
        _economyManager = economyManager;
    }

    // ── Registration ────────────────────────────────────────────────

    public void RegisterHarvester(int unitId, int playerId, string factionId, FixedVector2 position)
    {
        if (_harvesters.ContainsKey(unitId))
        {
            GD.PushWarning($"[HarvesterSystem] Harvester {unitId} already registered.");
            return;
        }

        if (!_configs.TryGetValue(factionId, out var config))
        {
            GD.PushError($"[HarvesterSystem] Unknown faction '{factionId}' for harvester {unitId}.");
            return;
        }

        var harvester = new HarvesterInstance
        {
            UnitId = unitId,
            PlayerId = playerId,
            FactionId = factionId,
            State = HarvesterState.Idle,
            Position = position,
            CorditeCarrying = 0,
            MaxCapacity = config.HarvesterCapacity,
            GatherRate = GatherRatePerSecond,
            AssignedNodeId = -1,
            AssignedRefineryId = -1,
            GatherAccumulator = FixedPoint.Zero
        };

        _harvesters.Add(unitId, harvester);
    }

    public void UnregisterHarvester(int unitId)
    {
        _harvesters.Remove(unitId);
    }

    public void RegisterCorditeNode(int nodeId, FixedVector2 position, int corditeAmount)
    {
        if (_corditeNodes.ContainsKey(nodeId))
            return;

        _corditeNodes.Add(nodeId, new CorditeNode
        {
            NodeId = nodeId,
            Position = position,
            RemainingCordite = corditeAmount,
            MaxCordite = corditeAmount
        });
    }

    public void UnregisterCorditeNode(int nodeId)
    {
        _corditeNodes.Remove(nodeId);
    }

    public void RegisterRefinery(int refineryId, int playerId, FixedVector2 position)
    {
        if (_refineries.ContainsKey(refineryId))
            return;

        _refineries.Add(refineryId, new RefineryLocation
        {
            RefineryId = refineryId,
            PlayerId = playerId,
            Position = position
        });
    }

    public void UnregisterRefinery(int refineryId)
    {
        _refineries.Remove(refineryId);
    }

    // ── Tick Processing ─────────────────────────────────────────────

    /// <summary>
    /// Processes all harvesters for one simulation tick.
    /// </summary>
    public void ProcessTick(FixedPoint deltaTime)
    {
        for (int i = 0; i < _harvesters.Count; i++)
        {
            HarvesterInstance harvester = _harvesters.Values[i];

            if (!_configs.TryGetValue(harvester.FactionId, out var config))
                continue;

            switch (harvester.State)
            {
                case HarvesterState.Idle:
                    ProcessIdle(harvester);
                    break;
                case HarvesterState.MovingToNode:
                    ProcessMovingToNode(harvester, config, deltaTime);
                    break;
                case HarvesterState.Gathering:
                    ProcessGathering(harvester, deltaTime);
                    break;
                case HarvesterState.MovingToRefinery:
                    ProcessMovingToRefinery(harvester, config, deltaTime);
                    break;
                case HarvesterState.Delivering:
                    ProcessDelivering(harvester);
                    break;
            }
        }
    }

    // ── State Handlers ──────────────────────────────────────────────

    private void ProcessIdle(HarvesterInstance harvester)
    {
        CorditeNode? node = FindNearestNode(harvester.Position);
        if (node == null)
            return; // No nodes available; stay idle

        harvester.AssignedNodeId = node.NodeId;
        harvester.State = HarvesterState.MovingToNode;

        // Also find a refinery for the return trip
        int refineryId = FindNearestRefineryId(harvester.PlayerId, harvester.Position);
        harvester.AssignedRefineryId = refineryId;
    }

    private void ProcessMovingToNode(HarvesterInstance harvester, FactionEconomyConfig config, FixedPoint deltaTime)
    {
        if (!_corditeNodes.TryGetValue(harvester.AssignedNodeId, out var node))
        {
            // Node was removed; go idle to find a new one
            harvester.AssignedNodeId = -1;
            harvester.State = HarvesterState.Idle;
            return;
        }

        if (node.IsDepleted)
        {
            harvester.AssignedNodeId = -1;
            harvester.State = HarvesterState.Idle;
            return;
        }

        // Move toward node
        harvester.Position = MoveToward(harvester.Position, node.Position, config.HarvesterSpeed, deltaTime);

        // Check arrival
        FixedPoint distSq = harvester.Position.DistanceSquaredTo(node.Position);
        if (distSq <= ArrivalDistanceSq)
        {
            harvester.State = HarvesterState.Gathering;
            harvester.GatherAccumulator = FixedPoint.Zero;
        }
    }

    private void ProcessGathering(HarvesterInstance harvester, FixedPoint deltaTime)
    {
        if (!_corditeNodes.TryGetValue(harvester.AssignedNodeId, out var node))
        {
            // Node removed while gathering
            if (harvester.CorditeCarrying > 0)
                harvester.State = HarvesterState.MovingToRefinery;
            else
                harvester.State = HarvesterState.Idle;
            return;
        }

        if (node.IsDepleted)
        {
            EventBus.Instance?.EmitNodeDepleted(node.NodeId);
            if (harvester.CorditeCarrying > 0)
                harvester.State = HarvesterState.MovingToRefinery;
            else
            {
                harvester.AssignedNodeId = -1;
                harvester.State = HarvesterState.Idle;
            }
            return;
        }

        // Accumulate cordite: GatherRate * deltaTime
        harvester.GatherAccumulator = harvester.GatherAccumulator + (harvester.GatherRate * deltaTime);

        // Extract whole units from the accumulator
        int gathered = harvester.GatherAccumulator.ToInt();
        if (gathered > 0)
        {
            // Clamp to what the node has left
            if (gathered > node.RemainingCordite)
                gathered = node.RemainingCordite;

            // Clamp to remaining capacity
            int spaceLeft = harvester.MaxCapacity - harvester.CorditeCarrying;
            if (gathered > spaceLeft)
                gathered = spaceLeft;

            harvester.CorditeCarrying = harvester.CorditeCarrying + gathered;
            node.RemainingCordite = node.RemainingCordite - gathered;

            // Subtract the integer portion from the accumulator
            harvester.GatherAccumulator = harvester.GatherAccumulator - FixedPoint.FromInt(gathered);
        }

        // Check if full or node depleted
        if (harvester.CorditeCarrying >= harvester.MaxCapacity)
        {
            harvester.State = HarvesterState.MovingToRefinery;
        }
        else if (node.IsDepleted)
        {
            EventBus.Instance?.EmitNodeDepleted(node.NodeId);
            if (harvester.CorditeCarrying > 0)
                harvester.State = HarvesterState.MovingToRefinery;
            else
            {
                harvester.AssignedNodeId = -1;
                harvester.State = HarvesterState.Idle;
            }
        }
    }

    private void ProcessMovingToRefinery(HarvesterInstance harvester, FactionEconomyConfig config, FixedPoint deltaTime)
    {
        // Re-find refinery if lost
        if (harvester.AssignedRefineryId < 0 || !_refineries.ContainsKey(harvester.AssignedRefineryId))
        {
            int refineryId = FindNearestRefineryId(harvester.PlayerId, harvester.Position);
            if (refineryId < 0)
                return; // No refinery; stay in this state until one appears
            harvester.AssignedRefineryId = refineryId;
        }

        RefineryLocation refinery = _refineries.Values[_refineries.IndexOfKey(harvester.AssignedRefineryId)];

        // Move toward refinery
        harvester.Position = MoveToward(harvester.Position, refinery.Position, config.HarvesterSpeed, deltaTime);

        // Check arrival
        FixedPoint distSq = harvester.Position.DistanceSquaredTo(refinery.Position);
        if (distSq <= ArrivalDistanceSq)
        {
            harvester.State = HarvesterState.Delivering;
        }
    }

    private void ProcessDelivering(HarvesterInstance harvester)
    {
        // Instant delivery
        if (harvester.CorditeCarrying > 0 && _economyManager != null)
        {
            _economyManager.DeliverHarvest(harvester.PlayerId, harvester.CorditeCarrying);
        }

        harvester.CorditeCarrying = 0;
        harvester.GatherAccumulator = FixedPoint.Zero;

        // Check if the assigned node is still valid
        if (harvester.AssignedNodeId >= 0 &&
            _corditeNodes.TryGetValue(harvester.AssignedNodeId, out var node) &&
            !node.IsDepleted)
        {
            harvester.State = HarvesterState.MovingToNode;
        }
        else
        {
            // Find a new node
            harvester.AssignedNodeId = -1;
            harvester.State = HarvesterState.Idle;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the nearest non-depleted CorditeNode to the given position.
    /// </summary>
    private CorditeNode? FindNearestNode(FixedVector2 position)
    {
        CorditeNode? best = null;
        FixedPoint bestDistSq = FixedPoint.MaxValue;

        for (int i = 0; i < _corditeNodes.Count; i++)
        {
            CorditeNode node = _corditeNodes.Values[i];
            if (node.IsDepleted)
                continue;

            FixedPoint distSq = position.DistanceSquaredTo(node.Position);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = node;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the nearest refinery belonging to the given player.
    /// Returns -1 if no refinery found.
    /// </summary>
    private int FindNearestRefineryId(int playerId, FixedVector2 position)
    {
        int bestId = -1;
        FixedPoint bestDistSq = FixedPoint.MaxValue;

        for (int i = 0; i < _refineries.Count; i++)
        {
            RefineryLocation refinery = _refineries.Values[i];
            if (refinery.PlayerId != playerId)
                continue;

            FixedPoint distSq = position.DistanceSquaredTo(refinery.Position);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = refinery.RefineryId;
            }
        }

        return bestId;
    }

    /// <summary>
    /// Moves a position toward a target at the given speed, returning the new position.
    /// </summary>
    private static FixedVector2 MoveToward(FixedVector2 from, FixedVector2 to, FixedPoint speed, FixedPoint deltaTime)
    {
        FixedVector2 diff = to - from;
        FixedPoint distSq = diff.LengthSquared;

        // Already at target
        if (distSq <= FixedPoint.Zero)
            return to;

        FixedPoint moveAmount = speed * deltaTime;
        FixedPoint dist = FixedPoint.Sqrt(distSq);

        // Would overshoot — snap to target
        if (moveAmount >= dist)
            return to;

        // Normalize direction and scale by move amount
        FixedVector2 direction = new FixedVector2(diff.X / dist, diff.Y / dist);
        return from + direction * moveAmount;
    }

    // ── Public Queries ──────────────────────────────────────────────

    public HarvesterInstance? GetHarvester(int unitId)
    {
        if (_harvesters.TryGetValue(unitId, out var h))
            return h;
        return null;
    }

    public CorditeNode? GetCorditeNode(int nodeId)
    {
        if (_corditeNodes.TryGetValue(nodeId, out var n))
            return n;
        return null;
    }

    public int HarvesterCount => _harvesters.Count;
    public int NodeCount => _corditeNodes.Count;
    public int RefineryCount => _refineries.Count;

    /// <summary>Returns all harvester instances for save/load serialization.</summary>
    public IList<HarvesterInstance> GetAllHarvesters() => _harvesters.Values;

    /// <summary>Returns all cordite nodes for save/load serialization.</summary>
    public IList<CorditeNode> GetAllCorditeNodes() => _corditeNodes.Values;

    /// <summary>Returns all refinery locations for save/load serialization.</summary>
    public IList<RefineryLocation> GetAllRefineries() => _refineries.Values;
}
