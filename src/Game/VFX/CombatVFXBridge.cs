using System.Collections.Generic;
using Godot;
using CorditeWars.Core;
using CorditeWars.Game.Units;

namespace CorditeWars.Game.VFX;

/// <summary>
/// Listens to combat events on the <see cref="EventBus"/> and spawns
/// GPU particle effects through <see cref="ParticleFactory"/>.
///
/// This is the visual counterpart of <see cref="Systems.Audio.CombatAudioBridge"/>.
/// It runs on the rendering side only — it never mutates simulation state.
///
/// Typical lifecycle:
///   1. GameSession creates this node after match start.
///   2. <see cref="Initialize"/> subscribes to EventBus signals and stores
///      the scene root under which spawned particles will be added.
///   3. Each tick, GameSession emits AttackFired / AttackImpact / UnitDeath.
///   4. This bridge calls the appropriate ParticleFactory method and adds
///      the resulting GpuParticles3D to <see cref="_particleRoot"/>.
///   5. One-shot particles auto-free via their Finished signal.
///   6. On cleanup, this node is freed and signals auto-disconnect.
/// </summary>
public partial class CombatVFXBridge : Node
{
    /// <summary>
    /// The 3D node under which all spawned particle effects are parented.
    /// Set by <see cref="Initialize"/>. If null, particles are added directly
    /// to this node's parent (which still works correctly).
    /// </summary>
    private Node3D? _particleRoot;

    /// <summary>
    /// Subscribes to all combat-related EventBus signals. Call once after
    /// the scene tree is populated and the particle root is known.
    /// </summary>
    /// <param name="particleRoot">
    /// Node3D that will parent all spawned particle effects. Pass the game
    /// world root (e.g. the main scene node) so particles are positioned in
    /// world-space correctly.
    /// </param>
    public void Initialize(Node3D? particleRoot = null)
    {
        _particleRoot = particleRoot;

        var bus = EventBus.Instance;
        if (bus == null)
        {
            GD.PushError("[CombatVFXBridge] EventBus not available.");
            return;
        }

        bus.AttackFired  += OnAttackFired;
        bus.AttackImpact += OnAttackImpact;
        bus.UnitDeath    += OnUnitDeath;

        GD.Print("[CombatVFXBridge] Initialized — listening for combat events.");
    }

    public override void _ExitTree()
    {
        var bus = EventBus.Instance;
        if (bus != null)
        {
            bus.AttackFired  -= OnAttackFired;
            bus.AttackImpact -= OnAttackImpact;
            bus.UnitDeath    -= OnUnitDeath;
        }
    }

    // ── Signal Handlers ──────────────────────────────────────────────

    private void OnAttackFired(int attackerId, int weaponType, Vector3 position)
    {
        var type = (WeaponType)weaponType;
        SpawnRequests(VFXDispatcher.GetAttackFiredEffects(type), position);
    }

    private void OnAttackImpact(int targetId, bool isHit, bool hasAoe, Vector3 position)
    {
        SpawnRequests(VFXDispatcher.GetAttackImpactEffects(isHit, hasAoe), position);
    }

    private void OnUnitDeath(int unitId, int unitCategory, Vector3 position)
    {
        var category = (UnitCategory)unitCategory;
        SpawnRequests(VFXDispatcher.GetUnitDeathEffects(category), position);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves each <see cref="VFXRequest"/> to a concrete
    /// <see cref="GpuParticles3D"/> and spawns it at
    /// <paramref name="basePos"/> + the request's offset.
    /// </summary>
    private void SpawnRequests(IReadOnlyList<VFXRequest> requests, Vector3 basePos)
    {
        foreach (var req in requests)
        {
            var particles = req.Effect switch
            {
                VFXEffectType.ExplosionSmall  => ParticleFactory.CreateExplosionSmall(),
                VFXEffectType.ExplosionMedium => ParticleFactory.CreateExplosionMedium(),
                VFXEffectType.ExplosionLarge  => ParticleFactory.CreateExplosionLarge(),
                VFXEffectType.SmokePuff       => ParticleFactory.CreateSmokePuff(),
                VFXEffectType.DustCloud       => ParticleFactory.CreateDustCloud(),
                VFXEffectType.MuzzleFlash     => ParticleFactory.CreateMuzzleFlash(),
                VFXEffectType.ThrusterTrail   => ParticleFactory.CreateThrusterTrail(),
                VFXEffectType.Spark           => ParticleFactory.CreateSpark(),
                VFXEffectType.WaterSplash     => ParticleFactory.CreateWaterSplash(),
                _                             => ParticleFactory.CreateExplosionSmall()
            };

            SpawnAt(particles, basePos + new Vector3(req.OffsetX, req.OffsetY, req.OffsetZ));
        }
    }

    /// <summary>
    /// Positions <paramref name="particles"/> at <paramref name="worldPos"/>
    /// and adds it to the particle root (or this node's parent as fallback).
    /// </summary>
    private void SpawnAt(GpuParticles3D particles, Vector3 worldPos)
    {
        Node? root = (_particleRoot as Node) ?? GetParent();
        if (root == null)
        {
            particles.QueueFree();
            return;
        }

        particles.GlobalPosition = worldPos;
        root.AddChild(particles);
    }
}
