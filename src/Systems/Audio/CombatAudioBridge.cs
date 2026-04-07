using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Units;

namespace CorditeWars.Systems.Audio;

/// <summary>
/// Listens to combat events on the <see cref="EventBus"/> and plays
/// the matching sounds through <see cref="AudioManager"/>.
///
/// This class is the single integration point between the deterministic
/// combat simulation and the non-deterministic audio system. It runs on
/// the rendering side only — it never mutates simulation state.
///
/// Typical lifecycle:
///   1. GameSession creates this node after match start.
///   2. <see cref="Initialize"/> subscribes to EventBus signals.
///   3. Each tick, GameSession emits AttackFired / AttackImpact / UnitDeath.
///   4. This bridge resolves the correct SoundEntry and plays it.
///   5. On cleanup, the node is freed and signals auto-disconnect.
/// </summary>
public partial class CombatAudioBridge : Node
{
    private AudioManager? _audioManager;

    public override void _Ready()
    {
        _audioManager = GetNode<AudioManager>("/root/AudioManager");
    }

    /// <summary>
    /// Subscribes to all combat-related EventBus signals. Call once after
    /// EventBus and AudioManager are available.
    /// </summary>
    public void Initialize()
    {
        var bus = EventBus.Instance;
        if (bus == null)
        {
            GD.PushError("[CombatAudioBridge] EventBus not available.");
            return;
        }

        bus.AttackFired += OnAttackFired;
        bus.AttackImpact += OnAttackImpact;
        bus.UnitDeath += OnUnitDeath;

        GD.Print("[CombatAudioBridge] Initialized — listening for combat events.");
    }

    public override void _ExitTree()
    {
        var bus = EventBus.Instance;
        if (bus != null)
        {
            bus.AttackFired -= OnAttackFired;
            bus.AttackImpact -= OnAttackImpact;
            bus.UnitDeath -= OnUnitDeath;
        }
    }

    // ── Signal Handlers ──────────────────────────────────────────────

    private void OnAttackFired(int attackerId, int weaponType, Vector3 position)
    {
        string? soundId = MapWeaponTypeToSoundId((WeaponType)weaponType);
        if (soundId == null) return;

        PlayCombatSound(soundId, position);
    }

    private void OnAttackImpact(int targetId, bool isHit, bool hasAoe, Vector3 position)
    {
        if (!isHit && !hasAoe) return; // Misses with no splash produce no impact sound

        string soundId;
        if (hasAoe)
        {
            soundId = "impact_explosion_medium";
        }
        else
        {
            // Use a generic bullet impact for direct hits without AoE.
            // The "metal" variant is the most common in an RTS context.
            soundId = "impact_bullet_metal";
        }

        PlayCombatSound(soundId, position);
    }

    private void OnUnitDeath(int unitId, int unitCategory, Vector3 position)
    {
        string soundId = MapCategoryToDeathSound((UnitCategory)unitCategory);
        PlayCombatSound(soundId, position);
    }

    // ── Mapping helpers ──────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="WeaponType"/> enum to the corresponding sound ID
    /// in the "combat" category of sound_manifest.json.
    /// Returns null for WeaponType.None.
    /// </summary>
    private static string? MapWeaponTypeToSoundId(WeaponType type)
    {
        return type switch
        {
            WeaponType.MachineGun    => "weapon_machinegun",
            WeaponType.Cannon        => "weapon_cannon_heavy",
            WeaponType.Missile       => "weapon_rocket_launcher",
            WeaponType.Rockets       => "weapon_rocket_launcher",
            WeaponType.Laser         => "weapon_laser",
            WeaponType.Flak          => "weapon_flak",
            WeaponType.Bomb          => "weapon_cannon_siege",
            WeaponType.Mortar        => "weapon_cannon_light",
            WeaponType.Sniper        => "weapon_autocannon",
            WeaponType.Flamethrower  => "weapon_plasma",   // No dedicated flamethrower sound; plasma is closest
            WeaponType.EMP           => "weapon_laser",
            WeaponType.GatlingGun    => "weapon_chaingun",
            WeaponType.SAM           => "weapon_sam_missile",
            WeaponType.Torpedo       => "weapon_torpedo",
            WeaponType.ChemicalSpray => "weapon_plasma",   // No dedicated chemical sound; plasma is closest
            WeaponType.None          => null,
            _                        => "weapon_machinegun"
        };
    }

    /// <summary>
    /// Maps a <see cref="UnitCategory"/> to the appropriate death sound ID.
    /// </summary>
    private static string MapCategoryToDeathSound(UnitCategory category)
    {
        return category switch
        {
            UnitCategory.Infantry    => "unit_death_infantry",
            UnitCategory.Helicopter  => "unit_death_aircraft",
            UnitCategory.Jet         => "unit_death_aircraft",
            UnitCategory.Defense     => "unit_death_building",
            // Naval units use a distinct water-hull destruction sound
            UnitCategory.PatrolBoat  => "unit_death_naval_small",
            UnitCategory.Destroyer   => "unit_death_naval_medium",
            UnitCategory.Submarine   => "unit_death_naval_medium",
            UnitCategory.CapitalShip => "unit_death_naval_large",
            _                        => "unit_death_vehicle"
        };
    }

    // ── Playback ─────────────────────────────────────────────────────

    private void PlayCombatSound(string soundId, Vector3 position)
    {
        if (_audioManager == null) return;

        SoundEntry? entry = SoundRegistry.Instance.IsLoaded
            ? SoundRegistry.Instance.GetSound("combat", soundId)
            : null;

        if (entry == null) return;

        string? file = SoundRegistry.PickVariant(entry);
        if (file == null) return;

        AudioStream? stream = GD.Load<AudioStream>(file);
        if (stream == null)
        {
            GD.PushWarning($"[CombatAudioBridge] Could not load audio file: {file}");
            return;
        }

        _audioManager.PlaySfx(stream, position);
    }
}
