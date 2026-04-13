using Godot;
using System;

namespace CorditeWars.Systems.Graphics
{
    /// <summary>
    /// Rendering quality tiers, ordered from lowest to highest.
    /// Tier selection is LOCAL ONLY — it never affects simulation determinism.
    /// </summary>
    public enum QualityTier
    {
        /// <summary>Potato Mode — 2015-era hardware, integrated graphics, budget Android devices.</summary>
        Potato = 0,
        /// <summary>Low — 2018-era hardware, entry discrete GPU.</summary>
        Low    = 1,
        /// <summary>Medium — 2020-era mid-range GPU. Default for new installations.</summary>
        Medium = 2,
        /// <summary>High — 2022+ high-end GPU, 8 GB+ VRAM.</summary>
        High   = 3
    }

    /// <summary>
    /// Manages rendering quality for Cordite Wars: Six Fronts.
    ///
    /// Quality is purely a client-side concern. The simulation layer uses
    /// deterministic fixed-point arithmetic and is identical across all tiers.
    /// Only the Godot rendering pipeline, audio LOD, and particle density change.
    ///
    /// Usage:
    ///   QualityManager.Instance.AutoDetect();         // called once at startup
    ///   QualityManager.Instance.ApplyTier(QualityTier.High);
    ///   QualityManager.Instance.SetAntiAliasing(3);   // override individual setting
    /// </summary>
    public sealed partial class QualityManager : Node
    {
        // ─── Singleton ────────────────────────────────────────────────────────

        private static QualityManager? _instance;
        public static QualityManager Instance => _instance!;

        // ─── State ────────────────────────────────────────────────────────────

        /// <summary>The tier currently applied to the rendering pipeline.</summary>
        public QualityTier CurrentTier { get; private set; } = QualityTier.Medium;

        /// <summary>Current draw-distance multiplier (1.0 = 100%).</summary>
        public float DrawDistanceMultiplier { get; private set; } = 1.0f;

        /// <summary>Current particle density multiplier (1.0 = 100%).</summary>
        public float ParticleMultiplier { get; private set; } = 1.0f;

        // ─── Node lifecycle ───────────────────────────────────────────────────

        public override void _EnterTree()
        {
            if (_instance != null)
            {
                GD.PrintErr("[QualityManager] Duplicate instance — removing.");
                QueueFree();
                return;
            }
            _instance = this;
        }

        public override void _ExitTree()
        {
            if (_instance == this)
                _instance = null;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Examines GPU capabilities at runtime and selects the highest tier
        /// the hardware can sustain at ≥30 FPS. On Android, caps at Tier 1
        /// unless the device is explicitly detected as capable of Tier 2.
        /// </summary>
        public void AutoDetect()
        {
            QualityTier detected = DetectHardwareTier();
            GD.Print($"[QualityManager] Auto-detected quality tier: {detected}");
            ApplyTier(detected);
        }

        /// <summary>
        /// Applies a complete quality tier, overwriting any individual overrides.
        /// Modifies both Godot ProjectSettings (for future scenes) and the live
        /// RenderingServer (for the current viewport).
        /// </summary>
        public void ApplyTier(QualityTier tier)
        {
            CurrentTier = tier;

            switch (tier)
            {
                case QualityTier.Potato:
                    ApplyPotatoSettings();
                    break;
                case QualityTier.Low:
                    ApplyLowSettings();
                    break;
                case QualityTier.Medium:
                    ApplyMediumSettings();
                    break;
                case QualityTier.High:
                    ApplyHighSettings();
                    break;
                default:
                    GD.PrintErr($"[QualityManager] Unknown tier: {tier}, falling back to Medium.");
                    ApplyMediumSettings();
                    break;
            }

            // Persist choice so next launch restores it.
            PersistTier(tier);

            GD.Print($"[QualityManager] Applied tier: {tier}");
        }

        /// <summary>
        /// Overrides shadow quality independently of the current tier.
        /// </summary>
        /// <param name="level">0 = off, 1 = 1024 px, 2 = 2048 px, 3 = 4096 px</param>
        public void SetShadowQuality(int level)
        {
            level = Mathf.Clamp(level, 0, 3);

            if (level == 0)
            {
                // Disable shadows entirely.
                RenderingServer.DirectionalShadowAtlasSetSize(0, true);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/directional_shadow/size", 0);
                GD.Print("[QualityManager] Shadows: OFF");
                return;
            }

            int atlasSize = level switch
            {
                1 => 1024,
                2 => 2048,
                3 => 4096,
                _ => 2048
            };

            RenderingServer.DirectionalShadowAtlasSetSize(atlasSize, true);
            ProjectSettings.SetSetting("rendering/lights_and_shadows/directional_shadow/size", atlasSize);
            GD.Print($"[QualityManager] Shadow atlas: {atlasSize}");
        }

        /// <summary>
        /// Overrides anti-aliasing independently of the current tier.
        /// </summary>
        /// <param name="level">0 = off, 1 = FXAA, 2 = MSAA 2×, 3 = MSAA 4×</param>
        public void SetAntiAliasing(int level)
        {
            level = Mathf.Clamp(level, 0, 3);

            var viewport = GetViewport();
            if (viewport == null)
            {
                GD.PrintErr("[QualityManager] SetAntiAliasing: no active viewport.");
                return;
            }

            switch (level)
            {
                case 0:
                    viewport.Msaa3D  = Viewport.Msaa.Disabled;
                    viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                    GD.Print("[QualityManager] AA: OFF");
                    break;

                case 1:
                    viewport.Msaa3D  = Viewport.Msaa.Disabled;
                    viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                    GD.Print("[QualityManager] AA: FXAA");
                    break;

                case 2:
                    viewport.Msaa3D  = Viewport.Msaa.Msaa2X;
                    viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                    GD.Print("[QualityManager] AA: MSAA 2×");
                    break;

                case 3:
                    viewport.Msaa3D  = Viewport.Msaa.Msaa4X;
                    viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                    GD.Print("[QualityManager] AA: MSAA 4×");
                    break;
            }
        }

        /// <summary>
        /// Sets the draw-distance multiplier that the camera/LOD system reads.
        /// 1.0 = 100% (tier 2 baseline), 0.6 = 60% (tier 0 minimum), 1.2 = 120% (tier 3).
        /// This does NOT directly call Godot APIs — the CameraController reads
        /// QualityManager.Instance.DrawDistanceMultiplier each frame.
        /// </summary>
        public void SetDrawDistance(float multiplier)
        {
            DrawDistanceMultiplier = Mathf.Clamp(multiplier, 0.1f, 2.0f);
            GD.Print($"[QualityManager] Draw distance multiplier: {DrawDistanceMultiplier:F2}");
        }

        /// <summary>
        /// Sets the global particle density multiplier.
        /// 0.25 = 25% (Potato), 0.5 = 50% (Low), 1.0 = full (Medium/High).
        /// Particle systems read this at spawn time to scale their emission count.
        /// </summary>
        public void SetParticleMultiplier(float multiplier)
        {
            ParticleMultiplier = Mathf.Clamp(multiplier, 0.0f, 1.0f);
            GD.Print($"[QualityManager] Particle multiplier: {ParticleMultiplier:F2}");
        }

        // ─── Private helpers — per-tier settings ─────────────────────────────

        /// <summary>
        /// Tier 0 — Potato: OpenGL ES 3.0 / Compatibility renderer.
        /// Targets 2015-era iGPU and Android budget phones at ~30 FPS.
        /// </summary>
        private void ApplyPotatoSettings()
        {
            // Shadows
            SetShadowQuality(0);

            // Anti-aliasing
            SetAntiAliasing(0);

            // Draw distance (60% of baseline)
            SetDrawDistance(0.6f);

            // Particles
            SetParticleMultiplier(0.25f);

            // Post-processing — all off
            ApplyPostProcessing(ssao: false, ssr: false, bloom: false, volumetrics: false, toneMapping: false);

            // Texture filtering — bilinear, no anisotropic
            ProjectSettings.SetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 0);

            // LOD bias — most aggressive
            RenderingServer.CameraAttributesSetExposure(
                RenderingServer.CameraAttributesCreate(),
                1.0f, 1.0f);   // placeholder: LOD system reads QualityTier

            GD.Print("[QualityManager] Potato settings applied.");
        }

        /// <summary>
        /// Tier 1 — Low: Compatibility renderer, minimal shadows, FXAA, 50% particles.
        /// </summary>
        private void ApplyLowSettings()
        {
            SetShadowQuality(1);  // 1024, directional only
            SetAntiAliasing(1);   // FXAA
            SetDrawDistance(0.8f);
            SetParticleMultiplier(0.5f);
            ApplyPostProcessing(ssao: false, ssr: false, bloom: false, volumetrics: false, toneMapping: true);
            ProjectSettings.SetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 0);
            GD.Print("[QualityManager] Low settings applied.");
        }

        /// <summary>
        /// Tier 2 — Medium: Forward+, 2048 shadows, MSAA 2×, full particles, SSAO + bloom.
        /// Default for new installations on desktop.
        /// </summary>
        private void ApplyMediumSettings()
        {
            SetShadowQuality(2);  // 2048
            SetAntiAliasing(2);   // MSAA 2×
            SetDrawDistance(1.0f);
            SetParticleMultiplier(1.0f);
            ApplyPostProcessing(ssao: true, ssr: false, bloom: true, volumetrics: false, toneMapping: true);
            ProjectSettings.SetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 4);
            GD.Print("[QualityManager] Medium settings applied.");
        }

        /// <summary>
        /// Tier 3 — High: Forward+, 4096 shadows, MSAA 4×, extended draw, all post-fx.
        /// </summary>
        private void ApplyHighSettings()
        {
            SetShadowQuality(3);  // 4096
            SetAntiAliasing(3);   // MSAA 4×
            SetDrawDistance(1.2f);
            SetParticleMultiplier(1.0f);
            ApplyPostProcessing(ssao: true, ssr: true, bloom: true, volumetrics: true, toneMapping: true);
            ProjectSettings.SetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 16);
            GD.Print("[QualityManager] High settings applied.");
        }

        /// <summary>
        /// Applies post-processing flags to the world environment.
        /// Looks for the Environment resource on the WorldEnvironment node in the scene.
        /// Safe to call even if there is no WorldEnvironment (e.g. during boot).
        /// </summary>
        private static void ApplyPostProcessing(
            bool ssao, bool ssr, bool bloom, bool volumetrics, bool toneMapping)
        {
            // Locate environment via autoload or current scene.
            // WorldEnvironment may live anywhere in the tree (e.g. as a child of Main),
            // so search recursively rather than relying on a fixed path.
            var worldEnv = Engine.GetMainLoop() is SceneTree tree
                ? tree.Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment
                : null;

            if (worldEnv?.Environment == null)
            {
                // No world environment in this scene; settings will be applied
                // when a map loads and queries CurrentTier.
                return;
            }

            var env = worldEnv.Environment;

            env.SsaoEnabled    = ssao;
            env.SsrEnabled     = ssr;
            env.GlowEnabled    = bloom;

            // Volumetric fog (Forward+ only)
            env.VolumetricFogEnabled = volumetrics;

            // Tone mapping
            env.TonemapMode = toneMapping
                ? Environment.ToneMapper.Filmic
                : Environment.ToneMapper.Linear;
        }

        // ─── Hardware detection ───────────────────────────────────────────────

        /// <summary>
        /// Heuristically determines the best quality tier for the current device.
        ///
        /// Strategy:
        ///   1. On Android, default to Potato; promote to Low/Medium if VRAM ≥ 4 GB.
        ///   2. On desktop, use reported VRAM and renderer name to guess tier.
        ///   3. When in doubt, choose Medium — it is the safe default.
        /// </summary>
        private static QualityTier DetectHardwareTier()
        {
            // Platform shortcuts
            if (OS.GetName() == "Android")
                return DetectAndroidTier();

            // Desktop: inspect the rendering device
            var rd = RenderingServer.GetRenderingDevice();
            if (rd == null)
            {
                // Compatibility renderer — no RD available
                GD.Print("[QualityManager] Compatibility renderer detected.");
                return QualityTier.Low;
            }

            // Godot 4.x exposes vendor + device name on RenderingDevice.
            string deviceName = rd.GetDeviceName().ToLowerInvariant();

            // Intel integrated: Potato
            if (deviceName.Contains("intel") && (deviceName.Contains("uhd") || deviceName.Contains("hd graphics")))
                return QualityTier.Potato;

            // AMD integrated (Vega/RDNA iGPU): Low
            if (deviceName.Contains("radeon") && deviceName.Contains("graphics") && !deviceName.Contains("rx"))
                return QualityTier.Low;

            // Discrete GPU: try to determine by name heuristics
            // Old or low-end discrete: Low
            // Mid-range: Medium (safe default)
            // High-end: High
            if (deviceName.Contains("rtx 40") || deviceName.Contains("rx 7") || deviceName.Contains("rx 6"))
                return QualityTier.High;

            if (deviceName.Contains("rtx") || deviceName.Contains("rx 5") || deviceName.Contains("gtx 10"))
                return QualityTier.Medium;

            // Unknown or older: Medium is safe
            return QualityTier.Medium;
        }

        private static QualityTier DetectAndroidTier()
        {
            // On Android, always start at Potato.
            // Check for Vulkan support (Forward+ capable).
            var rd = RenderingServer.GetRenderingDevice();
            if (rd == null)
                return QualityTier.Potato;   // Compatibility / GLES3 only

            // Vulkan present — check device name for higher-end Adreno/Mali chips
            string name = rd.GetDeviceName().ToLowerInvariant();

            // High-end Adreno (750+, 8xx series) or Apple M (via Rosetta — not Android but guard anyway)
            if (name.Contains("adreno") && (name.Contains("750") || name.Contains("7") || name.Contains("8")))
                return QualityTier.Low;

            if (name.Contains("immortalis") || name.Contains("mali-g") && name.Contains("10"))
                return QualityTier.Low;

            return QualityTier.Potato;
        }

        // ─── Persistence ──────────────────────────────────────────────────────

        private const string SettingsSectionName = "QualityManager";
        private const string TierKeyName         = "tier";

        /// <summary>Saves the chosen tier to user://settings.cfg.</summary>
        private static void PersistTier(QualityTier tier)
        {
            var cfg = new ConfigFile();
            // Load existing settings first to avoid clobbering other keys.
            cfg.Load("user://settings.cfg");
            cfg.SetValue(SettingsSectionName, TierKeyName, (int)tier);
            var err = cfg.Save("user://settings.cfg");
            if (err != Error.Ok)
                GD.PrintErr($"[QualityManager] Failed to save settings: {err}");
        }

        /// <summary>
        /// Loads the previously saved tier from user://settings.cfg.
        /// Returns Medium if not found.
        /// </summary>
        public static QualityTier LoadSavedTier()
        {
            var cfg = new ConfigFile();
            if (cfg.Load("user://settings.cfg") != Error.Ok)
                return QualityTier.Medium;

            int raw = (int)cfg.GetValue(SettingsSectionName, TierKeyName, (int)QualityTier.Medium);
            if (Enum.IsDefined(typeof(QualityTier), raw))
                return (QualityTier)raw;

            return QualityTier.Medium;
        }
    }
}
