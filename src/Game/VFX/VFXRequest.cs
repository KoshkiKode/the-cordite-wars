namespace CorditeWars.Game.VFX;

/// <summary>
/// Identifies the type of visual effect to spawn.
/// Kept as a simple enum so that <see cref="VFXDispatcher"/> remains a
/// plain C# class with no Godot dependencies (and is therefore unit-testable).
/// </summary>
public enum VFXEffectType
{
    ExplosionSmall,
    ExplosionMedium,
    ExplosionLarge,
    SmokePuff,
    DustCloud,
    MuzzleFlash,
    ThrusterTrail,
    Spark,
    WaterSplash
}

/// <summary>
/// A single resolved visual-effect request returned by <see cref="VFXDispatcher"/>.
/// Carries the effect type and a world-space offset from the event position so
/// that multi-particle events (e.g. capital-ship death) can fan out spatially.
/// </summary>
public readonly record struct VFXRequest(VFXEffectType Effect, float OffsetX, float OffsetY, float OffsetZ)
{
    /// <summary>Creates a request with no positional offset.</summary>
    public static VFXRequest At(VFXEffectType effect) => new(effect, 0f, 0f, 0f);
}
