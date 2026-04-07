using System.Collections.Generic;

namespace CorditeWars.UI.Minimap;

/// <summary>
/// Categories of minimap pings.
/// </summary>
public enum PingType
{
    /// <summary>Something is attacking here (auto-generated on base attack).</summary>
    Attack,

    /// <summary>Player-placed beacon ("look here").</summary>
    Beacon,

    /// <summary>General alert (e.g., nuke launch detected).</summary>
    Alert
}

/// <summary>
/// A single minimap ping instance — a temporary marker that pulses on the minimap
/// to draw the player's attention (C&amp;C-style).
/// </summary>
public struct MinimapPing
{
    /// <summary>Grid-space X coordinate of the ping.</summary>
    public int GridX;

    /// <summary>Grid-space Y coordinate of the ping.</summary>
    public int GridY;

    /// <summary>What kind of ping this is.</summary>
    public PingType Type;

    /// <summary>Index (0-7) of the player who created the ping.</summary>
    public int PlayerIndex;

    /// <summary>Simulation tick when the ping was created.</summary>
    public ulong StartTick;

    /// <summary>How many ticks the ping remains active (e.g. 90 = 3 seconds at 30 tps).</summary>
    public ulong DurationTicks;

    public MinimapPing(int gridX, int gridY, PingType type, int playerIndex,
                       ulong startTick, ulong durationTicks = 90)
    {
        GridX = gridX;
        GridY = gridY;
        Type = type;
        PlayerIndex = playerIndex;
        StartTick = startTick;
        DurationTicks = durationTicks;
    }

    /// <summary>
    /// Returns true if the ping has exceeded its lifetime.
    /// </summary>
    public bool IsExpired(ulong currentTick)
    {
        return currentTick >= StartTick + DurationTicks;
    }

    /// <summary>
    /// Returns the elapsed ticks since this ping was created.
    /// Useful for the renderer to compute a pulse phase (sine wave).
    /// </summary>
    public ulong ElapsedTicks(ulong currentTick)
    {
        return currentTick >= StartTick ? currentTick - StartTick : 0;
    }
}

/// <summary>
/// Manages the lifecycle of minimap pings. Tracks creation and automatic expiry.
/// The renderer queries <see cref="GetActivePings"/> each frame and handles the
/// visual pulse effect using tick-based sine wave calculations.
/// </summary>
public class MinimapPingSystem
{
    /// <summary>
    /// Currently active (non-expired) pings.
    /// </summary>
    public List<MinimapPing> ActivePings { get; } = new();

    /// <summary>
    /// Default ping duration in ticks (90 ticks = 3 seconds at 30 tps).
    /// </summary>
    public const ulong DefaultDurationTicks = 90;

    /// <summary>
    /// Creates a new ping at the given grid location.
    /// </summary>
    /// <param name="gridX">Grid-space X coordinate.</param>
    /// <param name="gridY">Grid-space Y coordinate.</param>
    /// <param name="type">Category of the ping.</param>
    /// <param name="playerIndex">Which player created the ping (0-7).</param>
    /// <param name="currentTick">The current simulation tick.</param>
    /// <param name="duration">Lifetime in ticks. Defaults to 90 (~3 seconds).</param>
    public void AddPing(int gridX, int gridY, PingType type, int playerIndex,
                        ulong currentTick, ulong duration = DefaultDurationTicks)
    {
        ActivePings.Add(new MinimapPing(gridX, gridY, type, playerIndex, currentTick, duration));
    }

    /// <summary>
    /// Removes expired pings. Call once per frame (or per tick).
    /// </summary>
    /// <param name="currentTick">The current simulation tick.</param>
    public void Update(ulong currentTick)
    {
        // Iterate backwards to safely remove while traversing.
        for (int i = ActivePings.Count - 1; i >= 0; i--)
        {
            if (ActivePings[i].IsExpired(currentTick))
            {
                ActivePings.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Returns the current list of active pings for the renderer to draw.
    /// </summary>
    public List<MinimapPing> GetActivePings()
    {
        return ActivePings;
    }
}
