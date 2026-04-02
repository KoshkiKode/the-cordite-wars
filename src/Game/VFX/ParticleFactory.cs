using Godot;
using UnnamedRTS.Systems.Graphics;

namespace UnnamedRTS.Game.VFX;

/// <summary>
/// Creates programmatic particle effects using GpuParticles3D + ParticleProcessMaterial.
/// All effects auto-free after emission completes. Quality-tier aware.
/// </summary>
public static class ParticleFactory
{
    // ── Explosions ─────────────────────────────────────────────────────────

    public static GpuParticles3D CreateExplosionSmall()
    {
        return CreateExplosion(
            baseCount: 20,
            lifetime: 0.5f,
            colorStart: new Color(1.0f, 0.7f, 0.1f),
            colorEnd: new Color(0.6f, 0.2f, 0.0f, 0.0f),
            spread: 45f,
            velocity: 4f,
            scale: 0.3f
        );
    }

    public static GpuParticles3D CreateExplosionMedium()
    {
        return CreateExplosion(
            baseCount: 50,
            lifetime: 0.8f,
            colorStart: new Color(1.0f, 0.5f, 0.0f),
            colorEnd: new Color(0.8f, 0.1f, 0.0f, 0.0f),
            spread: 60f,
            velocity: 6f,
            scale: 0.5f
        );
    }

    public static GpuParticles3D CreateExplosionLarge()
    {
        return CreateExplosion(
            baseCount: 100,
            lifetime: 1.2f,
            colorStart: new Color(1.0f, 0.3f, 0.0f),
            colorEnd: new Color(0.15f, 0.1f, 0.05f, 0.0f),
            spread: 90f,
            velocity: 8f,
            scale: 0.8f
        );
    }

    // ── Muzzle Flash ───────────────────────────────────────────────────────

    public static GpuParticles3D CreateMuzzleFlash()
    {
        int count = ScaleParticleCount(5);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 0, -1);
        material.Spread = 15f;
        material.InitialVelocityMin = 8f;
        material.InitialVelocityMax = 12f;
        material.Gravity = Vector3.Zero;
        material.ScaleMin = 0.1f;
        material.ScaleMax = 0.2f;

        // Bright yellow-white
        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1.0f, 1.0f, 0.7f));
        gradient.SetColor(1, new Color(1.0f, 0.9f, 0.3f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 0.1f, material, oneShot: true);
    }

    // ── Smoke Puff ─────────────────────────────────────────────────────────

    public static GpuParticles3D CreateSmokePuff()
    {
        int count = ScaleParticleCount(15);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 1, 0);
        material.Spread = 40f;
        material.InitialVelocityMin = 1f;
        material.InitialVelocityMax = 2.5f;
        material.Gravity = new Vector3(0, 0.3f, 0);
        material.ScaleMin = 0.3f;
        material.ScaleMax = 0.8f;
        material.DampingMin = 2f;
        material.DampingMax = 2f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.5f, 0.5f, 0.5f, 0.6f));
        gradient.SetColor(1, new Color(0.3f, 0.3f, 0.3f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 2.0f, material, oneShot: true);
    }

    // ── Dust Cloud ─────────────────────────────────────────────────────────

    public static GpuParticles3D CreateDustCloud()
    {
        int count = ScaleParticleCount(20);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 0.5f, 0);
        material.Spread = 70f;
        material.InitialVelocityMin = 1f;
        material.InitialVelocityMax = 3f;
        material.Gravity = new Vector3(0, -0.5f, 0);
        material.ScaleMin = 0.2f;
        material.ScaleMax = 0.6f;
        material.DampingMin = 3f;
        material.DampingMax = 3f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.6f, 0.5f, 0.35f, 0.5f));
        gradient.SetColor(1, new Color(0.5f, 0.4f, 0.3f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 1.5f, material, oneShot: true);
    }

    // ── Thruster Trail ─────────────────────────────────────────────────────

    public static GpuParticles3D CreateThrusterTrail()
    {
        int count = ScaleParticleCount(30);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 0, 1);
        material.Spread = 10f;
        material.InitialVelocityMin = 2f;
        material.InitialVelocityMax = 4f;
        material.Gravity = new Vector3(0, -0.2f, 0);
        material.ScaleMin = 0.05f;
        material.ScaleMax = 0.15f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.6f, 0.8f, 1.0f));
        gradient.SetColor(1, new Color(0.2f, 0.4f, 0.8f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        // Continuous emission (not one-shot)
        return BuildParticles(count, 0.6f, material, oneShot: false);
    }

    // ── Bullet Tracer ──────────────────────────────────────────────────────

    public static GpuParticles3D CreateBulletTracer()
    {
        int count = ScaleParticleCount(2);
        if (count < 1) count = 1;

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 0, -1);
        material.Spread = 2f;
        material.InitialVelocityMin = 50f;
        material.InitialVelocityMax = 60f;
        material.Gravity = Vector3.Zero;
        material.ScaleMin = 0.02f;
        material.ScaleMax = 0.04f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1.0f, 1.0f, 0.5f));
        gradient.SetColor(1, new Color(1.0f, 0.8f, 0.2f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 0.05f, material, oneShot: true);
    }

    // ── Spark ──────────────────────────────────────────────────────────────

    public static GpuParticles3D CreateSpark()
    {
        int count = ScaleParticleCount(10);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 1, 0);
        material.Spread = 120f;
        material.InitialVelocityMin = 3f;
        material.InitialVelocityMax = 7f;
        material.Gravity = new Vector3(0, -9.8f, 0);
        material.ScaleMin = 0.02f;
        material.ScaleMax = 0.05f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1.0f, 0.7f, 0.2f));
        gradient.SetColor(1, new Color(1.0f, 0.3f, 0.0f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 0.3f, material, oneShot: true);
    }

    // ── Water Splash ───────────────────────────────────────────────────────

    public static GpuParticles3D CreateWaterSplash()
    {
        int count = ScaleParticleCount(15);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 1, 0);
        material.Spread = 60f;
        material.InitialVelocityMin = 2f;
        material.InitialVelocityMax = 5f;
        material.Gravity = new Vector3(0, -9.8f, 0);
        material.ScaleMin = 0.05f;
        material.ScaleMax = 0.15f;
        material.DampingMin = 1f;
        material.DampingMax = 1f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.6f, 0.8f, 1.0f, 0.7f));
        gradient.SetColor(1, new Color(0.7f, 0.9f, 1.0f, 0.0f));
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, 0.5f, material, oneShot: true);
    }

    // ── Shared Helpers ─────────────────────────────────────────────────────

    private static GpuParticles3D CreateExplosion(int baseCount, float lifetime,
        Color colorStart, Color colorEnd, float spread, float velocity, float scale)
    {
        int count = ScaleParticleCount(baseCount);

        var material = new ParticleProcessMaterial();
        material.Direction = new Vector3(0, 1, 0);
        material.Spread = spread;
        material.InitialVelocityMin = velocity * 0.5f;
        material.InitialVelocityMax = velocity;
        material.Gravity = new Vector3(0, -4f, 0);
        material.ScaleMin = scale * 0.5f;
        material.ScaleMax = scale;
        material.DampingMin = 2f;
        material.DampingMax = 2f;

        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, colorStart);
        gradient.SetColor(1, colorEnd);
        colorRamp.Gradient = gradient;
        material.ColorRamp = colorRamp;

        return BuildParticles(count, lifetime, material, oneShot: true);
    }

    private static GpuParticles3D BuildParticles(int count, float lifetime,
        ParticleProcessMaterial material, bool oneShot)
    {
        var particles = new GpuParticles3D();
        particles.Amount = count > 0 ? count : 1;
        particles.Lifetime = lifetime;
        particles.OneShot = oneShot;
        particles.Explosiveness = oneShot ? 0.9f : 0f;
        particles.ProcessMaterial = material;
        particles.Emitting = true;

        // Use simple quad mesh for particles
        var drawMesh = new QuadMesh();
        drawMesh.Size = new Vector2(0.1f, 0.1f);
        particles.DrawPass1 = drawMesh;

        // Billboard mode for quads
        var drawMaterial = new StandardMaterial3D();
        drawMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        drawMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        drawMaterial.VertexColorUseAsAlbedo = true;
        drawMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        drawMesh.Material = drawMaterial;

        // Auto-free after emission for one-shot effects
        if (oneShot)
        {
            particles.Finished += () =>
            {
                if (GodotObject.IsInstanceValid(particles))
                    particles.QueueFree();
            };
        }

        return particles;
    }

    /// <summary>
    /// Scales particle count based on QualityManager settings.
    /// </summary>
    private static int ScaleParticleCount(int baseCount)
    {
        float multiplier = 1f;
        if (QualityManager.Instance != null)
        {
            multiplier = QualityManager.Instance.ParticleMultiplier;
        }

        int scaled = (int)(baseCount * multiplier);
        return scaled > 0 ? scaled : 1;
    }
}
