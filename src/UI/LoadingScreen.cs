using Godot;
using UnnamedRTS.Game.Assets;
using UnnamedRTS.Game.Factions;
using UnnamedRTS.Game.Tech;
using UnnamedRTS.Game.World;
using UnnamedRTS.Systems.Audio;
using UnnamedRTS.Systems.Graphics;
using UnnamedRTS.Systems.Localization;

namespace UnnamedRTS.UI;

/// <summary>
/// Progress bar loading screen. Loads all registries in sequence,
/// updating progress bar and status text. Transitions to MainMenu when done.
/// </summary>
public partial class LoadingScreen : Control
{
    private const string NextScene = "res://scenes/UI/MainMenu.tscn";

    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Label _tipLabel = null!;
    private int _currentStep;
    private bool _loadingComplete;

    private static readonly string[] LoadingTipKeys =
    {
        "TIP_VALKYR_REARM",
        "TIP_KRAGMORE_HORDE",
        "TIP_BASTION_REFINERY",
        "TIP_ARCLOFT_OVERWATCH",
        "TIP_IRONMARCH_FOB",
        "TIP_STORMREND_MOMENTUM",
        "TIP_CAMERA_CONTROLS",
        "TIP_COMMAND_UNITS",
        "TIP_CONTROL_GROUPS",
        "TIP_DESTROY_REACTORS"
    };

    private struct LoadStep
    {
        public string StatusKey;
        public float TargetPercent;
    }

    private static readonly LoadStep[] Steps =
    {
        new() { StatusKey = "LOADING_DETECTING_HARDWARE",  TargetPercent = 5 },
        new() { StatusKey = "LOADING_UNIT_DATA",           TargetPercent = 12 },
        new() { StatusKey = "LOADING_BUILDING_DATA",       TargetPercent = 22 },
        new() { StatusKey = "LOADING_ASSET_MANIFEST",      TargetPercent = 32 },
        new() { StatusKey = "LOADING_BUILDING_MANIFEST",   TargetPercent = 40 },
        new() { StatusKey = "LOADING_TERRAIN_MANIFEST",    TargetPercent = 48 },
        new() { StatusKey = "LOADING_UPGRADE_DATA",        TargetPercent = 56 },
        new() { StatusKey = "LOADING_MAPS",                TargetPercent = 64 },
        new() { StatusKey = "LOADING_SOUND_MANIFEST",      TargetPercent = 72 },
        new() { StatusKey = "LOADING_FACTION_DATA",        TargetPercent = 80 },
        new() { StatusKey = "LOADING_TRANSLATIONS",        TargetPercent = 90 },
        new() { StatusKey = "LOADING_READY",               TargetPercent = 100 },
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Main layout
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 200);
        margin.AddThemeConstantOverride("margin_right", 200);
        margin.AddThemeConstantOverride("margin_top", 0);
        margin.AddThemeConstantOverride("margin_bottom", 80);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 20);
        margin.AddChild(vbox);

        // LOADING text
        var loadingLabel = new Label();
        loadingLabel.Text = Tr("LOADING_TITLE");
        loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(loadingLabel, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(loadingLabel);

        // Progress bar
        _progressBar = new ProgressBar();
        _progressBar.MinValue = 0;
        _progressBar.MaxValue = 100;
        _progressBar.Value = 0;
        _progressBar.CustomMinimumSize = new Vector2(0, 24);
        _progressBar.ShowPercentage = false;
        UITheme.StyleProgressBar(_progressBar);
        vbox.AddChild(_progressBar);

        // Status text
        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        vbox.AddChild(_statusLabel);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 60);
        vbox.AddChild(spacer);

        // Tip text at bottom
        _tipLabel = new Label();
        string tipKey = LoadingTipKeys[GD.RandRange(0, LoadingTipKeys.Length - 1)];
        _tipLabel.Text = Tr(tipKey);
        _tipLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_tipLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        _tipLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.KeepSize);
        _tipLabel.OffsetTop = -60;
        _tipLabel.OffsetLeft = 200;
        _tipLabel.OffsetRight = -200;
        AddChild(_tipLabel);

        // Start loading on next frame to let UI render first
        CallDeferred(MethodName.StartLoading);
    }

    private async void StartLoading()
    {
        for (_currentStep = 0; _currentStep < Steps.Length; _currentStep++)
        {
            _statusLabel.Text = Tr(Steps[_currentStep].StatusKey);

            // Animate progress bar
            var tween = CreateTween();
            tween.TweenProperty(_progressBar, "value", (double)Steps[_currentStep].TargetPercent, 0.15f);
            await ToSignal(tween, Tween.SignalName.Finished);

            // Execute the actual loading step
            ExecuteStep(_currentStep);

            // Yield a frame between steps so the UI stays responsive
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        _loadingComplete = true;

        // Wait a moment at 100% then transition
        await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
        GetTree().ChangeSceneToFile(NextScene);
    }

    private void ExecuteStep(int step)
    {
        switch (step)
        {
            case 0: // Detect hardware
                var qm = QualityManager.Instance;
                if (qm != null)
                    qm.AutoDetect();
                else
                    GD.Print("[LoadingScreen] QualityManager not available, skipping auto-detect.");
                break;

            case 1: // Unit data — FactionRegistry handles this in LoadAll
                GD.Print("[LoadingScreen] Unit data will be loaded with faction registry.");
                break;

            case 2: // Building data — FactionRegistry handles this in LoadAll
                GD.Print("[LoadingScreen] Building data will be loaded with faction registry.");
                break;

            case 3: // Asset manifest
                try
                {
                    var assetReg = new AssetRegistry();
                    assetReg.Load("res://data/asset_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Asset manifest not found, skipping.");
                }
                break;

            case 4: // Building manifest
                try
                {
                    var buildingManifest = new BuildingManifest();
                    buildingManifest.Load("res://data/building_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Building manifest not found, skipping.");
                }
                break;

            case 5: // Terrain manifest
                try
                {
                    var terrainManifest = new TerrainManifest();
                    terrainManifest.Load("res://data/terrain_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Terrain manifest not found, skipping.");
                }
                break;

            case 6: // Upgrade data
                try
                {
                    var upgradeReg = new UpgradeRegistry();
                    upgradeReg.Load("res://data/upgrades");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Upgrade data not found, skipping.");
                }
                break;

            case 7: // Maps
                try
                {
                    var mapLoader = new MapLoader();
                    mapLoader.LoadAllMaps("res://data/maps");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Map data not found, skipping.");
                }
                break;

            case 8: // Sound manifest
                try
                {
                    SoundRegistry.Instance.Load("res://data/sound_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Sound manifest not found, skipping.");
                }
                break;

            case 9: // Faction data (loads factions + units + buildings)
                try
                {
                    var factionReg = new FactionRegistry();
                    factionReg.LoadAll("res://data/factions", "res://data/units", "res://data/buildings");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Faction data not found, skipping.");
                }
                break;

            case 10: // Translations & subtitles
                try
                {
                    LocalizationManager.Instance?.LoadAllTranslations();
                    SubtitleManager.Instance?.LoadSubtitles();
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Translation data not found, skipping.");
                }
                break;

            case 11: // Ready!
                GD.Print("[LoadingScreen] All registries loaded.");
                break;
        }
    }
}
