using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.AI;

/// <summary>
/// AI army management. Groups units into squads, manages scouting,
/// attack timing, and defense allocation.
/// Simulation code: FixedPoint, no LINQ, SortedList.
/// </summary>
public partial class AICommander : Node
{
    // ── Squad Definition ─────────────────────────────────────────────

    public enum SquadRole
    {
        Scout,
        MainArmy,
        Defense
    }

    public struct Squad
    {
        public int SquadId;
        public SquadRole Role;
        public SortedList<int, int> UnitIds; // unitId → unitId (SortedList as set)
        public FixedVector2 TargetPosition;
    }

    // ── State ────────────────────────────────────────────────────────

    private int _playerId;
    private string _factionId = string.Empty;
    private AIDifficulty _difficulty;
    private FixedVector2 _basePosition;
    private UnitSpawner? _unitSpawner;

    private readonly SortedList<int, Squad> _squads = new();
    private int _nextSquadId;

    // Attack thresholds (supply percentage)
    private int _attackThresholdPercent;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(
        int playerId,
        string factionId,
        AIDifficulty difficulty,
        FixedVector2 basePosition,
        UnitSpawner unitSpawner)
    {
        _playerId = playerId;
        _factionId = factionId;
        _difficulty = difficulty;
        _basePosition = basePosition;
        _unitSpawner = unitSpawner;

        Name = "AICommander";

        // Set attack threshold by difficulty
        _attackThresholdPercent = difficulty switch
        {
            AIDifficulty.Easy => 80,   // Attacks only when near supply cap
            AIDifficulty.Medium => 50, // Attacks at 50% supply
            AIDifficulty.Hard => 70,   // Attacks at 70% supply (optimal timing)
            _ => 60
        };

        // Create initial squads
        CreateSquad(SquadRole.Scout);
        CreateSquad(SquadRole.MainArmy);
        CreateSquad(SquadRole.Defense);
    }

    // ── Public API ───────────────────────────────────────────────────

    public int SquadCount => _squads.Count;

    /// <summary>
    /// Called by SkirmishAI each AI tick to update army management.
    /// </summary>
    public void Update(AIState currentAIState)
    {
        if (_unitSpawner is null) return;

        // Assign unassigned units to squads
        AssignUnitsToSquads();

        // Manage squad behavior based on AI state
        for (int i = 0; i < _squads.Count; i++)
        {
            Squad squad = _squads.Values[i];

            // Clean dead units from squad
            CleanDeadUnits(ref squad);

            switch (squad.Role)
            {
                case SquadRole.Scout:
                    UpdateScoutSquad(ref squad, currentAIState);
                    break;
                case SquadRole.MainArmy:
                    UpdateMainArmySquad(ref squad, currentAIState);
                    break;
                case SquadRole.Defense:
                    UpdateDefenseSquad(ref squad, currentAIState);
                    break;
            }

            // Write back (struct copy)
            _squads[_squads.Keys[i]] = squad;
        }
    }

    // ── Squad Management ─────────────────────────────────────────────

    private int CreateSquad(SquadRole role)
    {
        int id = _nextSquadId++;
        _squads.Add(id, new Squad
        {
            SquadId = id,
            Role = role,
            UnitIds = new SortedList<int, int>(),
            TargetPosition = _basePosition
        });
        return id;
    }

    private void AssignUnitsToSquads()
    {
        if (_unitSpawner is null) return;

        var allUnits = _unitSpawner.GetAllUnits();

        // Build a set of all currently assigned unit IDs
        var assignedIds = new SortedList<int, bool>();
        for (int s = 0; s < _squads.Count; s++)
        {
            var unitIds = _squads.Values[s].UnitIds;
            for (int u = 0; u < unitIds.Count; u++)
                assignedIds[unitIds.Keys[u]] = true;
        }

        // Find unassigned units belonging to this player
        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitNode3D unit = allUnits[i];
            if (!unit.IsAlive) continue;
            if (unit.FactionId != _factionId) continue;
            if (assignedIds.ContainsKey(unit.UnitId)) continue;

            // Assign unit based on category
            SquadRole targetRole = DetermineSquadRole(unit);
            AssignToSquadWithRole(unit.UnitId, targetRole);
        }
    }

    private SquadRole DetermineSquadRole(UnitNode3D unit)
    {
        // First 1-2 units go to scout squad
        int scoutSquadId = -1;
        for (int i = 0; i < _squads.Count; i++)
        {
            if (_squads.Values[i].Role == SquadRole.Scout)
            {
                scoutSquadId = _squads.Keys[i];
                break;
            }
        }

        if (scoutSquadId >= 0 && _squads[scoutSquadId].UnitIds.Count < 2)
            return SquadRole.Scout;

        // Keep ~20% for defense
        int defenseSquadId = -1;
        int mainSquadId = -1;
        int totalArmy = 0;
        int defenseCount = 0;

        for (int i = 0; i < _squads.Count; i++)
        {
            Squad sq = _squads.Values[i];
            totalArmy += sq.UnitIds.Count;
            if (sq.Role == SquadRole.Defense)
            {
                defenseSquadId = _squads.Keys[i];
                defenseCount = sq.UnitIds.Count;
            }
            else if (sq.Role == SquadRole.MainArmy)
            {
                mainSquadId = _squads.Keys[i];
            }
        }

        if (defenseSquadId >= 0 && totalArmy > 0)
        {
            int targetDefense = totalArmy / 5; // 20%
            if (defenseCount < targetDefense)
                return SquadRole.Defense;
        }

        return SquadRole.MainArmy;
    }

    private void AssignToSquadWithRole(int unitId, SquadRole role)
    {
        for (int i = 0; i < _squads.Count; i++)
        {
            if (_squads.Values[i].Role == role)
            {
                var squad = _squads.Values[i];
                if (!squad.UnitIds.ContainsKey(unitId))
                    squad.UnitIds.Add(unitId, unitId);
                _squads[_squads.Keys[i]] = squad;
                return;
            }
        }
    }

    private void CleanDeadUnits(ref Squad squad)
    {
        if (_unitSpawner is null) return;

        var deadIds = new List<int>();
        for (int i = 0; i < squad.UnitIds.Count; i++)
        {
            int unitId = squad.UnitIds.Keys[i];
            UnitNode3D? unit = _unitSpawner.GetUnit(unitId);
            if (unit is null || !unit.IsAlive)
                deadIds.Add(unitId);
        }

        for (int i = 0; i < deadIds.Count; i++)
            squad.UnitIds.Remove(deadIds[i]);
    }

    // ── Squad Behavior ───────────────────────────────────────────────

    private void UpdateScoutSquad(ref Squad squad, AIState state)
    {
        if (squad.UnitIds.Count == 0) return;

        // Scout explores map — move to random positions around the map
        // Simple exploration: spiral outward from base
        FixedPoint radius = FixedPoint.FromInt(20) + FixedPoint.FromInt(_nextSquadId % 40);
        FixedPoint angle = FixedPoint.FromInt((_nextSquadId * 37) % 360);

        // Simple position offset (approximate without trig — grid pattern)
        int gridStep = (_nextSquadId * 7) % 8;
        FixedPoint dx = FixedPoint.Zero;
        FixedPoint dy = FixedPoint.Zero;

        switch (gridStep)
        {
            case 0: dx = radius; break;
            case 1: dx = radius; dy = radius; break;
            case 2: dy = radius; break;
            case 3: dx = FixedPoint.Zero - radius; dy = radius; break;
            case 4: dx = FixedPoint.Zero - radius; break;
            case 5: dx = FixedPoint.Zero - radius; dy = FixedPoint.Zero - radius; break;
            case 6: dy = FixedPoint.Zero - radius; break;
            case 7: dx = radius; dy = FixedPoint.Zero - radius; break;
        }

        squad.TargetPosition = new FixedVector2(
            _basePosition.X + dx,
            _basePosition.Y + dy);
    }

    private void UpdateMainArmySquad(ref Squad squad, AIState state)
    {
        switch (state)
        {
            case AIState.Opening:
            case AIState.Expanding:
                // Rally near base
                squad.TargetPosition = new FixedVector2(
                    _basePosition.X + FixedPoint.FromInt(10),
                    _basePosition.Y + FixedPoint.FromInt(10));
                break;

            case AIState.Aggression:
                // Attack target — for now, target map center
                // In a full implementation, this would target enemy base
                squad.TargetPosition = new FixedVector2(
                    FixedPoint.FromInt(128),
                    FixedPoint.FromInt(128));
                break;

            case AIState.Crisis:
                // Pull back to base
                squad.TargetPosition = _basePosition;
                break;
        }
    }

    private void UpdateDefenseSquad(ref Squad squad, AIState state)
    {
        // Defense always stays near base
        squad.TargetPosition = _basePosition;
    }

    // ── Target Selection (priority-based) ────────────────────────────

    /// <summary>
    /// Selects the best enemy target to attack.
    /// Priority: economy (refineries) > production > army.
    /// Returns target position or base position if no targets found.
    /// </summary>
    public FixedVector2 SelectAttackTarget()
    {
        // In a full implementation, this would scan enemy buildings
        // For now, return map center as a reasonable attack point
        return new FixedVector2(
            FixedPoint.FromInt(128),
            FixedPoint.FromInt(128));
    }

    /// <summary>
    /// Returns true if the main army is large enough to attack.
    /// </summary>
    public bool IsArmyReadyToAttack(int currentSupply, int maxSupply)
    {
        if (maxSupply <= 0) return false;
        int supplyPercent = (currentSupply * 100) / maxSupply;
        return supplyPercent >= _attackThresholdPercent;
    }

    // ── Query ────────────────────────────────────────────────────────

    public Squad? GetSquad(int squadId)
    {
        if (_squads.ContainsKey(squadId))
            return _squads[squadId];
        return null;
    }

    public int GetMainArmySize()
    {
        for (int i = 0; i < _squads.Count; i++)
        {
            if (_squads.Values[i].Role == SquadRole.MainArmy)
                return _squads.Values[i].UnitIds.Count;
        }
        return 0;
    }
}
