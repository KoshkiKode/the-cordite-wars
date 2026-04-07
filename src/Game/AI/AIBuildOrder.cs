using Godot;
using System.Collections.Generic;
using CorditeWars.Core;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Buildings;
using CorditeWars.Game.Economy;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.AI;

/// <summary>
/// Step type in a build order.
/// </summary>
public enum BuildStepType
{
    BuildBuilding,
    ProduceUnit,
    WaitForResources
}

/// <summary>
/// Single step in a scripted build order.
/// </summary>
public struct BuildStep
{
    public BuildStepType Type;
    public string TargetId; // Building or unit type ID
    public int Count;       // How many to produce
    public FixedPoint OffsetX; // Placement offset from base
    public FixedPoint OffsetY;
}

/// <summary>
/// Scripted opening build orders per faction.
/// Steps execute in sequence. Simulation code: FixedPoint, no LINQ, SortedList.
/// </summary>
public partial class AIBuildOrder : Node
{
    private readonly List<BuildStep> _steps = new();
    private int _currentStep;
    private int _currentCount; // Units produced in current step
    private AIDifficulty _difficulty;

    public bool IsComplete => _currentStep >= _steps.Count;

    // ── Initialization ───────────────────────────────────────────────

    public void Initialize(string factionId, AIDifficulty difficulty)
    {
        _difficulty = difficulty;
        _currentStep = 0;
        _currentCount = 0;

        Name = "AIBuildOrder";

        // Build orders are similar across factions with minor differences.
        // All factions follow: Refinery -> Harvesters -> Supply -> Barracks -> Infantry -> Vehicle Factory -> Reactor
        CreateStandardBuildOrder(factionId, difficulty);
    }

    /// <summary>
    /// Appends naval Shipyard steps to the build order when the map is
    /// water-heavy.  Call this after <see cref="Initialize"/> when the terrain
    /// grid water analysis is available.
    /// </summary>
    public void AppendNavalBuildSteps(string factionId)
    {
        // Shipyard after Vehicle Factory and Reactor are up
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_shipyard",
            Count = 1,
            OffsetX = FixedPoint.FromInt(0),
            OffsetY = FixedPoint.FromInt(-10)  // toward shoreline (negative Y = south by convention)
        });
    }

    private void CreateStandardBuildOrder(string factionId, AIDifficulty difficulty)
    {
        _steps.Clear();

        // Step 1: Build Refinery near closest Cordite node
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_refinery",
            Count = 1,
            OffsetX = FixedPoint.FromInt(5),
            OffsetY = FixedPoint.FromInt(0)
        });

        // Step 2: Produce 2 more Harvesters
        int harvesterCount = difficulty == AIDifficulty.Hard ? 3 : 2;
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.ProduceUnit,
            TargetId = $"{factionId}_harvester",
            Count = harvesterCount,
            OffsetX = FixedPoint.Zero,
            OffsetY = FixedPoint.Zero
        });

        // Step 3: Build Supply Depot
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_supply_depot",
            Count = 1,
            OffsetX = FixedPoint.FromInt(-5),
            OffsetY = FixedPoint.FromInt(3)
        });

        // Step 4: Build Barracks
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_barracks",
            Count = 1,
            OffsetX = FixedPoint.FromInt(0),
            OffsetY = FixedPoint.FromInt(6)
        });

        // Step 5: Produce infantry
        int infantryCount = difficulty == AIDifficulty.Easy ? 3 : 5;
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.ProduceUnit,
            TargetId = $"{factionId}_infantry",
            Count = infantryCount,
            OffsetX = FixedPoint.Zero,
            OffsetY = FixedPoint.Zero
        });

        // Step 6: Build Vehicle Factory
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_vehicle_factory",
            Count = 1,
            OffsetX = FixedPoint.FromInt(8),
            OffsetY = FixedPoint.FromInt(4)
        });

        // Step 7: Build Reactor
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_reactor",
            Count = 1,
            OffsetX = FixedPoint.FromInt(-6),
            OffsetY = FixedPoint.FromInt(-3)
        });

        // Step 8: Build second Supply Depot
        _steps.Add(new BuildStep
        {
            Type = BuildStepType.BuildBuilding,
            TargetId = $"{factionId}_supply_depot",
            Count = 1,
            OffsetX = FixedPoint.FromInt(-5),
            OffsetY = FixedPoint.FromInt(6)
        });

        // Hard: additional barracks
        if (difficulty == AIDifficulty.Hard)
        {
            _steps.Add(new BuildStep
            {
                Type = BuildStepType.BuildBuilding,
                TargetId = $"{factionId}_barracks",
                Count = 1,
                OffsetX = FixedPoint.FromInt(3),
                OffsetY = FixedPoint.FromInt(8)
            });
        }
    }

    // ── Execution ────────────────────────────────────────────────────

    /// <summary>
    /// Executes the next build order step if affordable and requirements met.
    /// Called periodically by SkirmishAI during Opening/Expanding phases.
    /// </summary>
    public void ExecuteNextStep(
        int playerId,
        PlayerEconomy economy,
        FixedVector2 basePosition,
        EconomyManager economyManager,
        BuildingPlacer buildingPlacer,
        BuildingRegistry buildingRegistry,
        UnitDataRegistry unitDataRegistry)
    {
        if (IsComplete) return;

        BuildStep step = _steps[_currentStep];

        switch (step.Type)
        {
            case BuildStepType.BuildBuilding:
                if (TryExecuteBuildStep(step, playerId, economy, basePosition,
                    economyManager, buildingPlacer, buildingRegistry))
                {
                    _currentCount++;
                    if (_currentCount >= step.Count)
                    {
                        _currentStep++;
                        _currentCount = 0;
                    }
                }
                break;

            case BuildStepType.ProduceUnit:
                if (TryExecuteProduceStep(step, playerId, economy,
                    economyManager, unitDataRegistry))
                {
                    _currentCount++;
                    if (_currentCount >= step.Count)
                    {
                        _currentStep++;
                        _currentCount = 0;
                    }
                }
                break;

            case BuildStepType.WaitForResources:
                // Wait until we can afford the next step's cost
                if (_currentStep + 1 < _steps.Count)
                {
                    BuildStep nextStep = _steps[_currentStep + 1];
                    int requiredCordite = GetStepCost(nextStep, buildingRegistry, unitDataRegistry);
                    if (economy.Cordite >= FixedPoint.FromInt(requiredCordite))
                    {
                        _currentStep++;
                        _currentCount = 0;
                    }
                }
                else
                {
                    _currentStep++;
                }
                break;
        }
    }

    private bool TryExecuteBuildStep(
        BuildStep step,
        int playerId,
        PlayerEconomy economy,
        FixedVector2 basePosition,
        EconomyManager economyManager,
        BuildingPlacer buildingPlacer,
        BuildingRegistry buildingRegistry)
    {
        // Check building exists in registry (try with and without faction prefix)
        string buildingId = step.TargetId;
        if (!buildingRegistry.HasBuilding(buildingId))
        {
            // Try generic name
            string generic = buildingId;
            int underscoreIdx = -1;
            for (int i = 0; i < generic.Length; i++)
            {
                if (generic[i] == '_')
                {
                    underscoreIdx = i;
                    break;
                }
            }
            if (underscoreIdx >= 0)
                generic = generic.Substring(underscoreIdx + 1);

            if (!buildingRegistry.HasBuilding(generic))
            {
                // Skip this step — building type doesn't exist
                return true;
            }
            buildingId = generic;
        }

        BuildingData data = buildingRegistry.GetBuilding(buildingId);

        // Check affordability
        if (!economy.CanAfford(data.Cost, data.SecondaryCost))
            return false;

        // Attempt to place — AI just deducts resources and creates building directly
        if (!economyManager.TryBuildBuilding(playerId, data))
            return false;

        GD.Print($"[AIBuildOrder P{playerId}] Built {buildingId}.");
        return true;
    }

    private bool TryExecuteProduceStep(
        BuildStep step,
        int playerId,
        PlayerEconomy economy,
        EconomyManager economyManager,
        UnitDataRegistry unitDataRegistry)
    {
        string unitId = step.TargetId;
        if (!unitDataRegistry.HasUnit(unitId))
        {
            // Try generic name
            string generic = unitId;
            int underscoreIdx = -1;
            for (int i = 0; i < generic.Length; i++)
            {
                if (generic[i] == '_')
                {
                    underscoreIdx = i;
                    break;
                }
            }
            if (underscoreIdx >= 0)
                generic = generic.Substring(underscoreIdx + 1);

            if (!unitDataRegistry.HasUnit(generic))
                return true; // Skip
            unitId = generic;
        }

        UnitData unitData = unitDataRegistry.GetUnitData(unitId);

        if (!economy.CanAfford(unitData.Cost, unitData.SecondaryCost))
            return false;

        if (economy.CurrentSupply + unitData.PopulationCost > economy.MaxSupply)
            return false;

        if (!economyManager.TryBuildUnit(playerId, unitData))
            return false;

        return true;
    }

    private int GetStepCost(BuildStep step, BuildingRegistry buildingReg, UnitDataRegistry unitDataReg)
    {
        switch (step.Type)
        {
            case BuildStepType.BuildBuilding:
                if (buildingReg.HasBuilding(step.TargetId))
                    return buildingReg.GetBuilding(step.TargetId).Cost;
                return 200;
            case BuildStepType.ProduceUnit:
                if (unitDataReg.HasUnit(step.TargetId))
                    return unitDataReg.GetUnitData(step.TargetId).Cost;
                return 100;
            default:
                return 0;
        }
    }
}
