using Godot;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Factions;
using CorditeWars.Game.Tech;
using CorditeWars.Game.World;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Graphics;

namespace CorditeWars.UI;

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

    private static readonly string[] LoadingTips =
    {
        "Valkyr jets must return to Airstrips to rearm.",
        "Kragmore's Horde Protocol rewards grouping 5+ units together.",
        "Bastion Refineries generate passive Cordite income.",
        "Arcloft can designate up to 3 Overwatch Zones on the map.",
        "Ironmarch FOB Trucks deploy into forward operating bases.",
        "Stormrend's Momentum Gauge fills when dealing damage.",
        "Use WASD to pan the camera, scroll wheel to zoom.",
        "Right-click to move or attack. Shift+click to queue orders.",
        "Ctrl+1-9 assigns control groups. Press the number to recall.",
        "Destroying enemy Reactors cripples their tech production."
    };

    private struct LoadStep
    {
        public string StatusText;
        public float TargetPercent;
    }

    private static readonly LoadStep[] Steps =
    {
        new() { StatusText = "Detecting hardware...",       TargetPercent = 5 },
        new() { StatusText = "Loading unit data...",        TargetPercent = 15 },
        new() { StatusText = "Loading building data...",    TargetPercent = 25 },
        new() { StatusText = "Loading asset manifest...",   TargetPercent = 35 },
        new() { StatusText = "Loading building manifest...", TargetPercent = 45 },
        new() { StatusText = "Loading terrain manifest...", TargetPercent = 55 },
        new() { StatusText = "Loading upgrade data...",     TargetPercent = 65 },
        new() { StatusText = "Loading maps...",             TargetPercent = 75 },
        new() { StatusText = "Loading sound manifest...",   TargetPercent = 85 },
        new() { StatusText = "Loading faction data...",     TargetPercent = 95 },
        new() { StatusText = "Ready!",                     TargetPercent = 100 },
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
        loadingLabel.Text = "LOADING...";
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
        _tipLabel.Text = LoadingTips[GD.RandRange(0, LoadingTips.Length - 1)];
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
            _statusLabel.Text = Steps[_currentStep].StatusText;

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
                    // Start loading music now that the registry is available
                    GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlayMusicById("music_loading");
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

            case 10: // Ready!
                GD.Print("[LoadingScreen] All registries loaded.");
                break;
        }
    }
}
