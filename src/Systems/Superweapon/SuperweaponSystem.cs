using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Systems.Superweapon;

/// <summary>
/// Type of strategic ability (superweapon). Determines the area-effect,
/// damage, and visual applied when the ability fires.
/// </summary>
public enum SuperweaponType
{
    /// <summary>Arcloft: heavy kinetic bombardment on a single target zone.</summary>
    OrbitalStrike,
    /// <summary>Arcloft: short-ranged EMP that disables all enemy units for several seconds.</summary>
    EMPBlast,
    /// <summary>Bastion: rapid multi-missile barrage covering a large area.</summary>
    MissileBarrage,
    /// <summary>Bastion: rapid deployment of reinforcement infantry around the target.</summary>
    ReinforcementDrop,
    /// <summary>Generic fallback — high-damage area explosion.</summary>
    Airstrike
}

/// <summary>
/// Immutable data that describes a superweapon ability (cooldown, range, effect).
/// </summary>
public sealed class SuperweaponData
{
    public string Id          { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FactionId   { get; init; } = string.Empty;
    public SuperweaponType Type { get; init; }

    /// <summary>Cooldown in simulation ticks (30 ticks = 1 s).</summary>
    public int CooldownTicks  { get; init; }

    /// <summary>Maximum range from any owned building, in grid cells.</summary>
    public FixedPoint Range   { get; init; }

    /// <summary>Radius of the area effect, in grid cells.</summary>
    public FixedPoint AreaOfEffect { get; init; }

    /// <summary>Base damage applied to each unit in the area.</summary>
    public FixedPoint Damage  { get; init; }
}

/// <summary>
/// Per-player runtime state for a superweapon: cooldown countdown and ready status.
/// </summary>
public sealed class PlayerSuperweaponState
{
    public int PlayerId          { get; init; }
    public SuperweaponData Data  { get; }
    public int CooldownRemaining { get; private set; }

    /// <summary>True when the weapon is ready to fire.</summary>
    public bool IsReady => CooldownRemaining <= 0;

    public PlayerSuperweaponState(int playerId, SuperweaponData data)
    {
        PlayerId = playerId;
        Data = data;
        // Start on full cooldown so there's no immediate firing at match start
        CooldownRemaining = data.CooldownTicks;
    }

    /// <summary>Decrements the cooldown by one tick.</summary>
    public void Tick()
    {
        if (CooldownRemaining > 0)
            CooldownRemaining--;
    }

    /// <summary>Arms the cooldown after the weapon fires.</summary>
    public void Arm() => CooldownRemaining = Data.CooldownTicks;

    /// <summary>
    /// Fraction of cooldown completed [0.0 – 1.0]. Used for HUD progress bars.
    /// </summary>
    public float ChargePercent => Data.CooldownTicks > 0
        ? 1f - ((float)CooldownRemaining / Data.CooldownTicks)
        : 1f;
}

/// <summary>
/// Result of a superweapon firing: which units were hit, how much damage, any
/// secondary effects (EMP duration, unit spawns, etc.).
/// </summary>
public sealed class SuperweaponResult
{
    /// <summary>Unit IDs that took direct damage.</summary>
    public List<int> HitUnitIds { get; init; } = new();

    /// <summary>Damage dealt to each hit unit (parallel to HitUnitIds).</summary>
    public List<FixedPoint> DamagePerUnit { get; init; } = new();

    /// <summary>True if an EMP effect was applied.</summary>
    public bool IsEMP { get; init; }

    /// <summary>Duration of EMP stun in simulation ticks (0 if not EMP).</summary>
    public int EMPDurationTicks { get; init; }

    /// <summary>Unit type IDs spawned by ReinforcementDrop (empty otherwise).</summary>
    public List<string> SpawnedUnitTypeIds { get; init; } = new();

    /// <summary>The world position targeted.</summary>
    public FixedVector2 TargetPosition { get; init; }

    /// <summary>True if the weapon was actually fired (cooldown was ready).</summary>
    public bool DidFire { get; init; }
}

/// <summary>
/// Manages all superweapon states for all players. Processes cooldown ticks
/// and resolves ability activations.
///
/// Deterministic: uses FixedPoint math only. No System.Random.
/// </summary>
public sealed class SuperweaponSystem
{
    // player id → weapon state
    private readonly Dictionary<int, PlayerSuperweaponState> _states = new();

    // ── Built-in ability catalogue ───────────────────────────────────

    private static readonly Dictionary<string, SuperweaponData> _catalogue = new()
    {
        ["arcloft_orbital_strike"] = new SuperweaponData
        {
            Id = "arcloft_orbital_strike",
            DisplayName = "Orbital Strike",
            Description = "A high-velocity kinetic penetrator dropped from orbit. Devastates a small target zone.",
            FactionId = "arcloft",
            Type = SuperweaponType.OrbitalStrike,
            CooldownTicks = 1800, // 60 s at 30 Hz
            Range          = FixedPoint.FromInt(80),
            AreaOfEffect   = FixedPoint.FromInt(5),
            Damage         = FixedPoint.FromInt(300)
        },
        ["arcloft_emp_blast"] = new SuperweaponData
        {
            Id = "arcloft_emp_blast",
            DisplayName = "EMP Blast",
            Description = "Electromagnetic pulse that disables all enemy units in a wide radius for 5 seconds.",
            FactionId = "arcloft",
            Type = SuperweaponType.EMPBlast,
            CooldownTicks = 2700, // 90 s
            Range          = FixedPoint.FromInt(100),
            AreaOfEffect   = FixedPoint.FromInt(12),
            Damage         = FixedPoint.FromInt(20)
        },
        ["bastion_missile_barrage"] = new SuperweaponData
        {
            Id = "bastion_missile_barrage",
            DisplayName = "Missile Barrage",
            Description = "Saturation bombardment with cluster munitions over a large target area.",
            FactionId = "bastion",
            Type = SuperweaponType.MissileBarrage,
            CooldownTicks = 1500, // 50 s
            Range          = FixedPoint.FromInt(90),
            AreaOfEffect   = FixedPoint.FromInt(10),
            Damage         = FixedPoint.FromInt(120)
        },
        ["bastion_reinforcement_drop"] = new SuperweaponData
        {
            Id = "bastion_reinforcement_drop",
            DisplayName = "Reinforcement Drop",
            Description = "Airdrop of four elite infantry squads at the target location.",
            FactionId = "bastion",
            Type = SuperweaponType.ReinforcementDrop,
            CooldownTicks = 2100, // 70 s
            Range          = FixedPoint.FromInt(100),
            AreaOfEffect   = FixedPoint.Zero,
            Damage         = FixedPoint.Zero
        }
    };

    /// <summary>Returns the catalogue entry for a superweapon by id, or null.</summary>
    public static SuperweaponData? GetData(string id)
        => _catalogue.TryGetValue(id, out var d) ? d : null;

    /// <summary>Returns all catalogue entries for a faction.</summary>
    public static IEnumerable<SuperweaponData> GetFactionWeapons(string factionId)
    {
        foreach (var kv in _catalogue)
        {
            if (kv.Value.FactionId == factionId)
                yield return kv.Value;
        }
    }

    // ── Player Registration ──────────────────────────────────────────

    /// <summary>
    /// Registers a player with a specific superweapon ability.
    /// Only one weapon per player.
    /// </summary>
    public void RegisterPlayer(int playerId, string weaponId)
    {
        if (!_catalogue.TryGetValue(weaponId, out var data)) return;
        _states[playerId] = new PlayerSuperweaponState(playerId, data);
    }

    // ── Tick ─────────────────────────────────────────────────────────

    /// <summary>
    /// Decrements all player cooldowns by one tick.
    /// Call once per simulation tick.
    /// </summary>
    public void Tick()
    {
        foreach (var state in _states.Values)
            state.Tick();
    }

    // ── Activation ───────────────────────────────────────────────────

    /// <summary>
    /// Attempts to fire the superweapon for <paramref name="playerId"/> targeting
    /// <paramref name="target"/>. Returns a <see cref="SuperweaponResult"/> with
    /// hit details, or a non-fired result if the weapon is on cooldown.
    ///
    /// Callers are responsible for applying damage from the result.
    /// </summary>
    public SuperweaponResult TryActivate(
        int playerId,
        FixedVector2 target,
        IReadOnlyList<CorditeWars.Systems.Pathfinding.SimUnit> allUnits)
    {
        if (!_states.TryGetValue(playerId, out var state) || !state.IsReady)
            return new SuperweaponResult { TargetPosition = target, DidFire = false };

        state.Arm();

        var result = new SuperweaponResult
        {
            TargetPosition = target,
            DidFire        = true,
            IsEMP          = state.Data.Type == SuperweaponType.EMPBlast,
            EMPDurationTicks = state.Data.Type == SuperweaponType.EMPBlast ? 150 : 0 // 5 s
        };

        if (state.Data.Type == SuperweaponType.ReinforcementDrop)
        {
            // Spawn 4 Bastion infantry units — IDs provided to caller
            string unitTypeId = "bastion_soldier"; // default infantry
            for (int i = 0; i < 4; i++)
                result.SpawnedUnitTypeIds.Add(unitTypeId);
            return result;
        }

        // Area damage for all other weapon types
        FixedPoint aoeSq = state.Data.AreaOfEffect * state.Data.AreaOfEffect;
        for (int i = 0; i < allUnits.Count; i++)
        {
            var unit = allUnits[i];
            if (!unit.IsAlive) continue;
            if (unit.PlayerId == playerId) continue; // friendly fire off

            FixedVector2 diff = unit.Movement.Position - target;
            FixedPoint distSq = diff.X * diff.X + diff.Y * diff.Y;
            if (distSq > aoeSq) continue;

            result.HitUnitIds.Add(unit.UnitId);
            result.DamagePerUnit.Add(state.Data.Damage);
        }

        return result;
    }

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Returns the weapon state for a player, or null if not registered.</summary>
    public PlayerSuperweaponState? GetState(int playerId)
        => _states.TryGetValue(playerId, out var s) ? s : null;

    /// <summary>Returns true if the player's superweapon is ready to fire.</summary>
    public bool IsReady(int playerId)
        => _states.TryGetValue(playerId, out var s) && s.IsReady;

    /// <summary>Returns the charge percentage [0,1] for HUD display.</summary>
    public float GetChargePercent(int playerId)
        => _states.TryGetValue(playerId, out var s) ? s.ChargePercent : 0f;

    public IReadOnlyDictionary<int, PlayerSuperweaponState> AllStates => _states;
}
