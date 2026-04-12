using System.Collections.Generic;
using CorditeWars.Game.Campaign;
using CorditeWars.Game.World;

namespace CorditeWars.Game;

/// <summary>
/// Campaign-specific context attached to a <see cref="MatchConfig"/> when the
/// match originates from a campaign mission. Absent for skirmish / multiplayer.
/// </summary>
public sealed class CampaignMatchContext
{
    /// <summary>Faction the human player is running (e.g. "arcloft").</summary>
    public string FactionId { get; init; } = string.Empty;

    /// <summary>Unique mission identifier (e.g. "arcloft_03").</summary>
    public string MissionId { get; init; } = string.Empty;

    /// <summary>1-based mission number within the faction's campaign.</summary>
    public int MissionNumber { get; init; }

    /// <summary>Human-readable mission name shown in the objectives panel.</summary>
    public string MissionName { get; init; } = string.Empty;

    /// <summary>
    /// List of objective strings shown in the HUD and pause menu.
    /// These are flavour / informational — the actual win condition is driven
    /// by <see cref="MatchConfig.WinCondition"/>.
    /// </summary>
    public string[] Objectives { get; init; } = [];

    /// <summary>Typed (structured) objectives for the mission objective tracker.</summary>
    public TypedObjectiveData[] TypedObjectives { get; init; } = [];

    /// <summary>
    /// Set of building IDs the player is allowed to construct in this mission.
    /// Accumulated from all missions up to and including this one.
    /// <c>null</c> means all buildings are allowed (used for skirmish/multiplayer).
    /// </summary>
    public HashSet<string>? AllowedBuildingIds { get; init; }
}

/// <summary>
/// Configuration for a single player slot in a match.
/// </summary>
public sealed class PlayerConfig
{
    public int PlayerId { get; init; }
    public string FactionId { get; init; } = string.Empty;
    public bool IsAI { get; init; }
    public int AIDifficulty { get; init; }
    public string PlayerName { get; init; } = string.Empty;
}

/// <summary>
/// Configuration for starting a match. Passed to <see cref="GameSession.StartMatch"/>.
/// </summary>
public sealed class MatchConfig
{
    public string MapId { get; init; } = string.Empty;
    public PlayerConfig[] PlayerConfigs { get; init; } = [];
    public ulong MatchSeed { get; init; }
    public int GameSpeed { get; init; } = 1;
    public bool FogOfWar { get; init; } = true;
    public int StartingCordite { get; init; } = 5000;

    /// <summary>
    /// Victory condition for the match.
    /// Defaults to <see cref="WinCondition.DestroyHQ"/> (standard skirmish).
    /// Campaign missions may override this to <see cref="WinCondition.KillAllUnits"/>.
    /// </summary>
    public WinCondition WinCondition { get; init; } = WinCondition.DestroyHQ;

    /// <summary>
    /// When set, a map is generated procedurally using this config instead of
    /// loading a static map identified by <see cref="MapId"/>.
    /// </summary>
    public MapGenConfig? MapGeneration { get; init; }

    /// <summary>When true, starts the built-in tutorial sequence.</summary>
    public bool IsTutorial { get; init; }

    /// <summary>
    /// Which tutorial mission to load (1, 2, or 3).
    /// 1 = Movement &amp; Camera; 2 = Buildings &amp; Units; 3 = Advanced Strategy.
    /// Only meaningful when <see cref="IsTutorial"/> is true.
    /// </summary>
    public int TutorialMission { get; init; } = 1;

    /// <summary>
    /// When set, this match is a campaign mission.
    /// <see cref="Main"/> uses this to save progress and display objectives.
    /// Null for skirmish and multiplayer matches.
    /// </summary>
    public CampaignMatchContext? Campaign { get; init; }
}
