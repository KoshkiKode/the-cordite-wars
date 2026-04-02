using Godot;
using UnnamedRTS.Systems.Networking;

namespace UnnamedRTS.Core;

/// <summary>
/// Global game manager autoload. Handles game state, tick management,
/// and coordination between all major subsystems.
/// This is the central authority for the deterministic game simulation.
/// </summary>
public partial class GameManager : Node
{
    /// <summary>
    /// The fixed simulation tick rate. All game logic runs at this rate
    /// for deterministic lockstep networking compatibility.
    /// </summary>
    public const int SimTickRate = 30;

    /// <summary>
    /// Current simulation tick number. Monotonically increasing.
    /// Used for deterministic replay and lockstep sync.
    /// </summary>
    public ulong CurrentTick { get; private set; }

    /// <summary>
    /// Current state of the game.
    /// </summary>
    public GameState State { get; private set; } = GameState.Boot;

    /// <summary>
    /// Random number generator seeded per-match for deterministic simulation.
    /// All gameplay randomness MUST use this — never System.Random or GD.Randf().
    /// </summary>
    public DeterministicRng Rng { get; private set; } = new(0);

    /// <summary>
    /// Whether the current match is networked multiplayer.
    /// When true, tick advancement is gated by LockstepManager.
    /// </summary>
    public bool IsMultiplayer { get; private set; }

    /// <summary>
    /// Reference to the lockstep manager for multiplayer matches.
    /// Null in single-player.
    /// </summary>
    public LockstepManager? Lockstep { get; private set; }

    /// <summary>
    /// Reference to the command system for injecting remote commands.
    /// Set externally during initialization.
    /// </summary>
    public CommandBuffer? CommandBuffer { get; set; }

    public override void _Ready()
    {
        GD.Print("[GameManager] Initialized.");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State != GameState.Playing)
            return;

        if (IsMultiplayer && Lockstep != null)
        {
            ulong nextTick = CurrentTick + 1;

            // In multiplayer: only advance if all players' commands are received
            if (!Lockstep.CanAdvanceTick(nextTick))
                return; // Waiting for remote commands

            // Confirm our own tick (signals we have no more commands for nextTick)
            Lockstep.ConfirmLocalTick(nextTick);

            // Get merged commands for this tick and inject into the command buffer
            var commands = Lockstep.GetCommandsForTick(nextTick);
            if (CommandBuffer != null)
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    CommandBuffer.AddCommand(commands[i]);
                }
            }
        }

        CurrentTick++;

        if (IsMultiplayer && Lockstep != null)
        {
            // Compute and submit checksum every 30 ticks (1 second)
            if (CurrentTick % 30 == 0)
            {
                // Checksum computation requires the simulation state list.
                // The actual SimUnit list is provided by the tick pipeline caller.
                // Here we emit a signal / delegate pattern for the pipeline to supply it.
                // For now, the checksum is triggered externally after the tick pipeline runs.
            }
        }
    }

    /// <summary>
    /// Starts a new single-player match with the given RNG seed.
    /// </summary>
    public void StartMatch(ulong seed)
    {
        CurrentTick = 0;
        Rng = new DeterministicRng(seed);
        IsMultiplayer = false;
        Lockstep = null;
        State = GameState.Playing;
        EventBus.Instance?.EmitMatchStarted(seed);
        GD.Print($"[GameManager] Match started with seed {seed}.");
    }

    /// <summary>
    /// Starts a multiplayer match with lockstep networking.
    /// Called after lobby setup — all clients must use the same seed.
    /// </summary>
    public void StartMultiplayerMatch(ulong seed, LockstepManager lockstep)
    {
        CurrentTick = 0;
        Rng = new DeterministicRng(seed);
        IsMultiplayer = true;
        Lockstep = lockstep;
        State = GameState.Playing;
        EventBus.Instance?.EmitMatchStarted(seed);
        GD.Print($"[GameManager] Multiplayer match started with seed {seed}.");
    }

    /// <summary>
    /// Pauses the simulation. Input is still processed.
    /// </summary>
    public void PauseMatch()
    {
        State = GameState.Paused;
        EventBus.Instance?.EmitMatchPaused();
        GD.Print("[GameManager] Match paused.");
    }

    /// <summary>
    /// Resumes a paused match.
    /// </summary>
    public void ResumeMatch()
    {
        State = GameState.Playing;
        EventBus.Instance?.EmitMatchResumed();
        GD.Print("[GameManager] Match resumed.");
    }

    /// <summary>
    /// Ends the current match.
    /// </summary>
    public void EndMatch()
    {
        State = GameState.PostGame;
        EventBus.Instance?.EmitMatchEnded();
        GD.Print($"[GameManager] Match ended at tick {CurrentTick}.");
    }

    /// <summary>
    /// Returns to the main menu state.
    /// </summary>
    public void ReturnToMenu()
    {
        State = GameState.MainMenu;
        CurrentTick = 0;
    }
}

/// <summary>
/// All possible states the game can be in.
/// </summary>
public enum GameState
{
    Boot,
    MainMenu,
    Loading,
    Playing,
    Paused,
    PostGame
}
